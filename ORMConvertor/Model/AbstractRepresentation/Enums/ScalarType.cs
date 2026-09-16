namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// Closed list of scalar types the model can name across ecosystems (decision 014).
/// Closed on purpose: only over a finite set can a target descriptor state what the
/// framework is able to express. Unsigned integers are deliberately absent - Java has
/// no counterpart, so they are kept more faithfully as an unknown type than widened.
/// Object means the source wrote the root object type, which is a different claim
/// than an unrecognized name.
///
/// The five values from Date on entered with decision 071: each has one exact
/// counterpart in both ecosystems (DateOnly/LocalDate, TimeOnly/LocalTime,
/// DateTimeOffset/OffsetDateTime, TimeSpan/Duration, byte[]/byte[]), and each is the
/// language side of a column family the database vocabulary (decision 019) already
/// names. The names are neutral: TimeOfDay rather than TimeOnly or LocalTime, and
/// ByteArray rather than Binary, which is the name of a column family.
/// </summary>
public enum ScalarType
{
    Bool = 1,
    Byte = 2,
    Short = 3,
    Int = 4,
    Long = 5,
    Float = 6,
    Double = 7,
    Decimal = 8,
    Char = 9,
    String = 10,
    DateTime = 11,
    Guid = 12,
    Object = 13,

    /// <summary>A calendar date without a time of day: DateOnly, LocalDate.</summary>
    Date = 14,

    /// <summary>A time of day without a date: TimeOnly, LocalTime. Not an elapsed time.</summary>
    TimeOfDay = 15,

    /// <summary>A date and time with an offset from UTC: DateTimeOffset, OffsetDateTime.</summary>
    DateTimeOffset = 16,

    /// <summary>An elapsed amount of time: TimeSpan, Duration. Not a time of day.</summary>
    Duration = 17,

    /// <summary>An array of bytes: byte[] in both languages.</summary>
    ByteArray = 18,
}
