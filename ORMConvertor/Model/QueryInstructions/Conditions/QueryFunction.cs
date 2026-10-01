namespace Model.QueryInstructions.Conditions;

/// <summary>
/// The closed vocabulary of scalar functions a query expression may call (decision 107).
/// Closed on purpose, the way <see cref="Model.AbstractRepresentation.Enums.ScalarType"/>
/// is: an aggregate travels as a string only because its five names are one word in all six
/// targets, whereas a scalar function is spelled differently per target - <c>LEN</c>,
/// <c>length</c>, <c>.Length</c> - so the name has to be a value every visitor has a row
/// for, and a function a target does not speak has to be something its descriptor can
/// leave out. Since decision 113 a function enters here once it has a spelling in T-SQL -
/// the writer of the escape path, so what it does not spell nobody does -, and a target
/// whose descriptor leaves it out writes the query in native SQL; until then a function
/// needed a verified spelling in all four target languages. What has no row is refused by
/// the readers, by name.
/// </summary>
public enum QueryFunction
{
    Upper = 1,
    Lower = 2,
    Trim = 3,

    /// <summary>
    /// <c>SUBSTRING(text, start, length)</c>, three arguments, the start counted from one
    /// as three of the four languages count it; the LINQ reader adds one to the position it
    /// reads and the LINQ visitor takes it off again (decision 107, after the way decision
    /// 051 translates a LIKE pattern instead of carrying it). A two-argument form the source
    /// wrote is read with the length of the text as its third argument, which selects the
    /// same characters.
    /// </summary>
    Substring = 4,

    /// <summary>The number of characters: T-SQL <c>LEN</c>, which every target maps onto over SQL Server.</summary>
    Length = 5,

    /// <summary>The first non-null argument of two or more.</summary>
    Coalesce = 6,

    Abs = 7,
    Year = 8,
    Month = 9,
    Day = 10,

    /// <summary>The current moment of the database, without an argument.</summary>
    CurrentTimestamp = 11,

    /// <summary>
    /// A value with every wildcard of the dialect made literal, so that it can stand inside a
    /// LIKE pattern as text and not as a pattern (decision 107). The one function no language
    /// spells as a word: it is what EF Core's provider does to the argument of a string
    /// method at run time, and the model carries the fact so that a value with a wildcard in
    /// it selects the same rows in every target (decisions 053 and 065). Written as a chain
    /// of replacements over the wildcards of SQL Server and the escape character itself,
    /// with the escape character on the comparison it stands under.
    /// </summary>
    EscapePattern = 12,

    /// <summary>
    /// <c>DATEADD(unit, n, moment)</c> (decision 113): the moment moved by a whole number of
    /// units, which the call carries as <see cref="QueryExpression.Unit"/> - a keyword in
    /// T-SQL, not a value, so it is no argument.
    /// </summary>
    DateAdd = 13,

    /// <summary>
    /// <c>DATEDIFF(unit, start, end)</c> (decision 113): the number of boundaries of the unit
    /// crossed between the two moments, as T-SQL counts it - between 10:00:59 and 10:01:00 lies
    /// one minute -, not the length of the interval in whole units.
    /// </summary>
    DateDiff = 14,

    /// <summary>
    /// <c>ROUND(x, places)</c> (decision 113), always with two arguments because T-SQL has no
    /// other form; the database rounds, by its own rule.
    /// </summary>
    Round = 15,

    /// <summary>The square root, a floating-point number (decision 113).</summary>
    Sqrt = 16,

    /// <summary>
    /// A conversion into a scalar (decision 113), which the call carries as
    /// <see cref="QueryExpression.CastTo"/> - the type of the model, never a database type, as
    /// the model carries types everywhere (decision 024). Only the five scalars every target
    /// language names a conversion into: the whole numbers of 32 and 64 bits, the two
    /// floating-point numbers and text - the set Jakarta Persistence 3.2 states, which T-SQL
    /// writes without a length or a precision that could change the value.
    /// </summary>
    Cast = 17,
}

/// <summary>
/// The unit of <see cref="QueryFunction.DateAdd"/> and <see cref="QueryFunction.DateDiff"/>
/// (decision 113): a closed list, because T-SQL writes it as a keyword and every target names
/// it by a word of its own.
/// </summary>
public enum DateUnit
{
    Year = 1,
    Month = 2,
    Day = 3,
    Hour = 4,
    Minute = 5,
    Second = 6,
}

/// <summary>
/// The ranking functions a window of the vocabulary computes (decision 113): the number of
/// the row within its partition, and the two ranks that tie equal rows. Window aggregates and
/// frames are no part of the vocabulary.
/// </summary>
public enum RankingFunction
{
    RowNumber = 1,
    Rank = 2,
    DenseRank = 3,
}
