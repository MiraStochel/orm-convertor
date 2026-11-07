using Model;
using SampleData;

namespace ORMConvertorAPI.Dtos;

/// <summary>
/// The SEDE query catalog of decision 208 as the page and the Java suite read it: the source
/// framework the queries are written for, the entity units over the SEDE tables, and the
/// queries, each naming the entity units it reads. A query the page runs is sent to
/// <c>/convert</c> as those units plus the query as one more unit.
/// </summary>
public record SedeCatalogDefinition(
    ORMEnum SourceOrm,
    string DataSet,
    List<ConversionSource> Entities,
    List<SedeQueryDefinition> Queries
);

/// <summary>
/// One query of the set: its number in SEDE, the part of the set and how much of it the tool
/// translates as numbers of their enums, the title and the description its author wrote, its
/// parameters with the values the run binds, the tables it reads (which are the names of its
/// entity units without the extension), the columns of its result, whether it orders its rows
/// and the ordering key the Java suite's run compares rows by (decision 170), its text in
/// T-SQL with the placeholders as parameters, the author's text with the placeholders of SEDE,
/// and the note of its status.
/// </summary>
public record SedeQueryDefinition(
    string Key,
    int QuerySetId,
    SedeSet Set,
    string Title,
    string Description,
    List<SedeParameterDefinition> Parameters,
    List<string> Tables,
    List<string> Columns,
    bool Ordered,
    List<string> OrderingKey,
    string Sql,
    string SedeSql,
    SedeTranslation Translation,
    string Note
);

public record SedeParameterDefinition(string Name, string SqlType, string Value);
