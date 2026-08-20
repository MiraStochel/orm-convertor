using Model.AbstractRepresentation.Enums;

namespace NHibernateWrappers.Convertors;

/// <summary>
/// One reading of an NHibernate type attribute in the neutral vocabulary (decision 019):
/// the family, the facets the type name itself claims, and - for a name outside the
/// vocabulary - the literal spelling for the escape path. A null Type with a SourceType
/// means the vocabulary does not capture the name; a non-null Narrowing carries the
/// reason where the family is coarser than the name and the difference deserves a record.
/// </summary>
public readonly record struct NHibernateTypeReading(
    DatabaseType? Type,
    bool? IsUnicode = null,
    int? Length = null,
    int? Precision = null,
    int? Scale = null,
    string? SourceType = null,
    string? Narrowing = null);

public static class DatabaseTypeConvertor
{
    /// <summary>
    /// The type attribute of a property element read into the neutral vocabulary. The
    /// aliases are those TypeFactory of NHibernate 5.7.0 registers; a name it does not
    /// register - a user type, or a spelling of another system - has no family and keeps
    /// only its literal spelling, which the caller records (decisions 010 and 019).
    /// Never throws on an unknown name: a type outside the vocabulary is a record and a
    /// poorer artifact, not a failed conversion.
    /// </summary>
    public static NHibernateTypeReading FromNHibernate(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentNullException(nameof(type));
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "boolean" or "bool" => new(DatabaseType.Boolean),
            "byte" => new(DatabaseType.TinyInt),
            "int16" or "short" => new(DatabaseType.SmallInt),
            "int32" or "int" or "integer" => new(DatabaseType.Integer),
            "int64" or "long" => new(DatabaseType.BigInt),

            "decimal" or "big_decimal" => new(DatabaseType.Decimal),

            // Currency is DbType.Currency - money under the SQL Server dialect. The
            // vocabulary has no money family (a type of one system), so the claim is
            // read as its exact decimal shape and the narrowing is reported.
            "currency" => new(DatabaseType.Decimal, Precision: 19, Scale: 4,
                Narrowing: "NHibernate type 'Currency' has no family of its own in the neutral vocabulary; "
                    + "it is read as Decimal with precision 19 and scale 4, the shape of the money type it maps to (decision 019)."),

            "single" or "float" => new(DatabaseType.Real),
            "double" => new(DatabaseType.DoublePrecision),

            "date" => new(DatabaseType.Date),
            "time" or "timeastimespan" => new(DatabaseType.Time),

            // Timestamp is NHibernate's DateTime-valued type, not a rowversion column.
            "datetime" or "datetime2" or "dbtimestamp" or "timestamp" => new(DatabaseType.Timestamp),

            // The name itself claims no fractional seconds.
            "datetimenoms" => new(DatabaseType.Timestamp, Precision: 0),
            "datetimeoffset" => new(DatabaseType.TimestampWithTimeZone),

            // A single character is a fixed-length character column of length one; the
            // unicode facet is what tells Char from AnsiChar (decision 019).
            "char" => new(DatabaseType.Char, IsUnicode: true, Length: 1),
            "ansichar" => new(DatabaseType.Char, IsUnicode: false, Length: 1),
            "stringfixedlength" => new(DatabaseType.Char, IsUnicode: true),
            "ansistringfixedlength" => new(DatabaseType.Char, IsUnicode: false),
            "string" => new(DatabaseType.VarChar, IsUnicode: true),
            "ansistring" => new(DatabaseType.VarChar, IsUnicode: false),
            "stringclob" => new(DatabaseType.Text, IsUnicode: true),
            "ansistringclob" => new(DatabaseType.Text, IsUnicode: false),

            // Binary is Byte[] over DbType.Binary, which the SQL Server driver renders
            // as the variable-length binary type; Byte[] and System.Byte[] are the same
            // type under its CLR aliases.
            "binary" or "byte[]" or "system.byte[]" => new(DatabaseType.VarBinary),
            "binaryblob" => new(DatabaseType.Blob),

            "guid" => new(DatabaseType.Uuid),
            "xml" or "xmldoc" or "xmldocument" => new(DatabaseType.Xml),

            _ => new(null, SourceType: type.Trim()),
        };
    }

    /// <summary>
    /// The NHibernate type name of a family. The unicode facet picks between the ansi
    /// and unicode variants of character data; unstated falls to the unicode variant,
    /// which is NHibernate's own default, so nothing is claimed beyond the target's
    /// convention. The length tells a single character (Char/AnsiChar) from a
    /// fixed-length string.
    /// </summary>
    public static string ToNHibernate(DatabaseType type, bool? isUnicode = null, int? length = null) => type switch
    {
        DatabaseType.Boolean => "Boolean",
        DatabaseType.TinyInt => "Byte",
        DatabaseType.SmallInt => "Int16",
        DatabaseType.Integer => "Int32",
        DatabaseType.BigInt => "Int64",

        DatabaseType.Decimal => "Decimal",
        DatabaseType.Real => "Single",
        DatabaseType.DoublePrecision => "Double",

        DatabaseType.Date => "Date",
        DatabaseType.Time => "TimeAsTimeSpan",
        DatabaseType.Timestamp => "DateTime",
        DatabaseType.TimestampWithTimeZone => "DateTimeOffset",

        DatabaseType.Char when length == 1 => isUnicode == false ? "AnsiChar" : "Char",
        DatabaseType.Char => isUnicode == false ? "AnsiStringFixedLength" : "StringFixedLength",
        DatabaseType.VarChar => isUnicode == false ? "AnsiString" : "String",
        DatabaseType.Text => isUnicode == false ? "AnsiStringClob" : "StringClob",

        // TypeFactory of 5.7.0 registers the binary type under the lowercase alias -
        // "Binary" resolves to nothing - and the XML document type under XmlDoc, not Xml.
        // Both spellings verified against the package the acceptance level runs on.
        DatabaseType.Binary or DatabaseType.VarBinary => "binary",
        DatabaseType.Blob => "BinaryBlob",

        DatabaseType.Uuid => "Guid",
        DatabaseType.Xml => "XmlDoc",

        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// NHibernate's own default assumption about the column behind a scalar - the
    /// language-to-database table of this framework, which is why it lives here and not
    /// in Common (decision 014). Null means no claim: for Object NHibernate decides at
    /// runtime, and writing anything down would state more than the source did.
    /// </summary>
    public static string? GuessFromScalarType(ScalarType scalarType)
    {
        return scalarType switch
        {
            ScalarType.Bool => ToNHibernate(DatabaseType.Boolean),
            ScalarType.Byte => ToNHibernate(DatabaseType.TinyInt),
            ScalarType.Short => ToNHibernate(DatabaseType.SmallInt),
            // The reference documentation's default for System.Char is the unicode
            // single character - the case the unicode facet exists for (decision 019).
            ScalarType.Char => ToNHibernate(DatabaseType.Char, isUnicode: true, length: 1),
            ScalarType.Int => ToNHibernate(DatabaseType.Integer),
            ScalarType.Long => ToNHibernate(DatabaseType.BigInt),
            ScalarType.Double => ToNHibernate(DatabaseType.DoublePrecision),
            ScalarType.Float => ToNHibernate(DatabaseType.Real),
            ScalarType.Decimal => ToNHibernate(DatabaseType.Decimal),
            ScalarType.String => ToNHibernate(DatabaseType.VarChar, isUnicode: true),
            ScalarType.DateTime => ToNHibernate(DatabaseType.Timestamp),
            ScalarType.Guid => ToNHibernate(DatabaseType.Uuid),
            ScalarType.Object => null,
            _ => null,
        };
    }
}
