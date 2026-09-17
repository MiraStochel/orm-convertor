using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace JavaEntityParsing;

/// <summary>
/// Conversion between a Java type name and the neutral <see cref="LangType"/> - the Java
/// counterpart of the C# convertor in Common, living in the ecosystem project rather than
/// in Common because decision 076 keeps Common untouched by the Java side (decision 077).
/// Language only: the column type a Java type maps to is a fact of the dialect, not of
/// the type (decision 014).
///
/// Nullability is Java's own axis: a primitive can never be null, a reference type always
/// can. What a JPA mapping says on top of that - a NOT NULL column, an identifier - is the
/// JPA layer's reading, applied to the type before it enters the model.
/// </summary>
public static class JavaTypeConvertor
{
    private static readonly HashSet<string> Primitives = new(StringComparer.Ordinal)
    {
        "int", "long", "short", "byte", "boolean", "float", "double", "char",
    };

    public static bool IsPrimitive(string typeText)
        => Primitives.Contains(StripPackage(typeText.Trim()));

    /// <summary>
    /// Reads a Java type name into a <see cref="LangType"/>. Never throws on an unknown
    /// name - that becomes Unknown with the name as written (decision 014). A reference is
    /// not recognized here: whether a name denotes an entity is the claim of an annotation
    /// or of the resolution phase. Nullability follows the language unless the caller
    /// states it: a primitive is never nullable, everything else is.
    /// </summary>
    public static LangType FromString(string? typeText, bool? isNullable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeText);

        var text = typeText.Trim();

        if (TryReadCollection(text, out var elementText, out var kind))
        {
            // The element of a collection is not nullable: a collection of entities holds
            // entities, and the question mark a C# target would write on the element is a
            // claim no Java declaration makes.
            return LangType.Collection(FromString(elementText, isNullable: false), kind, isNullable ?? true);
        }

        var primitive = IsPrimitive(text);
        var nullable = isNullable ?? !primitive;

        return TryReadScalar(text) is ScalarType scalar
            ? LangType.Scalar(scalar, nullable)
            : LangType.Unknown(text, nullable);
    }

    /// <summary>
    /// Renders a <see cref="LangType"/> as a Java type. A non-nullable scalar renders as a
    /// primitive where Java has one and a nullable one as its wrapper; the caller decides
    /// about an identifier, which is always a wrapper (decision 077). Collections render
    /// as the interface, List or Set, the way decision 035 renders them for NHibernate.
    /// </summary>
    public static string ToString(LangType langType, bool forceWrapper = false)
    {
        ArgumentNullException.ThrowIfNull(langType);

        return langType.Category switch
        {
            LangTypeCategory.Scalar => ScalarName(langType.ScalarType!.Value, langType.IsNullable || forceWrapper),
            LangTypeCategory.Reference => langType.TargetEntity!,
            LangTypeCategory.Collection =>
                $"{CollectionName(langType.CollectionKind!.Value)}<{ToString(langType.ElementType!, forceWrapper: true)}>",
            LangTypeCategory.Unknown => langType.SourceName!,
            _ => throw new ArgumentOutOfRangeException(nameof(langType), langType.Category, null),
        };
    }

    /// <summary>The import a rendered type needs, or null for a primitive, java.lang and an unknown name.</summary>
    public static string? ImportFor(LangType langType)
    {
        ArgumentNullException.ThrowIfNull(langType);

        return langType.Category switch
        {
            LangTypeCategory.Scalar => ScalarImport(langType.ScalarType!.Value),
            LangTypeCategory.Collection => langType.CollectionKind == CollectionKind.Set ? "java.util.Set" : "java.util.List",
            _ => null,
        };
    }

    /// <summary>The concrete class an empty collection of the kind is created with, and its import.</summary>
    public static (string Expression, string Import) EmptyCollection(CollectionKind kind) => kind switch
    {
        CollectionKind.Set => ("new HashSet<>()", "java.util.HashSet"),
        _ => ("new ArrayList<>()", "java.util.ArrayList"),
    };

    private static string ScalarName(ScalarType scalar, bool wrapper) => scalar switch
    {
        ScalarType.Bool => wrapper ? "Boolean" : "boolean",
        ScalarType.Byte => wrapper ? "Byte" : "byte",
        ScalarType.Short => wrapper ? "Short" : "short",
        ScalarType.Int => wrapper ? "Integer" : "int",
        ScalarType.Long => wrapper ? "Long" : "long",
        ScalarType.Float => wrapper ? "Float" : "float",
        ScalarType.Double => wrapper ? "Double" : "double",
        ScalarType.Char => wrapper ? "Character" : "char",
        ScalarType.Decimal => "BigDecimal",
        ScalarType.String => "String",
        ScalarType.DateTime => "LocalDateTime",
        ScalarType.Guid => "UUID",
        ScalarType.Object => "Object",
        ScalarType.Date => "LocalDate",
        ScalarType.TimeOfDay => "LocalTime",
        ScalarType.DateTimeOffset => "OffsetDateTime",
        ScalarType.Duration => "Duration",
        ScalarType.ByteArray => "byte[]",
        _ => throw new ArgumentOutOfRangeException(nameof(scalar), scalar, null),
    };

    private static string? ScalarImport(ScalarType scalar) => scalar switch
    {
        ScalarType.Decimal => "java.math.BigDecimal",
        ScalarType.DateTime => "java.time.LocalDateTime",
        ScalarType.Guid => "java.util.UUID",
        ScalarType.Date => "java.time.LocalDate",
        ScalarType.TimeOfDay => "java.time.LocalTime",
        ScalarType.DateTimeOffset => "java.time.OffsetDateTime",
        ScalarType.Duration => "java.time.Duration",
        _ => null,
    };

    private static string CollectionName(CollectionKind kind) => kind switch
    {
        CollectionKind.Unspecified or CollectionKind.List => "List",
        CollectionKind.Set => "Set",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static bool TryReadCollection(string text, out string elementText, out CollectionKind kind)
    {
        elementText = string.Empty;
        kind = CollectionKind.Unspecified;

        var genericStart = text.IndexOf('<');
        if (genericStart < 0 || !text.EndsWith('>'))
        {
            return false;
        }

        var name = StripPackage(text[..genericStart].Trim());
        var argumentText = text[(genericStart + 1)..^1].Trim();

        if (argumentText.Length == 0 || HasTopLevelComma(argumentText))
        {
            return false;
        }

        switch (name)
        {
            case "List" or "ArrayList" or "LinkedList":
                kind = CollectionKind.List;
                break;
            case "Set" or "HashSet" or "LinkedHashSet" or "TreeSet" or "SortedSet":
                kind = CollectionKind.Set;
                break;
            case "Collection" or "Iterable":
                kind = CollectionKind.Unspecified;
                break;
            default:
                return false;
        }

        elementText = argumentText;
        return true;
    }

    private static bool HasTopLevelComma(string text)
    {
        var depth = 0;

        foreach (var c in text)
        {
            switch (c)
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    return true;
            }
        }

        return false;
    }

    private static ScalarType? TryReadScalar(string text) => StripPackage(text).Replace(" ", string.Empty) switch
    {
        "boolean" or "Boolean" => ScalarType.Bool,
        "byte" or "Byte" => ScalarType.Byte,
        "short" or "Short" => ScalarType.Short,
        "int" or "Integer" => ScalarType.Int,
        "long" or "Long" => ScalarType.Long,
        "float" or "Float" => ScalarType.Float,
        "double" or "Double" => ScalarType.Double,
        "char" or "Character" => ScalarType.Char,
        "BigDecimal" => ScalarType.Decimal,
        "String" => ScalarType.String,
        "LocalDateTime" => ScalarType.DateTime,
        "UUID" => ScalarType.Guid,
        "Object" => ScalarType.Object,
        "LocalDate" => ScalarType.Date,
        "LocalTime" => ScalarType.TimeOfDay,
        "OffsetDateTime" => ScalarType.DateTimeOffset,
        "Duration" => ScalarType.Duration,
        "byte[]" or "Byte[]" => ScalarType.ByteArray,
        _ => null,
    };

    /// <summary>The simple name of a qualified one; an array keeps its brackets.</summary>
    public static string StripPackage(string text)
    {
        var generic = text.IndexOf('<');
        var head = generic < 0 ? text : text[..generic];
        var lastDot = head.LastIndexOf('.');
        return lastDot < 0 ? text : text[(lastDot + 1)..];
    }
}
