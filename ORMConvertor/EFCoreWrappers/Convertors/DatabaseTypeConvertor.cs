using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers.Convertors;

/// <summary>
/// One reading of an EF Core column type - the literal SQL of [Column(TypeName = ...)] or
/// a CLR type name - in the neutral vocabulary (decision 019): the family, the facets the
/// name and its arguments claim, and whether the literal spelling belongs on the escape
/// path because the family is coarser than the name (money, datetime) or missing
/// altogether. A null Type means the vocabulary does not capture the name.
/// </summary>
public readonly record struct SqlTypeReading(
    DatabaseType? Type,
    bool? IsUnicode = null,
    int? Length = null,
    int? Precision = null,
    int? Scale = null,
    bool KeepLiteral = false);

public class DatabaseTypeConvertor
{
    /// <summary>
    /// Reads a SQL type as EF Core spells it, including parenthesized arguments -
    /// varchar(50), decimal(18,2), datetime2(3) - which become the facet the family
    /// measures itself by. Never throws on an unknown name: the literal spelling is kept
    /// on the escape path and the caller records it (decisions 010 and 019).
    /// </summary>
    public static SqlTypeReading FromEfCore(string? columnTypeOrClr)
    {
        if (string.IsNullOrWhiteSpace(columnTypeOrClr))
        {
            throw new ArgumentNullException(nameof(columnTypeOrClr));
        }

        var type = columnTypeOrClr.Trim();
        var (name, first, second) = SplitArguments(type);

        return name.ToLowerInvariant() switch
        {
            "bit" => new(DatabaseType.Boolean),
            "tinyint" => new(DatabaseType.TinyInt),
            "smallint" => new(DatabaseType.SmallInt),
            "int" or "integer" => new(DatabaseType.Integer),
            "bigint" => new(DatabaseType.BigInt),

            "decimal" or "numeric" => new(DatabaseType.Decimal, Precision: first, Scale: second),

            // The money types are types of one system; the family keeps their exact
            // decimal shape and the literal spelling rides the escape path (decision 019).
            "money" => new(DatabaseType.Decimal, Precision: 19, Scale: 4, KeepLiteral: true),
            "smallmoney" => new(DatabaseType.Decimal, Precision: 10, Scale: 4, KeepLiteral: true),

            // T-SQL float(n) with n <= 24 is single precision; bare float is double.
            "float" when first is <= 24 => new(DatabaseType.Real, KeepLiteral: true),
            "float" => new(DatabaseType.DoublePrecision),
            "real" => new(DatabaseType.Real),

            "date" => new(DatabaseType.Date),
            "time" => new(DatabaseType.Time, Precision: first),

            // datetime and smalldatetime are the Timestamp family at the precision the
            // name itself fixes; their narrower range is what the literal records.
            "datetime" => new(DatabaseType.Timestamp, Precision: 3, KeepLiteral: true),
            "smalldatetime" => new(DatabaseType.Timestamp, Precision: 0, KeepLiteral: true),
            "datetime2" => new(DatabaseType.Timestamp, Precision: first),
            "datetimeoffset" => new(DatabaseType.TimestampWithTimeZone, Precision: first),

            "char" => new(DatabaseType.Char, IsUnicode: false, Length: first),
            "nchar" => new(DatabaseType.Char, IsUnicode: true, Length: first),
            "varchar" => new(DatabaseType.VarChar, IsUnicode: false, Length: first),
            "nvarchar" => new(DatabaseType.VarChar, IsUnicode: true, Length: first),
            "text" => new(DatabaseType.Text, IsUnicode: false),
            "ntext" => new(DatabaseType.Text, IsUnicode: true),

            "binary" => new(DatabaseType.Binary, Length: first),
            "varbinary" => new(DatabaseType.VarBinary, Length: first),
            "image" => new(DatabaseType.Blob, KeepLiteral: true),

            "uniqueidentifier" or "uuid" => new(DatabaseType.Uuid),
            "xml" => new(DatabaseType.Xml),

            // A rowversion column is eight bytes of binary; the literal keeps the exact
            // type beside the coarser family. The type name states only the storage -
            // EF Core reads the version claim from [Timestamp], not from here, so the
            // version flag (decision 030) is not set by this reading.
            "rowversion" or "timestamp" => new(DatabaseType.VarBinary, Length: 8, KeepLiteral: true),
            "sql_variant" => new(null, KeepLiteral: true),

            // CLR type fall-back
            "long" or "int64" => new(DatabaseType.BigInt),
            "int32" => new(DatabaseType.Integer),
            "int16" or "short" => new(DatabaseType.SmallInt),
            "byte" => new(DatabaseType.TinyInt),
            "bool" or "boolean" => new(DatabaseType.Boolean),
            "system.decimal" => new(DatabaseType.Decimal),
            "double" => new(DatabaseType.DoublePrecision),
            "single" => new(DatabaseType.Real),
            "system.datetime" => new(DatabaseType.Timestamp),
            "system.timespan" or "timespan" => new(DatabaseType.Time),
            "system.datetimeoffset" => new(DatabaseType.TimestampWithTimeZone),
            "guid" or "system.guid" => new(DatabaseType.Uuid),

            _ => new(null, KeepLiteral: true),
        };
    }

    /// <summary>
    /// Splits "name(a[,b])" into the name and up to two integer arguments; "max" and a
    /// missing argument list both come back as null.
    /// </summary>
    private static (string Name, int? First, int? Second) SplitArguments(string type)
    {
        var open = type.IndexOf('(');

        if (open < 0)
        {
            return (type, null, null);
        }

        var name = type[..open].Trim();
        var arguments = type[(open + 1)..].TrimEnd(')').Split(',');

        int? first = arguments.Length > 0 && int.TryParse(arguments[0].Trim(), out var a) ? a : null;
        int? second = arguments.Length > 1 && int.TryParse(arguments[1].Trim(), out var b) ? b : null;

        return (name, first, second);
    }

    /// <summary>
    /// The SQL type of a family as EF Core's SQL Server provider spells it. The unicode
    /// facet picks the n-variant of character data; unstated falls to unicode, which is
    /// the provider's own convention for .NET strings. Length, precision and scale are
    /// carried by their own annotations, so they do not appear here.
    /// </summary>
    public static string ToEFCore(DatabaseType type, bool? isUnicode = null) => type switch
    {
        DatabaseType.Boolean => "bit",
        DatabaseType.TinyInt => "tinyint",
        DatabaseType.SmallInt => "smallint",
        DatabaseType.Integer => "int",
        DatabaseType.BigInt => "bigint",

        DatabaseType.Decimal => "decimal",
        DatabaseType.Real => "real",
        DatabaseType.DoublePrecision => "float",

        DatabaseType.Date => "date",
        DatabaseType.Time => "time",
        DatabaseType.Timestamp => "datetime2",
        DatabaseType.TimestampWithTimeZone => "datetimeoffset",

        DatabaseType.Char => isUnicode == false ? "char" : "nchar",
        DatabaseType.VarChar => isUnicode == false ? "varchar" : "nvarchar",
        DatabaseType.Text => isUnicode == false ? "text" : "ntext",

        DatabaseType.Binary => "binary",
        DatabaseType.VarBinary => "varbinary",
        DatabaseType.Blob => "image",

        DatabaseType.Uuid => "uniqueidentifier",
        DatabaseType.Xml => "xml",

        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
