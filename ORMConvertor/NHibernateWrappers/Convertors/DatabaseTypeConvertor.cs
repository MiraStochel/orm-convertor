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

/// <summary>
/// One naming of a type claim on the emission side: the type name NHibernate 5.7.0
/// registers, and - where no registered name says exactly what the family and its facets
/// claim - the reason the nearest registered name changes the claim. The counterpart of
/// Narrowing on <see cref="NHibernateTypeReading"/>: the table states the difference,
/// the builder reports it at the point of emission (decision 010).
/// </summary>
public readonly record struct NHibernateTypeNaming(string Name, string? Narrowing = null);

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
    ///
    /// Every emitted name is registered by TypeFactory of NHibernate 5.7.0 - a name the
    /// registry does not know fails the session factory build with an undeterminable
    /// type, so an invalid mapping would come out of a valid input. Two claims have no
    /// registered name at all: the fixed-length string (StringFixedLength and
    /// AnsiStringFixedLength are names of DbType values, not of NHibernate types) and
    /// the non-unicode large text (only StringClob exists). There the nearest registered
    /// name is written and the difference travels as the Narrowing of the result, for
    /// the builder to report at the point of emission.
    /// </summary>
    public static NHibernateTypeNaming ToNHibernate(DatabaseType type, bool? isUnicode = null, int? length = null) => type switch
    {
        DatabaseType.Boolean => new("Boolean"),
        DatabaseType.TinyInt => new("Byte"),
        DatabaseType.SmallInt => new("Int16"),
        DatabaseType.Integer => new("Int32"),
        DatabaseType.BigInt => new("Int64"),

        DatabaseType.Decimal => new("Decimal"),
        DatabaseType.Real => new("Single"),
        DatabaseType.DoublePrecision => new("Double"),

        DatabaseType.Date => new("Date"),
        DatabaseType.Time => new("TimeAsTimeSpan"),
        DatabaseType.Timestamp => new("DateTime"),
        DatabaseType.TimestampWithTimeZone => new("DateTimeOffset"),

        DatabaseType.Char when length == 1 => isUnicode == false ? new("AnsiChar") : new("Char"),
        DatabaseType.Char when isUnicode == false => new("AnsiString",
            "NHibernate 5.7.0 registers no fixed-length string type ('AnsiStringFixedLength' is not in TypeFactory), "
            + "so 'AnsiString' is written and the claim changes from fixed-length to variable-length character data (decision 019)."),
        DatabaseType.Char => new("String",
            "NHibernate 5.7.0 registers no fixed-length string type ('StringFixedLength' is not in TypeFactory), "
            + "so 'String' is written and the claim changes from fixed-length to variable-length character data (decision 019)."),
        DatabaseType.VarChar => isUnicode == false ? new("AnsiString") : new("String"),
        DatabaseType.Text when isUnicode == false => new("StringClob",
            "NHibernate 5.7.0 registers no non-unicode large-text type ('AnsiStringClob' is not in TypeFactory), "
            + "so 'StringClob' is written and the non-unicode facet of the claim is dropped (decision 019)."),
        DatabaseType.Text => new("StringClob"),

        // TypeFactory of 5.7.0 registers the binary type under the lowercase alias -
        // "Binary" resolves to nothing - and the XML document type under XmlDoc, not Xml.
        // Both spellings verified against the package the acceptance level runs on.
        DatabaseType.Binary or DatabaseType.VarBinary => new("binary"),
        DatabaseType.Blob => new("BinaryBlob"),

        DatabaseType.Uuid => new("Guid"),
        DatabaseType.Xml => new("XmlDoc"),

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
        // Every claim guessed here has an exactly matching registered name, so taking
        // the name alone drops no narrowing.
        return scalarType switch
        {
            ScalarType.Bool => ToNHibernate(DatabaseType.Boolean).Name,
            ScalarType.Byte => ToNHibernate(DatabaseType.TinyInt).Name,
            ScalarType.Short => ToNHibernate(DatabaseType.SmallInt).Name,
            // The reference documentation's default for System.Char is the unicode
            // single character - the case the unicode facet exists for (decision 019).
            ScalarType.Char => ToNHibernate(DatabaseType.Char, isUnicode: true, length: 1).Name,
            ScalarType.Int => ToNHibernate(DatabaseType.Integer).Name,
            ScalarType.Long => ToNHibernate(DatabaseType.BigInt).Name,
            ScalarType.Double => ToNHibernate(DatabaseType.DoublePrecision).Name,
            ScalarType.Float => ToNHibernate(DatabaseType.Real).Name,
            ScalarType.Decimal => ToNHibernate(DatabaseType.Decimal).Name,
            ScalarType.String => ToNHibernate(DatabaseType.VarChar, isUnicode: true).Name,
            ScalarType.DateTime => ToNHibernate(DatabaseType.Timestamp).Name,
            ScalarType.Guid => ToNHibernate(DatabaseType.Uuid).Name,
            ScalarType.Object => null,
            _ => null,
        };
    }
}
