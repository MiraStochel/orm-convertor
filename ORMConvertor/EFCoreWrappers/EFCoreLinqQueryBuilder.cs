using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace EFCoreWrappers;

/// <summary>
/// Emits a LINQ method chain, which is EF Core's own query form (decision 022).
///
/// The artifact is a method taking a <c>DbContext</c> and returning <c>IQueryable</c>, with
/// the root written as <c>ctx.Set&lt;T&gt;()</c>. Both are deliberate and both are what make
/// the third verification level possible: a query that returns a list would have to be
/// executed to be judged, and a root naming a DbSet property would need a generated context
/// class the query builder has no way to produce (decision 027).
/// </summary>
public class EFCoreLinqQueryBuilder : AbstractQueryBuilder
{
    private LinqScope scope = new();
    private EFCoreLinqQueryVisitor visitor = null!;
    private readonly List<string> tupleAliases = [];
    private string orderingAfterProjection = string.Empty;

    /// <summary>
    /// The visitor of the scope a subquery is being rendered inside, so that the nested
    /// visitor can resolve correlated references to the outer lambda's parameter; null at
    /// the top level (decision 061).
    /// </summary>
    private EFCoreLinqQueryVisitor? enclosingVisitor;

    /// <summary>
    /// Lambda parameters of every enclosing scope. C# forbids a nested lambda parameter
    /// shadowing an enclosing one, so the nested source step renames on collision.
    /// </summary>
    private HashSet<string> enclosingParams = new(StringComparer.Ordinal);

    /// <summary>
    /// Set while composing a subquery operand: the operator decides the chain's ending -
    /// a bare one-column Select for IN, no projection for EXISTS, a terminal aggregate call
    /// appended by the renderer for a scalar comparison (decision 061).
    /// </summary>
    private ComparisonOperator? operandContext;

    public override TargetFrameworkDescriptor Descriptor => EFCoreDescriptor.Instance;

    protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact)
    {
        var map = EntityFor(clauses.From.Table);
        var entity = map?.Entity.Name ?? SingularOf(clauses.From.Table);

        scope = new LinqScope
        {
            Entities = AliasedEntities(clauses),
            Param = FreshName(clauses.From.Alias ?? "c"),
        };
        scope.ElementParam = FreshName(scope.Param == "e" ? "el" : "e");
        scope.Aliases.Add(clauses.From.Alias ?? clauses.From.Table);

        visitor = new EFCoreLinqQueryVisitor(
            scope,
            (kind, reason, feature) => Report(kind, reason, feature),
            RenderSubQuery,
            enclosingVisitor);

        tupleAliases.Clear();
        tupleAliases.Add(clauses.From.Alias ?? entity);
        orderingAfterProjection = string.Empty;

        artifact.ResultEntity = entity;
        artifact.Source.Append($"ctx.Set<{entity}>()");

        if (map is null)
        {
            Report(
                ConversionRecordKind.Convention,
                $"No entity was mapped to table '{clauses.From.Table}', so the type name '{entity}' was derived from it.",
                QueryFeature.Projection,
                entity: entity);
        }
    }

    protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact)
    {
        foreach (var join in clauses.Joins)
        {
            var rightAlias = join.RightTableAlias ?? Bare(join.RightTable).ToLowerInvariant();
            var rightMap = EntityFor(join.RightTable);
            var rightEntity = rightMap?.Entity.Name ?? SingularOf(join.RightTable);

            if (!TryReadJoinKeys(join.OnCondition, rightAlias, out var pairs))
            {
                // A join dropped in the condition's place would return different rows -
                // it filters and it multiplies (decision 065).
                Report(
                    ConversionRecordKind.Failure,
                    $"The join onto '{join.RightTable}' is not a conjunction of column equalities, which is the only form a LINQ join takes; no artifact was generated.",
                    QueryFeature.Join);
                continue;
            }

            var leftParam = scope.Param;
            var leftKeys = KeySelector(pairs.Select(p => visitor.Operand(p.Left)).ToList());
            var rightKeys = KeySelector(pairs
                .Select(p => $"{rightAlias}.{PropertyFor(rightMap, p.Right.Property ?? p.Right.Constant?.Text ?? string.Empty)}")
                .ToList());

            var members = scope.Composite
                ? string.Join(", ", tupleAliases.Select(a => $"{leftParam}.{a}")) + $", {rightAlias}"
                : $"{leftParam}, {rightAlias}";

            var arguments =
                $"ctx.Set<{rightEntity}>(), " +
                $"{leftParam} => {leftKeys}, " +
                $"{rightAlias} => {rightKeys}, " +
                $"({leftParam}, {rightAlias}) => new {{ {members} }}";

            if (join.Kind == JoinKind.Full)
            {
                // EF Core 10 has LeftJoin and RightJoin but no full outer join, and an inner
                // join in its place would return different rows (decision 065). The full join
                // is composed from the operators that do exist: the left join's rows,
                // concatenated with the right join's rows that found no left match. Concat is
                // UNION ALL, so matched pairs are not doubled - the filter excludes them from
                // the right branch - and genuine duplicates are not collapsed the way Union
                // would. The filter stands on the root member because that one is never null
                // in the left branch. A faithful translation is neither a loss nor a
                // convention, so no record is issued.
                var root = scope.Composite ? tupleAliases[0] : leftParam;
                var probe = FreshName("x");
                var chainSoFar = string.Concat(artifact.Source.ToString(), artifact.Joins.ToString());

                artifact.Joins.Append(
                    $"\n        .LeftJoin({arguments})" +
                    $"\n        .Concat({chainSoFar}" +
                    $"\n            .RightJoin({arguments})" +
                    $"\n            .Where({probe} => {probe}.{root} == null))");
            }
            else
            {
                var method = join.Kind switch
                {
                    JoinKind.Inner => "Join",
                    JoinKind.Left => "LeftJoin",
                    JoinKind.Right => "RightJoin",
                    _ => null,
                };

                if (method is null)
                {
                    // No catch-all translation: a JoinKind value without an operator must not
                    // come out as a neighbouring join (decision 053).
                    Report(
                        ConversionRecordKind.Failure,
                        $"Join kind {join.Kind} has no LINQ operator; no artifact was generated.",
                        QueryFeature.JoinKind);
                    continue;
                }

                artifact.Joins.Append($"\n        .{method}({arguments})");
            }

            tupleAliases.Add(rightAlias);
            scope.Aliases.Add(join.RightTableAlias ?? join.RightTable);
            scope.Composite = true;
            scope.Param = FreshParam();
        }
    }

    private static string KeySelector(List<string> keys)
        => keys.Count == 1 ? keys[0] : $"new {{ {string.Join(", ", keys)} }}";

    private string FreshParam()
    {
        foreach (var candidate in new[] { "t", "row", "q", "z" })
        {
            if (!tupleAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase)
                && !enclosingParams.Contains(candidate))
            {
                return candidate;
            }
        }

        return "row" + tupleAliases.Count;
    }

    /// <summary>A name not taken by any enclosing lambda parameter (decision 061).</summary>
    private string FreshName(string candidate)
    {
        var name = candidate;
        for (int i = 1; enclosingParams.Contains(name); i++)
        {
            name = candidate + i;
        }

        return name;
    }

    /// <summary>
    /// Splits a join condition into left/right key pairs. LINQ takes two key selectors, so
    /// anything that is not a conjunction of equalities has no shape to go into.
    /// </summary>
    private static bool TryReadJoinKeys(
        ConditionNode condition,
        string rightAlias,
        out List<(QueryOperand Left, QueryOperand Right)> pairs)
    {
        pairs = [];
        return Collect(condition, rightAlias, pairs);
    }

    private static bool Collect(
        ConditionNode node,
        string rightAlias,
        List<(QueryOperand Left, QueryOperand Right)> pairs)
    {
        switch (node)
        {
            case LogicalCondition logical when logical.Operator == LogicalOperator.And:
                return logical.Operands.All(operand => Collect(operand, rightAlias, pairs));

            case ComparisonCondition comparison
                when comparison.Operator == ComparisonOperator.Equal
                     && comparison.Right is not null
                     && comparison.Left.IsColumn
                     && comparison.Right.IsColumn:
                {
                    var rightIsTarget = string.Equals(comparison.Right.Table, rightAlias, StringComparison.OrdinalIgnoreCase);
                    var leftIsTarget = string.Equals(comparison.Left.Table, rightAlias, StringComparison.OrdinalIgnoreCase);

                    if (rightIsTarget)
                    {
                        pairs.Add((comparison.Left, comparison.Right));
                        return true;
                    }

                    if (leftIsTarget)
                    {
                        pairs.Add((comparison.Right, comparison.Left));
                        return true;
                    }

                    return false;
                }

            default:
                return false;
        }
    }

    protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Filter is null)
        {
            return;
        }

        artifact.Filter.Append($"\n        .Where({scope.Param} => {clauses.Filter.Accept(visitor)})");
    }

    protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.GroupBys.Count == 0)
        {
            return;
        }

        var keys = clauses.GroupBys.Select(g => visitor.Visit(g)).ToList();
        var selector = keys.Count == 1 ? keys[0] : $"new {{ {string.Join(", ", keys)} }}";

        artifact.Grouping.Append($"\n        .GroupBy({scope.Param} => {selector})");

        // From here on the lambda parameter holds a grouping, which is why the projection
        // step has to run after this one.
        scope.ElementParam = scope.Param;
        scope.Grouped = true;
        scope.GroupKeys = clauses.GroupBys;
        scope.Param = "g";
    }

    protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.PostFilter is null)
        {
            return;
        }

        artifact.PostFilter.Append($"\n        .Where({scope.Param} => {clauses.PostFilter.Accept(visitor)})");
    }

    protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.OrderBys.Count == 0)
        {
            return;
        }

        var before = new List<OrderByInstruction>();
        var after = new List<OrderByInstruction>();

        foreach (var order in clauses.OrderBys)
        {
            // Ordering by a projection alias can only happen once the projection exists, so
            // it goes after Select. The slotted artifact is what makes that possible without
            // the step order having to change (decision 023).
            var byAlias = order.Table is null
                          && clauses.Projections.Any(p =>
                              string.Equals(p.Alias, order.Attribute, StringComparison.OrdinalIgnoreCase));

            (byAlias ? after : before).Add(order);
        }

        artifact.Ordering.Append(Chain(before, scope.Param, o => visitor.Visit(o)));
        orderingAfterProjection = Chain(after, "p", o => $"p.{o.Attribute}");
    }

    private static string Chain(
        List<OrderByInstruction> orders,
        string param,
        Func<OrderByInstruction, string> key)
    {
        var text = string.Empty;
        for (int i = 0; i < orders.Count; i++)
        {
            var method = (i == 0, orders[i].Asc) switch
            {
                (true, true) => "OrderBy",
                (true, false) => "OrderByDescending",
                (false, true) => "ThenBy",
                (false, false) => "ThenByDescending",
            };

            text += $"\n        .{method}({param} => {key(orders[i])})";
        }

        return text;
    }

    protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact)
    {
        // Inside a subquery operand the operator owns the ending (decision 061): EXISTS
        // needs no projection under its Any(), IN needs the single column bare - an
        // anonymous type would not Contains against the outer operand - and a scalar
        // comparison gets its terminal aggregate appended by the renderer.
        if (operandContext is { } context)
        {
            if (context == ComparisonOperator.In)
            {
                artifact.Projection.Append(
                    $"\n        .Select({scope.Param} => {visitor.Visit(clauses.Projections[0])})");
            }

            return;
        }

        // Rule Q3: no projection means the whole entity is materialized, and in LINQ that is
        // simply the absence of a Select.
        if (clauses.ProjectsWholeEntity && !scope.Grouped)
        {
            return;
        }

        if (clauses.ProjectsWholeEntity)
        {
            artifact.Projection.Append($"\n        .Select({scope.Param} => {scope.Param}.Key)");
            return;
        }

        var members = clauses.Projections
            .Select(p => $"{p.Alias ?? visitor.Property(p.Table, p.Attribute)} = {visitor.Visit(p)}")
            .ToList();

        artifact.Projection.Append($"\n        .Select({scope.Param} => new {{ {string.Join(", ", members)} }})");
    }

    protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Offset is null && clauses.Limit is null)
        {
            return;
        }

        // T-SQL counts rows in bigint, Skip and Take in Int32; a value between the two has
        // no faithful LINQ form and dropping it would change which rows come back.
        if (clauses.Offset > int.MaxValue || clauses.Limit > int.MaxValue)
        {
            Report(
                ConversionRecordKind.Failure,
                "The pagination value exceeds Int32, which Skip and Take cannot carry; no artifact was generated.",
                QueryFeature.Pagination);
            return;
        }

        if (clauses.Offset is { } offset)
        {
            artifact.Pagination.Append($"\n        .Skip({offset})");
        }

        if (clauses.Limit is { } limit)
        {
            artifact.Pagination.Append($"\n        .Take({limit})");
        }
    }

    /// <summary>
    /// Renders a subquery operand as a nested chain from its own <c>ctx.Set&lt;T&gt;()</c>
    /// root (decision 061). The visitor decides what surrounds it - Contains for IN, Any for
    /// EXISTS, nothing for a scalar - and this renderer decides how the chain ends: bare for
    /// EXISTS, a one-column Select for IN, a terminal aggregate call for a scalar. A scalar
    /// subquery that is not a single ungrouped aggregate has no faithful LINQ form -
    /// First() would silently pick one row where SQL refuses several - and refuses.
    /// </summary>
    private string? RenderSubQuery(SubQueryInstruction subQuery, ComparisonOperator op)
    {
        var clauses = NormalizeSubQueryOperand(subQuery, op);
        if (clauses is null)
        {
            return null;
        }

        var scalar = op is not (ComparisonOperator.Exists or ComparisonOperator.In);

        if (scalar && (clauses.GroupBys.Count > 0 || clauses.Projections[0].Function is null))
        {
            Report(
                ConversionRecordKind.Failure,
                "A scalar subquery that is not a single ungrouped aggregate has no LINQ form - First() would silently pick one row where SQL refuses several; no artifact was generated.",
                QueryFeature.Subquery);
            return null;
        }

        if (op == ComparisonOperator.In
            && clauses.GroupBys.Count == 0
            && clauses.Projections[0].Function is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                "An aggregate projected without a grouping cannot stand inside a LINQ subquery's Select; no artifact was generated.",
                QueryFeature.Subquery);
            return null;
        }

        var savedScope = scope;
        var savedVisitor = visitor;
        var savedTuples = tupleAliases.ToList();
        var savedOrderingAfter = orderingAfterProjection;
        var savedContext = operandContext;
        var savedEnclosingVisitor = enclosingVisitor;
        var savedEnclosingParams = enclosingParams;

        enclosingVisitor = visitor;
        enclosingParams = new HashSet<string>(enclosingParams, StringComparer.Ordinal)
        {
            savedScope.Param,
            savedScope.ElementParam,
        };
        operandContext = op;

        var artifact = Compose(clauses);

        var chain = string.Concat(
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
            artifact.Projection,
            orderingAfterProjection,
            artifact.Pagination);

        if (scalar)
        {
            chain += TerminalAggregate(clauses.Projections[0]);
        }

        scope = savedScope;
        visitor = savedVisitor;
        tupleAliases.Clear();
        tupleAliases.AddRange(savedTuples);
        orderingAfterProjection = savedOrderingAfter;
        operandContext = savedContext;
        enclosingVisitor = savedEnclosingVisitor;
        enclosingParams = savedEnclosingParams;

        // The chain was written for a multi-line method body; embedded in a condition it
        // reads as one expression, so the step breaks are flattened.
        return chain.Replace("\n        ", "");
    }

    /// <summary>The terminal call a scalar subquery's single aggregate becomes.</summary>
    private string TerminalAggregate(ProjectInstruction projection)
    {
        if (projection.Function == "COUNT")
        {
            if (projection.Attribute != "*")
            {
                Report(
                    ConversionRecordKind.Convention,
                    $"COUNT({projection.Attribute}) was written as Count(), which counts rows rather than non-null values.",
                    QueryFeature.Aggregation);
            }

            return ".Count()";
        }

        var method = projection.Function switch
        {
            "SUM" => "Sum",
            "MIN" => "Min",
            "MAX" => "Max",
            "AVG" => "Average",
            _ => null,
        };

        if (method is null)
        {
            Report(
                ConversionRecordKind.Failure,
                $"Aggregate function {projection.Function} has no LINQ counterpart; the query was not generated.",
                QueryFeature.Aggregation);
            return string.Empty;
        }

        return $".{method}({scope.Param} => {visitor.Column(projection.Table, projection.Attribute, null)})";
    }

    protected override List<ConversionSource> BuildSetOperation(SetOperationInstruction instruction)
    {
        var chain = RenderSetOperation(instruction, out var elementEntity);
        if (chain is null)
        {
            return [];
        }

        var returnType = elementEntity is not null ? $"IQueryable<{elementEntity}>" : "IQueryable";
        var method =
            $$"""
            public static {{returnType}} Query(DbContext ctx)
            {
                return {{chain}};
            }
            """;

        return [new() { Content = method, ContentType = ConversionContentType.CSharpQuery }];
    }

    private string? RenderSetOperation(SetOperationInstruction instruction, out string? elementEntity)
    {
        elementEntity = null;

        var left = RenderOperandChain(instruction.Left, out var leftEntity);
        var right = RenderOperandChain(instruction.Right, out var rightEntity);
        if (left is null || right is null)
        {
            return null;
        }

        // LINQ set operations compose two queryables of one element type. Two whole-entity
        // operands of the same entity keep that type; two projections each build an anonymous
        // type and are left to the compilation level to judge. What cannot type-check at all
        // is a whole entity against anything else, and emitting it anyway would only move the
        // error into the consumer's build (decision 053).
        if (!string.Equals(leftEntity, rightEntity, StringComparison.Ordinal))
        {
            Report(
                ConversionRecordKind.Failure,
                "The two sides of the set operation materialize different element types, which LINQ cannot compose; no artifact was generated.",
                QueryFeature.SetOperation);
            return null;
        }

        var method = visitor.Visit(instruction);
        if (method.Length == 0)
        {
            return null;
        }

        elementEntity = leftEntity;
        return $"{left}\n        .{method}({right})";
    }

    /// <summary>
    /// One operand as a full chain from its own <c>ctx.Set&lt;T&gt;()</c> root. The entity
    /// name comes out only when the operand materializes whole entities of a nameable type;
    /// null stands for an anonymous element type.
    /// </summary>
    private string? RenderOperandChain(SubQueryInstruction operand, out string? elementEntity)
    {
        var body = Unwrap(operand.Instructions);

        if (body.Count == 1 && body[0] is SetOperationInstruction nested)
        {
            return RenderSetOperation(nested, out elementEntity);
        }

        elementEntity = null;

        var clauses = Normalize(body);
        if (clauses is null)
        {
            return null;
        }

        var artifact = Compose(clauses);
        elementEntity = clauses.ProjectsWholeEntity && !scope.Composite && !scope.Grouped
            ? artifact.ResultEntity
            : null;

        return string.Concat(
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
            artifact.Projection,
            orderingAfterProjection,
            artifact.Pagination);
    }

    protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact)
    {
        var chain = string.Concat(
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
            artifact.Projection,
            orderingAfterProjection,
            artifact.Pagination);

        // A projection into an anonymous type, a tuple produced by a join and a grouping all
        // have element types the artifact cannot name, so the method is typed non-generically.
        var returnType = clauses.ProjectsWholeEntity && !scope.Composite && !scope.Grouped
            ? $"IQueryable<{artifact.ResultEntity}>"
            : "IQueryable";

        var method =
            $$"""
            public static {{returnType}} Query(DbContext ctx)
            {
                return {{chain}};
            }
            """;

        return [new() { Content = method, ContentType = ConversionContentType.CSharpQuery }];
    }

    private static string Bare(string table) => EntityTableNaming.BareName(table);

    private static string SingularOf(string table) => EntityTableNaming.EntityNameFor(table);
}
