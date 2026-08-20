namespace CSharpEntityParsing;

/// <summary>
/// The language facts of one property declaration — what the class itself says about the
/// property, before any annotation or convention of a framework is applied.
/// </summary>
/// <param name="Type">
/// The written type with the nullable question mark stripped; <paramref name="IsNullable"/>
/// carries the question mark as its own fact.
/// </param>
public sealed record PropertyReading(
    string Type,
    string Name,
    string AccessModifiers,
    List<string> OtherModifiers,
    bool HasGetter,
    bool HasSetter,
    string? DefaultValue,
    bool IsNullable);
