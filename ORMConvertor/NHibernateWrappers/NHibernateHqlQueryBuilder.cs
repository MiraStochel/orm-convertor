using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model;
using Model.AbstractRepresentation;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace NHibernateWrappers;

/// <summary>
/// Emits HQL, NHibernate's own query language (decision 022). HQL rather than NHibernate
/// LINQ, because a LINQ target would make this builder a near-copy of the EF Core one and
/// would cost the translation matrix its only non-LINQ .NET target.
///
/// Two artifacts leave here: the runnable C# method and the bare HQL, so that verification
/// and any other consumer can take the query itself without digging it out of the code
/// (decision 025).
/// </summary>
public class NHibernateHqlQueryBuilder : AbstractQueryBuilder
{
    private NHibernateHqlQueryVisitor visitor = null!;

    /// <summary>
    /// Alias dictionary of the scope currently being composed, kept so that a nested
    /// subquery can merge it under its own aliases: HQL writes a correlated reference to
    /// the outer query's alias verbatim, so the inner visitor has to know the outer
    /// entities too, with its own aliases shadowing (decision 061).
    /// </summary>
    private Dictionary<string, EntityMap> currentAliases = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, EntityMap>? enclosingAliases;

    public override TargetFrameworkDescriptor Descriptor => NHibernateDescriptor.Instance;

    protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact)
    {
        var aliased = AliasedEntities(clauses);
        if (enclosingAliases is not null)
        {
            foreach (var (outerAlias, outerMap) in enclosingAliases)
            {
                aliased.TryAdd(outerAlias, outerMap);
            }
        }

        currentAliases = aliased;
        visitor = new NHibernateHqlQueryVisitor(
            aliased,
            (kind, reason, feature) => Report(kind, reason, feature),
            RenderSubQuery);

        var map = EntityFor(clauses.From.Table);
        var entity = map?.Entity.Name ?? SingularOf(clauses.From.Table);
        var alias = clauses.From.Alias ?? entity.ToLowerInvariant();

        artifact.ResultEntity = entity;

        // HQL names the entity, not the table: the source's own qualified table name never
        // appears in the output, which is the clearest single sign that this is not SQL.
        artifact.Source.Append($"from {entity} {alias}");

        if (map is null)
        {
            Report(
                ConversionRecordKind.Convention,
                $"No entity was mapped to table '{clauses.From.Table}', so the entity name '{entity}' was derived from it.",
                QueryFeature.Projection,
                entity: entity);
        }
    }

    protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact)
    {
        foreach (var join in clauses.Joins)
        {
            if (artifact.Joins.Length > 0)
            {
                artifact.Joins.AppendLine();
            }

            artifact.Joins.Append("    ").Append(join.Accept(visitor));
        }
    }

    protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Filter is null)
        {
            return;
        }

        artifact.Filter.Append("where ").Append(clauses.Filter.Accept(visitor));
    }

    protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.GroupBys.Count == 0)
        {
            return;
        }

        artifact.Grouping
            .Append("group by ")
            .Append(string.Join(", ", clauses.GroupBys.Select(g => g.Accept(visitor))));
    }

    protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.PostFilter is null)
        {
            return;
        }

        artifact.PostFilter.Append("having ").Append(clauses.PostFilter.Accept(visitor));
    }

    protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.OrderBys.Count == 0)
        {
            return;
        }

        artifact.Ordering
            .Append("order by ")
            .Append(string.Join(", ", clauses.OrderBys.Select(o => o.Accept(visitor))));
    }

    protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact)
    {
        // Rule Q3: without a projection HQL materializes the whole entity, and the way to
        // say that is to leave the select clause out entirely.
        if (clauses.ProjectsWholeEntity)
        {
            return;
        }

        artifact.Projection
            .Append("select ")
            .Append(string.Join(", ", clauses.Projections.Select(p => p.Accept(visitor))));
    }

    /// <summary>
    /// HQL in NHibernate 5.7.0 has no limit or offset of its own: pagination belongs to the
    /// IQuery the generated method returns, so this slot holds API calls rather than query
    /// text and the final step places it after CreateQuery (decision 060). That the bare
    /// HQL artifact does not carry it is a property of the format, not a finding about the
    /// input, so no record is issued - the same reasoning decision 028 applied to the
    /// missing assembly attribute.
    /// </summary>
    protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Offset is null && clauses.Limit is null)
        {
            return;
        }

        if (clauses.Offset > int.MaxValue || clauses.Limit > int.MaxValue)
        {
            Report(
                ConversionRecordKind.Failure,
                "The pagination value exceeds Int32, which SetFirstResult and SetMaxResults cannot carry; no artifact was generated.",
                QueryFeature.Pagination);
            return;
        }

        if (clauses.Offset is { } offset)
        {
            artifact.Pagination.Append($"\n        .SetFirstResult({offset})");
        }

        if (clauses.Limit is { } limit)
        {
            artifact.Pagination.Append($"\n        .SetMaxResults({limit})");
        }
    }

    /// <summary>
    /// Renders a subquery operand as a bare HQL select (decision 061). HQL admits subqueries
    /// only in the select and where clauses, and inside one it takes neither an ordering nor
    /// a pagination: the ordering is dropped with a record - reordering the rows of an IN,
    /// EXISTS or scalar operand does not change which rows the outer query returns - while a
    /// pagination lives only on the IQuery API, which cannot reach inside the HQL text, so
    /// it refuses (the sentence decision 060 said for set-operation operands).
    /// </summary>
    private string? RenderSubQuery(SubQueryInstruction subQuery, ComparisonOperator op)
    {
        var clauses = NormalizeSubQueryOperand(subQuery, op);
        if (clauses is null)
        {
            return null;
        }

        if (clauses.Offset is not null || clauses.Limit is not null)
        {
            Report(
                ConversionRecordKind.Failure,
                "A pagination inside a subquery cannot be carried in HQL text - SetFirstResult and SetMaxResults live on the IQuery, outside the query; no artifact was generated.",
                QueryFeature.Pagination);
            return null;
        }

        if (clauses.OrderBys.Count > 0)
        {
            Report(
                ConversionRecordKind.Loss,
                "HQL does not allow an ordering inside a subquery; it was dropped, which does not change which rows the outer query returns.",
                QueryFeature.Ordering);
        }

        var enclosingVisitor = visitor;
        var enclosing = enclosingAliases;
        var current = currentAliases;

        enclosingAliases = currentAliases;
        var artifact = Compose(clauses);

        visitor = enclosingVisitor;
        enclosingAliases = enclosing;
        currentAliases = current;

        var parts = new[]
        {
            artifact.Projection,
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
        };

        // The slots are written for a multi-line query; inside parentheses the subquery
        // reads as one line, so the lines are joined with their indentation trimmed.
        return string.Join(" ", parts
            .Where(p => p.Length > 0)
            .Select(p => string.Join(" ", p.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()))));
    }

    protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact)
    {
        // HQL clause order is SQL's, so the projection moves back to the front here - the
        // steps themselves ran in relational order (decision 023).
        var parts = new[]
        {
            artifact.Projection,
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
        };

        var hql = string.Join("\n", parts.Where(p => p.Length > 0).Select(p => p.ToString()));
        var indented = string.Join("\n", hql.Split('\n').Select(line => "        " + line));

        var method =
            $$""""
            public static IQuery Query(ISession session)
            {
                return session.CreateQuery(
                    """
            {{indented}}
                    """){{artifact.Pagination}};
            }
            """";

        return
        [
            new() { Content = method, ContentType = ConversionContentType.CSharpQuery },
            new() { Content = hql, ContentType = ConversionContentType.HqlQuery },
        ];
    }

    private static string SingularOf(string table) => EntityTableNaming.EntityNameFor(table);
}
