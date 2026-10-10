using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Model;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;
using ScriptDomLiteral = Microsoft.SqlServer.TransactSql.ScriptDom.Literal;
using ModelExpression = Model.QueryInstructions.Conditions.QueryExpression;
using QueryExpression = Microsoft.SqlServer.TransactSql.ScriptDom.QueryExpression;

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
/// a document to the next. Beside them stand the facts the source stated about the text -
/// the dialect it is written in (decision 088) and its parameters (decision 084) - neither
/// of which the grammar reads, and therefore neither of which the grammar has to be taught.
///
/// What stands over the whole text - both guards, the grammar, the refusal of a statement
/// that does not read - is <see cref="SqlText"/> (decision 108), because a bare SQL unit is a
/// script and its SELECTs are queries with builders of their own. A wrapper whose text is
/// one command of its host calls <see cref="Read(string)"/>, which takes both steps; the
/// Dapper wrapper, whose bare unit is a script, takes the first itself and reads each
/// SELECT with a reader of its own.
/// </summary>
/// <param name="declaredSourceDialect">
/// The dialect the source declared for this text (decision 088). A declaration of a system
/// this version does not read stops the reading: the text is refused and no artifact comes
/// of it. The parameter is required rather than defaulted, because a seventh framework
/// writing its queries in SQL must say where its declaration comes from instead of falling
/// silently through the guard (S1); null is the answer for a source that declared nothing,
/// and it reads exactly as it did before.
/// </param>
public class SqlQueryReader(
    AbstractQueryBuilder queryBuilder,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    SourceSqlDialect? declaredSourceDialect,
    IReadOnlyDictionary<string, SqlParameterFacts>? statedParameters = null,
    ParseLimits? limits = null)
{
    private readonly ParseLimits limits = limits ?? ParseLimits.Default;

    private string sourceAlias = "t";

    /// <summary>
    /// The names the statement gives a table or a common table expression, which a derived
    /// table lifted into a definition of the whole query may not take (decision 112).
    /// </summary>
    private HashSet<string> reservedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The base name of every table reference of a statement, a reference to a common table expression included.</summary>
    private sealed class StatementTableNames : TSqlFragmentVisitor
    {
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);

        public override void Visit(NamedTableReference node)
        {
            Names.Add(node.SchemaObject.BaseIdentifier.Value);
            base.Visit(node);
        }
    }

    /// <summary>
    /// One common table expression into a definition of the query (decision 112). A
    /// definition whose body reads its own name is recursive (decision 113): T-SQL has no
    /// keyword for it, and the reference reads as a row source of that name like any other -
    /// the builder template finds the definition behind it and holds the body to the rules
    /// of recursion, the same for every source.
    /// </summary>
    private void ReadCommonTableExpression(CommonTableExpression expression)
    {
        var name = expression.ExpressionName.Value;

        if (queryBuilder.Defines(name))
        {
            Report(
                ConversionRecordKind.Failure,
                $"The WITH clause defines '{name}' twice, which T-SQL does not accept; no artifact was generated.",
                QueryFeature.IntermediateResult);
            return;
        }

        queryBuilder.Define(name, ReadDefinitionBody(expression.QueryExpression, expression.Columns, name));
    }

    /// <summary>
    /// A derived table into a definition of the query named by its alias (decision 112),
    /// lifted out of the scope it stands in - exact, because the definition sees nothing of
    /// the query around it, which the builder template holds. Null when the table cannot be
    /// a definition: its alias would name two things in one query.
    /// </summary>
    private string? ReadDerivedTable(QueryDerivedTable derived)
    {
        var name = derived.Alias?.Value;
        if (name is null)
        {
            Report(
                ConversionRecordKind.Failure,
                "A derived table without an alias has no name a definition of the query could take, and T-SQL does not accept it; no artifact was generated.",
                QueryFeature.IntermediateResult);
            return null;
        }

        if (queryBuilder.Defines(name) || reservedNames.Contains(name))
        {
            Report(
                ConversionRecordKind.Failure,
                $"The derived table '{name}' takes a name the query already gives a table, a common table expression or another derived table, and the representation names each intermediate result once per query; no artifact was generated.",
                QueryFeature.IntermediateResult);
            return null;
        }

        queryBuilder.Define(name, ReadDefinitionBody(derived.QueryExpression, derived.Columns, name));
        return name;
    }

    /// <summary>
    /// The body of a definition, read as a scope of its own and closed without becoming an
    /// instruction of the scope around it. A list of column names renames the columns of the
    /// body's naming SELECT - an exact rewrite, which every target writes as aliases because
    /// HQL has no column list.
    /// </summary>
    private SubQueryInstruction ReadDefinitionBody(QueryExpression expression, IList<Identifier> columns, string name)
    {
        var enclosingAlias = sourceAlias;

        queryBuilder.Push();
        ReadQueryExpression(expression);
        sourceAlias = enclosingAlias;
        var body = queryBuilder.PopOperand();

        return columns.Count == 0 ? body : Renamed(body, [.. columns.Select(c => c.Value)], name);
    }

    private SubQueryInstruction Renamed(SubQueryInstruction body, IReadOnlyList<string> columns, string name)
    {
        var inner = body.Instructions;
        while (inner.Count == 1 && inner[0] is SubQueryInstruction wrapped)
        {
            inner = wrapped.Instructions;
        }

        if (inner.Count > 0 && inner[0] is SetOperationInstruction operation)
        {
            return new SubQueryInstruction([operation with { Left = Renamed(operation.Left, columns, name) }, .. inner.Skip(1)]);
        }

        var projections = inner.OfType<ProjectInstruction>().ToList();
        if (projections.Count != columns.Count)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The column list of '{name}' names {columns.Count} columns where its body projects {projections.Count}; no artifact was generated.",
                QueryFeature.IntermediateResult);
            return body;
        }

        var position = 0;
        return new SubQueryInstruction([.. inner.Select(instruction =>
            instruction is ProjectInstruction projection ? projection with { Alias = columns[position++] } : instruction)]);
    }

    /// <summary>
    /// The construct that sank the condition being read, when the parser can name it - a
    /// parameter, a NULL among the values of an IN list - so that the clause's refusal says
    /// what the caller would have to change (F11). The category overrides the clause's own
    /// only for a parameter, which has a category of its own (decision 070).
    /// </summary>
    private (string What, QueryFeature? Category)? unread;

    /// <summary>
    /// Reads the one SELECT the text states into the builder: the text is the argument of
    /// one construct of the host framework, so a second SELECT refuses it as much as a
    /// statement that writes (decision 108). A text that cannot be read leaves its reason on
    /// the channel and the builder empty; the builder is the caller's either way, because
    /// only the caller can say that the unit yielded a query at all (decision 081).
    /// </summary>
    public void Read(string sql)
    {
        if (SqlText.Selects(sql, report, declaredSourceDialect, limits) is [var select])
        {
            Read(select);
        }
    }

    /// <summary>
    /// Reads one SELECT the whole-text step found (<see cref="SqlText.Selects"/>) into the
    /// builder. A refusal here refuses this query alone: the neighbours in a script have
    /// builders and readers of their own (decision 108).
    /// </summary>
    public void Read(SqlSelect select)
    {
        ArgumentNullException.ThrowIfNull(select);

        var statement = select.Statement;

        // The names a derived table must not take, because a definition is found by its name
        // and lifted to the whole query (decision 112): every table the statement reads, and
        // every common table expression it defines, whichever comes first in the text.
        var names = new StatementTableNames();
        statement.Accept(names);
        reservedNames = names.Names;

        if (statement.WithCtesAndXmlNamespaces is { } with)
        {
            // XMLNAMESPACES declares prefixes for FOR XML and the XML methods, neither of
            // which the representation carries; the query would mean nothing without them.
            if (with.XmlNamespaces is not null)
            {
                Report(
                    ConversionRecordKind.Failure,
                    "The SELECT declares XML namespaces, which the query representation does not carry; no artifact was generated.");
                return;
            }

            foreach (var expression in with.CommonTableExpressions)
            {
                reservedNames.Add(expression.ExpressionName.Value);
            }

            foreach (var expression in with.CommonTableExpressions)
            {
                ReadCommonTableExpression(expression);
            }
        }

        ReadOptimizerHints(statement.OptimizerHints);
        ReadQueryExpression(statement.QueryExpression);
    }

    /// <summary>
    /// The OPTION clause. MAXRECURSION is a fact of the query - it decides whether a recursive
    /// query finishes or is stopped with an error - and the builder carries it (decision 113);
    /// every other hint steers the plan, not the rows, so it is a loss rather than a refusal
    /// (decision 048) - but it is not nothing, which is what it used to be.
    /// </summary>
    private void ReadOptimizerHints(IList<OptimizerHint> hints)
    {
        var dropped = new List<OptimizerHint>();

        foreach (var hint in hints)
        {
            if (hint is LiteralOptimizerHint { HintKind: OptimizerHintKind.MaxRecursion, Value: IntegerLiteral literal }
                && int.TryParse(literal.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var maximum))
            {
                queryBuilder.LimitRecursion(maximum);
                continue;
            }

            dropped.Add(hint);
        }

        if (dropped.Count > 0)
        {
            Report(
                ConversionRecordKind.Loss,
                "The SELECT carries an OPTION clause, which the query representation does not carry; the query hints were dropped.");
        }
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
                // FOR XML and FOR JSON turn the rows into one value; a query read without the
                // clause returns something else entirely (decision 070).
                if (query.ForClause is not null)
                {
                    Report(
                        ConversionRecordKind.Failure,
                        "The SELECT carries a FOR clause, which returns one document instead of rows and which the query representation does not carry; no artifact was generated.",
                        QueryFeature.Projection);
                    break;
                }

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

        return RowCount.Bound(Parameter(name, stated, stated.IsCollection));
    }

    /// <summary>
    /// The parameter a variable of the text stands for: the named one of that name, or - where
    /// the wrapper stated that the source binds it by its order (decision 113) - the positional
    /// one at that position.
    /// </summary>
    private static QueryParameter Parameter(string name, SqlParameterFacts stated, bool isCollection)
        => stated.Position is { } position
            ? QueryParameter.Positional(position, stated.Scalar, isCollection)
            : QueryParameter.Named(name, stated.Scalar, isCollection);

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

        // A derived table is the source of rows a definition of the query is, named by its
        // alias (decision 112); the scope reads it by that name, as it reads a table.
        if (current is QueryDerivedTable derived)
        {
            if (ReadDerivedTable(derived) is not { } definition)
            {
                return;
            }

            sourceAlias = definition;
            queryBuilder.From(definition, definition);

            foreach (var join in joins)
            {
                ReadJoin(join);
            }

            return;
        }

        if (current is not NamedTableReference table)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The query source is a {current.GetType().Name}, which the query representation cannot carry.");
            return;
        }

        if (!ReadTableModifiers(table))
        {
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
        // different rows (decisions 065 and 070): refused, never dropped. A derived table is
        // the one row source besides a table it may stand on (decision 112).
        var derived = join.SecondTableReference as QueryDerivedTable;
        if (join.SecondTableReference is not NamedTableReference && derived is null)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join onto something other than a table or a derived table is not carried by the query representation, and a query emitted without its join would return different rows; no artifact was generated.",
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

        if (derived is not null)
        {
            if (ReadDerivedTable(derived) is { } definition)
            {
                queryBuilder.Join(kind, sourceAlias, definition, condition, definition);
            }

            return;
        }

        var right = (NamedTableReference)join.SecondTableReference;
        if (!ReadTableModifiers(right))
        {
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

    /// <summary>
    /// What a table reference may carry beside its name. TABLESAMPLE returns a sample of the
    /// rows, so a query read without it returns a different set and is refused (decision 070);
    /// a hint changes how the server reads, not which rows the query names, so it is a loss
    /// with the artifact still emitted (decision 048). Both used to be dropped without a word.
    /// Returns false when the reference cannot be read at all.
    /// </summary>
    private bool ReadTableModifiers(NamedTableReference table)
    {
        if (table.TableSampleClause is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The table '{NameAndAlias(table).Name}' is read with TABLESAMPLE, which returns a sample of its rows and which the query representation does not carry; no artifact was generated.");
            return false;
        }

        if (table.TableHints.Count > 0)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The table '{NameAndAlias(table).Name}' is read with a table hint, which the query representation does not carry; the hint was dropped.");
        }

        return true;
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
            ReadProjection(scalar.Expression, alias);
        }
    }

    /// <summary>
    /// One projected value (decision 107): a column, an aggregate over a column, the whole
    /// row as <c>COUNT(*)</c>, an expression - arithmetic, a function of the vocabulary,
    /// a CASE -, or a constant under an alias (decision 113), each qualified by the source
    /// alias where the text left it unqualified. A constant without an alias is not a shape
    /// any target names a column by and stays the loss it was; a construct outside the
    /// vocabulary is dropped with a record that names it, under the category of expressions.
    /// </summary>
    private void ReadProjection(ScalarExpression expression, string? alias)
    {
        var operand = ReadOperand(expression);

        if (operand is null)
        {
            var (what, category) = unread ?? ($"'{Describe(expression)}'", QueryFeature.Projection);
            unread = null;
            Report(
                ConversionRecordKind.Loss,
                $"The projected expression {what} is not a column, an aggregate or an expression the query representation carries, and was dropped.",
                category ?? QueryFeature.Projection);
            return;
        }

        // A constant under an alias is a column of the result with a value every row shares -
        // the starting depth of a recursion is one (decision 113); without an alias it names
        // no column any target could read it by, and stays the loss it was.
        if (operand.IsConstant && !operand.IsAggregate && alias is null)
        {
            Report(
                ConversionRecordKind.Loss,
                $"The projected expression '{Describe(expression)}' is a constant without an alias, which names no column, and was dropped.",
                QueryFeature.Projection);
            return;
        }

        queryBuilder.Project(Qualified(operand), alias);
    }

    /// <summary>A column operand the text left unqualified, qualified by the source alias - the projection and the grouping name their table.</summary>
    private QueryOperand Qualified(QueryOperand operand)
        => operand.IsColumn && operand.Table is null
            ? QueryOperand.Column(sourceAlias, operand.Property!, operand.Function, operand.Distinct)
            : operand;

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
            // A column, qualified by the source alias where the text left it bare, as the
            // projection is; or since decision 113 an expression, which the builder template
            // holds the projection, the HAVING and the ordering to.
            if (specification is ExpressionGroupingSpecification { Expression: { } written }
                && ReadOperand(written) is { } key
                && (key is { IsColumn: true, IsAggregate: false } || key is { IsExpression: true, IsAggregate: false }))
            {
                queryBuilder.GroupBy(Qualified(key));
                continue;
            }

            var (what, category) = unread ?? ("a grouping key that is neither a column nor an expression", QueryFeature.Grouping);
            unread = null;
            Report(
                ConversionRecordKind.Failure,
                $"The grouping uses {what}, and a query grouped differently would return different rows; no artifact was generated.",
                category ?? QueryFeature.Grouping);
        }
    }

    /// <summary>
    /// The ordering keys (decision 107): a column, an aggregate - <c>ORDER BY COUNT(*) DESC</c>,
    /// which used to be dropped -, an expression, or an alias of the projection, which the
    /// grammar hands over as an unqualified column. A bare number is a position in T-SQL, not
    /// a value, and stays the loss it was.
    /// </summary>
    private void ReadOrderBy(QuerySpecification query)
    {
        if (query.OrderByClause is null)
        {
            return;
        }

        foreach (var element in query.OrderByClause.OrderByElements)
        {
            var key = ReadOperand(element.Expression);
            if (key is not null && !key.IsConstant)
            {
                queryBuilder.OrderBy(key, element.SortOrder != SortOrder.Descending);
                continue;
            }

            var (what, category) = unread ?? ($"'{Print(element.Expression)}'", QueryFeature.Ordering);
            unread = null;
            Report(
                ConversionRecordKind.Loss,
                $"The ordering key {what} is not a column, an aggregate or an expression the query representation carries, and was dropped.",
                category ?? QueryFeature.Ordering);
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

            // A comparison against the rows of a subquery under ALL, ANY or SOME (decision
            // 119); ScriptDom reads SOME as Any. The compared value is read in the enclosing
            // scope before the nested one opens, so a correlated subquery sees the aliases it
            // refers to and the outer value is never taken for one of the subquery's columns.
            case SubqueryComparisonPredicate quantified:
                {
                    var op = MapOperator(quantified.ComparisonType);
                    var left = ReadOperand(quantified.Expression);
                    if (op is null || left is null)
                    {
                        return null;
                    }

                    var subQuery = QueryOperand.Nested(ReadSubQueryOperand(quantified.Subquery.QueryExpression));
                    if (quantified.SubqueryComparisonPredicateType == SubqueryComparisonPredicateType.None)
                    {
                        return new ComparisonCondition(left, op.Value, subQuery);
                    }

                    var quantifier = quantified.SubqueryComparisonPredicateType == SubqueryComparisonPredicateType.All
                        ? Quantifier.All
                        : Quantifier.Any;
                    if (QuantifiedComparisons.RewriteNote(op.Value, quantifier) is { } note)
                    {
                        Report(ConversionRecordKind.Convention, note, QueryFeature.Subquery);
                    }

                    return ComparisonCondition.Quantified(left, op.Value, quantifier, subQuery);
                }

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

                    // The escape character travels on the comparison (decision 102), as the
                    // character the query wrote. A variable in its place is a fact about how
                    // the pattern reads that the caller would supply, which the representation
                    // does not carry - and a pattern read without its escape treats the
                    // escaped wildcard as a wildcard again (decision 070).
                    string? escape = null;
                    if (like.EscapeExpression is StringLiteral literalEscape)
                    {
                        escape = literalEscape.Value;
                    }
                    else if (like.EscapeExpression is not null)
                    {
                        Report(
                            ConversionRecordKind.Failure,
                            $"The ESCAPE clause of a LIKE predicate names '{Print(like.EscapeExpression)}', which is not a string literal; the query representation carries the escape as a character the query states, and a pattern matched without it would select different rows; no artifact was generated.",
                            QueryFeature.Filtering);
                        return null;
                    }

                    ConditionNode node = new ComparisonCondition(left, ComparisonOperator.Like, right, escape);
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

    /// <summary>
    /// One operand of the six shapes (decisions 024, 061, 074, 083, 107): a column, a
    /// literal, a subquery, a variable as the parameter of the query, and since decision 107
    /// an expression - arithmetic and concatenation, a function of the closed vocabulary, a
    /// CASE - with an aggregate over a column or over an expression as the aggregate shape.
    /// Null for what the representation does not carry, with the construct named in
    /// <see cref="unread"/> for the clause to refuse or the projection to drop by name.
    /// </summary>
    private QueryOperand? ReadOperand(ScalarExpression? expression)
    {
        switch (expression)
        {
            case ParenthesisExpression parenthesis:
                return ReadOperand(parenthesis.Expression);

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

            case BinaryExpression binary:
                return ReadBinary(binary);

            case FunctionCall call:
                return ReadFunctionCall(call);

            case CoalesceExpression coalesce:
                return ReadCall(QueryFunction.Coalesce, coalesce.Expressions, Print(coalesce));

            case SearchedCaseExpression searched:
                return ReadSearchedCase(searched);

            case SimpleCaseExpression simple:
                return ReadSimpleCase(simple);

            case ScalarSubquery scalar:
                return QueryOperand.Nested(ReadSubQueryOperand(scalar.QueryExpression));

            case CastCall cast:
                return ReadCast(cast);

            // CURRENT_TIMESTAMP without parentheses is the grammar's own call, not a function
            // call by name - which is how the T-SQL writer spells the current moment, so that
            // its own text reads back (decision 107).
            case ParameterlessCall { ParameterlessCallType: ParameterlessCallType.CurrentTimestamp }:
                return QueryOperand.Computed(ModelExpression.Call(QueryFunction.CurrentTimestamp, []));

            // A T-SQL variable in operand position is a parameter of the query: the value
            // the caller binds (decision 083). The @ is T-SQL's decoration and is stripped,
            // the way the quotes of a string literal are - the model carries the bare name.
            // The scalar comes along only where the source stated one (decision 084);
            // otherwise the builder template derives it from the other side.
            case VariableReference variable:
                {
                    var name = variable.Name.TrimStart('@');
                    return QueryOperand.Bound(Parameter(name, StatedFor(name), isCollection: false));
                }

            case null:
                return null;

            default:
                // CONVERT, a NULLIF, an IIF, a subquery with more than one column and the
                // rest of what T-SQL computes: outside the vocabulary, named.
                unread ??= ($"'{Print(expression)}', which is a construct outside the vocabulary of expressions the query representation carries", QueryFeature.Expression);
                return null;
        }
    }

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// The arithmetic operators, and <c>+</c> as a concatenation where a side is a string
    /// literal or an expression whose scalar is a string by its own shape; a <c>+</c> over
    /// two columns is carried as an addition and the builder's gate, which has the mapping,
    /// says whether it concatenates. The bitwise operators have no counterpart in three of
    /// the four targets and stay outside the vocabulary.
    /// </summary>
    private QueryOperand? ReadBinary(BinaryExpression binary)
    {
        ExpressionOperator? op = binary.BinaryExpressionType switch
        {
            BinaryExpressionType.Add => ExpressionOperator.Add,
            BinaryExpressionType.Subtract => ExpressionOperator.Subtract,
            BinaryExpressionType.Multiply => ExpressionOperator.Multiply,
            BinaryExpressionType.Divide => ExpressionOperator.Divide,
            BinaryExpressionType.Modulo => ExpressionOperator.Modulo,
            _ => null,
        };

        if (op is null)
        {
            unread ??= ($"the operator {binary.BinaryExpressionType} in '{Print(binary)}', which is outside the vocabulary of expressions the query representation carries", QueryFeature.Expression);
            return null;
        }

        var left = ReadOperand(binary.FirstExpression);
        var right = ReadOperand(binary.SecondExpression);
        if (left is null || right is null || !IsLeaf(left, binary.FirstExpression) || !IsLeaf(right, binary.SecondExpression))
        {
            return null;
        }

        if (op == ExpressionOperator.Add && (ReadsAsString(left) || ReadsAsString(right)))
        {
            op = ExpressionOperator.Concat;
        }

        return QueryOperand.Computed(ModelExpression.Binary(op.Value, left, right));
    }

    /// <summary>A value that is a string by the shape the reader saw: a string literal, or an expression that yields one.</summary>
    private static bool ReadsAsString(QueryOperand operand)
    {
        if (operand.IsAggregate)
        {
            return false;
        }

        if (operand.IsConstant)
        {
            return operand.Constant!.Type is ScalarType.String or ScalarType.Char;
        }

        return operand.IsExpression
               && (operand.Expression!.Operator == ExpressionOperator.Concat
                   || operand.Expression.Function is QueryFunction.Upper or QueryFunction.Lower or QueryFunction.Trim
                       or QueryFunction.Substring or QueryFunction.EscapePattern
                   || operand.Expression.CastTo == ScalarType.String
                   || operand.Expression.IsListAggregate);
    }

    /// <summary>
    /// A leaf of an expression: a list of values and a collection parameter have no place in
    /// one (decision 107), and the factory would refuse them; refused here first, by name.
    /// </summary>
    private bool IsLeaf(QueryOperand operand, ScalarExpression written)
    {
        if (operand.IsValueList || operand.Parameter?.IsCollection == true)
        {
            unread ??= ($"the collection '{Print(written)}' inside an expression, which no target computes with", QueryFeature.Expression);
            return false;
        }

        return true;
    }

    /// <summary>
    /// A function call: one of the five aggregates over a column, over the whole row or
    /// over an expression; a function of the vocabulary under its T-SQL name - <c>ISNULL</c>
    /// is a COALESCE of two, <c>CONCAT</c> of several arguments is nested, <c>GETDATE()</c>
    /// and <c>CURRENT_TIMESTAMP</c> are the same moment, and since decision 113
    /// <c>DATEADD</c>, <c>DATEDIFF</c>, <c>ROUND</c>, <c>SQRT</c>, <c>STRING_AGG</c> and the
    /// three ranking functions over a window; anything else - <c>REPLACE</c>,
    /// <c>SYSDATETIME()</c>, an aggregate over a window - is outside the vocabulary and named.
    /// </summary>
    private QueryOperand? ReadFunctionCall(FunctionCall call)
    {
        var name = call.FunctionName.Value.ToUpperInvariant();

        if (call.OverClause is not null)
        {
            return ReadWindow(call, name);
        }

        if (name is "COUNT" or "SUM" or "MIN" or "MAX" or "AVG")
        {
            return ReadAggregate(call, name);
        }

        switch (name)
        {
            case "DATEADD": return ReadDateFunction(QueryFunction.DateAdd, call);
            case "DATEDIFF": return ReadDateFunction(QueryFunction.DateDiff, call);
            case "ROUND" when call.Parameters.Count == 2: return ReadCall(QueryFunction.Round, call.Parameters, Print(call));
            case "ROUND":
                unread ??= ($"'{Print(call)}', a ROUND whose third argument truncates instead of rounding, which the vocabulary of expressions does not carry", QueryFeature.Expression);
                return null;
            case "SQRT": return ReadCall(QueryFunction.Sqrt, call.Parameters, Print(call));
            case "STRING_AGG": return ReadListAggregate(call);
            case "UPPER": return ReadCall(QueryFunction.Upper, call.Parameters, Print(call));
            case "LOWER": return ReadCall(QueryFunction.Lower, call.Parameters, Print(call));
            case "TRIM": return ReadCall(QueryFunction.Trim, call.Parameters, Print(call));
            case "SUBSTRING": return ReadCall(QueryFunction.Substring, call.Parameters, Print(call));
            case "LEN": return ReadCall(QueryFunction.Length, call.Parameters, Print(call));
            case "ISNULL": return ReadCall(QueryFunction.Coalesce, call.Parameters, Print(call));
            case "COALESCE": return ReadCall(QueryFunction.Coalesce, call.Parameters, Print(call));
            case "ABS": return ReadCall(QueryFunction.Abs, call.Parameters, Print(call));
            case "YEAR": return ReadCall(QueryFunction.Year, call.Parameters, Print(call));
            case "MONTH": return ReadCall(QueryFunction.Month, call.Parameters, Print(call));
            case "DAY": return ReadCall(QueryFunction.Day, call.Parameters, Print(call));
            case "GETDATE":
            case "CURRENT_TIMESTAMP":
                return ReadCall(QueryFunction.CurrentTimestamp, call.Parameters, Print(call));
            case "CONCAT": return ReadConcat(call);
            default:
                unread ??= ($"the function {name} in '{Print(call)}', which is outside the vocabulary of expressions the query representation carries", QueryFeature.Expression);
                return null;
        }
    }

    /// <summary>
    /// A ranking function over a window (decision 113): <c>ROW_NUMBER</c>, <c>RANK</c> or
    /// <c>DENSE_RANK</c>, without an argument, with the partitions and the ordering of its
    /// OVER clause. An aggregate over a window and a frame of rows compute something the
    /// vocabulary has no place for - carried as a plain aggregate it would hold another value -
    /// and are refused by name; where the window stands is the builder template's to judge.
    /// </summary>
    private QueryOperand? ReadWindow(FunctionCall call, string name)
    {
        RankingFunction? ranking = name switch
        {
            "ROW_NUMBER" => RankingFunction.RowNumber,
            "RANK" => RankingFunction.Rank,
            "DENSE_RANK" => RankingFunction.DenseRank,
            _ => null,
        };

        var over = call.OverClause;
        if (ranking is null || call.Parameters.Count > 0 || over.WindowFrameClause is not null || over.OrderByClause is null)
        {
            unread ??= ($"the windowed function '{Print(call)}' - an aggregate over a window, a frame of rows, or a ranking without an ordering - which the vocabulary of expressions does not carry", QueryFeature.WindowFunction);
            return null;
        }

        var partitions = new List<QueryOperand>(over.Partitions.Count);
        foreach (var written in over.Partitions)
        {
            if (ReadOperand(written) is not { } partition || !IsLeaf(partition, written))
            {
                return null;
            }

            partitions.Add(partition);
        }

        return ReadOrdering(over.OrderByClause.OrderByElements) is { } ordering
            ? QueryOperand.Computed(ModelExpression.Window(ranking.Value, partitions, ordering))
            : null;
    }

    /// <summary>The keys of an ordering inside a window or a list aggregate (decision 113); null when one of them is not carried.</summary>
    private List<OrderingKey>? ReadOrdering(IList<ExpressionWithSortOrder> elements)
    {
        var ordering = new List<OrderingKey>(elements.Count);
        foreach (var element in elements)
        {
            if (ReadOperand(element.Expression) is not { } key || !IsLeaf(key, element.Expression))
            {
                return null;
            }

            ordering.Add(new OrderingKey(key, element.SortOrder != SortOrder.Descending));
        }

        return ordering;
    }

    /// <summary>
    /// <c>DATEADD</c> or <c>DATEDIFF</c> (decision 113): the datepart, a keyword T-SQL hands
    /// over as an identifier, read into the unit of the vocabulary - its abbreviations too -,
    /// and the two arguments after it. A datepart outside the six units - a week, a quarter, a
    /// millisecond - is refused by name.
    /// </summary>
    private QueryOperand? ReadDateFunction(QueryFunction function, FunctionCall call)
    {
        if (call.Parameters.Count != 3 || call.Parameters[0] is not IdentifierLiteral datepart)
        {
            unread ??= ($"'{Print(call)}', which is not a datepart and two arguments", QueryFeature.Expression);
            return null;
        }

        DateUnit? unit = datepart.Value.ToLowerInvariant() switch
        {
            "year" or "yy" or "yyyy" => DateUnit.Year,
            "month" or "mm" or "m" => DateUnit.Month,
            "day" or "dd" or "d" => DateUnit.Day,
            "hour" or "hh" => DateUnit.Hour,
            "minute" or "mi" or "n" => DateUnit.Minute,
            "second" or "ss" or "s" => DateUnit.Second,
            _ => null,
        };

        if (unit is null)
        {
            unread ??= ($"the datepart {datepart.Value} in '{Print(call)}', which is not one of the units the vocabulary of expressions carries", QueryFeature.Expression);
            return null;
        }

        var arguments = new List<QueryOperand>(2);
        foreach (var written in call.Parameters.Skip(1))
        {
            if (ReadOperand(written) is not { } argument || !IsLeaf(argument, written))
            {
                return null;
            }

            arguments.Add(argument);
        }

        return QueryOperand.Computed(ModelExpression.Call(function, arguments, unit));
    }

    /// <summary>
    /// <c>STRING_AGG(value, separator) [WITHIN GROUP (ORDER BY …)]</c> (decision 113): the
    /// separator is a string the query states, as the escape of a LIKE is; a separator the
    /// caller would supply is a fact the representation does not carry.
    /// </summary>
    private QueryOperand? ReadListAggregate(FunctionCall call)
    {
        if (call.Parameters.Count != 2 || call.Parameters[1] is not StringLiteral separator || call.UniqueRowFilter == UniqueRowFilter.Distinct)
        {
            unread ??= ($"'{Print(call)}', a STRING_AGG whose separator is not a string literal", QueryFeature.ListAggregation);
            return null;
        }

        if (ReadOperand(call.Parameters[0]) is not { } value || !IsLeaf(value, call.Parameters[0]))
        {
            return null;
        }

        var ordering = call.WithinGroupClause is { OrderByClause: { } order } ? ReadOrdering(order.OrderByElements) : [];
        return ordering is null ? null : QueryOperand.Computed(ModelExpression.ListAggregate(value, separator.Value, ordering));
    }

    /// <summary>
    /// <c>CAST(x AS type)</c> (decision 113), read only where the type is one the writer of
    /// the escape path writes back for a scalar of the vocabulary: <c>INT</c>, <c>BIGINT</c>,
    /// <c>REAL</c>, bare <c>FLOAT</c> and <c>NVARCHAR(MAX)</c>. A conversion with a length, a
    /// precision or into text that is not unicode could cut or change the value, which the
    /// model - carrying the scalar and not the type - would not carry, so it is refused by
    /// name; so is a conversion into anything else.
    /// </summary>
    private QueryOperand? ReadCast(CastCall cast)
    {
        ScalarType? scalar = cast.DataType switch
        {
            SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.Int, Parameters.Count: 0 } => ScalarType.Int,
            SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.BigInt, Parameters.Count: 0 } => ScalarType.Long,
            SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.Real, Parameters.Count: 0 } => ScalarType.Float,
            SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.Float, Parameters.Count: 0 } => ScalarType.Double,
            SqlDataTypeReference { SqlDataTypeOption: SqlDataTypeOption.NVarChar, Parameters: [MaxLiteral] } => ScalarType.String,
            _ => null,
        };

        if (scalar is null)
        {
            unread ??= ($"the conversion '{Print(cast)}', whose type is not one the vocabulary converts into without a length or a precision that could change the value", QueryFeature.Expression);
            return null;
        }

        if (ReadOperand(cast.Parameter) is not { } value || !IsLeaf(value, cast.Parameter))
        {
            return null;
        }

        return QueryOperand.Computed(ModelExpression.Call(QueryFunction.Cast, [value], castTo: scalar));
    }

    /// <summary>
    /// One of the five aggregates: over the whole row (<c>COUNT(*)</c>, the star parsed as a
    /// wildcard column), over a column, or over an expression (decision 107), with DISTINCT
    /// inside the function as the modifier of the aggregate (decision 102).
    /// </summary>
    private QueryOperand? ReadAggregate(FunctionCall call, string function)
    {
        var distinct = call.UniqueRowFilter == UniqueRowFilter.Distinct;
        var parameter = call.Parameters.FirstOrDefault();

        if (parameter is ColumnReferenceExpression { ColumnType: ColumnType.Wildcard })
        {
            return QueryOperand.Column(sourceAlias, "*", function, distinct);
        }

        if (parameter is null || call.Parameters.Count != 1)
        {
            unread ??= ($"'{Print(call)}', an aggregate without exactly one argument", QueryFeature.Aggregation);
            return null;
        }

        var argument = ReadOperand(parameter);
        if (argument is null || !IsLeaf(argument, parameter))
        {
            return null;
        }

        // An aggregate over an aggregate - MAX(SUM(x)), MAX(COUNT(*)) - has no form in any
        // target, and the operand holds one aggregate: rebuilt under the outer function it
        // would lose the inner one and compute another value. The template's gate refuses the
        // aggregate it finds inside an expression (decision 107); this one is refused here, in
        // the gate's words, and reading goes on over the inner aggregate.
        if (argument.IsAggregate)
        {
            Report(
                ConversionRecordKind.Failure,
                $"The aggregate {function} stands over '{argument}', which is an aggregate itself, and no target writes an aggregate over an aggregate; no artifact was generated.",
                QueryFeature.Expression);
            return argument;
        }

        if (argument.IsColumn)
        {
            return QueryOperand.Column(argument.Table, argument.Property!, function, distinct);
        }

        if (argument.IsConstant)
        {
            return QueryOperand.Value(argument.Constant!, function);
        }

        if (argument.IsParameter)
        {
            return QueryOperand.Bound(argument.Parameter!, function);
        }

        if (argument.IsExpression)
        {
            return QueryOperand.Computed(argument.Expression!, function, distinct);
        }

        unread ??= ($"'{Print(call)}', an aggregate over a subquery", QueryFeature.Aggregation);
        return null;
    }

    /// <summary>A call of a function of the vocabulary; an arity the function does not take is named and refused, as the factory would refuse it.</summary>
    private QueryOperand? ReadCall(QueryFunction function, IList<ScalarExpression> written, string text)
    {
        var (least, most) = ModelExpression.Arity(function);
        if (written.Count < least || written.Count > most)
        {
            unread ??= ($"'{text}', whose number of arguments the function {function} of the vocabulary does not take", QueryFeature.Expression);
            return null;
        }

        var arguments = new List<QueryOperand>(written.Count);
        foreach (var expression in written)
        {
            var argument = ReadOperand(expression);
            if (argument is null || !IsLeaf(argument, expression))
            {
                return null;
            }

            arguments.Add(argument);
        }

        return QueryOperand.Computed(ModelExpression.Call(function, arguments));
    }

    /// <summary>CONCAT of any number of arguments as a left-nested concatenation - the model is binary and a target with an n-ary spelling flattens it again.</summary>
    private QueryOperand? ReadConcat(FunctionCall call)
    {
        if (call.Parameters.Count < 2)
        {
            unread ??= ($"'{Print(call)}', a CONCAT of fewer than two arguments", QueryFeature.Expression);
            return null;
        }

        QueryOperand? result = null;
        foreach (var expression in call.Parameters)
        {
            var argument = ReadOperand(expression);
            if (argument is null || !IsLeaf(argument, expression))
            {
                return null;
            }

            result = result is null ? argument : QueryOperand.Computed(ModelExpression.Binary(ExpressionOperator.Concat, result, argument));
        }

        return result;
    }

    private QueryOperand? ReadSearchedCase(SearchedCaseExpression searched)
    {
        var branches = new List<CaseBranch>(searched.WhenClauses.Count);
        foreach (var clause in searched.WhenClauses)
        {
            var when = ReadCondition(clause.WhenExpression);
            var then = ReadOperand(clause.ThenExpression);
            if (when is null || then is null || !IsLeaf(then, clause.ThenExpression))
            {
                unread ??= ($"'{Print(searched)}', a CASE with a branch the query representation does not carry", QueryFeature.Expression);
                return null;
            }

            branches.Add(new CaseBranch(when, then));
        }

        return ReadCaseElse(searched.ElseExpression, branches, Print(searched));
    }

    /// <summary>A simple CASE read as the searched form with an equality per branch - an exact rewrite (decision 107).</summary>
    private QueryOperand? ReadSimpleCase(SimpleCaseExpression simple)
    {
        var input = ReadOperand(simple.InputExpression);
        if (input is null || !IsLeaf(input, simple.InputExpression))
        {
            return null;
        }

        var branches = new List<CaseBranch>(simple.WhenClauses.Count);
        foreach (var clause in simple.WhenClauses)
        {
            var value = ReadOperand(clause.WhenExpression);
            var then = ReadOperand(clause.ThenExpression);
            if (value is null || then is null || !IsLeaf(value, clause.WhenExpression) || !IsLeaf(then, clause.ThenExpression))
            {
                unread ??= ($"'{Print(simple)}', a CASE with a branch the query representation does not carry", QueryFeature.Expression);
                return null;
            }

            branches.Add(new CaseBranch(new ComparisonCondition(input, ComparisonOperator.Equal, value), then));
        }

        return ReadCaseElse(simple.ElseExpression, branches, Print(simple));
    }

    private QueryOperand? ReadCaseElse(ScalarExpression? written, List<CaseBranch> branches, string text)
    {
        QueryOperand? otherwise = null;
        if (written is not null and not NullLiteral)
        {
            otherwise = ReadOperand(written);
            if (otherwise is null || !IsLeaf(otherwise, written))
            {
                unread ??= ($"'{text}', a CASE whose ELSE the query representation does not carry", QueryFeature.Expression);
                return null;
            }
        }

        return QueryOperand.Computed(ModelExpression.Case(branches, otherwise));
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
            ? QueryOperand.Bound(Parameter(name, stated, isCollection: true))
            : null;
    }

    /// <summary>What the wrapper peeled off the source about one parameter, or nothing.</summary>
    private SqlParameterFacts StatedFor(string name)
        => statedParameters?.TryGetValue(name, out var facts) == true ? facts : default;

    /// <summary>
    /// Reads the values IN enumerates into a list operand (decision 074). Every element has
    /// to be a literal, because the list carries values the query itself states: a variable
    /// is a parameter, which since decision 102 stands among the values as well - a scalar
    /// one; a collection parameter, which MyBatis's foreach writes as a parenthesized
    /// element (decision 084), is no element of a list -, a NULL is no value the model
    /// carries (decision 002) and would make NOT IN mean different things in SQL and in
    /// LINQ, and a column or a function is no value at all. Each of the refused ones sinks
    /// the condition, named, for the enclosing clause to refuse.
    /// </summary>
    private QueryOperand? ReadValueList(IList<ScalarExpression> elements)
    {
        var values = new List<QueryOperand>(elements.Count);

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
                if (operand.Parameter!.IsCollection)
                {
                    unread ??= (
                        $"the collection parameter '{Print(element)}' among the values of an IN list, which takes single values only",
                        QueryFeature.QueryParameter);
                    return null;
                }

                values.Add(operand);
                continue;
            }

            if (!operand.IsConstant || operand.Function is not null)
            {
                unread ??= ($"'{Print(element)}' among the values of an IN list, which is not a literal", null);
                return null;
            }

            values.Add(operand);
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
