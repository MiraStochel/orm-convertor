using Common.Convertors;
using Model.AbstractRepresentation.Enums;

namespace NHibernateWrappers.Convertors;

/// <summary>
/// The other half of what an NHibernate type name states. <see cref="DatabaseTypeConvertor"/>
/// reads the column side of the name; this reads the value side - the CLR type of the IType
/// the name resolves to, in the scalar vocabulary. One name states both at once, which is
/// why Date and DateOnlyAsDate read one family there and two scalars here (decision 071).
///
/// Read where a type name types a value rather than a column: the type a named query
/// declares for one of its parameters, &lt;query-param type="Int32"/&gt;, which decision 083
/// carries as the parameter's stated scalar.
/// </summary>
public static class ScalarTypeConvertor
{
    /// <summary>
    /// The scalar a type name states, or null where it states none of the vocabulary - an
    /// XML document, an unsigned integer, a user type -, which the caller records. The names
    /// are those <see cref="DatabaseTypeConvertor.FromNHibernate"/> reads, with YesNo and
    /// TrueFalse beside them, whose column is a character and whose value is a bool. A name
    /// qualified with System. is a CLR type, which TypeFactory of NHibernate 5.7.0 resolves
    /// by reflection rather than by its registry, and reads as the scalar of that type.
    /// </summary>
    public static ScalarType? FromNHibernate(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        var name = type.Trim();

        return name.ToLowerInvariant() switch
        {
            "boolean" or "bool" or "yesno" or "truefalse" => ScalarType.Bool,
            "byte" => ScalarType.Byte,
            "int16" or "short" => ScalarType.Short,
            "int32" or "int" or "integer" => ScalarType.Int,
            "int64" or "long" => ScalarType.Long,
            "decimal" or "big_decimal" or "currency" => ScalarType.Decimal,
            "single" or "float" => ScalarType.Float,
            "double" => ScalarType.Double,

            // Date, LocalDate and Time read a DateTime whatever their column; the names that
            // read the newer CLR types say so in their own spelling (decision 071).
            "date" or "localdate" or "time"
                or "datetime" or "datetime2" or "dbtimestamp" or "timestamp"
                or "localdatetime" or "utcdatetime" or "datetimenoms" => ScalarType.DateTime,
            "dateonlyasdate" => ScalarType.Date,
            "timeonlyastime" or "timeonlyasticks" or "timeonlyasdatetime" => ScalarType.TimeOfDay,
            "timeastimespan" or "timespan" => ScalarType.Duration,
            "datetimeoffset" => ScalarType.DateTimeOffset,

            "char" or "ansichar" => ScalarType.Char,
            "string" or "ansistring" or "stringfixedlength" or "ansistringfixedlength"
                or "stringclob" or "ansistringclob" => ScalarType.String,

            "binary" or "byte[]" or "system.byte[]" or "binaryblob" => ScalarType.ByteArray,
            "guid" => ScalarType.Guid,

            _ => FromClrName(name),
        };
    }

    /// <summary>
    /// A CLR type named by its full name, assembly-qualified or not. Object is no answer:
    /// a parameter typed so would make the generated method take any value, which is the
    /// one thing decision 083 typed parameters to prevent.
    /// </summary>
    private static ScalarType? FromClrName(string name)
    {
        var clrName = name.Split(',')[0].Trim();

        if (!clrName.StartsWith("System.", StringComparison.Ordinal))
        {
            return null;
        }

        return CSharpTypeConvertor.FromString(clrName) is { Category: LangTypeCategory.Scalar, ScalarType: { } scalar }
            && scalar != ScalarType.Object
                ? scalar
                : null;
    }
}
