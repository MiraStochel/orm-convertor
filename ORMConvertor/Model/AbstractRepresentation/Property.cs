using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public class Property
{
    public required string Name { get; set; }

    /// <summary>
    /// Language type of the property, including its language-side nullability.
    /// Null means nobody stated the type - a property known only from a mapping
    /// descriptor, not from source code (decision 014).
    /// </summary>
    public LangType? Type { get; set; }

    public AccessModifier? AccessModifier { get; set; }

    public List<string> OtherModifiers { get; set; } = [];

    public bool HasGetter { get; set; } = false;

    public bool HasSetter { get; set; } = false;

    public string? DefaultValue { get; set; }
}
