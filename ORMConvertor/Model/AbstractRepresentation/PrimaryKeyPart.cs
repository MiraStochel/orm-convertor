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
    /// Canonical generator parameters (decision 020): the closed vocabulary fixes meaning and
    /// unit, so BlockSize holds the number of values in a block whatever the source called it.
    /// Without them a sequence-backed key translates into a mapping that compiles and does
    /// not run, because it points at a sequence the target database need not have.
    /// </summary>
    public IReadOnlyDictionary<GeneratorParameter, string> StrategyParameters { get; init; } =
        new Dictionary<GeneratorParameter, string>();

    /// <summary>
    /// Parameters as the source wrote them, where the vocabulary did not capture them: all
    /// parameters of a strategy that stayed on the escape path (Unspecified with a named
    /// generator), and words local to one generator elsewhere. A record of the source next
    /// to the canonical value, like SourceStrategyName (decision 020).
    /// </summary>
    public Dictionary<string, string> SourceStrategyParameters { get; init; } = [];
}
