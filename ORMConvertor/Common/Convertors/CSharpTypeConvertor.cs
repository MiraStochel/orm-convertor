using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;

namespace Common.Convertors;

/// <summary>
/// Conversion between a C# type name and the neutral <see cref="LangType"/>. Language
/// only: the table from a language type to a database column type is a default
/// assumption of a concrete framework, so it lives in the wrappers, not here
/// (decision 014).
/// </summary>
public static class CSharpTypeConvertor
{
    /// <summary>
    /// Reads a C# type name into a <see cref="LangType"/>. Never throws on an
    /// unrecognized name - that becomes <see cref="LangTypeCategory.Unknown"/> with the
    /// name kept as the source wrote it. A reference is not recognized here: whether a
    /// name denotes an entity is a claim of a mapping or an annotation, so the category
    /// is assigned by the parser that read the claim, not by the name alone.
    /// </summary>
    /// <param name="typeText">The C# type as written in the source, without a nullable suffix.</param>
    /// <param name="isNullable">Language-side nullability of the property.</param>
    public static LangType FromString(string? typeText, bool isNullable = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeText);

        var text = typeText.Trim();

        // A nullable suffix normally never arrives - the parsers strip it - but a
        // recursive call on a collection element can carry one.
        if (text.EndsWith('?'))
        {
            return FromString(text[..^1], isNullable: true);
        }

        if (TryReadCollection(text, out var elementText, out var kind))
        {
            return LangType.Collection(FromString(elementText), kind, isNullable);
        }

        return TryReadScalar(text) is ScalarType scalar
            ? LangType.Scalar(scalar, isNullable)
            : LangType.Unknown(text, isNullable);
    }

    /// <summary>
    /// Renders a <see cref="LangType"/> as a C# type, without a nullable suffix on the
    /// top level - whether one belongs there is the builder's call (a key property, for
    /// one, never carries it). A collection element renders its own suffix, since no
    /// builder decides about it. An unknown type renders under the name the source
    /// wrote: there is no canonical alternative, and silence would mean an incomplete
    /// artifact instead of an incomplete claim (decision 014).
    /// </summary>
    public static string ToString(LangType langType)
    {
        ArgumentNullException.ThrowIfNull(langType);

        return langType.Category switch
        {
            LangTypeCategory.Scalar => ScalarName(langType.ScalarType!.Value),
            LangTypeCategory.Reference => langType.TargetEntity!,
            LangTypeCategory.Collection =>
                $"{CollectionName(langType.CollectionKind!.Value)}<{ElementName(langType.ElementType!)}>",
            LangTypeCategory.Unknown => langType.SourceName!,
            _ => throw new ArgumentOutOfRangeException(nameof(langType), langType.Category, null),
        };
    }

    private static string ElementName(LangType element)
        => element.IsNullable ? $"{ToString(element)}?" : ToString(element);

    private static string ScalarName(ScalarType scalar) => scalar switch
    {
        ScalarType.Bool => "bool",
        ScalarType.Byte => "byte",
        ScalarType.Short => "short",
        ScalarType.Int => "int",
        ScalarType.Long => "long",
        ScalarType.Float => "float",
        ScalarType.Double => "double",
        ScalarType.Decimal => "decimal",
        ScalarType.Char => "char",
        ScalarType.String => "string",
        ScalarType.DateTime => "DateTime",
        ScalarType.Guid => "Guid",
        ScalarType.Object => "object",
        _ => throw new ArgumentOutOfRangeException(nameof(scalar), scalar, null),
    };

    private static string CollectionName(CollectionKind kind) => kind switch
    {
        // Unspecified means the source did not commit to a kind; List is the C# default
        // shape and restates no claim the source never made about ordering semantics.
        CollectionKind.Unspecified or CollectionKind.List => "List",
        CollectionKind.Set => "HashSet",
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

        var name = StripNamespace(text[..genericStart].Trim());
        var argumentText = text[(genericStart + 1)..^1].Trim();

        // A comma at the top level means more than one type argument - a dictionary or
        // a tuple - which is no collection of entities; the whole name stays unknown.
        if (argumentText.Length == 0 || HasTopLevelComma(argumentText))
        {
            return false;
        }

        switch (name)
        {
            case "List" or "IList" or "IReadOnlyList":
                kind = CollectionKind.List;
                break;
            case "HashSet" or "ISet" or "IReadOnlySet":
                kind = CollectionKind.Set;
                break;
            case "ICollection" or "IEnumerable" or "IReadOnlyCollection":
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

    private static ScalarType? TryReadScalar(string text) => StripNamespace(text).ToLowerInvariant() switch
    {
        "bool" or "boolean" => ScalarType.Bool,
        "byte" => ScalarType.Byte,
        "short" or "int16" => ScalarType.Short,
        "int" or "int32" => ScalarType.Int,
        "long" or "int64" => ScalarType.Long,
        "float" or "single" => ScalarType.Float,
        "double" => ScalarType.Double,
        "decimal" => ScalarType.Decimal,
        "char" => ScalarType.Char,
        "string" => ScalarType.String,
        "datetime" => ScalarType.DateTime,
        "guid" => ScalarType.Guid,
        "object" => ScalarType.Object,
        _ => null,
    };

    private static string StripNamespace(string text)
    {
        var lastDot = text.LastIndexOf('.');
        return lastDot < 0 ? text : text[(lastDot + 1)..];
    }
}
