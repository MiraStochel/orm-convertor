using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model;
using Model.QueryInstructions;

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

    public override TargetFrameworkDescriptor Descriptor => NHibernateDescriptor.Instance;

    protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact)
    {
        visitor = new NHibernateHqlQueryVisitor(
            AliasedEntities(clauses),
            (kind, reason, feature) => Report(kind, reason, feature));

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
                    """);
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
