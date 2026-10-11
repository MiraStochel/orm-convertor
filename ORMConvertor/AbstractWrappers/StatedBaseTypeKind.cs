namespace AbstractWrappers;

/// <summary>
/// What a source says about a name on the header of an entity class, as far as it says
/// anything (decision 048): the kind decides what <see cref="AbstractEntityBuilder.ReportStatedBaseTypes"/>
/// reports about a name no unit of the conversion declares. A name that resolves to an
/// entity of the conversion is a mapped hierarchy whatever its kind.
/// </summary>
public enum StatedBaseTypeKind
{
    /// <summary>The source states the name is the class the entity extends - Java's extends clause.</summary>
    Class,

    /// <summary>
    /// The name stands where the language puts the base class, and the source does not say
    /// whether it is one - the first entry of a C# base list, which is the base class if
    /// there is one and an interface otherwise.
    /// </summary>
    ClassOrInterface,

    /// <summary>
    /// The source says the name is an interface - Java's implements, or any later entry of
    /// a C# base list. Nothing of the mapping is inherited through it.
    /// </summary>
    Interface,
}
