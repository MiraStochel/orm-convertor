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

    /// <summary>The profile of the implementation whose query this is (decision 076).</summary>
    protected abstract JpaImplementationProfile Profile { get; }

    /// <summary>
    /// What JPQL does not speak goes out as native SQL through createNativeQuery, written by
    /// the shared T-SQL writer under this builder's descriptor (decision 113).
    /// </summary>
    protected override AbstractQueryBuilder NativeSqlBuilder() => new JpaNativeSqlQueryBuilder(Descriptor, Profile);

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

        // An intermediate result is read under its own name, and always with an alias: HQL
        // does not resolve a path over a common table expression read without one (decision
        // 112, verified). It materializes into no class, so the query is the untyped one.
        var definition = IsDefinition(clauses.From.Table);
        var map = EntityFor(clauses.From.Table);
        var entity = definition ? clauses.From.Table : map?.Entity.Name ?? EntityTableNaming.EntityNameFor(clauses.From.Table);
        var alias = clauses.From.Alias ?? (definition ? entity : entity.ToLowerInvariant());

        var intermediate = aliased
            .Where(pair => pair.Value.Table is { } table && IsDefinition(table))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        visitor = new JpqlQueryVisitor(
            aliased,
            alias,
            (kind, reason, feature) => Report(kind, reason, feature),
            RenderSubQuery,
            Expressions,
            intermediate,
            Profile);

        artifact.ResultEntity = definition ? null : entity;
        artifact.Source.Append($"from {entity} {JpqlNames.Alias(alias)}");

        // JPQL has no other spelling of an entity name than the name itself (JpqlNames).
        if (!definition && Profile.EntityNamesRefused?.Contains(entity) == true)
        {
            ReportUnspoken(
                $"JPQL in {Profile.Implementation} does not read '{entity}', which is spelled like a word of its grammar, as the name of an entity",
                QueryFeature.Projection);
        }

        if (map is null && !definition)
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
        if (clauses.GroupBys.Count == 0)
        {
            return;
        }

        // An implementation that binds every literal as a parameter (EclipseLink 5.0.0,
        // measured) writes a key with a literal in it as another expression than the same
        // value in the select list, and SQL Server refuses the query; the native SQL writes
        // both the same way (decision 113).
        if (Profile.BindsLiterals && clauses.GroupBys.FirstOrDefault(g => g.Key.IsExpression && ContainsConstant(g.Key)) is { } bound)
        {
            ReportUnspoken(
                $"{Profile.Implementation} binds the literal of the grouping key '{bound.Key}' as a parameter, so its GROUP BY would differ from the same value in the select list",
                QueryFeature.ComputedGrouping);
            return;
        }

        artifact.Grouping.Append("group by ").Append(string.Join(", ", clauses.GroupBys.Select(g => g.Accept(visitor))));
    }

    /// <summary>Whether a constant stands anywhere in the operand short of a subquery, the conditions of a CASE included.</summary>
    private static bool ContainsConstant(QueryOperand operand)
        => operand.IsConstant || (operand.IsExpression && OperandStructure.Inside(operand.Expression!).Any(ContainsConstant));

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

        // The whole row of an intermediate result is its columns: HQL refuses `select d` over
        // a derived root, which has no identity to select by (decision 112, verified).
        if (clauses.ProjectsWholeEntity && IsDefinition(clauses.From.Table))
        {
            var row = JpqlNames.Alias(clauses.From.Alias ?? clauses.From.Table);
            artifact.Projection.Append(distinct).Append(string.Join(", ", ColumnsOf(clauses.From.Table).Select(column => $"{row}.{JpqlNames.Alias(column)}")));
            return;
        }

        if (clauses.ProjectsWholeEntity)
        {
            var alias = JpqlNames.Alias(clauses.From.Alias ?? artifact.ResultEntity!.ToLowerInvariant());
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
        // template (decision 085). T-SQL counts in bigint, so the native SQL carries it
        // (decision 113).
        if (clauses.Offset?.Value > int.MaxValue || clauses.Limit?.Value > int.MaxValue)
        {
            ReportUnspoken(
                "The pagination value exceeds Integer, which setFirstResult and setMaxResults cannot carry",
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
    /// the ordering is dropped with a record, the slice sends the query to native SQL, as
    /// for HQL (decision 113).
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
            ReportUnspoken(
                "A pagination inside a subquery cannot be carried in JPQL text - setFirstResult and setMaxResults live on the query object",
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
        return text is null || WithClause() is not { } with ? [] : FinalizeText(with + text, null, string.Empty);
    }

    /// <summary>
    /// The parameters a slice inside an intermediate result names in the text (decision
    /// 112): the slice of the query lives on the query object, but a definition's slice is
    /// part of its body, so its parameter is bound by name like any other.
    /// </summary>
    private readonly HashSet<string> boundInText = new(StringComparer.Ordinal);

    /// <summary>
    /// The definitions of the query as HQL's with clause before the statement (decision
    /// 112): each body composed through the eight steps on one line, its slice written into
    /// the text - offset and fetch first, which HQL takes inside a common table expression
    /// where JPQL takes no slice at all. Empty for a query that defines nothing, null when a
    /// body could not be rendered and the reason is on the channel. Only Hibernate reaches
    /// here: EclipseLink's descriptor sends a query with an intermediate result to native SQL
    /// before any step (decision 113).
    /// </summary>
    private string? WithClause()
    {
        boundInText.Clear();

        if (Definitions.Count == 0)
        {
            return string.Empty;
        }

        var rendered = new List<string>(Definitions.Count);
        foreach (var definition in Definitions)
        {
            var clauses = NormalizeDefinition(definition, out var setOperation);
            var body = setOperation is not null
                ? RenderSetOperation(setOperation)
                : clauses is not null ? OneLine(Compose(clauses), withOrdering: true) + SliceText(clauses) : null;

            if (body is null)
            {
                return null;
            }

            // A body that is a set operation - a recursive one is (decision 113) - spans lines,
            // and every one of them is indented, not only the first.
            var indented = string.Join("\n", body.Split('\n').Select(line => "    " + line));
            rendered.Add($"{definition.Name} as (\n{indented}\n)");
        }

        return $"with {string.Join(",\n", rendered)}\n";
    }

    /// <summary>The slice of a definition's body as HQL text: a number, or the parameter by the name the method binds it under.</summary>
    private string SliceText(QueryClauses clauses)
    {
        string Count(RowCount count)
        {
            if (count.Parameter is not { } parameter)
            {
                return count.Value!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            boundInText.Add(QueryParameterNaming.IdentifierFor(parameter));
            return parameter.IsPositional ? $"?{parameter.Position}" : $":{parameter.Name}";
        }

        var text = string.Empty;
        if (clauses.Offset is { } offset)
        {
            text += $" offset {Count(offset)} rows";
        }

        if (clauses.Limit is { } limit)
        {
            text += $" fetch first {Count(limit)} rows only";
        }

        return text;
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
            ReportUnspoken($"The set operation {instruction.OperationType} has no JPQL form", QueryFeature.SetOperation);
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
            ReportUnspoken(
                "A pagination inside a set operation operand cannot be carried in JPQL text",
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
        return WithClause() is { } with
            ? FinalizeText(with + jpql, clauses.ProjectsWholeEntity ? artifact.ResultEntity : null, artifact.Pagination.ToString())
            : [];
    }

    /// <summary>
    /// The two artifacts: a method returning the typed query for a whole-entity result and
    /// the untyped Query otherwise, and the bare JPQL in a text block.
    /// </summary>
    private List<ConversionSource> FinalizeText(string jpql, string? resultEntity, string pagination)
    {
        var indented = JpaQueryMethod.TextBlock(jpql);
        var typed = resultEntity is not null;
        var returnType = typed ? $"TypedQuery<{resultEntity}>" : "Query";
        var resultClass = typed ? $", {resultEntity}.class" : string.Empty;

        // setParameter takes the name or the order, whichever the query wrote, and takes a
        // collection for a collection parameter without a call of its own (decision 083).
        // Only the parameters the JPQL names are bound: the slice lives on the query object,
        // so a row count is an argument of setMaxResults and binding it by name again would
        // name a parameter the query does not have, which JPA rejects (decision 085).
        var bound = BoundParameters.ToList();
        var binding = string.Concat(Parameters.Where(p => bound.Contains(p) || boundInText.Contains(QueryParameterNaming.IdentifierFor(p))).Select(p =>
        {
            var name = QueryParameterNaming.IdentifierFor(p);
            var key = p.IsPositional ? p.Position!.Value.ToString() : $"\"{p.Name}\"";
            return $"\n        .setParameter({key}, {name})";
        }));

        var method =
            $$""""
            public static {{returnType}} {{MethodName}}(EntityManager em{{JpaQueryMethod.Parameters(Parameters)}}) {
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
