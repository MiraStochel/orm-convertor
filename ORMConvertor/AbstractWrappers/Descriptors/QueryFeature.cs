namespace AbstractWrappers.Descriptors;

/// <summary>
/// Categories of query capability a target framework can express or fail to express
/// (decision 022, paper rule Q14). Separate from <see cref="MappingFactCategory"/> on
/// purpose: a mapping fact describes a table, a column or a key, whereas a query loss
/// concerns an instruction.
///
/// The vocabulary follows the categories requirement T2 uses to divide the translation
/// matrix, so that a report can be read against the same axis the evaluation is written on.
/// </summary>
public enum QueryFeature
{
    Projection = 1,
    Filtering = 2,
    Join = 3,

    /// <summary>
    /// The kind of join, separate from <see cref="Join"/> because frameworks differ inside
    /// it: HQL has no full outer join at all, while EF Core 10 composes one from LeftJoin
    /// and RightJoin (decision 065), and a descriptor that knew only "join" could not say
    /// that.
    /// </summary>
    JoinKind = 4,

    Aggregation = 5,
    Grouping = 6,
    PostAggregationFiltering = 7,
    Ordering = 8,
    Pagination = 9,
    Subquery = 10,
    SetOperation = 11,

    /// <summary>
    /// A value the caller supplies at execution time. The query IR has no notion of one, so
    /// a parameter in the source is a loss until it does (decision 024).
    /// </summary>
    QueryParameter = 12,
}
