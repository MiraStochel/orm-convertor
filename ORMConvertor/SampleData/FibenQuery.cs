namespace SampleData;

/// <summary>
/// Where a FIBEN query stands in Kim's taxonomy of nested queries (W. Kim, 1982), as the
/// benchmark classifies it: a single block, or a nested query whose inner block is
/// uncorrelated without an aggregate (N), uncorrelated with one (A), correlated without one
/// (J) or correlated with one (JA). The values are explicit because they travel over REST as
/// numbers, as every enum there does.
/// </summary>
public enum FibenNesting
{
    NonNested = 10,
    TypeN = 20,
    TypeA = 30,
    TypeJ = 40,
    TypeJA = 50,
}

/// <summary>
/// How much of a FIBEN query the tool translates (decision 149), in the three values of
/// decision 110. The value is a claim the on-demand run checks, not a guess: every query with
/// the first value has to reach every target from its text and agree with the source over the
/// data; the run records which do.
/// </summary>
public enum FibenTranslation
{
    /// <summary>The text states the benchmark's query, and the run holds every target to it.</summary>
    AsSpecified = 10,

    /// <summary>The text returns less or other than the benchmark's query; the note says what.</summary>
    Simplified = 20,

    /// <summary>The tool does not translate the query; the note says why, and the run checks that it is refused.</summary>
    NotTranslated = 30,
}

/// <summary>
/// One of the 237 distinct SQL queries of the FIBEN benchmark (decision 149), as
/// <c>SampleData/Fiben/queries.json</c> carries it: its number in the benchmark, Kim's type,
/// the natural-language questions it answers - the first the benchmark's own, the rest its
/// paraphrases -, the tables it reads, the columns it projects in their order and the ordering
/// key the run compares its rows by (decision 170: the columns its ordering falls on, in its
/// order; empty for a query without one), its text in T-SQL over the tables of
/// <c>database/fiben/schema.sql</c>, written for Dapper, the benchmark's own text in the Db2
/// dialect it was converted from, and how much of it the tool translates with a note. A query
/// has no parameter: the benchmark writes every value into the text.
/// </summary>
public sealed record FibenQuery(
    string Key,
    int Number,
    FibenNesting Nesting,
    IReadOnlyList<string> Questions,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> OrderingKey,
    string Sql,
    string BenchmarkSql,
    FibenTranslation Translation,
    string Note)
{
    /// <summary>The benchmark's own question; the paraphrases follow it in <see cref="Questions"/>.</summary>
    public string Question => Questions[0];

    /// <summary>Whether the query orders its rows: a reading of the key, never a fact of its own.</summary>
    public bool Ordered => OrderingKey.Count > 0;
}
