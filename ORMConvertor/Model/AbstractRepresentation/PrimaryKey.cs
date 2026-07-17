namespace Model.AbstractRepresentation;

public sealed class PrimaryKey
{
    public required IReadOnlyList<PrimaryKeyPart> Parts { get; init; }
}