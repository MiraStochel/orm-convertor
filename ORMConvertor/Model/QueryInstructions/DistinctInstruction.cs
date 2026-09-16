namespace Model.QueryInstructions;

/// <summary>
/// The (sub)query collapses duplicate rows of its final projection (decision 073). A marker
/// with no values, carried per (sub)query scope the way <see cref="PaginationInstruction"/>
/// is: DISTINCT is a property of the whole projection, not of one column, and a whole-entity
/// projection has no projection instruction to hang it on (rule Q3). Idempotent, so a scope
/// may carry it more than once without a rule against it.
///
/// Not rendered through the visitor: Normalize sorts it into the clauses and the projection
/// step of each builder writes the one word its target spells - SELECT DISTINCT, .Distinct(),
/// select distinct. Its place in the relational order is after the projection and before the
/// pagination, which is what decides which LINQ steps may follow it.
/// </summary>
public sealed record DistinctInstruction : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => string.Empty;
}
