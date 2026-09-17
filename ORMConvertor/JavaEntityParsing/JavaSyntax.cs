namespace JavaEntityParsing;

/// <summary>
/// The kinds of value an annotation argument can hold that the readers tell apart. Other
/// keeps the text of anything else - an arithmetic expression, a method reference - so
/// that a record can name what was not read.
/// </summary>
public enum JavaAnnotationValueKind
{
    String,
    Number,
    Boolean,
    Char,

    /// <summary>A bare or qualified name: an enum constant such as GenerationType.IDENTITY, or a constant.</summary>
    Name,

    /// <summary>A class literal, Customer.class; <see cref="JavaAnnotationValue.Text"/> holds the type name without .class.</summary>
    ClassLiteral,

    Annotation,
    Array,
    Other,
}

public sealed record JavaAnnotationValue(
    JavaAnnotationValueKind Kind,
    string Text,
    JavaAnnotation? Annotation = null,
    IReadOnlyList<JavaAnnotationValue>? Items = null)
{
    /// <summary>The elements of an array value, or the value itself as a one-element list - annotations admit both spellings.</summary>
    public IReadOnlyList<JavaAnnotationValue> AsList => Kind == JavaAnnotationValueKind.Array ? Items ?? [] : [this];

    /// <summary>The last segment of a name: IDENTITY out of GenerationType.IDENTITY.</summary>
    public string SimpleName => Text.Split('.')[^1];
}

/// <summary>An argument of an annotation; a null name is the single-value form, which means "value".</summary>
public sealed record JavaAnnotationArgument(string? Name, JavaAnnotationValue Value);

public sealed record JavaAnnotation(string Name, IReadOnlyList<JavaAnnotationArgument> Arguments, int Line, int Column)
{
    /// <summary>The annotation's name without its package: Column out of jakarta.persistence.Column.</summary>
    public string SimpleName => Name.Split('.')[^1];

    /// <summary>The package the annotation was written with, or null when it was written bare.</summary>
    public string? Qualifier
    {
        get
        {
            var dot = Name.LastIndexOf('.');
            return dot < 0 ? null : Name[..dot];
        }
    }

    /// <summary>The argument of the given element, "value" covering the single-value form.</summary>
    public JavaAnnotationValue? this[string element] =>
        Arguments.FirstOrDefault(a => string.Equals(a.Name ?? "value", element, StringComparison.Ordinal))?.Value;

    public string? String(string element) => this[element] is { Kind: JavaAnnotationValueKind.String } v ? v.Text : null;

    public bool? Boolean(string element) => this[element] is { Kind: JavaAnnotationValueKind.Boolean } v ? v.Text == "true" : null;

    public int? Int(string element) => this[element] is { Kind: JavaAnnotationValueKind.Number } v
        && int.TryParse(v.Text.Replace("_", string.Empty).TrimEnd('L', 'l'), out var n) ? n : null;
}

public sealed record JavaField(
    string Name,
    string Type,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<JavaAnnotation> Annotations,
    string? Initializer,
    int Line);

public sealed record JavaMethod(
    string Name,
    string ReturnType,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<JavaAnnotation> Annotations,
    int ParameterCount,
    int Line);

public sealed record JavaClass(
    string Name,
    IReadOnlyList<string> Modifiers,
    IReadOnlyList<JavaAnnotation> Annotations,
    IReadOnlyList<JavaField> Fields,
    IReadOnlyList<JavaMethod> Methods,
    IReadOnlyList<JavaClass> NestedClasses,
    string? Extends,
    IReadOnlyList<string> Interfaces,
    int Line)
{
    public bool IsStatic => Modifiers.Contains("static", StringComparer.Ordinal);

    /// <summary>The getter of a field by the JavaBeans convention: getName, or isName for a boolean.</summary>
    public JavaMethod? GetterOf(string name) => Methods.FirstOrDefault(m =>
        m.ParameterCount == 0
        && (IsAccessor(m.Name, "get", name) || IsAccessor(m.Name, "is", name)));

    public JavaMethod? SetterOf(string name) => Methods.FirstOrDefault(m =>
        m.ParameterCount == 1 && IsAccessor(m.Name, "set", name));

    private static bool IsAccessor(string method, string prefix, string property)
        => method.Length > prefix.Length
           && method.StartsWith(prefix, StringComparison.Ordinal)
           && string.Equals(method[prefix.Length..], Capitalize(property), StringComparison.Ordinal);

    /// <summary>The property name a JavaBeans accessor names: bornOn out of getBornOn, URL out of getURL (JavaBeans §8.8).</summary>
    public static string? PropertyOfAccessor(string method)
    {
        foreach (var prefix in new[] { "get", "set", "is" })
        {
            if (method.Length > prefix.Length && method.StartsWith(prefix, StringComparison.Ordinal))
            {
                var rest = method[prefix.Length..];
                return rest.Length > 1 && char.IsUpper(rest[0]) && char.IsUpper(rest[1])
                    ? rest
                    : char.ToLowerInvariant(rest[0]) + rest[1..];
            }
        }

        return null;
    }

    public static string Capitalize(string name)
        => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}

public sealed record JavaCompilationUnit(string? Package, IReadOnlyList<string> Imports, IReadOnlyList<JavaClass> Classes);
