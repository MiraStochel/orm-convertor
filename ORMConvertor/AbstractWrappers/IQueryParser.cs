using Model;
using Model.AbstractRepresentation;

namespace AbstractWrappers;

/// <summary>
/// Reads a query unit into the query builder. The content type comes along because a
/// framework's queries may arrive in more than one language - Dapper's SQL bare or wrapped
/// in a C# call, NHibernate's LINQ beside HQL - and which one it is, is what the unit
/// declares, never what its text looks like (decisions 025 and 047).
/// </summary>
public interface IQueryParser : IParser
{
    /// <param name="contentType">Language of the unit, as the caller declared it.</param>
    /// <param name="source">The unit's content.</param>
    /// <param name="entityMaps">
    /// Mapping IR of the same conversion, where a target naming entities rather than tables
    /// needs to map back through it.
    /// </param>
    void Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null);
}
