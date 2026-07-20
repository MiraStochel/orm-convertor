using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public class Property
{
    public required string Name { get; set; }

    public required CLRTypeModel Type { get; set; }

    public bool IsNullable { get; set; } = false;

    public AccessModifier? AccessModifier { get; set; }

    public List<string> OtherModifiers { get; set; } = [];

    public bool HasGetter { get; set; } = false;

    public bool HasSetter { get; set; } = false;

    public string? DefaultValue { get; set; }
}
