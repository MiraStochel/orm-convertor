using System.Globalization;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
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
public abstract class LinqQueryParser(Func<AbstractQueryBuilder> queryBuilders) : IQueryParser
{
    /// <summary>
    /// The builder of the query being read. Assigned at the start of every Parse from the
    /// factory the orchestration supplied: one query, one fresh builder (decision 081). The
    /// parser may not make one itself - a builder belongs to the target framework and this
    /// parser to the source (S1) - and it is never touched outside a Parse call.
    /// </summary>
    protected AbstractQueryBuilder queryBuilder = default!;

    private IReadOnlyList<EntityMap>? entityMaps;
    private string sourceAlias = "t";

    /// <summary>
    /// The construct that sank the condition being read, when the parser can name it - a
    /// value from the enclosing scope, which is what a parameter looks like in LINQ - so
    /// that the clause's refusal says what the caller would have to change (F11). The
    /// category overrides the clause's own only for a parameter, which has a category of
    /// its own (decision 070).
    /// </summary>
    private (string What, QueryFeature? Category)? unread;

    public bool CanParse(ConversionContentType contentType)
        => contentType == ConversionContentType.CSharpQuery;

    /// <summary>
    /// The content type is not consulted here: LINQ is the only language this parser claims
    /// (see CanParse), so there is nothing to branch on. It is in the signature because the
    /// unit declares its language and a parser reading two of them - the Dapper one, with
    /// SQL bare beside SQL wrapped in C# - has to be told which (decision 047).
    /// </summary>
    public IReadOnlyCollection<AbstractQueryBuilder> Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? maps = null)
    {
        queryBuilder = queryBuilders();
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
                return [queryBuilder];
            }
        }

        queryBuilder.Push();
        Report(
            ConversionRecordKind.Failure,
            "No LINQ query chain was found in the source; nothing was translated.");
        queryBuilder.Pop();

        // The builder leaves even when it was refused: it holds the records of what went
        // wrong, and only the parser can say that this unit yielded a query (decision 081).
        return [queryBuilder];
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
        bool distinct = false;
        RowCount? pendingOffset = null;
        RowCount? pendingLimit = null;

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

            RowCount? count;

            // A value from the enclosing scope is the parameter of the same name, which is
            // the shape a page size has in any real chain (decision 085). It is the same
            // reading the parser does in operand position (decision 083); the scalar is the
            // builder template's to fill in, and here it comes from the clause.
            if (argument is IdentifierNameSyntax fromScope)
            {
                count = RowCount.Bound(QueryParameter.Named(fromScope.Identifier.Text));
            }
            else if (argument is LiteralExpressionSyntax literal && literal.Token.Value is int value && value >= 0)
            {
                count = RowCount.Literal(value);
            }
            else
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"The argument of {step.Name}() is neither a non-negative integer literal nor a value from the enclosing scope, and a pagination the artifact does not carry would change which rows the query returns; no artifact was generated.",
                    QueryFeature.Pagination);
                return;
            }

            if (step.Name == "Skip")
            {
                pendingOffset = count;
            }
            else
            {
                pendingLimit = count;
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

            if (step.Name == "Distinct")
            {
                queryBuilder.Distinct();
                distinct = true;
                continue;
            }

            // The representation carries DISTINCT over the final projection (decision 073),
            // so a step that does not commute with the collapse cannot follow it: a
            // projection after it returns duplicates SELECT DISTINCT would fold, a grouping
            // or a join after it groups or multiplies other rows. Filters, orderings, the
            // slice and materialization commute and pass.
            if (distinct && DoesNotCommuteWithDistinct(step.Name))
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"{step.Name}() after Distinct() does not commute with the collapse, which the query representation carries over the final projection; no artifact was generated.",
                    step.Name switch
                    {
                        "Select" => QueryFeature.Projection,
                        "GroupBy" => QueryFeature.Grouping,
                        _ => QueryFeature.Join,
                    });
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

    /// <summary>
    /// The steps that read differently before and after a Distinct() (decision 073). An
    /// unknown step is not among them on purpose: the parser cannot tell what it would
    /// change, and it stays the loss decision 070 made it.
    /// </summary>
    private static bool DoesNotCommuteWithDistinct(string method) => method is
        "Select" or "GroupBy" or "Join" or "LeftJoin" or "RightJoin";

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
                Report(
                    ConversionRecordKind.Loss,
                    "Select() applied after a set operation has no place in the query representation; the operands' own shape is kept.",
                    QueryFeature.Projection);
                return;

            // A Distinct() over the composed result is recorded beside the set operation;
            // what it means there - an identity, a UNION ALL turned UNION, or a refusal - is
            // the template's to say (decision 073).
            case "Distinct":
                queryBuilder.Distinct();
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

            default:
                ReportUnsupported(step.Name, null);
                break;
        }
    }

    /// <summary>
    /// A step the parser does not know. It stays a loss on purpose (decision 070): the
    /// parser cannot tell what an unknown call would change, and refusing every one of
    /// them would refuse Include() or TagWith() too, which change no rows. The steps known
    /// to change rows are named above; the record therefore claims only that the call was
    /// left out, not that the output is merely poorer.
    /// </summary>
    private void ReportUnsupported(string method, QueryFeature? feature)
        => Report(
            ConversionRecordKind.Loss,
            $"The query calls {method}(), which the query representation does not carry; the call was left out.",
            feature);

    private void HandleWhere(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(
                ConversionRecordKind.Failure,
                "A Where() whose argument is not a lambda cannot be read, and a query emitted without its filter would return different rows; no artifact was generated.",
                QueryFeature.Filtering);
            return;
        }

        var condition = ParseCondition(body!);
        if (condition is null)
        {
            // Silence here used to lose the whole predicate, then a loss record let the
            // query go out without it; both returned rows the source excluded (decision 070).
            Refuse($"predicate '{body}'", "a query emitted without its filter would return different rows", QueryFeature.Filtering);
            return;
        }

        queryBuilder.Where(condition);
    }

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

    private void HandleJoin(InvocationExpressionSyntax node, JoinKind kind)
    {
        // A join both filters and multiplies, so a query emitted without one returns
        // different rows (decisions 065 and 070): refused, never dropped.
        var args = node.ArgumentList.Arguments;
        if (args.Count < 3)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join with too few arguments cannot be read, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        string rightTable = ResolveTable(NameOfSource(args[0].Expression));
        string rightAlias = (rightTable.Split('.').LastOrDefault() ?? rightTable).ToLowerInvariant();

        if (args[1].Expression is not SimpleLambdaExpressionSyntax outer
            || args[2].Expression is not SimpleLambdaExpressionSyntax inner
            || outer.Body is not ExpressionSyntax outerBody
            || inner.Body is not ExpressionSyntax innerBody)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join whose key selectors are not lambdas cannot be read, and a query emitted without its join would return different rows; no artifact was generated.",
                QueryFeature.Join);
            return;
        }

        var onCondition = BuildJoinCondition(outerBody, innerBody, sourceAlias, rightAlias);
        if (onCondition is null)
        {
            Report(
                ConversionRecordKind.Failure,
                "A join whose key selectors do not pair up column for column has no shape the query representation carries, and a query emitted without its join would return different rows; no artifact was generated.",
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
        // Grouping decides which rows come back, so a key left out is a different query
        // (decision 070); a key inside an anonymous type used to vanish without a record.
        if (!TryReadLambdaBody(node, out var body))
        {
            Report(
                ConversionRecordKind.Failure,
                "A GroupBy() whose argument is not a lambda cannot be read, and a query grouped differently would return different rows; no artifact was generated.",
                QueryFeature.Grouping);
            return;
        }

        if (body is AnonymousObjectCreationExpressionSyntax anon)
        {
            foreach (var initializer in anon.Initializers)
            {
                var key = MemberName(initializer.Expression);
                if (key is null)
                {
                    RefuseGroupingKey(initializer.Expression);
                    continue;
                }

                queryBuilder.GroupBy(AliasOf(initializer.Expression), key);
            }

            return;
        }

        var name = MemberName(body!);
        if (name is null)
        {
            RefuseGroupingKey(body!);
            return;
        }

        queryBuilder.GroupBy(AliasOf(body!), name);
    }

    private void RefuseGroupingKey(ExpressionSyntax key)
        => Report(
            ConversionRecordKind.Failure,
            $"The grouping key '{key}' is not a column reference, and a query grouped differently would return different rows; no artifact was generated.",
            QueryFeature.Grouping);

    private void HandleHaving(InvocationExpressionSyntax node)
    {
        if (!TryReadLambdaBody(node, out var body) || body is not BinaryExpressionSyntax binary)
        {
            Report(
                ConversionRecordKind.Failure,
                "A post-aggregation filter that is not a simple comparison cannot be read, and a query emitted without it would return different rows; no artifact was generated.",
                QueryFeature.PostAggregationFiltering);
            return;
        }

        var op = MapOperator(binary.Kind());
        var left = ReadHavingOperand(binary.Left);
        var right = ReadHavingOperand(binary.Right);

        if (op is null || left is null || right is null)
        {
            Refuse($"post-aggregation filter '{binary}'", "a query emitted without it would return different rows", QueryFeature.PostAggregationFiltering);
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
    /// shape the Where handler reads. A Contains whose receiver is an inline collection of
    /// literals - <c>new[] { 1, 2, 3 }</c>, <c>new int[] { … }</c>, <c>new List&lt;int&gt;
    /// { … }</c> - is IN over a list of values (decision 074); a receiver that is a bare
    /// identifier is a collection from the enclosing scope, which is a collection parameter
    /// (decision 083). Any other receiver - a member access, a string - is no subquery and
    /// no list and stays unread.
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
                    QueryOperand? right;
                    if (TryDecompose(member.Expression, out var root, out var steps))
                    {
                        right = null;
                    }
                    else if (TryReadInlineCollection(member.Expression, out var values))
                    {
                        if (values is null)
                        {
                            return null;
                        }

                        right = values;
                    }
                    else if (member.Expression is IdentifierNameSyntax collection)
                    {
                        right = QueryOperand.Bound(
                            QueryParameter.Named(collection.Identifier.Text, isCollection: true));
                    }
                    else
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

                    right ??= QueryOperand.Nested(ReadSubQueryOperand(root!, steps));
                    return new ComparisonCondition(value, ComparisonOperator.In, right);
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
    /// Reads an inline collection of literals as the values of an IN list (decision 074).
    /// Three C# spellings carry one: an implicit array, a typed array with an initializer,
    /// and an object creation with a collection initializer. Returns false when the
    /// expression is none of them; returns true with a null operand when it is one but an
    /// element sinks it - a null literal is no value the model carries (decision 002), a
    /// bare identifier is a value from the enclosing scope, so a parameter, which decision
    /// 083 keeps out of the list even though it gave it an operand of its own,
    /// and an empty initializer is a predicate no target writes as a filter.
    /// </summary>
    private bool TryReadInlineCollection(ExpressionSyntax expression, out QueryOperand? values)
    {
        values = null;

        var initializer = expression switch
        {
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer,
            ArrayCreationExpressionSyntax array => array.Initializer,
            ObjectCreationExpressionSyntax creation when creation.Initializer?.IsKind(SyntaxKind.CollectionInitializerExpression) == true
                => creation.Initializer,
            _ => null,
        };

        if (initializer is null)
        {
            return false;
        }

        if (initializer.Expressions.Count == 0)
        {
            unread ??= ("an empty collection as the receiver of Contains, which is a predicate no target writes as a filter", null);
            return true;
        }

        var constants = new List<QueryConstant>(initializer.Expressions.Count);
        foreach (var element in initializer.Expressions)
        {
            if (IsNullLiteral(element))
            {
                unread ??= ("null among the values of an inline collection, which is no value the query representation carries", null);
                return true;
            }

            var operand = ReadOperand(element);
            if (operand is not null && operand.IsParameter)
            {
                unread ??= (
                    $"the parameter '{element}' from the enclosing scope among the values of an inline collection, which carries only values the query itself states",
                    QueryFeature.QueryParameter);
                return true;
            }

            if (operand is null || !operand.IsConstant || operand.Function is not null)
            {
                unread ??= ($"'{element}' among the values of an inline collection, which is not a literal", null);
                return true;
            }

            constants.Add(operand.Constant!);
        }

        values = QueryOperand.ValueList(constants);
        return true;
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
        IdentifierNameSyntax identifier => ValueFromScope(identifier),
        _ => null,
    };

    /// <summary>
    /// A bare identifier in operand position is a value captured from the enclosing scope -
    /// the LINQ form of a query parameter, and the one form that needs no decoration
    /// stripping, because C# writes the name itself (decision 083). What the value is, the
    /// chain does not say; the builder template derives the scalar from the other side of
    /// the comparison.
    /// </summary>
    private static QueryOperand ValueFromScope(IdentifierNameSyntax identifier)
        => QueryOperand.Bound(QueryParameter.Named(identifier.Identifier.Text));

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

        // A terminal aggregate over Distinct() (decision 073): Count, Sum and Average
        // aggregate over the collapsed set - COUNT(DISTINCT ...), which the model does not
        // carry - and refuse; Max and Min do not depend on the collapse, so the call is left
        // out with a record. Either way the marker goes, so that the scope does not also
        // report the collapse over its single-row aggregate projection.
        if (steps.Any(s => s.Name == "Distinct"))
        {
            var terminal = member.Name.Identifier.Text;
            if (function is "MAX" or "MIN")
            {
                Report(
                    ConversionRecordKind.Convention,
                    $"Distinct() before {terminal}() changes nothing, as the extreme of a set does not depend on duplicates; the call was left out.",
                    QueryFeature.Aggregation);
            }
            else
            {
                Report(
                    ConversionRecordKind.Failure,
                    $"{terminal}() over Distinct() aggregates over the collapsed set - {function}(DISTINCT ...), which the query representation does not carry; no artifact was generated.",
                    QueryFeature.Aggregation);
            }

            steps.RemoveAll(s => s.Name == "Distinct");
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

            // The other grammatical number comes from the one rule (decision 050); a glued
            // "s" used to answer differently for an entity already ending in s.
            var byConvention = entityMaps.FirstOrDefault(m =>
                m.Entity?.Name is { Length: > 0 } entityName
                && EntityTableNaming.TableCandidatesFor(entityName)
                    .Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)));
            if (byConvention is not null)
            {
                return Qualify(byConvention, byConvention.Table ?? name);
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
