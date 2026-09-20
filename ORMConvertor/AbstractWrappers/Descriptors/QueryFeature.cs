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
    /// A value the caller supplies at execution time. The model carries one as the fifth
    /// shape of a condition operand (decision 083) and as either count of a pagination
    /// (decision 085), and every descriptor marks the category expressible, so the
    /// mechanical check of rule Q14 never fires on it.
    ///
    /// What is still recorded under this category is therefore never an inability of the
    /// target. It is a limit of the model - a parameter among the values of an IN list,
    /// which carries only values the query itself states (decision 074) - or a parameter
    /// the generated method could not be given: one whose scalar does not follow from what
    /// it is compared against, one that would need two scalars at once, a name that is no
    /// plain identifier, named and positional forms mixed in one query, a collection
    /// parameter outside the right side of IN, and MyBatis's <c>${}</c>, which substitutes
    /// text rather than binding a value (decision 082).
    /// </summary>
    QueryParameter = 12,
}
