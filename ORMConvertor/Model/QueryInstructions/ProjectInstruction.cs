namespace Model.QueryInstructions;

/// <summary>
/// One projected column, optionally under an aggregate function. <paramref name="Distinct"/>
/// is the modifier of the function (decision 102): <c>COUNT(DISTINCT x)</c> aggregates over
/// the distinct values of its argument. It is a fact about the function and sits beside its
/// name, which is why it is not the scope marker of decision 073 - <c>SELECT DISTINCT
/// COUNT(DISTINCT x)</c> carries both. Meaningful only with a function; the builder
/// template refuses it over <c>*</c>, which no SQL target spells.
/// </summary>
public sealed record ProjectInstruction(
    string Table,
    string Attribute,
    string? Alias = null,
    string? Function = null,
    bool Distinct = false
) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
