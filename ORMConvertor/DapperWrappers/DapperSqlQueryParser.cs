using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using ScriptDomLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DapperWrappers;

/// <summary>
/// Reads a Dapper query. Two stages, because a Dapper query in the wild is T-SQL inside C#:
/// Roslyn finds the Query&lt;T&gt; call and takes its string literal, then a T-SQL parser
/// turns that string into instructions (decision 026). A bare SQL source skips the first
/// stage.
///
/// The T-SQL parser is a parser of a <em>language</em>, exactly as Roslyn is for C#. It is
/// not a dependency on Dapper, which is what S1 forbids.
/// </summary>
public class DapperSqlQueryParser(AbstractQueryBuilder queryBuilder) : IQueryParser
{
    private static readonly string[] DapperMethods =
    [
        "Query", "QueryAsync",
        "QueryFirst", "QueryFirstAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "ExecuteScalar", "ExecuteScalarAsync",
    ];

    private string sourceAlias = "t";

    public bool CanParse(ConversionContentType contentType) => contentType is
        ConversionContentType.SqlQuery or ConversionContentType.CSharpQuery;

    public void Parse(string source) => Parse(source, null);

    public void Parse(string source, IReadOnlyList<EntityMap>? entityMaps)
    {
        var sql = LooksLikeCSharp(source) ? ExtractSql(source) : source;
        if (sql is null)
        {
            return;
        }

        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(new StringReader(sql), out var errors);

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                // A parse error carries a line and a column, which is what S7 asks the UI to
                // show and what no other source of ours can give.
                Report(
                    ConversionRecordKind.Failure,
                    $"The SQL could not be parsed at line {error.Line}, column {error.Column}: {error.Message}");
            }

            return;
        }

        if (FindQuerySpecification(fragment) is not { } query)
        {
            Report(ConversionRecordKind.Failure, "The SQL contains no SELECT statement to translate.");
            return;
        }

        queryBuilder.Push();
        ReadFrom(query);
        ReadSelect(query);
        ReadWhere(query);
        ReadGroupBy(query);
        ReadHaving(query);
        ReadOrderBy(query);
        queryBuilder.Pop();
    }

    private static bool LooksLikeCSharp(string source)
        => source.Contains("Query", StringComparison.Ordinal)
           && (source.Contains('(') && source.Contains(';'));

    /// <summary>
    /// Pulls the SQL out of a Dapper call. The literal is read through the token's value, so
    /// verbatim strings, raw string literals and escapes have already been resolved by
    /// Roslyn rather than being unwound by hand.
    /// </summary>
    private string? ExtractSql(string source)
    {
        var tree = CSharpSyntaxTree.ParseText("public class Snippet\n{\n" + source + "\n}\n");
        var root = tree.GetCompilationUnitRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member
                || !DapperMethods.Contains(member.Name.Identifier.Text))
            {
                continue;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                // RawKind rather than IsKind: ScriptDom is in scope here too and the two
                // libraries each bring an extension of that name.
                if (argument.Expression is LiteralExpressionSyntax literal
                    && literal.RawKind == (int)SyntaxKind.StringLiteralExpression)
                {
                    return literal.Token.ValueText;
                }
            }

            Report(
                ConversionRecordKind.Incompleteness,
                "The Dapper call does not pass the SQL as a string literal, so the query could not be read.");
            return null;
        }

        Report(ConversionRecordKind.Failure, "No Dapper query call was found in the source.");
        return null;
    }

    private static QuerySpecification? FindQuerySpecification(TSqlFragment fragment)
    {
        // Navigated explicitly rather than with a visitor: a visitor descends into subqueries
        // too, and their instructions would then be emitted into the outer scope.
        if (fragment is not TSqlScript script)
        {
            return null;
        }

        foreach (var statement in script.Batches.SelectMany(b => b.Statements))
        {
            if (statement is SelectStatement select)
            {
                return Unwrap(select.QueryExpression);
            }
        }

        return null;
    }

    private static QuerySpecification? Unwrap(QueryExpression expression) => expression switch
    {
        QuerySpecification specification => specification,
        QueryParenthesisExpression parenthesis => Unwrap(parenthesis.QueryExpression),
        _ => null,
    };

    private void ReadFrom(QuerySpecification query)
    {
        var reference = query.FromClause?.TableReferences.FirstOrDefault();
        if (reference is null)
        {
            Report(ConversionRecordKind.Failure, "The SELECT has no FROM clause (rule Q2).");
            return;
        }

        if (query.FromClause!.TableReferences.Count > 1)
        {
            Report(
                ConversionRecordKind.Loss,
                "Comma-separated table references are a cross join the query representation cannot carry; only the first was read.",
                QueryFeature.Join);
        }

        var joins = new List<QualifiedJoin>();
        var current = reference;

        // A join tree leans left, so walking down the first reference reaches the base table
        // and unwinding it emits the joins in the order they were written.
        while (current is QualifiedJoin join)
        {
            joins.Add(join);
            current = join.FirstTableReference;
        }

        joins.Reverse();

        if (current is not NamedTableReference table)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The query source is a {current.GetType().Name}, which the query representation cannot carry.");
            return;
        }

        var (name, alias) = NameAndAlias(table);
        sourceAlias = alias;
        queryBuilder.From(name, alias);

        foreach (var join in joins)
        {
            ReadJoin(join);
        }
    }

    private void ReadJoin(QualifiedJoin join)
    {
        if (join.SecondTableReference is not NamedTableReference right)
        {
            Report(
                ConversionRecordKind.Loss,
                "A join onto something other than a table is not carried by the query representation; it was dropped.",
                QueryFeature.Join);
            return;
        }

        var kind = join.QualifiedJoinType switch
        {
            QualifiedJoinType.Inner => JoinKind.Inner,
            QualifiedJoinType.LeftOuter => JoinKind.Left,
            QualifiedJoinType.RightOuter => JoinKind.Right,
            _ => JoinKind.Full,
        };

        var condition = ReadCondition(join.SearchCondition);
        if (condition is null)
        {
            Report(
                ConversionRecordKind.Loss,
                "A join condition the condition tree cannot carry was dropped along with its join.",
                QueryFeature.Join);
            return;
        }

        var (name, alias) = NameAndAlias(right);
        queryBuilder.Join(kind, sourceAlias, name, condition, alias);
    }

    private static (string Name, string Alias) NameAndAlias(NamedTableReference table)
    {
        var schema = table.SchemaObject.SchemaIdentifier?.Value;
        var bare = table.SchemaObject.BaseIdentifier.Value;
        var name = schema is null ? bare : $"{schema}.{bare}";
        return (name, table.Alias?.Value ?? bare);
    }

    private void ReadSelect(QuerySpecification query)
    {
        foreach (var element in query.SelectElements)
        {
            // Rule Q3: SELECT * is the absence of a projection, not a projection of everything.
            if (element is SelectStarExpression)
            {
                continue;
            }

            if (element is not SelectScalarExpression scalar)
            {
                Report(
                    ConversionRecordKind.Loss,
                    "A select element that is not a scalar expression was dropped.",
                    QueryFeature.Projection);
                continue;
            }

            var alias = scalar.ColumnName?.Value;

            switch (scalar.Expression)
            {
                case ColumnReferenceExpression column when ReadColumn(column) is { } reference:
                    queryBuilder.Project(reference.Table ?? sourceAlias, reference.Column, alias);
                    break;

                case FunctionCall call:
                    ReadAggregateProjection(call, alias);
                    break;

                default:
                    Report(
                        ConversionRecordKind.Loss,
                        $"The projected expression '{Describe(scalar.Expression)}' is not a column or an aggregate and was dropped.",
                        QueryFeature.Projection);
                    break;
            }
        }
    }

    private void ReadAggregateProjection(FunctionCall call, string? alias)
    {
        var function = call.FunctionName.Value.ToUpperInvariant();
        var parameter = call.Parameters.FirstOrDefault();

        // COUNT(*) parses as a function whose single parameter is a star.
        if (parameter is null || call.UniqueRowFilter == UniqueRowFilter.NotSpecified && parameter is ColumnReferenceExpression { ColumnType: ColumnType.Wildcard })
        {
            queryBuilder.Project(sourceAlias, "*", alias, function);
            return;
        }

        if (parameter is ColumnReferenceExpression column && ReadColumn(column) is { } reference)
        {
            queryBuilder.Project(reference.Table ?? sourceAlias, reference.Column, alias, function);
            return;
        }

        Report(
            ConversionRecordKind.Loss,
            $"The argument of {function} is not a column reference; the projection was dropped.",
            QueryFeature.Aggregation);
    }

    private void ReadWhere(QuerySpecification query)
    {
        if (query.WhereClause is null)
        {
            return;
        }

        var condition = ReadCondition(query.WhereClause.SearchCondition);
        if (condition is null)
        {
            Report(
                ConversionRecordKind.Loss,
                "The WHERE clause uses a construct the condition tree cannot carry and was dropped.",
                QueryFeature.Filtering);
            return;
        }

        queryBuilder.Where(condition);
    }

    private void ReadHaving(QuerySpecification query)
    {
        if (query.HavingClause is null)
        {
            return;
        }

        var condition = ReadCondition(query.HavingClause.SearchCondition);
        if (condition is null)
        {
            Report(
                ConversionRecordKind.Loss,
                "The HAVING clause uses a construct the condition tree cannot carry and was dropped.",
                QueryFeature.PostAggregationFiltering);
            return;
        }

        queryBuilder.Having(condition);
    }

    private void ReadGroupBy(QuerySpecification query)
    {
        if (query.GroupByClause is null)
        {
            return;
        }

        if (query.GroupByClause.GroupByOption != GroupByOption.None)
        {
            Report(
                ConversionRecordKind.Loss,
                $"GROUP BY {query.GroupByClause.GroupByOption} is not carried by the query representation.",
                QueryFeature.Grouping);
        }

        foreach (var specification in query.GroupByClause.GroupingSpecifications)
        {
            if (specification is ExpressionGroupingSpecification expression
                && expression.Expression is ColumnReferenceExpression column
                && ReadColumn(column) is { } reference)
            {
                queryBuilder.GroupBy(reference.Table ?? sourceAlias, reference.Column);
                continue;
            }

            Report(
                ConversionRecordKind.Loss,
                "A grouping key that is not a column reference was dropped.",
                QueryFeature.Grouping);
        }
    }

    private void ReadOrderBy(QuerySpecification query)
    {
        if (query.OrderByClause is null)
        {
            return;
        }

        foreach (var element in query.OrderByClause.OrderByElements)
        {
            if (element.Expression is ColumnReferenceExpression column && ReadColumn(column) is { } reference)
            {
                queryBuilder.OrderBy(reference.Table, reference.Column, element.SortOrder != SortOrder.Descending);
                continue;
            }

            Report(
                ConversionRecordKind.Loss,
                "An ordering key that is not a column reference was dropped.",
                QueryFeature.Ordering);
        }
    }

    private ConditionNode? ReadCondition(BooleanExpression? expression)
    {
        switch (expression)
        {
            case BooleanParenthesisExpression parenthesis:
                return ReadCondition(parenthesis.Expression);

            case BooleanNotExpression not:
                {
                    var operand = ReadCondition(not.Expression);
                    return operand is null ? null : new NotCondition(operand);
                }

            case BooleanBinaryExpression binary:
                {
                    var op = binary.BinaryExpressionType == BooleanBinaryExpressionType.And
                        ? LogicalOperator.And
                        : LogicalOperator.Or;

                    var left = ReadCondition(binary.FirstExpression);
                    var right = ReadCondition(binary.SecondExpression);
                    if (left is null || right is null)
                    {
                        return null;
                    }

                    var operands = new List<ConditionNode>();
                    Flatten(left, op, operands);
                    Flatten(right, op, operands);
                    return new LogicalCondition(op, operands);
                }

            case BooleanIsNullExpression isNull:
                {
                    var operand = ReadOperand(isNull.Expression);
                    return operand is null
                        ? null
                        : new ComparisonCondition(
                            operand,
                            isNull.IsNot ? ComparisonOperator.IsNotNull : ComparisonOperator.IsNull);
                }

            case BooleanComparisonExpression comparison:
                {
                    var op = MapOperator(comparison.ComparisonType);
                    var left = ReadOperand(comparison.FirstExpression);
                    var right = ReadOperand(comparison.SecondExpression);
                    return op is null || left is null || right is null
                        ? null
                        : new ComparisonCondition(left, op.Value, right);
                }

            case LikePredicate like:
                {
                    var left = ReadOperand(like.FirstExpression);
                    var right = ReadOperand(like.SecondExpression);
                    if (left is null || right is null)
                    {
                        return null;
                    }

                    if (like.EscapeExpression is not null)
                    {
                        Report(
                            ConversionRecordKind.Loss,
                            "The ESCAPE clause of a LIKE predicate is not carried by the query representation.",
                            QueryFeature.Filtering);
                    }

                    ConditionNode node = new ComparisonCondition(left, ComparisonOperator.Like, right);
                    return like.NotDefined ? new NotCondition(node) : node;
                }

            // BETWEEN is rewritten as two comparisons, which rule Q14 explicitly permits and
            // which is exact rather than approximate.
            case BooleanTernaryExpression ternary
                when ternary.TernaryExpressionType is BooleanTernaryExpressionType.Between
                    or BooleanTernaryExpressionType.NotBetween:
                {
                    var value = ReadOperand(ternary.FirstExpression);
                    var low = ReadOperand(ternary.SecondExpression);
                    var high = ReadOperand(ternary.ThirdExpression);
                    if (value is null || low is null || high is null)
                    {
                        return null;
                    }

                    Report(
                        ConversionRecordKind.Convention,
                        "A BETWEEN predicate was rewritten as a pair of comparisons (rule Q14).",
                        QueryFeature.Filtering);

                    ConditionNode node = new LogicalCondition(LogicalOperator.And,
                    [
                        new ComparisonCondition(value, ComparisonOperator.GreaterThanOrEqual, low),
                        new ComparisonCondition(value, ComparisonOperator.LessThanOrEqual, high),
                    ]);

                    return ternary.TernaryExpressionType == BooleanTernaryExpressionType.NotBetween
                        ? new NotCondition(node)
                        : node;
                }

            default:
                return null;
        }
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

    private QueryOperand? ReadOperand(ScalarExpression? expression)
    {
        switch (expression)
        {
            case ColumnReferenceExpression column when ReadColumn(column) is { } reference:
                return QueryOperand.Column(reference.Table, reference.Column);

            case ScriptDomLiteral literal:
                return QueryOperand.Value(ReadConstant(literal));

            case UnaryExpression unary when unary.UnaryExpressionType == UnaryExpressionType.Negative
                                            && unary.Expression is ScriptDomLiteral inner:
                {
                    var constant = ReadConstant(inner);
                    return QueryOperand.Value(constant.Type is null
                        ? QueryConstant.Unrecognised("-" + constant.Text)
                        : QueryConstant.Of("-" + constant.Text, constant.Type.Value));
                }

            case FunctionCall call when call.Parameters.FirstOrDefault() is ColumnReferenceExpression parameter
                                        && ReadColumn(parameter) is { } aggregated:
                return QueryOperand.Column(
                    aggregated.Table,
                    aggregated.Column,
                    call.FunctionName.Value.ToUpperInvariant());

            default:
                return null;
        }
    }

    private static (string? Table, string Column)? ReadColumn(ColumnReferenceExpression column)
    {
        var parts = column.MultiPartIdentifier?.Identifiers;
        if (parts is null || parts.Count == 0)
        {
            return null;
        }

        return parts.Count == 1
            ? (null, parts[0].Value)
            : (parts[^2].Value, parts[^1].Value);
    }

    /// <summary>
    /// Turns a T-SQL literal into a typed constant (decision 024), undecorated: the quotes
    /// of a string belong to T-SQL and are added back by whichever target needs them.
    /// </summary>
    private QueryConstant ReadConstant(ScriptDomLiteral literal) => literal switch
    {
        IntegerLiteral integer => QueryConstant.Of(integer.Value, ScalarType.Int),
        NumericLiteral numeric => QueryConstant.Of(numeric.Value, ScalarType.Decimal),
        MoneyLiteral money => QueryConstant.Of(money.Value, ScalarType.Decimal),
        RealLiteral real => QueryConstant.Of(real.Value, ScalarType.Double),
        StringLiteral text => QueryConstant.Of(text.Value, ScalarType.String),
        _ => Unrecognised(literal),
    };

    private QueryConstant Unrecognised(ScriptDomLiteral literal)
    {
        Report(
            ConversionRecordKind.Incompleteness,
            $"The literal '{literal.Value}' has no counterpart in the scalar vocabulary; it is carried verbatim.",
            QueryFeature.Filtering);

        return QueryConstant.Unrecognised(literal.Value);
    }

    private static ComparisonOperator? MapOperator(BooleanComparisonType type) => type switch
    {
        BooleanComparisonType.Equals => ComparisonOperator.Equal,
        BooleanComparisonType.NotEqualToBrackets => ComparisonOperator.NotEqual,
        BooleanComparisonType.NotEqualToExclamation => ComparisonOperator.NotEqual,
        BooleanComparisonType.GreaterThan => ComparisonOperator.GreaterThan,
        BooleanComparisonType.GreaterThanOrEqualTo => ComparisonOperator.GreaterThanOrEqual,
        BooleanComparisonType.LessThan => ComparisonOperator.LessThan,
        BooleanComparisonType.LessThanOrEqualTo => ComparisonOperator.LessThanOrEqual,
        _ => null,
    };

    private static string Describe(TSqlFragment fragment) => fragment.GetType().Name;

    private void Report(ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => queryBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = queryBuilder.Descriptor.Framework,
            Artifact = ConversionContentType.SqlQuery,
            Feature = feature,
            Reason = reason,
        });
}
