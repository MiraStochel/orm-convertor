using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using JavaEntityParsing;
using Model;
using Model.AbstractRepresentation;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace JakartaPersistence;

/// <summary>
/// Emits JPQL (decision 077): the query language both implementations share, in the
/// standard form wherever JPQL 3.2 has one. Two artifacts leave here, as for NHibernate
/// (decision 025): a Java method over the EntityManager, and the bare JPQL. Pagination
/// lives on the query object outside the text, exactly as with HQL (decision 060);
/// set operations, standard since JPQL 3.2, compose the operands through the same eight
/// steps the Dapper builder uses.
/// </summary>
public abstract class AbstractJpaQueryBuilder : AbstractQueryBuilder
{
    private JpqlQueryVisitor visitor = null!;

    private Dictionary<string, EntityMap> currentAliases = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, EntityMap>? enclosingAliases;

    protected override ConversionContentType MethodArtifact => ConversionContentType.JavaQuery;

    /// <summary>Java methods are camelCase, so the name of a named query is spelled that way (decision 081).</summary>
    protected override string MethodName => QueryMethodNaming.CamelCase(QueryName, "query");

    /// <summary>
    /// JPQL is the one target language of the six that spells a positional parameter, so a
    /// positional parameter stays positional here and is renamed nowhere (decision 083).
    /// </summary>
    protected override bool WritesPositionalParameters => true;

    /// <summary>
    /// The slice is setFirstResult and setMaxResults on the query object, so a bound row
    /// count never reaches the JPQL text (decision 085).
    /// </summary>
    protected override bool WritesRowCountsIntoQueryText => false;

    /// <summary>
    /// The parameters as Java declarations, appended after the EntityManager (decision 083).
    /// A scalar goes in as the primitive, because a comparison never tests NULL - that is its
    /// own operator (decision 002) - and a collection as Collection of the wrapper, which is
    /// what setParameter binds and the only element form a Java generic takes.
    /// </summary>
    private string JavaParameters()
        => string.Concat(Parameters.Select(p =>
        {
            var element = LangType.Scalar(p.Type!.Value);
            var type = p.IsCollection
                ? $"Collection<{JavaTypeConvertor.ToString(element, forceWrapper: true)}>"
                : JavaTypeConvertor.ToString(element);

            return $", {type} {QueryParameterNaming.IdentifierFor(p)}";
        }));

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

        var map = EntityFor(clauses.From.Table);
        var entity = map?.Entity.Name ?? EntityTableNaming.EntityNameFor(clauses.From.Table);
        var alias = clauses.From.Alias ?? entity.ToLowerInvariant();

        visitor = new JpqlQueryVisitor(
            aliased,
            alias,
            (kind, reason, feature) => Report(kind, reason, feature),
            RenderSubQuery);

        artifact.ResultEntity = entity;
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
        if (clauses.Filter is not null)
        {
            artifact.Filter.Append("where ").Append(clauses.Filter.Accept(visitor));
        }
    }

    protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.GroupBys.Count > 0)
        {
            artifact.Grouping.Append("group by ").Append(string.Join(", ", clauses.GroupBys.Select(g => g.Accept(visitor))));
        }
    }

    protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.PostFilter is not null)
        {
            artifact.PostFilter.Append("having ").Append(clauses.PostFilter.Accept(visitor));
        }
    }

    protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.OrderBys.Count > 0)
        {
            artifact.Ordering.Append("order by ").Append(string.Join(", ", clauses.OrderBys.Select(o => o.Accept(visitor))));
        }
    }

    /// <summary>
    /// The select clause, always written: the standard form names the identification
    /// variable for a whole-entity projection (rule Q3 in JPQL's spelling), and DISTINCT
    /// sits inside it (decision 073).
    /// </summary>
    protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact)
    {
        var distinct = clauses.Distinct ? "select distinct " : "select ";

        if (clauses.ProjectsWholeEntity)
        {
            var alias = clauses.From.Alias ?? artifact.ResultEntity!.ToLowerInvariant();
            artifact.Projection.Append(distinct).Append(alias);
            return;
        }

        artifact.Projection.Append(distinct).Append(string.Join(", ", clauses.Projections.Select(p => p.Accept(visitor))));
    }

    /// <summary>
    /// JPQL has no limit or offset in the text: the slice is setFirstResult and
    /// setMaxResults on the query object (decision 060), so the slot holds API calls the
    /// final step places after createQuery, and the bare artifact does not carry it -
    /// a property of the format, not a finding about the input.
    /// </summary>
    protected override void BuildPagination(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Offset is null && clauses.Limit is null)
        {
            return;
        }

        // Only a stated number can be out of range; a bound count is typed Int by the
        // template (decision 085).
        if (clauses.Offset?.Value > int.MaxValue || clauses.Limit?.Value > int.MaxValue)
        {
            Report(
                ConversionRecordKind.Failure,
                "The pagination value exceeds Integer, which setFirstResult and setMaxResults cannot carry; no artifact was generated.",
                QueryFeature.Pagination);
            return;
        }

        // A bound count is not a parameter of the query here but an argument of the call, so
        // it is passed on where a number would stand and nothing binds it by name.
        if (clauses.Offset is { } offset)
        {
            artifact.Pagination.Append($"\n        .setFirstResult({Spelled(offset)})");
        }

        if (clauses.Limit is { } limit)
        {
            artifact.Pagination.Append($"\n        .setMaxResults({Spelled(limit)})");
        }
    }

    /// <summary>
    /// A subquery operand as a bare JPQL select (decision 061). JPQL admits subqueries in
    /// the where and having clauses and takes neither an ordering nor a slice inside one:
    /// the ordering is dropped with a record, the slice refuses, as for HQL.
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
                "A pagination inside a subquery cannot be carried in JPQL text - setFirstResult and setMaxResults live on the query object; no artifact was generated.",
                QueryFeature.Pagination);
            return null;
        }

        if (clauses.OrderBys.Count > 0)
        {
            Report(
                ConversionRecordKind.Loss,
                "JPQL does not allow an ordering inside a subquery; it was dropped, which does not change which rows the outer query returns.",
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

        return OneLine(artifact, withOrdering: false);
    }

    /// <summary>
    /// Set operations, standard since JPQL 3.2: each operand rendered through the eight
    /// steps and joined by the operator; a nested operation is parenthesized so that its
    /// grouping survives. An ordering or a slice over the composed result has no place in
    /// the text - the ordering is dropped with a record, a slice refuses (decision 060).
    /// </summary>
    protected override List<ConversionSource> BuildSetOperation(SetOperationInstruction instruction)
    {
        var text = RenderSetOperation(instruction);
        return text is null ? [] : FinalizeText(text, null, string.Empty);
    }

    private string? RenderSetOperation(SetOperationInstruction instruction)
    {
        var left = RenderSetOperand(instruction.Left);
        var right = RenderSetOperand(instruction.Right);
        if (left is null || right is null)
        {
            return null;
        }

        var keyword = instruction.OperationType switch
        {
            SetOperationType.Union => "union",
            SetOperationType.UnionAll => "union all",
            SetOperationType.Intersect => "intersect",
            SetOperationType.Except => "except",
            SetOperationType.ExceptAll => "except all",
            _ => null,
        };

        if (keyword is null)
        {
            Report(ConversionRecordKind.Failure, $"The set operation {instruction.OperationType} has no JPQL form; no artifact was generated.", QueryFeature.SetOperation);
            return null;
        }

        return $"{left}\n{keyword}\n{right}";
    }

    private string? RenderSetOperand(SubQueryInstruction operand)
    {
        var body = Unwrap(operand.Instructions);

        if (body.Count == 1 && body[0] is SetOperationInstruction nested)
        {
            var text = RenderSetOperation(nested);
            return text is null ? null : $"({text})";
        }

        var clauses = Normalize(body);
        if (clauses is null)
        {
            return null;
        }

        if (clauses.Offset is not null || clauses.Limit is not null)
        {
            Report(ConversionRecordKind.Failure,
                "A pagination inside a set operation operand cannot be carried in JPQL text; no artifact was generated.",
                QueryFeature.Pagination);
            return null;
        }

        var artifact = Compose(clauses);
        return OneLine(artifact, withOrdering: true);
    }

    private static string OneLine(QueryArtifact artifact, bool withOrdering)
    {
        var parts = new List<System.Text.StringBuilder>
        {
            artifact.Projection, artifact.Source, artifact.Joins, artifact.Filter, artifact.Grouping, artifact.PostFilter,
        };

        if (withOrdering)
        {
            parts.Add(artifact.Ordering);
        }

        return string.Join(" ", parts
            .Where(p => p.Length > 0)
            .Select(p => string.Join(" ", p.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()))));
    }

    protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact)
    {
        var parts = new[]
        {
            artifact.Projection, artifact.Source, artifact.Joins, artifact.Filter,
            artifact.Grouping, artifact.PostFilter, artifact.Ordering,
        };

        var jpql = string.Join("\n", parts.Where(p => p.Length > 0).Select(p => p.ToString()));
        return FinalizeText(jpql, clauses.ProjectsWholeEntity ? artifact.ResultEntity : null, artifact.Pagination.ToString());
    }

    /// <summary>
    /// The two artifacts: a method returning the typed query for a whole-entity result and
    /// the untyped Query otherwise, and the bare JPQL in a text block.
    /// </summary>
    private List<ConversionSource> FinalizeText(string jpql, string? resultEntity, string pagination)
    {
        var indented = string.Join("\n", jpql.Split('\n').Select(line => "        " + line));
        var typed = resultEntity is not null;
        var returnType = typed ? $"TypedQuery<{resultEntity}>" : "Query";
        var resultClass = typed ? $", {resultEntity}.class" : string.Empty;

        // setParameter takes the name or the order, whichever the query wrote, and takes a
        // collection for a collection parameter without a call of its own (decision 083).
        // Only the parameters the JPQL names are bound: the slice lives on the query object,
        // so a row count is an argument of setMaxResults and binding it by name again would
        // name a parameter the query does not have, which JPA rejects (decision 085).
        var binding = string.Concat(BoundParameters.Select(p =>
        {
            var name = QueryParameterNaming.IdentifierFor(p);
            var key = p.IsPositional ? p.Position!.Value.ToString() : $"\"{p.Name}\"";
            return $"\n        .setParameter({key}, {name})";
        }));

        var method =
            $$""""
            public static {{returnType}} {{MethodName}}(EntityManager em{{JavaParameters()}}) {
                return em.createQuery("""
            {{indented}}
                    """{{resultClass}}){{binding}}{{pagination}};
            }
            """";

        return
        [
            new() { Content = method, ContentType = ConversionContentType.JavaQuery },
            new() { Content = jpql, ContentType = ConversionContentType.JpqlQuery },
        ];
    }
}
