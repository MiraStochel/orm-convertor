using Model.AbstractRepresentation.Enums;

namespace DatabaseCatalog;

/// <summary>
/// The reverse of the language-to-database guesses the wrappers make: which language
/// scalar a database type family implies. Used by the completion phase for a property
/// known only to the mapping - a third-level convention that must carry its origin as a
/// record (decision 015). Null means no claim: the closed ScalarType list (decision 014)
/// has no counterpart for the family, so the property stays without a language type and
/// the completeness gate reports it instead of a guess being written down.
/// </summary>
public static class LanguageTypeInference
{
    public static ScalarType? FromDatabaseType(DatabaseType type) => type switch
    {
        DatabaseType.Boolean => ScalarType.Bool,
        DatabaseType.TinyInt => ScalarType.Byte,
        DatabaseType.SmallInt => ScalarType.Short,
        DatabaseType.Integer => ScalarType.Int,
        DatabaseType.BigInt => ScalarType.Long,

        DatabaseType.Decimal => ScalarType.Decimal,
        DatabaseType.DoublePrecision => ScalarType.Double,
        DatabaseType.Real => ScalarType.Float,

        DatabaseType.Date or DatabaseType.Timestamp => ScalarType.DateTime,

        DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Text
            or DatabaseType.Xml => ScalarType.String,

        DatabaseType.Uuid => ScalarType.Guid,

        // Time, TimestampWithTimeZone and the binary families have no counterpart in
        // the closed scalar list; no claim is made.
        _ => null,
    };
}
