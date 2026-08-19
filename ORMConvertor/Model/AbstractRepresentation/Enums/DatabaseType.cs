namespace Model.AbstractRepresentation.Enums;

/// <summary>
/// Closed vocabulary of database type families (decision 019). A value names a family that
/// exists across systems - the SQL standard's term where it has one - never a type of one
/// concrete DBMS. Unicode, length, precision and scale are facets on PropertyMap next to
/// the family, and a claim the vocabulary does not capture travels literally in
/// PropertyMap.SourceSqlType beside it, not instead of it. There is no None value: an
/// absent fact is a null PropertyMap.Type, which is what the completion phase and the
/// completeness gate test.
/// </summary>
public enum DatabaseType
{
    Boolean = 1,

    // Integer families by width. TinyInt is not standard but exists in several systems,
    // and ScalarType.Byte points at it; without it a byte property would silently widen.
    TinyInt = 2,
    SmallInt = 3,

    // Integer, not Int: the standard's word, and no collision with ScalarType.Int,
    // which claims something else - a language type, not a column.
    Integer = 4,
    BigInt = 5,

    /// <summary>Exact decimal; precision and scale are facets. Numeric, money and
    /// smallmoney read as this family.</summary>
    Decimal = 6,

    /// <summary>Approximate, single precision.</summary>
    Real = 7,

    /// <summary>Approximate, double precision. The standard's name - "float" means
    /// different widths in different systems.</summary>
    DoublePrecision = 8,

    Date = 9,
    Time = 10,

    /// <summary>Date and time; the fractional-second precision is the Precision facet,
    /// so datetime, datetime2 and smalldatetime are one family, not three values.</summary>
    Timestamp = 11,
    TimestampWithTimeZone = 12,

    /// <summary>Fixed-length character; unicode is the IsUnicode facet.</summary>
    Char = 13,

    /// <summary>Variable-length character; unicode is the IsUnicode facet.</summary>
    VarChar = 14,

    /// <summary>Large character data without a declared length.</summary>
    Text = 15,

    Binary = 16,
    VarBinary = 17,

    /// <summary>Large binary object.</summary>
    Blob = 18,

    Uuid = 19,
    Xml = 20,
}
