namespace SampleData;

/// <summary>
/// The part of the SEDE set a query is from (decision 208): the validation or the test part,
/// the two parts whose queries the authors of the set reviewed by hand. The values are
/// explicit because they travel over REST as numbers, as every enum there does.
/// </summary>
public enum SedeSet
{
    Validation = 10,
    Test = 20,
}

/// <summary>
/// How much of a SEDE query the tool translates (decision 208), in the three values of
/// decision 110. The value is a claim the on-demand run checks: a query with the first value
/// is one valid read over the schema as its author wrote it, and the run records which target
/// carries it and which refuses it; a query with the third is not - a script, a text that does
/// not parse or is not valid over the schema, a result no typed target can carry - and the note
/// says why.
/// </summary>
public enum SedeTranslation
{
    /// <summary>The text states the author's query, and the run holds every target to it.</summary>
    AsSpecified = 10,

    /// <summary>The text returns less or other than the author's query; the note says what.</summary>
    Simplified = 20,

    /// <summary>The tool does not translate the query; the note says why, and the run checks that it is refused.</summary>
    NotTranslated = 30,
}

/// <summary>
/// One parameter of a SEDE query: its name without the @, the SQL Server type of its value,
/// and the value the run binds - the author's default where the placeholder had one, otherwise
/// one chosen over the data (decision 208).
/// </summary>
public sealed record SedeParameter(string Name, string SqlType, string Value);

/// <summary>
/// One of the human-reviewed queries of SEDE (decision 208), as <c>SampleData/Sede/queries.json</c>
/// carries it: its number in SEDE, the part of the set, the title and the description its
/// author wrote, its parameters with their values, the tables it reads, the columns of its
/// result in their order and the ordering key the run compares its rows by (decision 170:
/// the columns its ordering falls on, in its order; empty for a query without one), its text
/// in T-SQL as the author wrote it with the placeholders turned into parameters, the author's
/// text with the placeholders, and how much of it the tool translates with a note.
/// </summary>
public sealed record SedeQuery(
    string Key,
    int QuerySetId,
    SedeSet Set,
    string Title,
    string Description,
    IReadOnlyList<SedeParameter> Parameters,
    IReadOnlyList<string> Tables,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> OrderingKey,
    string Sql,
    string SedeSql,
    SedeTranslation Translation,
    string Note)
{
    /// <summary>Whether the query orders its rows: a reading of the key, never a fact of its own.</summary>
    public bool Ordered => OrderingKey.Count > 0;
}
