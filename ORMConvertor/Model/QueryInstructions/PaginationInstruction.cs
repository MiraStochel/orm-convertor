namespace Model.QueryInstructions;

/// <summary>
/// Pagination of one (sub)query in offset-then-limit normal form (decision 060). Either
/// count may be absent, and at most one instruction is carried per (sub)query scope.
///
/// Each count is a <see cref="RowCount"/>: a number the query states, or a parameter the
/// caller binds (decision 085). Pagination written with a parameter is the shape paging
/// code actually has - a literal offset asks for one fixed page - so the slot carries both
/// and neither the instruction nor the visitor changes for it.
///
/// Not rendered through the visitor: the instruction holds two counts rather than a tree,
/// and where they land is a property of the target - inside the SELECT clause as TOP, after
/// the ordering as OFFSET/FETCH, at the end of a LINQ chain, or outside the query text
/// altogether on NHibernate's IQuery and JPA's Query. Normalize sorts the counts into the
/// clauses and the pagination step of each builder reads them from there.
/// </summary>
public sealed record PaginationInstruction(RowCount? Offset, RowCount? Limit) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => string.Empty;
}
