using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public sealed class PrimaryKeyPart
{
    // Type, column name, nullability etc. are taken from Property/PropertyMap.
    public required PropertyMap PropertyMap { get; init; }

    // Explicit 1-based order - NOT the position in the list (see design doc 001, §3.2).
    public required int Order { get; init; }

    // Per-part generation strategy, not for the whole key (see §3.4).
    public PrimaryKeyStrategy Strategy { get; init; } = PrimaryKeyStrategy.None;
}