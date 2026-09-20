using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using ScriptDomLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;

namespace TransactSql;

/// <summary>
/// Reads one T-SQL SELECT into a query builder (decision 082). A parser of a
/// <em>language</em>, exactly as Roslyn is for C#, so every framework whose queries are
/// written in SQL reads them here: Dapper, MyBatis, and the &lt;sql-query&gt; of an
/// NHibernate hbm.xml. Depending on it is not depending on any of those frameworks, which
/// is what S1 forbids (decision 026).
///
/// Deliberately not an <see cref="IQueryParser"/>. Which parser claims which content type
/// stays a statement of the wrapper (decisions 025, 047 and 081), and so does where the
/// text comes from - out of a Dapper call by Roslyn, out of an XML element, out of a
/// MyBatis mapper with its placeholders already substituted. The grammar itself has nothing
/// to parameterize, so the wrapper composes this reader rather than inheriting it; the
/// NHibernate XML parser could not inherit it in any case, being the reader of two
/// languages at once.
///
/// One reader per query: the builder and the report channel are fixed at construction, the
/// same shape <see cref="SqlQueryVisitor"/> has, so nothing carries over from one query of
/// a document to the next. Beside them stands one optional argument, the facts the source
/// stated about the query's parameters (decision 084) - nothing the grammar reads, and
/// therefore nothing the grammar has to be taught.
/// </summary>
public class SqlQueryReader(
    AbstractQueryBuilder queryBuilder,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    IReadOnlyDictionary<string, SqlParameterFacts>? statedParameters = null)
{
    private string sourceAlias = "t";

    /// <summary>
    /// The construct that sank the condition being read, when the parser can name it - a
    /// parameter, a NULL among the values of an IN list - so that the clause's refusal says
    /// what the caller would have to change (F11). The category overrides the clause's own
    /// only for a parameter, which has a category of its own (decision 070).
    /// </summary>
    private (string What, QueryFeature? Category)? unread;

    /// <summary>
    /// Reads the SELECT the text states into the builder. A text that cannot be read leaves
    /// its reason on the channel and the builder empty; the builder is the caller's either
    /// way, because only the caller can say that the unit yielded a query at all
    /// (decision 081).
    /// </summary>
    public void Read(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

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
    /// TOP and OFFSET/FETCH become the pagination of the (sub)query (decision 060). Two
    /// shapes carry a meaning the representation holds: a non-negative integer literal, and
    /// a variable, which is the value the caller binds (decision 085). Everything else -
    /// PERCENT, WITH TIES, an expression - refuses the artifact, because a query emitted
    /// without its pagination returns a different set of rows.
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

        RowCount? offset = null;
        RowCount? limit = null;

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

    /// <summary>
    /// A row count that is neither a number nor a parameter - an arithmetic expression, a
    /// function call - so the representation cannot hold it, and a query emitted without its
    /// pagination returns a different set of rows (decision 060).
    /// </summary>
    private void ReportUnreadableRowCount(string clause)
        => Report(
            ConversionRecordKind.Failure,
            $"The {clause} value is neither an integer literal nor a parameter, so the pagination cannot be carried, and dropping it would change which rows the query returns; no artifact was generated.",
            QueryFeature.Pagination);

    private static ScalarExpression? Unparenthesize(ScalarExpression? expression)
        => expression is ParenthesisExpression parenthesis ? Unparenthesize(parenthesis.Expression) : expression;

    /// <summary>
    /// The count the clause states, or the parameter it leaves to the caller
    /// (decision 085). A negative count arrives as a unary minus, which is not a literal
    /// here, and the guard keeps it from reaching a factory that refuses one.
    /// </summary>
    private RowCount? ReadRowCount(ScalarExpression? expression) => Unparenthesize(expression) switch
    {
        IntegerLiteral integer when long.TryParse(integer.Value, out var value) && value >= 0
            => RowCount.Literal(value),
        VariableReference variable => BoundRowCount(variable),
        _ => null,
    };

    /// <summary>
    /// A variable in a row count is the parameter of the same name, undecorated. Its scalar
    /// is the builder template's to fill in from the clause, so nothing is read here but
    /// what the source stated of its own - which for MyBatis includes that the value is a
    /// list, and that is what makes the template able to refuse it (decision 085).
    /// </summary>
    private RowCount BoundRowCount(VariableReference variable)
    {
        var name = variable.Name.TrimStart('@');
        var stated = StatedFor(name);

        return RowCount.Bound(QueryParameter.Named(name, stated.Scalar, stated.IsCollection));
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

        // A cross join multiplies rows, so reading the first source alone would translate a
        // different query (decision 070). The first is still read so that the rest of the
        // statement can report its own reasons.
        if (query.FromClause!.TableReferences.Count > 1)
        {
            Report(
                ConversionRecordKind.Failure,
                "Comma-separated table references are a cross join the query representation cannot carry, and a query emitted without it would return different rows; no artifact was generated.",
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
        // A join both filters and multiplies, so a query emitted without one returns
        // different rows (decisions 065 and 070): refused, never dropped.
        if (join.SecondTableReference is not NamedTableReference right)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join onto something other than a table is not carried by the query representation, and a query emitted without its join would return different rows; no artifact was generated.",
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
            Refuse("join's ON condition", "a query emitted without its join would return different rows", QueryFeature.Join);
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
        // DISTINCT is a property of the whole projection, carried per SELECT (decision 073)
        // - inside set-operation operands and subqueries too, where the grammar puts it. It
        // used to be skipped without a record, then refused (decision 070).
        if (query.UniqueRowFilter == UniqueRowFilter.Distinct)
        {
            queryBuilder.Distinct();
        }

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

        // DISTINCT inside the aggregate is a modifier of the function, not of the query, and
        // the projection vocabulary has no place for it (decision 073). It used to be dropped
        // in silence and the aggregate written over all values - a different value, not a
        // poorer one; now the projection goes, which leaves the rows as they are.
        if (call.UniqueRowFilter == UniqueRowFilter.Distinct)
        {
            Report(
                ConversionRecordKind.Loss,
                $"{function}(DISTINCT ...) aggregates over collapsed values, which the query representation does not carry; the projection was dropped.",
                QueryFeature.Aggregation);
            return;
        }

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
            Refuse("WHERE clause", "a query emitted without its filter would return different rows", QueryFeature.Filtering);
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
            Refuse("HAVING clause", "a query emitted without its post-aggregation filter would return different rows", QueryFeature.PostAggregationFiltering);
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

        // Grouping decides which rows come back, so a grouping read differently from the
        // source - an option dropped, a key left out - is a different query (decision 070).
        if (query.GroupByClause.GroupByOption != GroupByOption.None)
        {
            Report(
                ConversionRecordKind.Failure,
                $"GROUP BY {query.GroupByClause.GroupByOption} is not carried by the query representation, and a query grouped differently would return different rows; no artifact was generated.",
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
                ConversionRecordKind.Failure,
                "A grouping key that is not a column reference cannot be carried, and a query grouped differently would return different rows; no artifact was generated.",
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

            // IN carries two right sides: a subquery (decision 061) and a list of values
            // (decision 074). NOT IN is a negation over either.
            case InPredicate inPredicate:
                {
                    var value = ReadOperand(inPredicate.Expression);
                    var right = inPredicate.Subquery is not null
                        ? QueryOperand.Nested(ReadSubQueryOperand(inPredicate.Subquery.QueryExpression))
                        : ReadCollectionParameter(inPredicate.Values) ?? ReadValueList(inPredicate.Values);
                    if (value is null || right is null)
                    {
                        return null;
                    }

                    ConditionNode inNode = new ComparisonCondition(value, ComparisonOperator.In, right);
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

                    // A pattern read without its escape treats the escaped wildcard as a
                    // wildcard again and matches more rows (decision 070).
                    if (like.EscapeExpression is not null)
                    {
                        Report(
                            ConversionRecordKind.Failure,
                            "The ESCAPE clause of a LIKE predicate is not carried by the query representation, and a pattern matched without it would select different rows; no artifact was generated.",
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

            // An aggregate over DISTINCT values is a modifier the model does not carry
            // (decision 073); it sinks the condition and is named, so that the clause refuses
            // for the right reason instead of aggregating over all values in silence.
            case FunctionCall { UniqueRowFilter: UniqueRowFilter.Distinct } collapsed:
                unread ??= ($"{collapsed.FunctionName.Value.ToUpperInvariant()}(DISTINCT ...), an aggregate over collapsed values, which the query representation does not carry", null);
                return null;

            case FunctionCall call when call.Parameters.FirstOrDefault() is ColumnReferenceExpression parameter
                                        && ReadColumn(parameter) is { } aggregated:
                return QueryOperand.Column(
                    aggregated.Table,
                    aggregated.Column,
                    call.FunctionName.Value.ToUpperInvariant());

            case ScalarSubquery scalar:
                return QueryOperand.Nested(ReadSubQueryOperand(scalar.QueryExpression));

            // A T-SQL variable in operand position is a parameter of the query: the value
            // the caller binds (decision 083). The @ is T-SQL's decoration and is stripped,
            // the way the quotes of a string literal are - the model carries the bare name.
            // The scalar comes along only where the source stated one (decision 084);
            // otherwise the builder template derives it from the other side.
            case VariableReference variable:
                {
                    var name = variable.Name.TrimStart('@');
                    return QueryOperand.Bound(QueryParameter.Named(name, ScalarStatedFor(name)));
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// The one right side of IN that is neither a subquery nor a list of values: a
    /// collection parameter, whose elements the caller supplies (decisions 074 and 083).
    /// T-SQL has no syntax of its own for it - the text says <c>IN (@ids)</c>, which is a
    /// one-element list of values to the grammar - so it is read here only where the source
    /// stated that the parameter binds a list, which is a fact the wrapper carried in and
    /// the grammar could not have known. Null when it is not this shape, and the ordinary
    /// list reading follows.
    /// </summary>
    private QueryOperand? ReadCollectionParameter(IList<ScalarExpression> elements)
    {
        if (elements.Count != 1 || Unparenthesize(elements[0]) is not VariableReference variable)
        {
            return null;
        }

        var name = variable.Name.TrimStart('@');
        var stated = StatedFor(name);

        return stated.IsCollection
            ? QueryOperand.Bound(QueryParameter.Named(name, stated.Scalar, isCollection: true))
            : null;
    }

    private ScalarType? ScalarStatedFor(string name) => StatedFor(name).Scalar;

    /// <summary>What the wrapper peeled off the source about one parameter, or nothing.</summary>
    private SqlParameterFacts StatedFor(string name)
        => statedParameters?.TryGetValue(name, out var facts) == true ? facts : default;

    /// <summary>
    /// Reads the values IN enumerates into a list operand (decision 074). Every element has
    /// to be a literal, because the list carries values the query itself states: a variable
    /// is a parameter, which decision 083 keeps out of the list even though it gave it an
    /// operand of its own, a NULL is no value the model carries (decision 002) and would
    /// make NOT IN mean different things in SQL and in LINQ, and a column or a function is
    /// no value at all. Each of them sinks the condition, named, for the enclosing clause to
    /// refuse.
    /// </summary>
    private QueryOperand? ReadValueList(IList<ScalarExpression> elements)
    {
        var values = new List<QueryConstant>(elements.Count);

        foreach (var element in elements)
        {
            if (element is NullLiteral)
            {
                unread ??= ("NULL among the values of an IN list, which is no value the query representation carries", null);
                return null;
            }

            var operand = ReadOperand(element);
            if (operand is null)
            {
                unread ??= ($"'{Print(element)}' among the values of an IN list, which is not a literal", null);
                return null;
            }

            if (operand.IsParameter)
            {
                unread ??= (
                    $"the parameter '{Print(element)}' among the values of an IN list, which carries only values the query itself states",
                    QueryFeature.QueryParameter);
                return null;
            }

            if (!operand.IsConstant || operand.Function is not null)
            {
                unread ??= ($"'{Print(element)}' among the values of an IN list, which is not a literal", null);
                return null;
            }

            values.Add(operand.Constant!);
        }

        return values.Count == 0 ? null : QueryOperand.ValueList(values);
    }

    private static string Print(TSqlFragment fragment)
    {
        var generator = new Sql160ScriptGenerator();
        generator.GenerateScript(fragment, out var text);
        return text.Trim();
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

    /// <summary>
    /// Refuses the artifact for a clause the condition tree cannot carry (decision 070). A
    /// query emitted without its filter, join or grouping returns different rows, which is
    /// the line decision 053 drew for the builders; the parser holds it on the way in, over
    /// the same channel, so no artifact comes out. Reading goes on afterwards so that every
    /// reason reaches the caller at once.
    /// </summary>
    private void Refuse(string clause, string consequence, QueryFeature feature)
    {
        var (what, category) = unread ?? ("a construct the condition tree cannot carry", (QueryFeature?)null);
        unread = null;

        Report(
            ConversionRecordKind.Failure,
            $"The {clause} uses {what}, and {consequence}; no artifact was generated.",
            category ?? feature);
    }

    private void Report(ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => report(kind, reason, feature);
}
