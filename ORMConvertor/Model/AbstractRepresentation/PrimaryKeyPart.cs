using Model.AbstractRepresentation.Enums;

namespace Model.AbstractRepresentation;

public sealed class PrimaryKeyPart
{
    // Type, column name, nullability etc. are taken from Property/PropertyMap.
    public required PropertyMap PropertyMap { get; init; }

    // Explicit 1-based order - NOT the position in the list.
    public required int Order { get; init; }

    // Per-part generation strategy, not for the whole key (see §3.4).
    public PrimaryKeyStrategy Strategy { get; init; } = PrimaryKeyStrategy.Unspecified;

    /// <summary>
    /// What the source called the strategy, kept when the vocabulary did not capture it -
    /// a custom generator class, or a variant such as guid.comb next to Uuid. A record of
    /// the source next to the value, like PrimaryKey.SourceKeyClass (decision 011).
    /// </summary>
    public string? SourceStrategyName { get; init; }

    /// <summary>
    /// Generator parameters as key-value pairs: sequence name, block size, counter table.
    /// Without them a sequence-backed key translates into a mapping that compiles and does
    /// not run, because it points at a sequence the target database need not have.
    /// </summary>
    public Dictionary<string, string> StrategyParameters { get; init; } = [];
}
