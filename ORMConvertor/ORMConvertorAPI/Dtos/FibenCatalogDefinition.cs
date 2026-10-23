using Model;
using SampleData;

namespace ORMConvertorAPI.Dtos;

/// <summary>
/// The FIBEN query catalog of decision 149 as the page and the Java suite read it: the source
/// framework the queries are written for, the entity units over the FIBEN tables, and the 237
/// queries, each naming the entity units it reads. A query the page runs is sent to
/// <c>/convert</c> as those units plus the query as one more unit.
/// </summary>
public record FibenCatalogDefinition(
    ORMEnum SourceOrm,
    string DataSet,
    List<ConversionSource> Entities,
    List<FibenQueryDefinition> Queries
);

/// <summary>
/// One query of the benchmark: its number, Kim's type and how much of it the tool translates
/// as numbers of their enums, the questions it answers - the benchmark's own first -, the
/// tables it reads (which are the names of its entity units without the extension), the
/// columns it projects, whether it orders its rows and the ordering key the Java suite's run
/// compares rows by (decision 170: the columns the ordering falls on, in its order), its text
/// in T-SQL, the benchmark's text in the Db2 dialect, and the note of its status.
/// </summary>
public record FibenQueryDefinition(
    string Key,
    int Number,
    FibenNesting Nesting,
    List<string> Questions,
    List<string> Tables,
    List<string> Columns,
    bool Ordered,
    List<string> OrderingKey,
    string Sql,
    string BenchmarkSql,
    FibenTranslation Translation,
    string Note
);
