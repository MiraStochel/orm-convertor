using Model;
using Model.AbstractRepresentation;

namespace AbstractWrappers;

/// <summary>
/// Reads the queries of a unit into query builders. The content type comes along because a
/// framework's queries may arrive in more than one language - Dapper's SQL bare or wrapped
/// in a C# call, NHibernate's LINQ beside HQL - and which one it is, is what the unit
/// declares, never what its text looks like (decisions 025 and 047).
///
/// A unit may be a mapping and a query at once (decision 081): the orchestration offers
/// every non-blank unit to both passes and this parser takes the ones it claims through
/// <see cref="IParser.CanParse"/>, so a document whose halves are read by two parsers of
/// the same framework - the hbm.xml with a class beside a named query - needs neither a
/// value of its own in the vocabulary nor a client that cuts it up.
/// </summary>
public interface IQueryParser : IParser
{
    /// <param name="contentType">Language of the unit, as the caller declared it.</param>
    /// <param name="source">The unit's content.</param>
    /// <param name="entityMaps">
    /// Mapping IR of the same conversion, where a target naming entities rather than tables
    /// needs to map back through it.
    /// </param>
    /// <returns>
    /// The builders this parser filled, one per query it read out of the unit. The parser
    /// states what came of the unit instead of the orchestration guessing it from side
    /// effects - the query-side half of decision 066 - and an empty collection is no error
    /// in itself: a mapping document that happens to carry no query is a legitimate input
    /// (decision 081). Builders come from the factory the parser was constructed with,
    /// because a builder belongs to the target framework and a parser to the source (S1).
    /// </returns>
    IReadOnlyCollection<AbstractQueryBuilder> Parse(
        ConversionContentType contentType,
        string source,
        IReadOnlyList<EntityMap>? entityMaps = null);
}
