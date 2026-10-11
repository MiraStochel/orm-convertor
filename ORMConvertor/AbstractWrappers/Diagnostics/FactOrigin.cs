namespace AbstractWrappers.Diagnostics;

/// <summary>
/// Where a mapping fact the representation holds at generation time came from (decision
/// 124). Three places and no more, because a fact can be born in exactly three phases of
/// the pipeline: the reading of one input unit, the completion from the database catalog,
/// or a phase of the build itself.
/// </summary>
public enum FactOrigin
{
    /// <summary>
    /// The fact first stood in the representation after one input unit was read; that unit
    /// stated it. Under source precedence (decision 017) a fact is written once, so the
    /// unit after whose reading it appeared is the one that stated it.
    /// </summary>
    Source = 1,

    /// <summary>
    /// The catalog completion phase supplied it (decision 015); the <c>Supplied</c> record
    /// about the same entity, property and category is the fuller statement.
    /// </summary>
    Catalog = 2,

    /// <summary>
    /// A phase of the build put it there - a key class dissolved into its owner, a
    /// synthesized junction entity, a convention navigation resolved against the entity set.
    /// Nothing the caller sent and nothing the catalog said; the tool derived it.
    /// </summary>
    Derived = 3,
}
