using Common.Sql;
using Model.AbstractRepresentation.Enums;

namespace EFCoreWrappers.Convertors;

public class DatabaseTypeConvertor
{
    /// <summary>
    /// Reads what EF Core puts in [Column(TypeName = ...)] into the neutral vocabulary.
    /// The value is either a SQL type - which the shared dialect table reads (decision 086) -
    /// or a CLR type name, which is EF Core's own liberty and is read here, because a CLR
    /// name is no SQL at all. Never throws on an unknown name: the literal spelling is kept
    /// on the escape path and the caller records it (decisions 010 and 019).
    /// </summary>
    public static SqlTypeReading FromEfCore(string? columnTypeOrClr)
    {
        if (string.IsNullOrWhiteSpace(columnTypeOrClr))
        {
            throw new ArgumentNullException(nameof(columnTypeOrClr));
        }

        var reading = SqlTypeSpelling.Read(columnTypeOrClr);

        if (reading.Type is not null)
        {
            return reading;
        }

        return columnTypeOrClr.Trim().ToLowerInvariant() switch
        {
            "long" or "int64" => new(DatabaseType.BigInt),
            "int32" => new(DatabaseType.Integer),
            "int16" or "short" => new(DatabaseType.SmallInt),
            "byte" => new(DatabaseType.TinyInt),
            "bool" or "boolean" => new(DatabaseType.Boolean),
            "system.decimal" => new(DatabaseType.Decimal),
            "double" => new(DatabaseType.DoublePrecision),
            "single" => new(DatabaseType.Real),
            "system.datetime" => new(DatabaseType.Timestamp),
            "system.dateonly" or "dateonly" => new(DatabaseType.Date),
            "system.timeonly" or "timeonly" => new(DatabaseType.Time),
            "system.timespan" or "timespan" => new(DatabaseType.Time),
            "system.datetimeoffset" => new(DatabaseType.TimestampWithTimeZone),
            "guid" or "system.guid" or "uuid" => new(DatabaseType.Uuid),

            // Not a SQL name and not a CLR one either: the literal spelling travels and
            // the caller reports that no family was claimed.
            _ => reading,
        };
    }
}
