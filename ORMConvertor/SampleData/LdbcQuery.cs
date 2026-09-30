namespace SampleData;

/// <summary>
/// The LDBC SNB workload a query belongs to, in the order the specification lists them. The
/// values are explicit because they travel over REST as numbers, as every enum there does.
/// </summary>
public enum LdbcWorkload
{
    InteractiveShort = 10,
    InteractiveComplex = 20,
    BusinessIntelligence = 30,
}

/// <summary>
/// How much of an LDBC query the tool translates (decision 110). The value is a claim the
/// suite checks, not a guess: <c>LdbcCatalogTest</c> converts every query with a text from
/// Dapper into the other five frameworks over the LDBC tables as the catalog.
/// </summary>
public enum LdbcTranslation
{
    /// <summary>
    /// The text returns what the specification defines. A parameter the caller computes from
    /// the specification's own - the end of a window as its start plus a duration - does not
    /// change that, and the note says where it happens.
    /// </summary>
    AsSpecified = 10,

    /// <summary>The text returns less or other than the specification defines; the note says what.</summary>
    Simplified = 20,

    /// <summary>
    /// The tool does not translate the query. When the query has a text, it is the natural
    /// T-SQL formulation and the suite checks that the tool refuses it with a Failure record;
    /// without a text, the note says why no single SELECT states it at all.
    /// </summary>
    NotTranslated = 30,
}

/// <summary>
/// One parameter of an LDBC query: its name without the @, the SQL Server type of its
/// value, and an example value that returns rows over the scale factor 1 data set. A list
/// parameter is Dapper's collection parameter (decision 106), with the example values
/// separated by commas.
/// </summary>
public sealed record LdbcParameter(string Name, string SqlType, string Example, bool IsList = false);

/// <summary>
/// A target framework that refuses a query the others translate, and why - in the words of
/// the page, not of the record, which the page shows beside it when the query is run.
/// </summary>
public sealed record LdbcRefusal(Model.ORMEnum Target, string Reason);

/// <summary>
/// One read query of the LDBC Social Network Benchmark, as the page of decision 110 shows
/// it: the number and title the specification gives it, how much of it the tool translates,
/// a note saying what the text exercises, simplifies or cannot state, and the T-SQL text
/// over the tables of <c>database/ldbc/schema.sql</c>, written for Dapper. A translated query
/// reaches every target except those it names as refusing it.
/// </summary>
public sealed record LdbcQuery(
    string Key,
    LdbcWorkload Workload,
    int Number,
    string Title,
    LdbcTranslation Translation,
    string Note,
    string? Sql,
    IReadOnlyList<LdbcParameter> Parameters,
    IReadOnlyList<LdbcRefusal>? RefusedBy = null)
{
    /// <summary>The targets that refuse the query although it is translated into the others.</summary>
    public IReadOnlyList<LdbcRefusal> Refusals => RefusedBy ?? [];
}
