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
/// A target framework whose query language does not speak a query the others translate in
/// theirs, and which writes it in the native SQL of its dialect instead (decision 113), and
/// why - in the words of the page, as a refusal is.
/// </summary>
public sealed record LdbcFallback(Model.ORMEnum Target, string Reason);

/// <summary>
/// How a value of a parameter of the text is made from the fields of the driver's operation
/// (decision 117). The values are explicit because they travel over REST as numbers.
/// </summary>
public enum LdbcDerivation
{
    /// <summary>The field itself.</summary>
    Field = 10,

    /// <summary>
    /// The field, a moment, plus as many days as the operand field says: the end of a window
    /// the specification states as a start and a duration, which the text takes as one value.
    /// </summary>
    PlusDays = 20,

    /// <summary>The month after the one the field names, December followed by January.</summary>
    NextMonth = 30,
}

/// <summary>
/// One parameter of the text, bound to the operation of the driver: the parameter's name
/// without the @, the field of the operation it is made from and how (decision 117). The
/// value takes the SQL Server type the parameter declares; a moment or a date arrives as
/// milliseconds since the epoch in UTC, as the driver writes every date.
/// </summary>
public sealed record LdbcArgument(string Parameter, string Field, LdbcDerivation Derivation = LdbcDerivation.Field, string? Operand = null);

/// <summary>
/// How a field of the driver's result is put into the canonical form of decision 089, so that
/// it meets the column the text projects (decision 117). The values are explicit because they
/// travel over REST as numbers.
/// </summary>
public enum LdbcValueKind
{
    /// <summary>A number or a string, as it is.</summary>
    Value = 10,

    /// <summary>A moment, written by the driver as milliseconds since the epoch in UTC.</summary>
    Moment = 20,

    /// <summary>A date without a time - a birthday -, written by the driver as the milliseconds of its midnight in UTC.</summary>
    Date = 30,

    /// <summary>A truth value, which the text writes as the number 1 or 0.</summary>
    Flag = 40,

    /// <summary>
    /// A list whose order the specification leaves open, which the text joins into one string
    /// with the separator: both sides are compared as the set of their elements.
    /// </summary>
    Set = 50,

    /// <summary>A list whose order is its meaning - the persons along a path -, joined with the separator.</summary>
    Sequence = 60,
}

/// <summary>
/// One column of the text and the field of the driver's result it answers to (decision 117).
/// A list is joined with the separator; an element that is itself an object is the values of
/// the named fields joined with the element separator, as the text writes a university or a
/// company.
/// </summary>
public sealed record LdbcResultField(
    string Column,
    string Field,
    LdbcValueKind Kind = LdbcValueKind.Value,
    string? Separator = null,
    IReadOnlyList<string>? Elements = null,
    string? ElementSeparator = null);

/// <summary>
/// The binding of a query of the catalog to the validation set of LDBC Interactive v1, its
/// judge at the fourth verification level (decision 117): the operation of the driver the
/// query answers, the parameters of the text made from the fields of the operation, and the
/// columns of the text, in the order it projects them, each with the field of the expected
/// result it is compared with. The rows are compared in their order, because the
/// specification orders every query of the workload completely - but for the one it does not,
/// which says so and is compared as a set.
/// </summary>
public sealed record LdbcValidation(
    string Operation,
    IReadOnlyList<LdbcArgument> Arguments,
    IReadOnlyList<LdbcResultField> Fields,
    bool Ordered = true);

/// <summary>
/// One read query of the LDBC Social Network Benchmark, as the page of decision 110 shows
/// it: the number and title the specification gives it, how much of it the tool translates,
/// a note saying what the text exercises, simplifies or cannot state, and the T-SQL text
/// over the tables of <c>database/ldbc/schema.sql</c>, written for Dapper. A translated query
/// reaches every target except those it names as refusing it, and those it names as falling
/// back reach it in native SQL - the three values of decision 113, the second never passed
/// off as the first. A query of the Interactive workload is bound to the validation set that
/// judges it (decision 117); the BI workload has no such judge.
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
    IReadOnlyList<LdbcRefusal>? RefusedBy = null,
    IReadOnlyList<LdbcFallback>? FallbackBy = null,
    LdbcValidation? Validation = null)
{
    /// <summary>The targets that refuse the query although it is translated into the others.</summary>
    public IReadOnlyList<LdbcRefusal> Refusals => RefusedBy ?? [];

    /// <summary>The targets that write the query in native SQL because their query language does not speak it.</summary>
    public IReadOnlyList<LdbcFallback> Fallbacks => FallbackBy ?? [];
}
