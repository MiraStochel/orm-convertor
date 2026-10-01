namespace AbstractWrappers.Descriptors;

/// <summary>
/// Categories of query capability a target framework can express or fail to express
/// (decision 022, paper rule Q14). Separate from <see cref="MappingFactCategory"/> on
/// purpose: a mapping fact describes a table, a column or a key, whereas a query loss
/// concerns an instruction.
///
/// The vocabulary follows the categories requirement T2 uses to divide the translation
/// matrix, so that a report can be read against the same axis the evaluation is written on.
///
/// What a descriptor marks inexpressible is not dropped: since decision 113 the target
/// writes such a query whole in the native SQL of its dialect, with a record of kind
/// Fallback naming the feature, and only a target without an API for native SQL refuses it.
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
    /// target. It is a limit of the model - a collection parameter among the values of an
    /// IN list, which takes single values (decision 102) - or a parameter the generated
    /// method could not be given: one whose scalar does not follow from what
    /// it is compared against, one that would need two scalars at once, a name that is no
    /// plain identifier, named and positional forms mixed in one query, a collection
    /// parameter outside the right side of IN, and MyBatis's <c>${}</c>, which substitutes
    /// text rather than binding a value (decision 082).
    /// </summary>
    QueryParameter = 12,

    /// <summary>
    /// A value the query computes - arithmetic, concatenation, a scalar function, CASE
    /// (decision 107). The model carries one as the sixth shape of an operand, wherever an
    /// operand stands, and every descriptor marks the category expressible, because every
    /// target writes every shape; what a target does not speak is a <em>function</em>, and
    /// that is stated at a finer grain, in <see cref="TargetFrameworkDescriptor.Functions"/>.
    ///
    /// What is recorded under this category is therefore a limit of the model or of the
    /// vocabulary, not an inability of the target: a function outside the vocabulary
    /// (<c>REPLACE</c>, <c>CONVERT</c> with a style, a conversion with a length), a function
    /// the target's descriptor leaves out - which the target writes in native SQL since
    /// decision 113, with a Fallback record -, an expression whose scalar the gate cannot
    /// derive where the spelling depends on it, an expression projected without an alias, and
    /// an aggregate over an aggregate. A grouping by an expression was the one position the
    /// expression did not take until decision 113, which gave it a category of its own,
    /// <see cref="ComputedGrouping"/>.
    /// </summary>
    Expression = 13,

    /// <summary>
    /// A query as a source of rows - a common table expression, a derived table, a LINQ
    /// chain composed over a grouped projection or a slice (decision 112). The model carries
    /// one as a named intermediate result of the whole query, which a row source refers to
    /// by name. A target whose query language cannot express it writes the query in native
    /// SQL with a Fallback record (decision 113) - a definition left out would leave the row
    /// source naming a table that does not exist, so leaving it out was never an answer.
    /// Besides that, what is recorded here is a limit of the model - a definition that reads
    /// the query around it (a lateral reference), projects the whole entity or a column
    /// without a name, or shares its name with another definition or with a table the query
    /// reads - or a recursive definition that breaks a rule of the dialect (decision 113).
    /// </summary>
    IntermediateResult = 14,

    /// <summary>
    /// A recursive definition (decision 113): an intermediate result whose body is a
    /// UNION ALL of an anchor member and a recursive member that names the definition
    /// itself, with the limit of recursion the query may carry. Separate from
    /// <see cref="IntermediateResult"/> because the targets differ inside it: EF Core writes
    /// a definition as a variable of its method and has no recursion, HQL 7.4 has both. A
    /// target whose query language has no recursion writes the query in native SQL with a
    /// Fallback record; the rules a recursive definition is held to are the dialect's and
    /// refuse under <see cref="IntermediateResult"/>, whatever the target.
    /// </summary>
    Recursion = 15,

    /// <summary>
    /// A grouping by an expression - <c>GROUP BY YEAR(o.PlacedAt)</c> - rather than by a column
    /// (decision 113). Separate from <see cref="Grouping"/> because the targets differ inside
    /// it: standard JPQL groups by a path only, and LINQ names the members of a key of several
    /// parts, which an expression no projection names has no name for (decision 028). What the
    /// rule of grouping refuses - a projection, a HAVING or an ordering that names a column
    /// outside every key and every aggregate - is recorded under <see cref="Grouping"/>.
    /// </summary>
    ComputedGrouping = 16,

    /// <summary>
    /// A ranking function over a window - <c>ROW_NUMBER() OVER (PARTITION BY … ORDER BY …)</c>,
    /// <c>RANK</c>, <c>DENSE_RANK</c> (decision 113). T-SQL and HQL 7.4 have it, the other
    /// query languages do not and write the query in native SQL. Recorded here besides: a
    /// window anywhere but in a projection, which SQL does not allow.
    /// </summary>
    WindowFunction = 17,

    /// <summary>
    /// An aggregate that joins the values of a group into one text - <c>STRING_AGG</c>,
    /// <c>listagg</c>, LINQ's <c>string.Join</c> over a group (decision 113).
    /// </summary>
    ListAggregation = 18,
}
