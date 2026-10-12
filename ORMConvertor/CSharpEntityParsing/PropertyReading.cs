namespace CSharpEntityParsing;

/// <summary>
/// The language facts of one property declaration — what the class itself says about the
/// property, before any annotation or convention of a framework is applied.
/// </summary>
/// <param name="Type">
/// The written type with the nullable question mark stripped; <paramref name="IsNullable"/>
/// carries the question mark as its own fact.
/// </param>
/// <param name="HasSetter">
/// Whether the class offers its callers a setter: a set or init accessor without an access
/// modifier of its own (decision 134).
/// </param>
/// <param name="IsInitOnly">
/// Whether the setter offered is an init accessor, whose restriction to initialization the
/// model has no place for.
/// </param>
public sealed record PropertyReading(
    string Type,
    string Name,
    string AccessModifiers,
    List<string> OtherModifiers,
    bool HasGetter,
    bool HasSetter,
    bool IsInitOnly,
    string? DefaultValue,
    bool IsNullable);
