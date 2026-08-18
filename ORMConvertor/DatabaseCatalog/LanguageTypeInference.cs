using Model.AbstractRepresentation.Enums;

namespace DatabaseCatalog;

/// <summary>
/// The reverse of the language-to-database guesses the wrappers make: which language
/// scalar a database type implies. Used by the completion phase for a property known only
/// to the mapping - a third-level convention that must carry its origin as a record
/// (decision 015). Null means no claim: the closed ScalarType list (decision 014) has no
/// counterpart for the type, so the property stays without a language type and the
/// completeness gate reports it instead of a guess being written down.
/// </summary>
public static class LanguageTypeInference
{
    public static ScalarType? FromDatabaseType(DatabaseType type) => type switch
    {
        DatabaseType.BigInt => ScalarType.Long,
        DatabaseType.Int => ScalarType.Int,
        DatabaseType.SmallInt => ScalarType.Short,
        DatabaseType.TinyInt => ScalarType.Byte,
        DatabaseType.Bit => ScalarType.Bool,

        DatabaseType.Decimal or DatabaseType.Numeric
            or DatabaseType.Money or DatabaseType.SmallMoney => ScalarType.Decimal,

        DatabaseType.Float => ScalarType.Double,
        DatabaseType.Real => ScalarType.Float,

        DatabaseType.Date or DatabaseType.DateTime or DatabaseType.DateTime2
            or DatabaseType.SmallDateTime => ScalarType.DateTime,

        DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Text
            or DatabaseType.NChar or DatabaseType.NVarChar or DatabaseType.NText
            or DatabaseType.Xml => ScalarType.String,

        DatabaseType.UniqueIdentifier => ScalarType.Guid,
        DatabaseType.SqlVariant => ScalarType.Object,

        // Time, DateTimeOffset, the binary types and RowVersion have no counterpart in
        // the closed scalar list; no claim is made.
        _ => null,
    };
}
