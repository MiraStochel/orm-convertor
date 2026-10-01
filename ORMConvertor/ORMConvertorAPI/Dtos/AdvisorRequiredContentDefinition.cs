using Model;

namespace ORMConvertorAPI.Dtos;

/// <summary>
/// What the Advisor screen collects for one source framework: the units the conversion of
/// every query shares, and the templates of the queries it measures, each of which gets a
/// weight. Two lists because they are two fields of the Advisor's own form - a C# unit
/// declares its language and nothing else since decision 111, so the content type can no
/// longer tell a query template from an entity unit.
/// </summary>
public record AdvisorRequiredContentDefinition(
    ORMEnum OrmType,
    List<RequiredContentUnit> Required,
    List<RequiredContentUnit> Queries
);
