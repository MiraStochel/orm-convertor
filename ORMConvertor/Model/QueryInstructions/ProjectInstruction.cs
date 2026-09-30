using Model.QueryInstructions.Conditions;

namespace Model.QueryInstructions;

/// <summary>
/// One projected value with an optional alias (decision 107). The operand is the same
/// <see cref="QueryOperand"/> a comparison has on either side, so the projection stands
/// over whatever an operand can be: a column, optionally under an aggregate function with
/// its DISTINCT modifier (decision 102: the modifier sits beside the function, which now
/// sits on the operand), the whole entity as a column whose property is <c>*</c>, a
/// constant, or an expression. The loose quintuple this used to be was the very shape
/// decision 024 took out of the comparison, and an expression had no place in it.
///
/// An expression without an alias has no name any by-name target could read the column
/// under, and a name invented by the tool is forbidden (decision 028), so the builder
/// template refuses it; a column needs none, being named already.
/// </summary>
public sealed record ProjectInstruction(
    QueryOperand Operand,
    string? Alias = null
) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
