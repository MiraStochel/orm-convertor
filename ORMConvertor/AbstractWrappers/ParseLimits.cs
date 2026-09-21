namespace AbstractWrappers;

/// <summary>
/// The limits every parser observes over its input, read once when the application starts
/// and handed to the parsers by the orchestration (decision 092). Deliberately not a fact of
/// the conversion request: the input a limit defends against must not be able to carry
/// permission to exceed it, so nothing here is ever filled from a request body.
/// </summary>
public sealed record ParseLimits
{
    /// <summary>
    /// How deep a unit may nest before the parsers refuse it. Measured on 2026-09-21 over
    /// every language the tool reads recursively: the most expensive shape - nested
    /// subqueries, and T-SQL on plain parentheses - still read 1024 levels and killed the
    /// process at 2048, by overflowing the stack, which .NET cannot catch. 128 sits an order
    /// of magnitude below the first measured crash and an order of magnitude above the
    /// deepest input anyone writes or any builder of ours emits.
    /// </summary>
    public const int DefaultMaxNestingDepth = 128;

    /// <summary>
    /// The value that switches the cap off. An operator who writes it takes threat 2 back
    /// whole, process death included (threat-model.md). It is a value to write rather than a
    /// key to omit, so that no instance can lose the cap by forgetting to configure it.
    /// </summary>
    public const int Unlimited = 0;

    /// <summary>What every caller gets that says nothing: the cap at its default.</summary>
    public static ParseLimits Default { get; } = new();

    public int MaxNestingDepth { get; init; } = DefaultMaxNestingDepth;

    /// <summary>
    /// Whether nesting is capped at all. Anything at or below <see cref="Unlimited"/> is not,
    /// so a negative number configured by mistake reads as "off" rather than as "refuse
    /// everything" - the two failure modes are not equally bad, and this is the recoverable one.
    /// </summary>
    public bool CapsNesting => MaxNestingDepth > Unlimited;
}
