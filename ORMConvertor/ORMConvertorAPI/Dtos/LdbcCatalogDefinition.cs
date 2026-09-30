using Model;
using SampleData;

namespace ORMConvertorAPI.Dtos;

/// <summary>
/// The LDBC query catalog of decision 110 as the page reads it: the source framework the
/// queries are written for, the entity units over the LDBC tables, and the 41 read queries.
/// A query the page runs is sent to <c>/convert</c> as the entity units plus the query as one
/// more unit, so the page states no unit of its own.
/// </summary>
public record LdbcCatalogDefinition(
    ORMEnum SourceOrm,
    List<ConversionSource> Entities,
    List<LdbcQueryDefinition> Queries
);

/// <summary>
/// One read query of the specification: its workload and number, the title the specification
/// gives it, how much of it the tool translates and the note saying what that means, the T-SQL
/// text when there is one, the parameters with example values over scale factor 1, and the
/// targets that refuse it although the others translate it.
/// </summary>
public record LdbcQueryDefinition(
    string Key,
    LdbcWorkload Workload,
    int Number,
    string Title,
    LdbcTranslation Translation,
    string Note,
    string? Sql,
    List<LdbcParameterDefinition> Parameters,
    List<LdbcRefusalDefinition> RefusedBy
);

/// <summary>A parameter: name without the @, SQL Server type, example value; a list's values are comma-separated.</summary>
public record LdbcParameterDefinition(string Name, string SqlType, string Example, bool IsList);

/// <summary>A target that refuses the query, and why, in the page's words.</summary>
public record LdbcRefusalDefinition(ORMEnum Target, string Reason);
