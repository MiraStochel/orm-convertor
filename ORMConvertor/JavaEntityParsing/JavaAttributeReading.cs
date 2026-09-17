namespace JavaEntityParsing;

/// <summary>
/// Where a Java class keeps the annotations of its attributes: on the fields, or on the
/// getters. A framework decides it - JPA by where @Id sits, or by @Access - and the
/// language facts read the same either way.
/// </summary>
public enum JavaMemberAccess
{
    Field,
    Property,
}

/// <summary>
/// The language facts of one attribute of a Java class - what the class itself says,
/// before any annotation is interpreted (the Java counterpart of the C# PropertyReading).
/// Java has no property as a language element, so an attribute is a field together with
/// the accessors named after it; the access modifier is the getter's where one exists,
/// because that is what the property's visibility means to a caller.
/// </summary>
/// <param name="Type">The written type in its normalized spelling.</param>
/// <param name="AccessModifier">public, protected, private, or empty for package-private.</param>
/// <param name="DefaultValue">The initializer, only when it is a literal both languages spell alike; null otherwise.</param>
/// <param name="DroppedInitializer">An initializer that is not such a literal, kept for the record.</param>
/// <param name="IsNullable">Language nullability: false for a primitive, true for a reference type.</param>
/// <param name="Annotations">The annotations of the member the access type reads them from.</param>
/// <param name="HasTransientModifier">The Java transient modifier, which JPA reads as @Transient.</param>
/// <param name="DroppedModifiers">Modifiers with no counterpart in the model - final, volatile.</param>
public sealed record JavaAttributeReading(
    string Name,
    string Type,
    string AccessModifier,
    bool HasGetter,
    bool HasSetter,
    string? DefaultValue,
    string? DroppedInitializer,
    bool IsNullable,
    IReadOnlyList<JavaAnnotation> Annotations,
    bool HasTransientModifier,
    IReadOnlyList<string> DroppedModifiers,
    int Line)
{
    /// <summary>Whether the field had an initializer at all, whether or not it travelled.</summary>
    public bool HasInitializer => DefaultValue is not null || DroppedInitializer is not null;

    /// <summary>Whether the written type is a collection of the language vocabulary.</summary>
    public bool IsCollection => JavaTypeConvertor.FromString(Type).Category == Model.AbstractRepresentation.Enums.LangTypeCategory.Collection;
}
