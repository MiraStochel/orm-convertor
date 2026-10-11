namespace AbstractWrappers.Diagnostics;

/// <summary>
/// What became of one mapping fact in the target's artifacts (decision 124). The vocabulary
/// is deliberately the one a reader can check against the two texts: the fact is in the
/// output, it is not, or there is no output to look in.
/// </summary>
public enum FactOutcome
{
    /// <summary>
    /// The target expresses the category and no record says the fact was dropped on the
    /// way, so the artifact states it. This is the complement of the <c>Loss</c> records
    /// (decision 004): a loss the builder did not report would make the claim wrong, which
    /// is exactly the invariant those records already carry.
    /// </summary>
    Carried = 1,

    /// <summary>
    /// The fact is not in the output: the target has no way to record the category
    /// (descriptor, decision 009), or a <c>Loss</c> record names the fact.
    /// </summary>
    Dropped = 2,

    /// <summary>
    /// The entity's artifacts were not generated at all - the completeness gate refused it
    /// with a <c>Failure</c> record (decision 010) - so the question has no answer.
    /// </summary>
    NotGenerated = 3,
}
