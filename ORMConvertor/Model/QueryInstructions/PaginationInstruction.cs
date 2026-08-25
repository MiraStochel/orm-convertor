namespace Model.QueryInstructions;

/// <summary>
/// Pagination of one (sub)query in offset-then-limit normal form (decision 060). Both
/// values are non-negative row counts and either may be absent; at most one instruction
/// is carried per (sub)query scope.
///
/// Not rendered through the visitor: the instruction holds two numbers rather than a tree,
/// and where they land is a property of the target — inside the SELECT clause as TOP, after
/// the ordering as OFFSET/FETCH, at the end of a LINQ chain, or outside the query text
/// altogether on NHibernate's IQuery. Normalize sorts the numbers into the clauses and the
/// pagination step of each builder reads them from there.
/// </summary>
public sealed record PaginationInstruction(long? Offset, long? Limit) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => string.Empty;
}
