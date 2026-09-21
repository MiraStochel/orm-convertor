using Model;
using Model.AbstractRepresentation.Enums;

namespace Common.Sql;

/// <summary>
/// How a database dialect spells a type family, in both directions (decision 086). The one
/// home of a table that four places used to carry a copy of; it lives in Common because it
/// is a fact of a database system and of no framework - the language-to-database table that
/// decision 014 kept out of Common is the other kind, a framework's own default assumption,
/// and it stays in its wrapper.
///
/// It is deliberately not in TransactSql: that project is built around the ScriptDom
/// grammar, and a table of twenty names needs no grammar. Putting it there would drag the
/// parser into every wrapper that only spells types, and would leave a second dialect's
/// table living inside a project named after the first dialect's grammar.
/// </summary>
public static class SqlTypeSpelling
{
    /// <summary>
    /// Reads a literal SQL type - including parenthesized arguments, which become the facet
    /// the family measures itself by - into the neutral vocabulary. Never throws on an
    /// unknown name: the literal spelling is kept on the escape path and the caller records
    /// it (decisions 010 and 019).
    ///
    /// It takes no dialect, and the asymmetry with the writing direction is the point. The
    /// writing direction is governed by the dialect the target descriptor declares; the
    /// reading direction reads the only SQL this tool knows. Decision 082 said that of query
    /// text and it holds of type names: a literal type in a source artifact is written in
    /// the source project's dialect, about which the tool has no declaration at all, and
    /// pretending to read it in the target's dialect would be a false claim. A second
    /// dialect needs a declaration for the source side, which is an open item.
    /// </summary>
    public static SqlTypeReading Read(string? type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var (name, first, second) = SplitArguments(type.Trim());

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

            "uniqueidentifier" => new(DatabaseType.Uuid),
            "xml" => new(DatabaseType.Xml),

            // A rowversion column is eight bytes of binary; the literal keeps the exact
            // type beside the coarser family. The name states only the storage - the
            // version claim of decision 030 is read from elsewhere, never from here.
            "rowversion" or "timestamp" => new(DatabaseType.VarBinary, Length: 8, KeepLiteral: true),
            "sql_variant" => new(null, KeepLiteral: true),

            _ => new(null, KeepLiteral: true),
        };
    }

    /// <summary>
    /// The bare name of a family in the dialect, without arguments. This is the shape a
    /// target wants when it carries length, precision and scale in channels of their own -
    /// EF Core's [MaxLength] and [Precision] beside [Column(TypeName)]. The unicode facet
    /// picks the n-variant of character data; unstated falls to unicode, which is the SQL
    /// Server provider's own convention for .NET strings.
    /// </summary>
    public static string Name(DatabaseDialect dialect, DatabaseType type, bool? isUnicode = null)
        => dialect switch
        {
            DatabaseDialect.SqlServer2022 => SqlServerName(type, isUnicode),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    /// <summary>
    /// The complete literal type of a family and its facets in the dialect - name and
    /// arguments - for a target whose only channel is a column definition: the sql-type of
    /// an NHibernate column, the columnDefinition of a JPA one.
    ///
    /// Null when the model carries no facet the name would need. That is deliberate and it
    /// is what keeps the derivation honest: "nchar" alone is nchar(1) in T-SQL, so writing
    /// it for a fixed-length column of unknown length would narrow the column to a single
    /// character silently. The caller then writes nothing and keeps whatever its own
    /// vocabulary says, with the record that goes with it.
    /// </summary>
    public static string? Literal(
        DatabaseDialect dialect,
        DatabaseType type,
        bool? isUnicode = null,
        int? length = null,
        int? precision = null,
        int? scale = null)
        => dialect switch
        {
            DatabaseDialect.SqlServer2022 => SqlServerLiteral(type, isUnicode, length, precision, scale),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect), dialect, null),
        };

    private static string SqlServerName(DatabaseType type, bool? isUnicode) => type switch
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

    private static string? SqlServerLiteral(
        DatabaseType type, bool? isUnicode, int? length, int? precision, int? scale)
    {
        var name = SqlServerName(type, isUnicode);

        return type switch
        {
            // A length is part of the type here, not decoration: without it T-SQL reads
            // these as a single unit.
            DatabaseType.Char or DatabaseType.VarChar or DatabaseType.Binary or DatabaseType.VarBinary
                => length is { } characters ? $"{name}({characters})" : null,

            // Bare decimal is decimal(18,0) in T-SQL, which drops the fractional part.
            DatabaseType.Decimal when precision is { } digits
                => scale is { } decimals ? $"{name}({digits},{decimals})" : $"{name}({digits})",
            DatabaseType.Decimal => null,

            // The temporal families default to their widest fractional precision, so the
            // bare name narrows nothing and an unstated facet stays unstated.
            DatabaseType.Time or DatabaseType.Timestamp or DatabaseType.TimestampWithTimeZone
                => precision is { } seconds ? $"{name}({seconds})" : name,

            _ => name,
        };
    }

    /// <summary>
    /// Splits "name(a[,b])" into the name and up to two integer arguments; "max", a missing
    /// argument list and a trailing word such as "varchar not null" all come back as null.
    /// </summary>
    private static (string Name, int? First, int? Second) SplitArguments(string type)
    {
        var open = type.IndexOf('(');

        if (open < 0)
        {
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
