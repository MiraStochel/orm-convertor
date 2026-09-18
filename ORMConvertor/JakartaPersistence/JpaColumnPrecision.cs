using JavaEntityParsing;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace JakartaPersistence;

/// <summary>
/// What the precision facet of a column is called in @Column (decision 079). Jakarta
/// Persistence 3.2 gives precision to a decimal column and secondPrecision to a column of
/// type time or timestamp; the intermediate representation has one Precision facet and on
/// a temporal family it is the fractional-second precision (decision 019), so the JPA
/// layer chooses the spelling, never the fact.
/// </summary>
public enum JpaPrecisionKind
{
    /// <summary>Neither a family nor a declared type says what the column is: orm.xml, read before the class.</summary>
    Unknown,

    /// <summary>A temporal column with a fractional-second part: secondPrecision.</summary>
    FractionalSeconds,

    /// <summary>A calendar date, which has no fractional seconds: neither attribute reaches it.</summary>
    Date,

    /// <summary>Anything else, a decimal column above all: precision and scale.</summary>
    Other,
}

/// <summary>
/// The single predicate both directions of the JPA layer ask (decision 079): the parser to
/// know which attribute fills the precision facet, the builder to know which one to write.
/// </summary>
public static class JpaColumnPrecision
{
    /// <summary>
    /// The family the map states decides; where it states none, the language type does,
    /// because that is exactly what the implementation consults when nothing names the SQL
    /// type. Duration is deliberately not temporal here: neither ecosystem maps it to a
    /// time-of-day column - Hibernate 7.4.5 stores it as a number.
    /// </summary>
    public static JpaPrecisionKind Classify(DatabaseType? family, LangType? languageType)
    {
        if (family is { } type)
        {
            return type switch
            {
                DatabaseType.Time or DatabaseType.Timestamp or DatabaseType.TimestampWithTimeZone
                    => JpaPrecisionKind.FractionalSeconds,
                DatabaseType.Date => JpaPrecisionKind.Date,
                _ => JpaPrecisionKind.Other,
            };
        }

        if (languageType is { Category: LangTypeCategory.Scalar, ScalarType: { } scalar })
        {
            return scalar switch
            {
                ScalarType.DateTime or ScalarType.DateTimeOffset or ScalarType.TimeOfDay
                    => JpaPrecisionKind.FractionalSeconds,
                ScalarType.Date => JpaPrecisionKind.Date,
                _ => JpaPrecisionKind.Other,
            };
        }

        // An unknown language name (Instant, java.util.Date) says nothing either: the tool
        // does not guess a family from a type it could not place (decisions 014 and 075).
        return JpaPrecisionKind.Unknown;
    }

    /// <summary>
    /// The same question on the reading side, where the language type is still the text of
    /// the Java declaration and orm.xml carries none at all.
    /// </summary>
    public static JpaPrecisionKind Classify(DatabaseType? family, string? javaTypeText)
        => Classify(family, string.IsNullOrWhiteSpace(javaTypeText) ? null : JavaTypeConvertor.FromString(javaTypeText));
}
