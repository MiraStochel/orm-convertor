using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model;
using TransactSql;

namespace JakartaPersistence;

/// <summary>
/// The escape path of both JPA targets (decision 113): the query their query language does
/// not speak - an intermediate result in EclipseLink's JPQL, a slice inside a subquery in
/// either -, written whole by the shared T-SQL writer, the text the Dapper target writes,
/// and handed to <c>EntityManager.createNativeQuery</c>, with the entity class where the
/// query materializes the whole entity. The parameters are positional, <c>?1</c>, which is
/// the form the specification gives a native query, numbered in the order of the method's
/// signature and bound by setParameter.
///
/// The method returns Query in every case: Jakarta Persistence 3.2.0 declares
/// createNativeQuery with a result class as returning Query, not TypedQuery (verified
/// against the pinned API when the path was written), so the method says what the call
/// gives. Beside it goes the bare SQL, as Dapper publishes it (decision 025): it is the
/// query, and what the third level of verification reads (decision 113).
/// </summary>
/// <param name="descriptor">The descriptor of the JPA target whose records these are.</param>
/// <param name="profile">The implementation's profile: whether its native query expands a collection, or the method has to (decision 123).</param>
public sealed class JpaNativeSqlQueryBuilder(TargetFrameworkDescriptor descriptor, JpaImplementationProfile profile)
    : AbstractSqlQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => descriptor;

    protected override ConversionContentType MethodArtifact => ConversionContentType.JavaQuery;

    /// <summary>Java methods are camelCase, as the JPQL builder names them (decision 081).</summary>
    protected override string MethodName => QueryMethodNaming.CamelCase(QueryName, "query");

    /// <summary>
    /// A native query of JPA binds by position, so a positional parameter of the source stays
    /// what it was and is not reported as renamed; a named one is numbered in the text and
    /// keeps its name in the signature.
    /// </summary>
    protected override bool WritesPositionalParameters => true;

    protected override List<ConversionSource> Emit(string sql, string? resultEntity)
    {
        if (RefusesAnEntityOverDifferentRows(resultEntity, "createNativeQuery with the entity class"))
        {
            return [];
        }

        // Where the implementation expands a collection itself (Hibernate 7.4.5), every
        // parameter takes the next position in the order of the signature and is bound once.
        // Where it does not (EclipseLink 5.0.0), the method expands the collection (decision
        // 123): the scalars take the positions 1..s, each collection as many positions after
        // them as it has elements, counted when the method runs.
        var expanded = profile.NativeQueryExpandsCollection
            ? []
            : Parameters.Where(p => p.IsCollection).ToList();
        var fixedParameters = Parameters.Where(p => !expanded.Contains(p)).ToList();
        var positions = fixedParameters
            .Select((parameter, index) => (parameter, index))
            .ToDictionary(pair => QueryParameterNaming.IdentifierFor(pair.parameter), pair => pair.index + 1, StringComparer.Ordinal);

        var text = SqlPlaceholders.Respell(sql, Parameters, p =>
        {
            var name = QueryParameterNaming.IdentifierFor(p);
            if (!positions.TryGetValue(name, out var position))
            {
                return $"(?{name})";
            }

            return p.IsCollection ? $"(?{position})" : $"?{position}";
        });

        var resultClass = resultEntity is null ? string.Empty : $", {resultEntity}.class";
        var binding = string.Concat(fixedParameters.Select(p =>
        {
            var name = QueryParameterNaming.IdentifierFor(p);
            return $"\n        .setParameter({positions[name]}, {name})";
        }));

        if (expanded.Count == 0)
        {
            var plain =
                $$""""
                public static Query {{MethodName}}(EntityManager em{{JpaQueryMethod.Parameters(Parameters)}}) {
                    return em.createNativeQuery("""
                {{JpaQueryMethod.TextBlock(text)}}
                        """{{resultClass}}){{binding}};
                }
                """";

            return
            [
                new() { Content = plain, ContentType = ConversionContentType.JavaQuery },
                new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
            ];
        }

        var names = expanded.Select(QueryParameterNaming.IdentifierFor).ToList();
        var placeholders = string.Concat(names.Select(name =>
            $$"""

                var {{name}}Placeholders = new StringBuilder();
                for (var i = 0; i < {{name}}.size(); i++) {
                    {{name}}Placeholders.append(i == 0 ? "?" : ", ?").append(++position);
                }
            """));
        var replacements = string.Concat(names.Select(name => $".replace(\"(?{name})\", \"(\" + {name}Placeholders + \")\")"));
        var elements = string.Concat(names.Select(name =>
            $$"""

                for (var element : {{name}}) {
                    query.setParameter(++position, element);
                }
            """));

        var method =
            $$""""
            public static Query {{MethodName}}(EntityManager em{{JpaQueryMethod.Parameters(Parameters)}}) {
                var position = {{fixedParameters.Count}};{{placeholders}}
                var query = em.createNativeQuery("""
            {{JpaQueryMethod.TextBlock(text)}}
                    """{{replacements}}{{resultClass}}){{binding}};
                position = {{fixedParameters.Count}};{{elements}}
                return query;
            }
            """";

        return
        [
            new() { Content = method, ContentType = ConversionContentType.JavaQuery },
            new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ];
    }
}
