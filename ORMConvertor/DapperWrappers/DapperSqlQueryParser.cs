using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using ScriptDomLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace DapperWrappers;

/// <summary>
/// Reads a Dapper query. Two stages, because a Dapper query in the wild is T-SQL inside C#:
/// Roslyn finds the Query&lt;T&gt; call and takes its string literal, then a T-SQL parser
/// turns that string into instructions (decision 026). A bare SQL source skips the first
/// stage, and which of the two it is, is the content type the unit declares (decision 047).
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

    /// <summary>
    /// Which of the two stages the unit enters is decided by the language it declares, not
    /// by what its text looks like: a bare SELECT that happens to mention a table called
    /// QueryLog used to be taken for a C# snippet and refused for carrying no Dapper call
    /// (decisions 025 and 047).
    /// </summary>
    public void Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null)
    {
        var sql = contentType == ConversionContentType.CSharpQuery ? ExtractSql(source) : source;
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

        if (FindSelectStatement(fragment) is not { } select)
        {
            Report(ConversionRecordKind.Failure, "The SQL contains no SELECT statement to translate.");
            return;
        }

        ReadQueryExpression(select.QueryExpression);
    }

    /// <summary>
    /// Reads one query expression: a SELECT into its own subquery scope, a set operation
    /// recursively per rule Q12. The right operand always gets an explicit scope, so that a
    /// nested right side - A UNION (B INTERSECT C) - closes its own operations at deeper
    /// marks and cannot complete the outer one early.
    /// </summary>
    private void ReadQueryExpression(QueryExpression expression)
    {
        switch (expression)
        {
            case QueryParenthesisExpression parenthesis:
                ReadQueryExpression(parenthesis.QueryExpression);
                break;

            case QuerySpecification query:
                queryBuilder.Push();
                ReadFrom(query);
                ReadSelect(query);
                ReadWhere(query);
                ReadGroupBy(query);
                ReadHaving(query);
                ReadOrderBy(query);
                ReadPagination(query);
                queryBuilder.Pop();
                break;

            case BinaryQueryExpression binary:
                ReadSetOperationChain(binary);
                ReadTrailingClauses(binary);
                break;

            default:
                Report(
                    ConversionRecordKind.Failure,
                    $"The query is a {Describe(expression)}, which the query representation cannot carry.");
                break;
        }
    }

    private abstract record SetNode;
    private sealed record SetLeaf(QueryExpression Expression) : SetNode;
    private sealed record SetBranch(SetOperationType Operation, SetNode Left, SetNode Right) : SetNode;

    /// <summary>
    /// Reads a chain of set operations with SQL Server's own precedence. ScriptDom hands the
    /// chain over purely left-associated - A UNION B INTERSECT C arrives as
    /// (A UNION B) INTERSECT C - but the engine documents INTERSECT as binding first, so
    /// reading the tree literally would translate a different row set than the source means
    /// (decision 053). Parentheses in the source are their own node type and stay hard
    /// boundaries.
    /// </summary>
    private void ReadSetOperationChain(BinaryQueryExpression root)
    {
        var operands = new List<QueryExpression>();
        var operations = new List<SetOperationType>();
        Flatten(root, operands, operations);

        var nodes = operands.Select(SetNode (o) => new SetLeaf(o)).ToList();

        // An INTERSECT anywhere but at the front binds a pair the left-to-right fold would
        // not, so the emitted text has to say the grouping out loud.
        if (operations.Skip(1).Any(IsIntersect))
        {
            Report(
                ConversionRecordKind.Convention,
                "INTERSECT binds before UNION and EXCEPT (SQL Server operator precedence); the grouping was made explicit.",
                QueryFeature.SetOperation);
        }

        for (int i = 0; i < operations.Count;)
        {
            if (IsIntersect(operations[i]))
            {
                nodes[i] = new SetBranch(operations[i], nodes[i], nodes[i + 1]);
                nodes.RemoveAt(i + 1);
                operations.RemoveAt(i);
                continue;
            }

            i++;
        }

        while (operations.Count > 0)
        {
            nodes[0] = new SetBranch(operations[0], nodes[0], nodes[1]);
            nodes.RemoveAt(1);
            operations.RemoveAt(0);
        }

        EmitSetNode(nodes[0]);
    }

    private static bool IsIntersect(SetOperationType operation) => operation == SetOperationType.Intersect;

    private void Flatten(QueryExpression expression, List<QueryExpression> operands, List<SetOperationType> operations)
    {
        if (expression is BinaryQueryExpression binary)
        {
            Flatten(binary.FirstQueryExpression, operands, operations);
            operations.Add(MapSetOperation(binary));
            operands.Add(binary.SecondQueryExpression);
            return;
        }

        operands.Add(expression);
    }

    /// <summary>
    /// Emits one node of the regrouped chain. The right operand always gets an explicit
    /// scope, so that a nested right side closes its own operations at deeper marks and
    /// cannot complete the outer one early.
    /// </summary>
    private void EmitSetNode(SetNode node)
    {
        if (node is SetLeaf leaf)
        {
            ReadQueryExpression(leaf.Expression);
            return;
        }

        var branch = (SetBranch)node;
        EmitSetNode(branch.Left);
        queryBuilder.SetOperation(branch.Operation);
        queryBuilder.Push();
        EmitSetNode(branch.Right);
        queryBuilder.Pop();
    }

    private SetOperationType MapSetOperation(BinaryQueryExpression binary)
    {
        switch (binary.BinaryQueryExpressionType)
        {
            case BinaryQueryExpressionType.Union:
                return binary.All ? SetOperationType.UnionAll : SetOperationType.Union;
            case BinaryQueryExpressionType.Intersect when !binary.All:
                return SetOperationType.Intersect;
            case BinaryQueryExpressionType.Except when !binary.All:
                return SetOperationType.Except;
            case BinaryQueryExpressionType.Except:
                return SetOperationType.ExceptAll;
            default:
                // INTERSECT ALL has no place in the vocabulary; reading it as INTERSECT
                // would silently deduplicate (decision 053).
                Report(
                    ConversionRecordKind.Failure,
                    $"The set operation {binary.BinaryQueryExpressionType} ALL has no counterpart in the query representation; no artifact was generated.",
                    QueryFeature.SetOperation);
                return SetOperationType.Intersect;
        }
    }

    /// <summary>
    /// ORDER BY and OFFSET written after a set operation apply to the whole composed result,
    /// for which the query representation has no slot. Ordering only reorders the rows, so
    /// the query is still emitted with a loss record; a dropped OFFSET/FETCH would change
    /// which rows come back, so it refuses the artifact instead (decision 060).
    /// </summary>
    private void ReadTrailingClauses(BinaryQueryExpression binary)
    {
        if (binary.OrderByClause is not null)
        {
            Report(
                ConversionRecordKind.Loss,
                "An ORDER BY over a set operation has no place in the query representation; it was dropped.",
                QueryFeature.Ordering);
        }

        if (binary.OffsetClause is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                "An OFFSET/FETCH clause over a set operation cannot be carried, and dropping it would change which rows the query returns; no artifact was generated.",
                QueryFeature.Pagination);
        }
    }

    /// <summary>
    /// TOP and OFFSET/FETCH become the pagination of the (sub)query (decision 060). Only
    /// the shape whose meaning the representation holds is read - a non-negative integer
    /// literal count. Everything else - PERCENT, WITH TIES, an expression or a variable -
    /// refuses the artifact, because a query emitted without its pagination returns a
    /// different set of rows.
    /// </summary>
    private void ReadPagination(QuerySpecification query)
    {
        if (query.TopRowFilter is not null && query.OffsetClause is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                "TOP and OFFSET/FETCH in the same query are not valid T-SQL; no artifact was generated.",
                QueryFeature.Pagination);
            return;
        }

        long? offset = null;
        long? limit = null;

        if (query.TopRowFilter is { } top)
        {
            if (top.Percent || top.WithTies)
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"TOP {(top.Percent ? "PERCENT" : "WITH TIES")} has no counterpart in the query representation, and dropping it would change which rows the query returns; no artifact was generated.",
                    QueryFeature.Pagination);
                return;
            }

            if (ReadRowCount(top.Expression) is not { } topCount)
            {
                ReportUnreadableRowCount("TOP");
                return;
            }

            limit = topCount;
        }

        if (query.OffsetClause is { } clause)
        {
            if (ReadRowCount(clause.OffsetExpression) is not { } skipped)
            {
                ReportUnreadableRowCount("OFFSET");
                return;
            }

            offset = skipped;

            if (clause.FetchExpression is not null)
            {
                if (ReadRowCount(clause.FetchExpression) is not { } fetched)
                {
                    ReportUnreadableRowCount("FETCH");
                    return;
                }

                limit = fetched;
            }
        }

        queryBuilder.Paginate(offset, limit);
    }

    private void ReportUnreadableRowCount(string clause)
        => Report(
            ConversionRecordKind.Failure,
            $"The {clause} value is not an integer literal, so the pagination cannot be carried, and dropping it would change which rows the query returns; no artifact was generated.",
            QueryFeature.Pagination);

    /// <summary>A negative count arrives as a unary minus, which is not a literal here.</summary>
    private static long? ReadRowCount(ScalarExpression? expression) => expression switch
    {
        ParenthesisExpression parenthesis => ReadRowCount(parenthesis.Expression),
        IntegerLiteral integer when long.TryParse(integer.Value, out var value) => value,
        _ => null,
    };

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

    private static SelectStatement? FindSelectStatement(TSqlFragment fragment)
    {
        // Navigated explicitly rather than with a visitor: a visitor descends into subqueries
        // too, and their instructions would then be emitted into the outer scope.
        if (fragment is not TSqlScript script)
        {
            return null;
        }

        return script.Batches.SelectMany(b => b.Statements).OfType<SelectStatement>().FirstOrDefault();
    }

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

            case ExistsPredicate exists:
                return new ComparisonCondition(
                    QueryOperand.Nested(ReadSubQueryOperand(exists.Subquery.QueryExpression)),
                    ComparisonOperator.Exists);

            // IN with a subquery is the operator's only carried right side (decision 061);
            // IN with a list of values has no place in the model and keeps falling through
            // to the default, where the enclosing clause reports it.
            case InPredicate inPredicate when inPredicate.Subquery is not null:
                {
                    var value = ReadOperand(inPredicate.Expression);
                    if (value is null)
                    {
                        return null;
                    }

                    ConditionNode inNode = new ComparisonCondition(
                        value,
                        ComparisonOperator.In,
                        QueryOperand.Nested(ReadSubQueryOperand(inPredicate.Subquery.QueryExpression)));

                    return inPredicate.NotDefined ? new NotCondition(inNode) : inNode;
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
                        ? QueryConstant.Unrecognized("-" + constant.Text)
                        : QueryConstant.Of("-" + constant.Text, constant.Type.Value));
                }

            case FunctionCall call when call.Parameters.FirstOrDefault() is ColumnReferenceExpression parameter
                                        && ReadColumn(parameter) is { } aggregated:
                return QueryOperand.Column(
                    aggregated.Table,
                    aggregated.Column,
                    call.FunctionName.Value.ToUpperInvariant());

            case ScalarSubquery scalar:
                return QueryOperand.Nested(ReadSubQueryOperand(scalar.QueryExpression));

            default:
                return null;
        }
    }

    /// <summary>
    /// Reads a nested query expression into a subquery operand (decision 061). The scope is
    /// closed with PopOperand, so its instructions become the operand's body rather than
    /// instructions of the enclosing query, and the enclosing source alias survives the
    /// nested FROM.
    /// </summary>
    private SubQueryInstruction ReadSubQueryOperand(QueryExpression expression)
    {
        var enclosingAlias = sourceAlias;

        queryBuilder.Push();
        ReadQueryExpression(expression);
        sourceAlias = enclosingAlias;

        return queryBuilder.PopOperand();
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
        _ => Unrecognized(literal),
    };

    private QueryConstant Unrecognized(ScriptDomLiteral literal)
    {
        Report(
            ConversionRecordKind.Incompleteness,
            $"The literal '{literal.Value}' has no counterpart in the scalar vocabulary; it is carried verbatim.",
            QueryFeature.Filtering);

        return QueryConstant.Unrecognized(literal.Value);
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
