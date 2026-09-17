using Model.AbstractRepresentation.Enums;

namespace JakartaPersistence;

/// <summary>
/// One reading of a columnDefinition in the neutral vocabulary (decision 019): the
/// family, the facets the name and its arguments claim, and whether the literal spelling
/// belongs on the escape path because the family is coarser than the name or missing.
/// The same reading the EF Core wrapper gives a [Column(TypeName)] - a copy of its table
/// of T-SQL names rather than a reference to it, because one wrapper never sees another
/// (S1); a shared T-SQL reading project (the item for F8) is its future home (decision 077).
/// </summary>
public readonly record struct JpaSqlTypeReading(
    DatabaseType? Type,
    bool? IsUnicode = null,
    int? Length = null,
    int? Precision = null,
    int? Scale = null,
    bool KeepLiteral = false)
{
    public static JpaSqlTypeReading FromColumnDefinition(string columnDefinition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnDefinition);

        var (name, first, second) = SplitArguments(columnDefinition.Trim());

        return name.ToLowerInvariant() switch
        {
            "bit" => new(DatabaseType.Boolean),
            "tinyint" => new(DatabaseType.TinyInt),
            "smallint" => new(DatabaseType.SmallInt),
            "int" or "integer" => new(DatabaseType.Integer),
            "bigint" => new(DatabaseType.BigInt),
            "decimal" or "numeric" => new(DatabaseType.Decimal, Precision: first, Scale: second),
            "money" => new(DatabaseType.Decimal, Precision: 19, Scale: 4, KeepLiteral: true),
            "smallmoney" => new(DatabaseType.Decimal, Precision: 10, Scale: 4, KeepLiteral: true),
            "float" when first is <= 24 => new(DatabaseType.Real, KeepLiteral: true),
            "float" => new(DatabaseType.DoublePrecision),
            "real" => new(DatabaseType.Real),
            "date" => new(DatabaseType.Date),
            "time" => new(DatabaseType.Time, Precision: first),
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
            "uniqueidentifier" => new(DatabaseType.Uuid),
            "xml" => new(DatabaseType.Xml),
            "rowversion" or "timestamp" => new(DatabaseType.VarBinary, Length: 8, KeepLiteral: true),
            _ => new(null, KeepLiteral: true),
        };
    }

    private static (string Name, int? First, int? Second) SplitArguments(string type)
    {
        var open = type.IndexOf('(');

        if (open < 0)
        {
            // "varchar not null" and similar: the first word is the type.
            return (type.Split(' ')[0], null, null);
        }

        var name = type[..open].Trim();
        var close = type.IndexOf(')', open);
        var arguments = type[(open + 1)..(close < 0 ? type.Length : close)].Split(',');

        int? first = arguments.Length > 0 && int.TryParse(arguments[0].Trim(), out var a) ? a : null;
        int? second = arguments.Length > 1 && int.TryParse(arguments[1].Trim(), out var b) ? b : null;

        return (name, first, second);
    }
}
