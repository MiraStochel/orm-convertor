using AbstractWrappers.Descriptors;

namespace AbstractWrappers.Diagnostics;

/// <summary>
/// One mapping fact of the representation at generation time, with where it came from and
/// what became of it (decision 124). The records of decision 010 say what did <em>not</em>
/// travel - a loss, a convention, a supplied value; a fact that travelled had no record,
/// so a reader comparing input and output had nothing to pair them by. This is that
/// sentence, one per fact, written by the builder base alone: every write into the
/// representation goes through its methods, so no parser and no builder has to say
/// anything for the ledger to be complete, and a new framework pays nothing for it (S1).
/// </summary>
public sealed record ConversionFact
{
    /// <summary>Simple name of the entity the fact belongs to.</summary>
    public required string Entity { get; init; }

    /// <summary>
    /// The property the fact is about - a key part, a navigation for foreign key columns, the
    /// one column of a single-column unique constraint; null for a fact of the entity itself
    /// (table, schema, a constraint over several columns).
    /// </summary>
    public string? Property { get; init; }

    public required MappingFactCategory Category { get; init; }

    /// <summary>
    /// The fact's value as the representation holds it - the table name, the column name, the
    /// length, the strategy, the foreign key columns joined by commas - or null where the
    /// category has no value beyond being stated (the key, a version column, a transient
    /// property). Given so that a reader can find the fact in both texts; where it shows in
    /// them is the reader's search, not a statement of the tool (decision 124).
    /// </summary>
    public string? Value { get; init; }

    public required FactOrigin Origin { get; init; }

    /// <summary>
    /// The input unit that stated the fact, as records reference units (decision 066); null
    /// unless <see cref="Origin"/> is <see cref="FactOrigin.Source"/>.
    /// </summary>
    public string? Unit { get; init; }

    public required FactOutcome Outcome { get; init; }

    /// <summary>
    /// Where the fact stands in the unit that stated it, or null where the reader stated no
    /// position (decision 125). The position is the reader's statement, computed by the
    /// builder base from the reading scope the reader opened; a fact without one is not a
    /// fact without a source, only one whose reader did not say where.
    /// </summary>
    public SourcePosition? Source { get; init; }

    /// <summary>
    /// Where the fact stands in the artifacts of its entity, as many places as the builder
    /// marked while writing them (decision 125); empty where the builder marked nothing. A
    /// builder that marks nothing is complete all the same - the ledger row stands, only the
    /// position is missing - which is what keeps the layer optional for a new framework (S1).
    /// </summary>
    public IReadOnlyList<TargetPosition> Targets { get; init; } = [];
}
