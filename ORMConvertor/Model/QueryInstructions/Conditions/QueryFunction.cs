namespace Model.QueryInstructions.Conditions;

/// <summary>
/// The closed vocabulary of scalar functions a query expression may call (decision 107).
/// Closed on purpose, the way <see cref="Model.AbstractRepresentation.Enums.ScalarType"/>
/// is: an aggregate travels as a string only because its five names are one word in all six
/// targets, whereas a scalar function is spelled differently per target - <c>LEN</c>,
/// <c>length</c>, <c>.Length</c> - so the name has to be a value every visitor has a row
/// for, and a function a target does not speak has to be something its descriptor can
/// leave out. A function enters here only with a spelling verified for all four target
/// languages against the pinned versions; what has no row is refused by the readers, by
/// name.
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
}
