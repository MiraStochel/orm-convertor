using Model.AbstractRepresentation.Enums;

namespace DatabaseCatalog;

/// <summary>
/// The reverse of the language-to-database guesses the wrappers make: which language
/// scalar a database type family implies. Used by the completion phase for a property
/// known only to the mapping - a third-level convention that must carry its origin as a
/// record (decision 015). Since decision 071 every family of the vocabulary (decision
/// 019) has a counterpart in the closed ScalarType list, so null is kept only as the
/// answer for a family added later without a row here: no claim, the property stays
/// without a language type and the completeness gate reports it instead of a guess
/// being written down.
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

        // A date column is a date, not a date-time at midnight - the scalar EF Core's own
        // scaffolding and NHibernate 5.7.0 both read it into (decision 071). A time column
        // is a time of day: the elapsed-time reading is a framework default of the
        // source, never a claim of the schema.
        DatabaseType.Date => ScalarType.Date,
        DatabaseType.Time => ScalarType.TimeOfDay,
        DatabaseType.Timestamp => ScalarType.DateTime,
        DatabaseType.TimestampWithTimeZone => ScalarType.DateTimeOffset,

        DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Text
            or DatabaseType.Xml => ScalarType.String,

        DatabaseType.Binary or DatabaseType.VarBinary or DatabaseType.Blob => ScalarType.ByteArray,

        DatabaseType.Uuid => ScalarType.Guid,

        _ => null,
    };
}
