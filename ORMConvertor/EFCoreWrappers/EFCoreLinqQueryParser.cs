using AbstractWrappers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.AbstractRepresentation;
using Model.QueryInstructions.Enums;

namespace EFCoreWrappers;

public class EFCoreLinqQueryParser(AbstractQueryBuilder queryBuilder) : CSharpSyntaxWalker, IQueryParser
{
    private SemanticModel? semanticModel = null;
    private bool fromWasEmitted;
    private EntityMap? entityMap;

    public bool CanParse(ConversionContentType contentType)
    {
        return contentType == ConversionContentType.CSharpQuery;
    }

    public void Parse(string source)
    {
        Parse(source, null);
    }

    public void Parse(string source, EntityMap? entityMap)
    {
        this.entityMap = entityMap;

        // Parse the snippet into a Roslyn SyntaxTree.
        // Adding a dummy surrounding class/namespace keeps it syntactically valid.
        string wrappedSource = $@"""
            using System.Linq;
            namespace Dummy {{
            public class Snippet {{
            {source}
            }}
            }}
        """;

        queryBuilder.Push(); // We do not support nested queries for now

        var tree = CSharpSyntaxTree.ParseText(wrappedSource);
        var root = tree.GetCompilationUnitRoot();

        var compilation = CSharpCompilation.Create("QueryParser")
            .AddReferences(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(IQueryable).Assembly.Location))
            .AddSyntaxTrees(tree);

        semanticModel = compilation.GetSemanticModel(tree, ignoreAccessibility: true);

        Visit(root);
        queryBuilder.Pop();
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        base.VisitInvocationExpression(node);
        if (node.Expression is not MemberAccessExpressionSyntax member)
        {
            return;
        }

        string name = member.Name.Identifier.Text;
        switch (name)
        {
            case "Where": HandleWhere(node); break;
            case "Join": HandleJoin(node); break;
            case "Select": HandleSelect(node); break;
            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending": HandleOrderBy(node, name); break;
            case "GroupBy": HandleGroupBy(node); break;
        }
    }
    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        base.VisitMemberAccessExpression(node);

        if (fromWasEmitted)
        {
            return;
        }

        if (node.Expression is IdentifierNameSyntax id && id.Identifier.Text == "ctx")
        {
            // Resolve the table/schema via EntityMap when available
            // Here we assume that we have map for the entity being queried, this is not future proof
            string tableName = entityMap switch
            {
                { Table: { Length: > 0 } t, Schema: { Length: > 0 } s } => $"{s}.{t}",
                { Table: { Length: > 0 } t } => t,
                _ => node.Name.Identifier.Text
            };

            string aliasSource = node.Name switch
            {
                GenericNameSyntax generic when generic.TypeArgumentList.Arguments.Count > 0 =>
                    ExtractIdentifier(generic.TypeArgumentList.Arguments[0]),
                _ => node.Name.Identifier.Text
            };

            string alias = aliasSource.Length > 0
                ? aliasSource[..1].ToLower()
                : "t";

            queryBuilder.From(tableName, alias);
            fromWasEmitted = true;
        }
    }


    private void HandleWhere(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments.FirstOrDefault();
        if (arg?.Expression is not SimpleLambdaExpressionSyntax lambda)
        {
            return;
        }

        if (lambda.Body is not BinaryExpressionSyntax binary)
        {
            return;
        }

        bool lOk = TryParseOperand(binary.Left, out var lTable, out var lProp, out var lConst);
        bool rOk = TryParseOperand(binary.Right, out var rTable, out var rProp, out var rConst);
        
        if (!lOk || !rOk)
        {
            return;
        }

        queryBuilder.Select(lTable, lProp, lConst, MapOperator(binary.Kind()), rTable, rProp, rConst);
    }

    private void HandleJoin(InvocationExpressionSyntax node)
    {
        var args = node.ArgumentList.Arguments;
        if (args.Count < 4)
        {
            return;
        }

        string leftAlias = ExtractAliasFromPrev(node);
        string rightTable = ResolveTableName(args[0].Expression);
        string rightAlias = rightTable.ToLowerInvariant();

        if (args[1].Expression is not SimpleLambdaExpressionSyntax outer ||
            args[2].Expression is not SimpleLambdaExpressionSyntax inner)
        {
            return;
        }

        if (outer.Body is not ExpressionSyntax outerBody || inner.Body is not ExpressionSyntax innerBody)
        {
            return;
        }

        queryBuilder.Join(JoinKind.Inner, leftAlias, rightTable, ExtractMemberName(outerBody), ExtractMemberName(innerBody), rightAlias);
    }

    private void HandleSelect(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments.FirstOrDefault();
        if (arg?.Expression is not SimpleLambdaExpressionSyntax lambda)
        {
            return;
        }

        if (lambda.Body is not AnonymousObjectCreationExpressionSyntax anon)
        {
            return;
        }

        foreach (var init in anon.Initializers)
        {
            string alias = init.NameEquals?.Name.Identifier.Text ?? ExtractMemberName(init.Expression);

            if (init.Expression is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax mac)
            {
                string func = mac.Name.Identifier.Text.ToUpperInvariant();
                if (func is "COUNT" or "SUM" or "MIN" or "MAX" or "AVG")
                {
                    if (inv.ArgumentList.Arguments.First().Expression is SimpleLambdaExpressionSyntax lamAgg && lamAgg.Body is ExpressionSyntax aggBody)
                    {
                        queryBuilder.Project(ExtractAliasFromExpression(aggBody), ExtractMemberName(aggBody), alias, func);
                        continue;
                    }
                }
            }

            queryBuilder.Project(ExtractAliasFromExpression(init.Expression), ExtractMemberName(init.Expression), alias);
        }
    }

    private void HandleOrderBy(InvocationExpressionSyntax node, string method)
    {
        bool asc = method is "OrderBy" or "ThenBy";
        var arg = node.ArgumentList.Arguments.FirstOrDefault();
        if (arg?.Expression is not SimpleLambdaExpressionSyntax lambda)
        {
            return;
        }

        if (lambda.Body is not ExpressionSyntax body)
        {
            return;
        }

        queryBuilder.OrderBy(ExtractAliasFromExpression(body), ExtractMemberName(body), asc);
    }

    private void HandleGroupBy(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments.FirstOrDefault();
        if (arg?.Expression is not SimpleLambdaExpressionSyntax lambda)
        {
            return;
        }

        if (lambda.Body is not ExpressionSyntax body)
        {
            return;
        }

        queryBuilder.GroupBy(ExtractAliasFromExpression(body), ExtractMemberName(body));

        if (node.Parent is MemberAccessExpressionSyntax ma && ma.Parent is InvocationExpressionSyntax wInv && ma.Name.Identifier.Text == "Where")
        {
            HandleHaving(wInv);
        }
    }

    private void HandleHaving(InvocationExpressionSyntax node)
    {
        var arg = node.ArgumentList.Arguments.FirstOrDefault();
        if (arg?.Expression is not SimpleLambdaExpressionSyntax lambda)
        {
            return;
        }

        if (lambda.Body is not BinaryExpressionSyntax binary)
        {
            return;
        }

        bool lOk = TryParseAggOperand(binary.Left, out var lTable, out var lProp, out var lFunc, out var lConst);
        bool rOk = TryParseAggOperand(binary.Right, out var rTable, out var rProp, out var rFunc, out var rConst);
        if (!lOk || !rOk)
        {
            return;
        }

        queryBuilder.Having(lTable, lProp, lConst, lFunc, MapOperator(binary.Kind()), rTable, rProp, rConst, rFunc);
    }

    private static BooleanOperator MapOperator(SyntaxKind k) => k switch
    {
        SyntaxKind.EqualsExpression => BooleanOperator.Equal,
        SyntaxKind.NotEqualsExpression => BooleanOperator.NotEqual,
        SyntaxKind.GreaterThanExpression => BooleanOperator.GreaterThan,
        SyntaxKind.GreaterThanOrEqualExpression => BooleanOperator.GreaterThanOrEqual,
        SyntaxKind.LessThanExpression => BooleanOperator.LessThan,
        SyntaxKind.LessThanOrEqualExpression => BooleanOperator.LessThanOrEqual,
        _ => BooleanOperator.Equal
    };

    private static bool TryParseOperand(ExpressionSyntax expr, out string? table, out string? prop, out string? constant)
    {
        table = prop = constant = null;
        switch (expr)
        {
            case MemberAccessExpressionSyntax m when m.Expression is IdentifierNameSyntax id:
                table = id.Identifier.Text;
                prop = m.Name.Identifier.Text;
                constant = null;
                return true;
            case LiteralExpressionSyntax lit:
                table = null;
                prop = null;
                constant = lit.Token.Text;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseAggOperand(ExpressionSyntax expr, out string? tbl, out string? prop, out string? func, out string? constant)
    {
        tbl = prop = func = constant = null;
        if (expr is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax mac)
        {
            func = mac.Name.Identifier.Text.ToUpperInvariant();
            if (inv.ArgumentList.Arguments.First().Expression is SimpleLambdaExpressionSyntax lam && lam.Body is ExpressionSyntax body)
            {
                tbl = ExtractAliasFromExpression(body);
                prop = ExtractMemberName(body);
                return true;
            }
        }
        else if (TryParseOperand(expr, out tbl, out prop, out constant))
        {
            return true;
        }
        return false;
    }

    private static string ExtractMemberName(ExpressionSyntax expr) => expr switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        _ => throw new NotSupportedException()
    };

    private static string ExtractAliasFromExpression(ExpressionSyntax expr) => expr switch
    {
        MemberAccessExpressionSyntax m when m.Expression is IdentifierNameSyntax id => id.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => "t"
    };

    private static string ExtractAliasFromPrev(InvocationExpressionSyntax node)
        => node.Expression is MemberAccessExpressionSyntax m && m.Expression is IdentifierNameSyntax id ? id.Identifier.Text : "t";

    private static string ResolveTableName(ExpressionSyntax src) => src switch
    {
        MemberAccessExpressionSyntax m when m.Name is IdentifierNameSyntax id => id.Identifier.Text,
        IdentifierNameSyntax id => id.Identifier.Text,
        _ => "unknown_table"
    };

    private static string ExtractIdentifier(TypeSyntax typeSyntax) => typeSyntax switch
    {
        IdentifierNameSyntax ins => ins.Identifier.Text,
        QualifiedNameSyntax qns => qns.Right.Identifier.Text,
        GenericNameSyntax gns => gns.Identifier.Text,
        PredefinedTypeSyntax pds => pds.Keyword.ValueText,
        _ => typeSyntax.ToString()
    };
}
