using Common.Naming;
using JavaEntityParsing;
using Model.AbstractRepresentation;
using Model.QueryInstructions.Conditions;

namespace JakartaPersistence;

/// <summary>
/// What the two JPA query builders write alike into the Java method around a query - the
/// one in JPQL and the one in native SQL (decision 113): the declarations of its parameters
/// and the text block the query stands in.
/// </summary>
internal static class JpaQueryMethod
{
    /// <summary>
    /// The parameters as Java declarations, appended after the EntityManager (decision 083).
    /// A scalar goes in as the primitive, because a comparison never tests NULL - that is its
    /// own operator (decision 002) - and a collection as Collection of the wrapper, which is
    /// what setParameter binds and the only element form a Java generic takes.
    /// </summary>
    public static string Parameters(IEnumerable<QueryParameter> parameters)
        => string.Concat(parameters.Select(p =>
        {
            var element = LangType.Scalar(p.Type!.Value);
            var type = p.IsCollection
                ? $"Collection<{JavaTypeConvertor.ToString(element, forceWrapper: true)}>"
                : JavaTypeConvertor.ToString(element);

            return $", {type} {QueryParameterNaming.IdentifierFor(p)}";
        }));

    /// <summary>
    /// The query as the lines of a Java text block, each indented to the closing delimiter
    /// the method writes. A text block reads a backslash as the start of an escape and three
    /// quotes as its end, so both are escaped - a LIKE whose escape character is a backslash
    /// would otherwise come out as a different pattern, or not compile.
    /// </summary>
    public static string TextBlock(string query)
    {
        var escaped = query.Replace("\\", "\\\\").Replace("\"\"\"", "\\\"\"\"");
        return string.Join("\n", escaped.Split('\n').Select(line => "        " + line));
    }
}
