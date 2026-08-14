using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public sealed class Relation
{
    public string? Name { get; set; }                  // distinguishes multiple relations between the same pair of entities
    public required Cardinality Cardinality { get; set; }
    public required RelationRole Role { get; set; }
    public required string SourceEntity { get; set; }  // entity name, not a reference (decision 001)
    public required string TargetEntity { get; set; }

    /// <summary>FK column pairs; empty = columns not yet resolved (see F4/F5).</summary>
    public IReadOnlyList<ColumnPair> ColumnPairs { get; set; } = [];

    /// <summary>Navigation property name on the source entity, if any.</summary>
    public string? SourceNavigationProperty { get; set; }

    public string? InverseRelationName { get; set; }   // pairs Owning <-> Inverse
}