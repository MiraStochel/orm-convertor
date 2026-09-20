using Model.AbstractRepresentation.Enums;

namespace MyBatisWrappers;

/// <summary>
/// The <c>jdbcType</c> attribute in both directions (decision 084). It names a JDBC family,
/// not a type of one database system, so it lands on the neutral vocabulary of decision 019
/// almost one to one - and exactly as far as that vocabulary's family and unicode facet
/// reach. The length, the precision and the scale have no place in it at all, which is why
/// they stay inexpressible in the descriptor rather than being folded in here.
///
/// The names are MyBatis's own JdbcType enum, which is java.sql.Types with the additions a
/// driver needed; the two families the enum has no member for are answered with null rather
/// than with a neighbour, because a jdbcType that names another family would bind the value
/// through another type handler than the source did.
/// </summary>
public static class MyBatisJdbcType
{
    /// <summary>The attribute value for a family and its unicode facet, or null where the enum has none.</summary>
    public static string? Write(DatabaseType type, bool? isUnicode) => type switch
    {
        DatabaseType.Boolean => "BOOLEAN",
        DatabaseType.TinyInt => "TINYINT",
        DatabaseType.SmallInt => "SMALLINT",
        DatabaseType.Integer => "INTEGER",
        DatabaseType.BigInt => "BIGINT",
        DatabaseType.Decimal => "DECIMAL",
        DatabaseType.Real => "REAL",
        DatabaseType.DoublePrecision => "DOUBLE",
        DatabaseType.Date => "DATE",
        DatabaseType.Time => "TIME",
        DatabaseType.Timestamp => "TIMESTAMP",
        DatabaseType.TimestampWithTimeZone => "TIMESTAMP_WITH_TIMEZONE",
        DatabaseType.Char => isUnicode == true ? "NCHAR" : "CHAR",
        DatabaseType.VarChar => isUnicode == true ? "NVARCHAR" : "VARCHAR",
        DatabaseType.Text => isUnicode == true ? "LONGNVARCHAR" : "LONGVARCHAR",
        DatabaseType.Binary => "BINARY",
        DatabaseType.VarBinary => "VARBINARY",
        DatabaseType.Blob => "BLOB",

        // JdbcType names neither a uuid nor an xml column; OTHER and SQLXML would send the
        // value through a different type handler than the source chose.
        _ => null,
    };

    /// <summary>The family and unicode facet an attribute value claims; a null family means the name is unknown.</summary>
    public static (DatabaseType? Type, bool? IsUnicode) Read(string jdbcType)
    {
        ArgumentNullException.ThrowIfNull(jdbcType);

        return jdbcType.Trim().ToUpperInvariant() switch
        {
            "BOOLEAN" or "BIT" => (DatabaseType.Boolean, null),
            "TINYINT" => (DatabaseType.TinyInt, null),
            "SMALLINT" => (DatabaseType.SmallInt, null),
            "INTEGER" => (DatabaseType.Integer, null),
            "BIGINT" => (DatabaseType.BigInt, null),
            "DECIMAL" or "NUMERIC" => (DatabaseType.Decimal, null),
            "REAL" => (DatabaseType.Real, null),
            "FLOAT" or "DOUBLE" => (DatabaseType.DoublePrecision, null),
            "DATE" => (DatabaseType.Date, null),
            "TIME" or "TIME_WITH_TIMEZONE" => (DatabaseType.Time, null),
            "TIMESTAMP" => (DatabaseType.Timestamp, null),
            "TIMESTAMP_WITH_TIMEZONE" or "DATETIMEOFFSET" => (DatabaseType.TimestampWithTimeZone, null),
            "CHAR" => (DatabaseType.Char, false),
            "NCHAR" => (DatabaseType.Char, true),
            "VARCHAR" => (DatabaseType.VarChar, false),
            "NVARCHAR" => (DatabaseType.VarChar, true),
            "LONGVARCHAR" or "CLOB" => (DatabaseType.Text, false),
            "LONGNVARCHAR" or "NCLOB" => (DatabaseType.Text, true),
            "BINARY" => (DatabaseType.Binary, null),
            "VARBINARY" or "LONGVARBINARY" => (DatabaseType.VarBinary, null),
            "BLOB" => (DatabaseType.Blob, null),
            "SQLXML" => (DatabaseType.Xml, null),
            _ => (null, null),
        };
    }
}
