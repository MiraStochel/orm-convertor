using Model.AbstractRepresentation.Enums;

namespace NHibernateWrappers.Convertors;

public static class DatabaseTypeConvertor
{
    public static DatabaseType FromNHibernate(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentNullException(nameof(type));
        }

        return type.Trim().ToLowerInvariant() switch
        {
            "int64" or "long" => DatabaseType.BigInt,

            "int32" or "int" or "integer" => DatabaseType.Int,

            "int16" or "short" => DatabaseType.SmallInt,

            "byte" => DatabaseType.TinyInt,
            "boolean" or "bool" => DatabaseType.Bit,

            "decimal" or "big_decimal" => DatabaseType.Decimal,
            "currency" => DatabaseType.Money,
            "double" => DatabaseType.Float,
            "single" or "float" => DatabaseType.Real,

            "date" => DatabaseType.Date,
            "datetime" or "datetime2" or "datetimenoms" or "dbtimestamp" => DatabaseType.DateTime2,
            "smalldatetime" => DatabaseType.SmallDateTime,
            "time" or "timeastimespan" => DatabaseType.Time,
            "datetimeoffset" => DatabaseType.DateTimeOffset,

            "ansistringfixedlength" or "ansichar" or "char" => DatabaseType.Char,
            "ansistring" => DatabaseType.VarChar,
            "ansistringclob" => DatabaseType.Text,
            "stringfixedlength" => DatabaseType.NChar,
            "string" => DatabaseType.NVarChar,
            "stringclob" => DatabaseType.NText,

            "binary" => DatabaseType.Binary,
            "binaryblob" => DatabaseType.Image,

            "guid" => DatabaseType.UniqueIdentifier,
            "xml" => DatabaseType.Xml,
            "serializable" or "object" => DatabaseType.SqlVariant,
            "timestamp" or "rowversion" => DatabaseType.RowVersion,
            _ => throw new NotImplementedException(type),
        };
    }

    public static string ToNHibernate(DatabaseType type) => type switch
    {
        DatabaseType.BigInt => "Int64",
        DatabaseType.Int => "Int32",
        DatabaseType.SmallInt => "Int16",
        DatabaseType.TinyInt => "Byte",
        DatabaseType.Bit => "Boolean",

        DatabaseType.Decimal => "Decimal",
        DatabaseType.Numeric => "Decimal",
        DatabaseType.Money => "Currency",
        DatabaseType.SmallMoney => "Currency",

        DatabaseType.Float => "Double",
        DatabaseType.Real => "Single",

        DatabaseType.Date => "Date",
        DatabaseType.DateTime => "DateTime",
        DatabaseType.DateTime2 => "DateTime",
        DatabaseType.SmallDateTime => "DateTime",
        DatabaseType.Time => "TimeAsTimeSpan",
        DatabaseType.DateTimeOffset => "DateTimeOffset",

        DatabaseType.Char => "AnsiStringFixedLength",
        DatabaseType.VarChar => "AnsiString",
        DatabaseType.Text => "AnsiStringClob",
        DatabaseType.NChar => "StringFixedLength",
        DatabaseType.NVarChar => "String",
        DatabaseType.NText => "StringClob",

        DatabaseType.Binary => "Binary",
        DatabaseType.VarBinary => "Binary",
        DatabaseType.Image => "BinaryBlob",

        DatabaseType.UniqueIdentifier => "Guid",
        DatabaseType.Xml => "Xml",
        DatabaseType.SqlVariant => "Serializable",
        DatabaseType.RowVersion => "Timestamp",

        _ => throw new NotImplementedException(type.ToString())
    };

    public static string GuessFromPropertyType(CLRType clrType)
    {
        return clrType switch
        {
            CLRType.Bool => ToNHibernate(DatabaseType.Bit),
            CLRType.Byte => ToNHibernate(DatabaseType.TinyInt),
            CLRType.Char => ToNHibernate(DatabaseType.Char),
            CLRType.Int => ToNHibernate(DatabaseType.Int),
            CLRType.Long => ToNHibernate(DatabaseType.BigInt),
            CLRType.Double => ToNHibernate(DatabaseType.Float),
            CLRType.Float => ToNHibernate(DatabaseType.Real),
            CLRType.Decimal => ToNHibernate(DatabaseType.Decimal),
            CLRType.String => ToNHibernate(DatabaseType.NVarChar),
            CLRType.DateTime => ToNHibernate(DatabaseType.DateTime2),
            _ => throw new NotImplementedException(clrType.ToString())
        };
    }
}
