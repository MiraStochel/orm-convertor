namespace AbstractWrappers;

/// <summary>
/// The limits the tool observes over one conversion, read once when the application starts
/// and handed to the parsers and the query builders by the orchestration (decisions 092 and
/// 194). Deliberately not a fact of the conversion request: the input a limit defends against
/// must not be able to carry permission to exceed it, so nothing here is ever filled from a
/// request body.
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
    /// How many characters the text of one query may have, read or written, before the tool
    /// refuses it (decision 194). Measured on 2026-10-30: the longest query any consumer of ours
    /// compiles is under 100 KB - javac takes no string constant over 64 KB, Roslyn gives up on
    /// an expression of a few thousand terms -, the longest honest query of the limits run at the
    /// ceilings of its series is 150 KB, and the shapes whose text doubles with every level of
    /// the input - a comparison that tests its operand for NULL around an operand that nests
    /// another, an include that inlines a fragment twice - cross a megabyte at 12 to 19 levels
    /// of a one-kilobyte input. 2^20 characters sits an order of magnitude above everything a
    /// consumer takes and stops the doubling while it still costs milliseconds.
    /// </summary>
    public const int DefaultMaxQueryTextLength = 1 << 20;

    /// <summary>
    /// The value that switches a cap off. An operator who writes it takes threat 2 back whole,
    /// process death included (threat-model.md). It is a value to write rather than a key to
    /// omit, so that no instance can lose a cap by forgetting to configure it.
    /// </summary>
    public const int Unlimited = 0;

    /// <summary>What every caller gets that says nothing: both caps at their defaults.</summary>
    public static ParseLimits Default { get; } = new();

    public int MaxNestingDepth { get; init; } = DefaultMaxNestingDepth;

    public int MaxQueryTextLength { get; init; } = DefaultMaxQueryTextLength;

    /// <summary>
    /// Whether nesting is capped at all. Anything at or below <see cref="Unlimited"/> is not,
    /// so a negative number configured by mistake reads as "off" rather than as "refuse
    /// everything" - the two failure modes are not equally bad, and this is the recoverable one.
    /// </summary>
    public bool CapsNesting => MaxNestingDepth > Unlimited;

    /// <summary>Whether the text of a query is capped at all; the same reading of a non-positive number as <see cref="CapsNesting"/>.</summary>
    public bool CapsQueryText => MaxQueryTextLength > Unlimited;
}
