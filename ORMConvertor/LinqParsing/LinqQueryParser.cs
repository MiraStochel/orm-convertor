using System.Globalization;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace LinqParsing;

/// <summary>
/// Reads a LINQ query written in C# into the query IR (decision 026). Everything here is a
/// property of <c>System.Linq</c> rather than of any ORM: the same Where, Join, Select,
/// OrderBy and GroupBy mean the same thing under either provider. What differs between
/// frameworks is how the chain starts, and that is the one thing a subclass supplies.
///
/// The chain is decomposed explicitly, head to tail, the way the paper's Algorithm 2
/// describes. An earlier version rode on the visit order of a syntax walker, which made the
/// head-to-tail order accidental and turned "a Where directly after a GroupBy is a HAVING"
/// into a special case that had to look back up the tree.
/// </summary>
public abstract class LinqQueryParser(AbstractQueryBuilder queryBuilder) : IQueryParser
{
    protected readonly AbstractQueryBuilder queryBuilder = queryBuilder;

    private IReadOnlyList<EntityMap>? entityMaps;
    private string sourceAlias = "t";

    public bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.CSharpQuery;

    /// <summary>
    /// The content type is not consulted here: LINQ is the only language this parser claims
    /// (see CanParse), so there is nothing to branch on. It is in the signature because the
    /// unit declares its language and a parser reading two of them - the HQL one to come -
    /// has to be told which (decision 047).
    /// </summary>
    public void Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? maps = null)
    {
        entityMaps = maps;

        var tree = CSharpSyntaxTree.ParseText(Wrap(source));
        var root = tree.GetCompilationUnitRoot();

        // Outer nodes come before inner ones in document order, so the first invocation that
        // decomposes to a query root is the outermost link of the chain. A root appearing
        // inside a Join argument therefore cannot be mistaken for the query's own.
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (TryDecompose(invocation, out var queryRoot, out var steps))
            {
                EmitChain(queryRoot!, steps);
                return;
            }
        }

        queryBuilder.Push();
        Report(
            ConversionRecordKind.Failure,
            "No LINQ query chain was found in the source; nothing was translated.");
        queryBuilder.Pop();
    }

    /// <summary>
    /// Recognises the head of the chain — <c>ctx.Customers</c>, <c>ctx.Set&lt;T&gt;()</c>,
    /// <c>session.Query&lt;T&gt;()</c> — and says what it names.
    /// </summary>
    protected abstract bool TryReadQueryRoot(ExpressionSyntax expression, out LinqQueryRoot? root);

    private static string Wrap(string source) =>
        "using System;\n" +
        "using System.Linq;\n" +
        "using System.Collections.Generic;\n" +
        "\n" +
        "public class Snippet\n" +
        "{\n" +
        source +
        "\n}\n";

    private sealed record ChainStep(string Name, InvocationExpressionSyntax Node);

    private bool TryDecompose(
        ExpressionSyntax expression,
        out LinqQueryRoot? root,
        out List<ChainStep> steps)
    {
        steps = [];
        var current = expression;

        while (true)
        {
            if (TryReadQueryRoot(current, out root))
            {
                steps.Reverse();
                return true;
            }

            if (current is InvocationExpressionSyntax invocation
                && invocation.Expression is MemberAccessExpressionSyntax member)
            {
                steps.Add(new ChainStep(member.Name.Identifier.Text, invocation));
                current = member.Expression;
                continue;
            }

            root = null;
            return false;
        }
    }

    private void EmitSource(LinqQueryRoot root, string? elementParameter)
    {
        sourceAlias = elementParameter
            ?? (root.Name.Length > 0 ? root.Name[..1].ToLowerInvariant() : "t");
        queryBuilder.From(ResolveTable(root.Name), sourceAlias);
    }

    /// <summary>
    /// The parameter name of the chain's first lambda that ranges over source elements. The
    /// operand qualifiers read from lambda bodies are these names, so the source alias has
    /// to be the same name; the table's first letter is only the fallback for a chain that
    /// opens without such a lambda. Deriving the alias from the table alone broke exactly
    /// where C# forbids a nested lambda from reusing the enclosing parameter (decision 061):
    /// a subquery over the outer query's own table would have claimed the outer alias and
    /// with it the correlated references.
    /// </summary>
    private static string? FirstElementLambdaParameter(List<ChainStep> steps)
    {
        if (steps.Count == 0
            || steps[0].Name is not ("Where" or "Select" or "OrderBy" or "OrderByDescending" or "GroupBy"))
        {
            return null;
        }

        return steps[0].Node.ArgumentList.Arguments.FirstOrDefault()?.Expression
            is SimpleLambdaExpressionSyntax lambda
            ? lambda.Parameter.Identifier.Text
            : null;
    }

    /// <summary>
    /// Emits one whole chain into its own subquery scope. A set-operation step closes the
    /// scope as the left operand, arms the operation and reads its argument as a chain of its
    /// own, recursively (rule Q12); whatever follows a set operation applies to the composed
    /// result, for which the representation has no slot, so it is reported instead of
    /// emitted - as a failure where dropping it would change which rows come back
    /// (decision 053), as a loss elsewhere.
    /// </summary>
    private void EmitChain(LinqQueryRoot root, List<ChainStep> steps, Action? beforeClose = null)
    {
        queryBuilder.Push();
        EmitSource(root, FirstElementLambdaParameter(steps));

        bool inSetOperation = false;
        long? pendingOffset = null;
        long? pendingLimit = null;

        // Pagination is recorded when the scope closes, so that Skip and Take collected
        // along the chain end up as one instruction in offset-then-limit normal form
        // (decision 060).
        void FlushPagination()
        {
            queryBuilder.Paginate(pendingOffset, pendingLimit);
            pendingOffset = pendingLimit = null;
        }

        void HandleSkipOrTake(ChainStep step)
        {
            if (step.Name == "Skip" && pendingLimit is not null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    "Skip() after Take() slices differently from the offset-then-limit form the query representation carries; no artifact was generated.",
                    QueryFeature.Pagination);
                return;
            }

            if ((step.Name == "Skip" ? pendingOffset : pendingLimit) is not null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"A repeated {step.Name}() has no counterpart in the query representation; no artifact was generated.",
                    QueryFeature.Pagination);
                return;
            }

            var argument = step.Node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (argument is not LiteralExpressionSyntax literal || literal.Token.Value is not int value || value < 0)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The argument of {step.Name}() is not a non-negative integer literal, and a pagination the artifact does not carry would change which rows the query returns; no artifact was generated.",
                    QueryFeature.Pagination);
                return;
            }

            if (step.Name == "Skip")
            {
                pendingOffset = value;
            }
            else
            {
                pendingLimit = value;
            }
        }

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (TryMapSetOperation(step.Name, out var operation))
            {
                var argument = step.Node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (argument is null || !TryDecompose(argument, out var innerRoot, out var innerSteps))
                {
                    Report(
                        ConversionRecordKind.Failure,
                        $"The argument of {step.Name}() is not a query chain, so the set operation could not be read; no artifact was generated.",
                        QueryFeature.SetOperation);
                    continue;
                }

                if (!inSetOperation)
                {
                    FlushPagination();
                    queryBuilder.Pop();
                }

                queryBuilder.SetOperation(operation);
                queryBuilder.Push();
                EmitChain(innerRoot!, innerSteps);
                queryBuilder.Pop();
                inSetOperation = true;
                continue;
            }

            if (inSetOperation)
            {
                ReportStepAfterSetOperation(step);
                continue;
            }

            if (step.Name is "Skip" or "Take")
            {
                HandleSkipOrTake(step);
                continue;
            }

            // The representation carries pagination as the last relational operation, so a
            // step that does not commute with the slice cannot follow it: a filter moved in
            // front of the slice selects different rows (decision 060). Projection and
            // materialization commute and pass.
            if ((pendingOffset is not null || pendingLimit is not null) && !CommutesWithPagination(step.Name))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Name}() after Skip() or Take() does not commute with the slice, which the query representation carries only as the last operation; no artifact was generated.",
                    QueryFeature.Pagination);
                continue;
            }

            EmitStep(step, followsGrouping: i > 0 && steps[i - 1].Name == "GroupBy");
        }

        if (!inSetOperation)
        {
            beforeClose?.Invoke();
            FlushPagination();
            queryBuilder.Pop();
        }
        else if (beforeClose is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                "An aggregate over a set operation cannot be carried as a subquery; no artifact was generated.",
                QueryFeature.Subquery);
        }
    }

    private static bool CommutesWithPagination(string method) => method is
        "Select" or "ToList" or "ToArray" or "ToListAsync" or "ToArrayAsync"
        or "AsQueryable" or "AsNoTracking" or "AsNoTrackingWithIdentityResolution";

    private static bool TryMapSetOperation(string method, out SetOperationType operation)
    {
        switch (method)
        {
            case "Union": operation = SetOperationType.Union; return true;
            case "Concat": operation = SetOperationType.UnionAll; return true;
            case "Intersect": operation = SetOperationType.Intersect; return true;
            case "Except": operation = SetOperationType.Except; return true;
            default: operation = default; return false;
        }
    }

    private void ReportStepAfterSetOperation(ChainStep step)
    {
        switch (step.Name)
        {
            // Materialisation and tracking say nothing about the query's structure.
            case "ToList":
            case "ToArray":
            case "ToListAsync":
            case "ToArrayAsync":
            case "AsQueryable":
            case "AsNoTracking":
            case "AsNoTrackingWithIdentityResolution":
                return;

            case "OrderBy":
            case "OrderByDescending":
            case "ThenBy":
            case "ThenByDescending":
                Report(
                    ConversionRecordKind.Loss,
                    "An ordering applied after a set operation has no place in the query representation; it was dropped.",
                    QueryFeature.Ordering);
                return;

            case "Take":
            case "Skip":
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Name}() applied after a set operation cannot be carried, and dropping it would change which rows the query returns; no artifact was generated.",
                    QueryFeature.Pagination);
                return;

            case "Select":
            case "Distinct":
                Report(
                    ConversionRecordKind.Loss,
                    $"{step.Name}() applied after a set operation has no place in the query representation; the operands' own shape is kept.",
                    QueryFeature.Projection);
                return;

            case "Where":
            case "Join":
            case "LeftJoin":
            case "RightJoin":
            case "GroupBy":
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Name}() applied after a set operation cannot be carried, and dropping it would change which rows the query returns; no artifact was generated.",
                    step.Name switch
                    {
                        "Where" => QueryFeature.Filtering,
                        "GroupBy" => QueryFeature.Grouping,
                        _ => QueryFeature.Join,
                    });
                return;

            default:
                ReportUnsupported(step.Name, null);
                return;
        }
    }

    private void EmitStep(ChainStep step, bool followsGrouping)
    {
        switch (step.Name)
        {
            // A Where directly after a GroupBy filters the groups, which is HAVING.
            case "Where" when followsGrouping:
                HandleHaving(step.Node);
                break;
            case "Where":
                HandleWhere(step.Node);
                break;

            case "Join":
                HandleJoin(step.Node, JoinKind.Inner);
                break;

            // EF Core 10 added explicit outer joins; before it, LINQ could only express
            // an inner join directly.
            case "LeftJoin":
                HandleJoin(step.Node, JoinKind.Left);
                break;
            case "RightJoin":
                HandleJoin(step.Node, JoinKind.Right);
                break;

            case "Select":
                HandleSelect(step.Node);
                break;

            case "OrderBy":
            case "ThenBy":
                HandleOrderBy(step.Node, asc: true);
                break;
            case "OrderByDescending":
            case "ThenByDescending":
                HandleOrderBy(step.Node, asc: false);
                break;

            case "GroupBy":
                HandleGroupBy(step.Node);
                break;

            // Materialisation and tracking say nothing about the query's structure.
            case "ToList":
            case "ToArray":
            case "ToListAsync":
            case "ToArrayAsync":
            case "AsQueryable":
            case "AsNoTracking":
            case "AsNoTrackingWithIdentityResolution":
                break;

            case "Distinct":
                ReportUnsupported(step.Name, QueryFeature.Projection);
                break;

            default:
                ReportUnsupported(step.Name, null);
                break;
        }
    }

    private void ReportUnsupported(string method, QueryFeature? feature)
        => Report(
            ConversionRecordKind.Loss,
            $"The query calls {method}(), which the query representation does not carry; the output is poorer than the input.",
            feature);

    private void HandleWhere(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(ConversionRecordKind.Loss, "A Where() argument was not a lambda and was dropped.", QueryFeature.Filtering);
            return;
        }

        var condition = ParseCondition(body!);
        if (condition is null)
        {
            // Silence here used to lose the whole predicate. F11 forbids exactly that.
            Report(
                ConversionRecordKind.Loss,
                $"The predicate '{body}' uses a construct the condition tree cannot carry, so the whole filter was dropped.",
                QueryFeature.Filtering);
            return;
        }

        queryBuilder.Where(condition);
    }

    private void HandleJoin(InvocationExpressionSyntax node, JoinKind kind)
    {
        var args = node.ArgumentList.Arguments;
        if (args.Count < 3)
        {
            Report(ConversionRecordKind.Loss, "A join with too few arguments was dropped.", QueryFeature.Join);
            return;
        }

        string rightTable = ResolveTable(NameOfSource(args[0].Expression));
        string rightAlias = (rightTable.Split('.').LastOrDefault() ?? rightTable).ToLowerInvariant();

        if (args[1].Expression is not SimpleLambdaExpressionSyntax outer
            || args[2].Expression is not SimpleLambdaExpressionSyntax inner
            || outer.Body is not ExpressionSyntax outerBody
            || inner.Body is not ExpressionSyntax innerBody)
        {
            Report(ConversionRecordKind.Loss, "A join whose key selectors are not lambdas was dropped.", QueryFeature.Join);
            return;
        }

        var onCondition = BuildJoinCondition(outerBody, innerBody, sourceAlias, rightAlias);
        if (onCondition is null)
        {
            Report(
                ConversionRecordKind.Loss,
                "A join whose key selectors do not pair up column for column was dropped.",
                QueryFeature.Join);
            return;
        }

        queryBuilder.Join(kind, sourceAlias, rightTable, onCondition, rightAlias);
    }

    /// <summary>
    /// Simple keys (ol =&gt; ol.OrderId) yield one equality; composite keys expressed with
    /// anonymous types are paired positionally into an AND of several equalities.
    /// </summary>
    private ConditionNode? BuildJoinCondition(
        ExpressionSyntax outerBody,
        ExpressionSyntax innerBody,
        string leftAlias,
        string rightAlias)
    {
        if (outerBody is AnonymousObjectCreationExpressionSyntax outerAnon
            && innerBody is AnonymousObjectCreationExpressionSyntax innerAnon)
        {
            if (outerAnon.Initializers.Count == 0
                || outerAnon.Initializers.Count != innerAnon.Initializers.Count)
            {
                return null;
            }

            var equalities = new List<ConditionNode>();
            for (int i = 0; i < outerAnon.Initializers.Count; i++)
            {
                var left = MemberName(outerAnon.Initializers[i].Expression);
                var right = MemberName(innerAnon.Initializers[i].Expression);
                if (left is null || right is null)
                {
                    return null;
                }

                equalities.Add(new ComparisonCondition(
                    QueryOperand.Column(leftAlias, left),
                    ComparisonOperator.Equal,
                    QueryOperand.Column(rightAlias, right)));
            }

            return equalities.Count == 1
                ? equalities[0]
                : new LogicalCondition(LogicalOperator.And, equalities);
        }

        var outerName = MemberName(outerBody);
        var innerName = MemberName(innerBody);
        if (outerName is null || innerName is null)
        {
            return null;
        }

        return new ComparisonCondition(
            QueryOperand.Column(leftAlias, outerName),
            ComparisonOperator.Equal,
            QueryOperand.Column(rightAlias, innerName));
    }

    private void HandleSelect(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(ConversionRecordKind.Loss, "A Select() argument was not a lambda and was dropped.", QueryFeature.Projection);
            return;
        }

        switch (body)
        {
            // Select(c => c) materializes the whole entity: rule Q3's default, so no
            // projection instruction is recorded.
            case IdentifierNameSyntax:
                return;

            case AnonymousObjectCreationExpressionSyntax anon:
                foreach (var initializer in anon.Initializers)
                {
                    EmitProjection(
                        initializer.Expression,
                        initializer.NameEquals?.Name.Identifier.Text ?? MemberName(initializer.Expression));
                }

                return;

            // Select(c => c.Name) is a projection of one column - the commonest shape there
            // is, and one an earlier version dropped entirely.
            case MemberAccessExpressionSyntax member:
                EmitProjection(member, MemberName(member));
                return;

            default:
                Report(
                    ConversionRecordKind.Loss,
                    $"The projection '{body}' is not a shape the query representation carries; the whole entity is materialized instead.",
                    QueryFeature.Projection);
                return;
        }
    }

    private void EmitProjection(ExpressionSyntax expression, string? alias)
    {
        if (TryReadAggregate(expression, out var function, out var table, out var attribute))
        {
            queryBuilder.Project(table ?? sourceAlias, attribute!, alias, function);
            return;
        }

        var name = MemberName(expression);
        if (name is null)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The projected expression '{expression}' is not a column reference and was dropped.",
                QueryFeature.Projection);
            return;
        }

        queryBuilder.Project(AliasOf(expression), name, alias);
    }

    private void HandleOrderBy(InvocationExpressionSyntax node, bool asc)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(ConversionRecordKind.Loss, "An ordering argument was not a lambda and was dropped.", QueryFeature.Ordering);
            return;
        }

        var name = MemberName(body!);
        if (name is null)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The ordering key '{body}' is not a column reference and was dropped.",
                QueryFeature.Ordering);
            return;
        }

        queryBuilder.OrderBy(AliasOf(body!), name, asc);
    }

    private void HandleGroupBy(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(ConversionRecordKind.Loss, "A GroupBy() argument was not a lambda and was dropped.", QueryFeature.Grouping);
            return;
        }

        if (body is AnonymousObjectCreationExpressionSyntax anon)
        {
            foreach (var initializer in anon.Initializers)
            {
                var key = MemberName(initializer.Expression);
                if (key is not null)
                {
                    queryBuilder.GroupBy(AliasOf(initializer.Expression), key);
                }
            }

            return;
        }

        var name = MemberName(body!);
        if (name is null)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The grouping key '{body}' is not a column reference and was dropped.",
                QueryFeature.Grouping);
            return;
        }

        queryBuilder.GroupBy(AliasOf(body!), name);
    }

    private void HandleHaving(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body) || body is not BinaryExpressionSyntax binary)
        {
            Report(
                ConversionRecordKind.Loss,
                "A post-aggregation filter that is not a simple comparison was dropped.",
                QueryFeature.PostAggregationFiltering);
            return;
        }

        var op = MapOperator(binary.Kind());
        var left = ReadHavingOperand(binary.Left);
        var right = ReadHavingOperand(binary.Right);

        if (op is null || left is null || right is null)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The post-aggregation filter '{binary}' uses a construct the condition tree cannot carry and was dropped.",
                QueryFeature.PostAggregationFiltering);
            return;
        }

        queryBuilder.Having(new ComparisonCondition(left, op.Value, right));
    }

    private QueryOperand? ReadHavingOperand(ExpressionSyntax expression)
    {
        if (TryReadAggregate(expression, out var function, out var table, out var attribute))
        {
            return QueryOperand.Column(table ?? sourceAlias, attribute!, function);
        }

        return ReadOperand(expression);
    }

    /// <summary>
    /// Reads g.Sum(x =&gt; x.Total), g.Count() and their kin. The element the lambda ranges
    /// over is a row of the source, so its columns are qualified by the source alias rather
    /// than by the lambda's own parameter name, which is not a table alias at all.
    /// </summary>
    private bool TryReadAggregate(
        ExpressionSyntax expression,
        out string? function,
        out string? table,
        out string? attribute)
    {
        function = table = attribute = null;

        // The receiver has to be a bare identifier - the grouping's own lambda parameter.
        // An aggregate whose receiver is a chain is a scalar subquery, not a group
        // aggregate, and belongs to ReadScalarSubQuery (decision 061).
        if (expression is not InvocationExpressionSyntax invocation
            || invocation.Expression is not MemberAccessExpressionSyntax member
            || member.Expression is not IdentifierNameSyntax)
        {
            return false;
        }

        var name = member.Name.Identifier.Text.ToUpperInvariant();
        if (name is not ("COUNT" or "SUM" or "MIN" or "MAX" or "AVG"))
        {
            return false;
        }

        function = name;
        table = sourceAlias;

        var argument = invocation.ArgumentList.Arguments.FirstOrDefault();
        if (argument is null)
        {
            // g.Count() counts rows, not a column.
            attribute = "*";
            return true;
        }

        if (argument.Expression is SimpleLambdaExpressionSyntax lambda
            && lambda.Body is ExpressionSyntax lambdaBody
            && MemberName(lambdaBody) is { } column)
        {
            attribute = column;
            return true;
        }

        function = table = null;
        return false;
    }

    private ConditionNode? ParseCondition(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                return ParseCondition(parenthesized.Expression);

            case PrefixUnaryExpressionSyntax unary when unary.IsKind(SyntaxKind.LogicalNotExpression):
                {
                    var operand = ParseCondition(unary.Operand);
                    return operand is null ? null : new NotCondition(operand);
                }

            case BinaryExpressionSyntax logical when logical.IsKind(SyntaxKind.LogicalAndExpression)
                                                  || logical.IsKind(SyntaxKind.LogicalOrExpression):
                {
                    var op = logical.IsKind(SyntaxKind.LogicalAndExpression)
                        ? LogicalOperator.And
                        : LogicalOperator.Or;

                    var left = ParseCondition(logical.Left);
                    var right = ParseCondition(logical.Right);
                    if (left is null || right is null)
                    {
                        return null;
                    }

                    // Chains of the same operator (a && b && c) are flattened into one node.
                    var operands = new List<ConditionNode>();
                    Flatten(left, op, operands);
                    Flatten(right, op, operands);
                    return new LogicalCondition(op, operands);
                }

            case BinaryExpressionSyntax comparison:
                return ParseComparison(comparison);

            case InvocationExpressionSyntax invocation:
                return ReadSubQueryCondition(invocation);

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads Contains and Any over a query root as a subquery condition (decision 061):
    /// <c>chain.Select(x =&gt; x.Col).Contains(value)</c> is IN,
    /// <c>chain.Any()</c> is EXISTS and <c>chain.Any(predicate)</c> is
    /// <c>Where(predicate).Any()</c> - the Any invocation itself has exactly the one-lambda
    /// shape the Where handler reads. A Contains whose receiver is not a query chain - a
    /// local collection, a string - is no subquery and stays unread.
    /// </summary>
    private ConditionNode? ReadSubQueryCondition(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        switch (member.Name.Identifier.Text)
        {
            case "Contains":
                {
                    if (!TryDecompose(member.Expression, out var root, out var steps))
                    {
                        return null;
                    }

                    var value = invocation.ArgumentList.Arguments.Count == 1
                        ? ReadOperand(invocation.ArgumentList.Arguments[0].Expression)
                        : null;
                    if (value is null)
                    {
                        return null;
                    }

                    var sub = ReadSubQueryOperand(root!, steps);
                    return new ComparisonCondition(value, ComparisonOperator.In, QueryOperand.Nested(sub));
                }

            case "Any":
                {
                    if (!TryDecompose(member.Expression, out var root, out var steps))
                    {
                        return null;
                    }

                    if (invocation.ArgumentList.Arguments.Count == 1)
                    {
                        steps.Add(new ChainStep("Where", invocation));
                    }

                    var sub = ReadSubQueryOperand(root!, steps);
                    return new ComparisonCondition(QueryOperand.Nested(sub), ComparisonOperator.Exists);
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads a nested chain into a subquery operand (decision 061). The scope is closed
    /// with PopOperand, so its instructions become the operand's body rather than
    /// instructions of the enclosing query, and the enclosing source alias survives the
    /// nested source step.
    /// </summary>
    private SubQueryInstruction ReadSubQueryOperand(
        LinqQueryRoot root,
        List<ChainStep> steps,
        Action? beforeClose = null)
    {
        var enclosingAlias = sourceAlias;

        queryBuilder.Push();
        EmitChain(root, steps, beforeClose);
        sourceAlias = enclosingAlias;

        return queryBuilder.PopOperand();
    }

    private static void Flatten(ConditionNode node, LogicalOperator op, List<ConditionNode> into)
    {
        if (node is LogicalCondition logical && logical.Operator == op)
        {
            into.AddRange(logical.Operands);
            return;
        }

        into.Add(node);
    }

    private ConditionNode? ParseComparison(BinaryExpressionSyntax binary)
    {
        var op = MapOperator(binary.Kind());
        if (op is null)
        {
            return null;
        }

        bool leftIsNull = IsNullLiteral(binary.Left);
        bool rightIsNull = IsNullLiteral(binary.Right);

        if (leftIsNull && rightIsNull)
        {
            return null;
        }

        // A comparison with a null literal is normalized to IS NULL / IS NOT NULL
        // (decision 002).
        if (leftIsNull || rightIsNull)
        {
            if (op is not (ComparisonOperator.Equal or ComparisonOperator.NotEqual))
            {
                return null;
            }

            var operand = ReadOperand(leftIsNull ? binary.Right : binary.Left);
            if (operand is null)
            {
                return null;
            }

            return new ComparisonCondition(
                operand,
                op == ComparisonOperator.Equal ? ComparisonOperator.IsNull : ComparisonOperator.IsNotNull);
        }

        var left = ReadOperand(binary.Left);
        var right = ReadOperand(binary.Right);
        if (left is null || right is null)
        {
            return null;
        }

        return new ComparisonCondition(left, op.Value, right);
    }

    private QueryOperand? ReadOperand(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax identifier
            => QueryOperand.Column(identifier.Identifier.Text, member.Name.Identifier.Text),
        LiteralExpressionSyntax literal => QueryOperand.Value(ReadConstant(literal)),
        PrefixUnaryExpressionSyntax negation when negation.IsKind(SyntaxKind.UnaryMinusExpression)
            && negation.Operand is LiteralExpressionSyntax inner
            => QueryOperand.Value(Negate(ReadConstant(inner))),
        InvocationExpressionSyntax invocation when ReadScalarSubQuery(invocation) is { } nested => nested,
        _ => null,
    };

    /// <summary>
    /// Reads a terminal aggregate over a query root - <c>ctx.Set&lt;T&gt;().Max(x =&gt;
    /// x.Total)</c> - as a scalar subquery operand (decision 061): the aggregate becomes the
    /// subquery's own projection, recorded just before the nested scope closes so that it
    /// carries the nested source's alias. Count(predicate) folds its predicate into a Where
    /// the way Any(predicate) does.
    /// </summary>
    private QueryOperand? ReadScalarSubQuery(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var function = member.Name.Identifier.Text switch
        {
            "Max" => "MAX",
            "Min" => "MIN",
            "Sum" => "SUM",
            "Average" => "AVG",
            "Count" => "COUNT",
            _ => null,
        };

        if (function is null || !TryDecompose(member.Expression, out var root, out var steps))
        {
            return null;
        }

        string attribute;
        var argument = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
        if (argument is null)
        {
            // Count() counts rows; every other aggregate needs a column to range over.
            if (function != "COUNT")
            {
                return null;
            }

            attribute = "*";
        }
        else if (function == "COUNT" && argument is SimpleLambdaExpressionSyntax)
        {
            steps.Add(new ChainStep("Where", invocation));
            attribute = "*";
        }
        else if (argument is SimpleLambdaExpressionSyntax lambda
            && lambda.Body is ExpressionSyntax body
            && MemberName(body) is { } column)
        {
            attribute = column;
        }
        else
        {
            return null;
        }

        var sub = ReadSubQueryOperand(
            root!,
            steps,
            () => queryBuilder.Project(sourceAlias, attribute, null, function));

        return QueryOperand.Nested(sub);
    }

    /// <summary>
    /// Turns a C# literal into a typed constant (decision 024). The token's own value
    /// carries the type, so 2000m arrives as the decimal 2000 and leaves the suffix behind
    /// in the source where it belongs.
    /// </summary>
    private QueryConstant ReadConstant(LiteralExpressionSyntax literal)
    {
        var value = literal.Token.Value;

        switch (value)
        {
            case string text: return QueryConstant.Of(text, ScalarType.String);
            case char character: return QueryConstant.Of(character.ToString(), ScalarType.Char);
            case bool flag: return QueryConstant.Of(flag ? "true" : "false", ScalarType.Bool);
            case int number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Int);
            case long number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Long);
            case decimal number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Decimal);
            case double number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Double);
            case float number: return QueryConstant.Of(number.ToString(CultureInfo.InvariantCulture), ScalarType.Float);
        }

        Report(
            ConversionRecordKind.Incompleteness,
            $"The literal '{literal}' has no counterpart in the scalar vocabulary; it is carried verbatim.",
            QueryFeature.Filtering);

        return QueryConstant.Unrecognized(literal.Token.ValueText);
    }

    private static QueryConstant Negate(QueryConstant constant)
        => constant.Type is null
            ? QueryConstant.Unrecognized("-" + constant.Text)
            : QueryConstant.Of("-" + constant.Text, constant.Type.Value);

    private static bool IsNullLiteral(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NullLiteralExpression);

    private static ComparisonOperator? MapOperator(SyntaxKind kind) => kind switch
    {
        SyntaxKind.EqualsExpression => ComparisonOperator.Equal,
        SyntaxKind.NotEqualsExpression => ComparisonOperator.NotEqual,
        SyntaxKind.GreaterThanExpression => ComparisonOperator.GreaterThan,
        SyntaxKind.GreaterThanOrEqualExpression => ComparisonOperator.GreaterThanOrEqual,
        SyntaxKind.LessThanExpression => ComparisonOperator.LessThan,
        SyntaxKind.LessThanOrEqualExpression => ComparisonOperator.LessThanOrEqual,
        _ => null,
    };

    private static bool TryReadLambdaBody(InvocationExpressionSyntax node, out ExpressionSyntax? body)
    {
        body = null;

        if (node.ArgumentList.Arguments.FirstOrDefault()?.Expression is SimpleLambdaExpressionSyntax lambda
            && lambda.Body is ExpressionSyntax lambdaBody)
        {
            body = lambdaBody;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Member name of a column reference, or null when the expression is not one. Returning
    /// null rather than throwing is the point: an unsupported shape is a record, never an
    /// exception escaping to the caller.
    /// </summary>
    private static string? MemberName(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => null,
    };

    private string AliasOf(ExpressionSyntax expression) => expression switch
    {
        MemberAccessExpressionSyntax member when member.Expression is IdentifierNameSyntax identifier
            => identifier.Identifier.Text,
        _ => sourceAlias,
    };

    private static string NameOfSource(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax invocation when invocation.Expression is MemberAccessExpressionSyntax member
            => TypeArgumentOf(member.Name) ?? member.Name.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        _ => "unknown_table",
    };

    /// <summary>
    /// Resolves the name the source wrote — a DbSet name or an entity name — to a qualified
    /// table, using the mapping IR built by the entity parsers (rule Q2).
    /// </summary>
    private string ResolveTable(string name)
    {
        if (entityMaps is { Count: > 0 })
        {
            var byTable = entityMaps.FirstOrDefault(m =>
                string.Equals(m.Table, name, StringComparison.OrdinalIgnoreCase));
            if (byTable is not null)
            {
                return Qualify(byTable, byTable.Table ?? name);
            }

            var byEntity = entityMaps.FirstOrDefault(m =>
                string.Equals(m.Entity?.Name, name, StringComparison.OrdinalIgnoreCase));
            if (byEntity is not null)
            {
                return Qualify(byEntity, byEntity.Table ?? name);
            }

            var byPlural = entityMaps.FirstOrDefault(m =>
                string.Equals((m.Entity?.Name ?? string.Empty) + "s", name, StringComparison.OrdinalIgnoreCase));
            if (byPlural is not null)
            {
                return Qualify(byPlural, byPlural.Table ?? name);
            }
        }

        return name;
    }

    private static string Qualify(EntityMap map, string table)
        => string.IsNullOrWhiteSpace(map.Schema) ? table : $"{map.Schema}.{table}";

    protected static string? TypeArgumentOf(SimpleNameSyntax name)
        => name is GenericNameSyntax generic && generic.TypeArgumentList.Arguments.Count > 0
            ? generic.TypeArgumentList.Arguments[0] switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                GenericNameSyntax nested => nested.Identifier.Text,
                var other => other.ToString(),
            }
            : null;

    private void Report(ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => queryBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = queryBuilder.Descriptor.Framework,
            Artifact = ConversionContentType.CSharpQuery,
            Feature = feature,
            Reason = reason,
        });
}
