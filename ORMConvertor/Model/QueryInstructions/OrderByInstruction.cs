using Model.QueryInstructions.Conditions;

namespace Model.QueryInstructions;

/// <summary>
/// One ordering key with its direction (decision 107). The key is a <see cref="QueryOperand"/>:
/// a column, a column under an aggregate function (<c>ORDER BY COUNT(*) DESC</c>, the
/// commonest ordering of a grouped query), an expression, or the alias of a projection -
/// which is a column operand without a table whose property is the alias, the shape
/// decision 073 matches against the projections.
/// </summary>
public sealed record OrderByInstruction(
    QueryOperand Operand,
    bool Asc = true
) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
