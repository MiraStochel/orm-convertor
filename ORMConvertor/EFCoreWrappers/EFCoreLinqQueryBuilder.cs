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

    public override TargetFrameworkDescriptor Descriptor => EFCoreDescriptor.Instance;

    protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact)
    {
        var map = EntityFor(clauses.From.Table);
        var entity = map?.Entity.Name ?? SingularOf(clauses.From.Table);

        scope = new LinqScope
        {
            Entities = AliasedEntities(clauses),
            Param = clauses.From.Alias ?? "c",
        };
        scope.ElementParam = scope.Param == "e" ? "el" : "e";

        visitor = new EFCoreLinqQueryVisitor(scope, (kind, reason, feature) => Report(kind, reason, feature));

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
                Report(
                    ConversionRecordKind.Loss,
                    $"The join onto '{join.RightTable}' is not a conjunction of column equalities, which is the only form a LINQ join takes; it was dropped.",
                    QueryFeature.Join);
                continue;
            }

            var method = join.Kind switch
            {
                JoinKind.Inner => "Join",
                JoinKind.Left => "LeftJoin",
                JoinKind.Right => "RightJoin",
                _ => null,
            };

            if (method is null)
            {
                // EF Core 10 has LeftJoin and RightJoin but no full outer join. That is a
                // narrowing inside the JoinKind category, so it is reported here at the point
                // of emission rather than declared in the descriptor.
                Report(
                    ConversionRecordKind.Loss,
                    "A full outer join has no LINQ operator in EF Core 10; an inner join was generated instead.",
                    QueryFeature.JoinKind);
                method = "Join";
            }

            var leftParam = scope.Param;
            var leftKeys = KeySelector(pairs.Select(p => visitor.Operand(p.Left)).ToList());
            var rightKeys = KeySelector(pairs
                .Select(p => $"{rightAlias}.{PropertyFor(rightMap, p.Right.Property ?? p.Right.Constant?.Text ?? string.Empty)}")
                .ToList());

            var members = scope.Composite
                ? string.Join(", ", tupleAliases.Select(a => $"{leftParam}.{a}")) + $", {rightAlias}"
                : $"{leftParam}, {rightAlias}";

            artifact.Joins.Append(
                $"\n        .{method}(ctx.Set<{rightEntity}>(), " +
                $"{leftParam} => {leftKeys}, " +
                $"{rightAlias} => {rightKeys}, " +
                $"({leftParam}, {rightAlias}) => new {{ {members} }})");

            tupleAliases.Add(rightAlias);
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
            if (!tupleAliases.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return "row" + tupleAliases.Count;
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
            orderingAfterProjection);

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
