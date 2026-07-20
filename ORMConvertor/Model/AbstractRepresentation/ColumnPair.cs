namespace Model.AbstractRepresentation;

/// <summary>
/// Ordered pair of columns of a composite FK (source.Col1 <-> target.Col1).
/// A class rather than a tuple on purpose - System.Text.Json does not serialize tuple items.
/// </summary>
public sealed class ColumnPair
{
    public required PropertyMap Source { get; init; }
    public required PropertyMap Target { get; init; }
}