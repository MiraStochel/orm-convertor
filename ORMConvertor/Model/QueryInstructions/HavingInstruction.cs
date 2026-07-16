using Model.QueryInstructions.Conditions;

namespace Model.QueryInstructions;

public sealed record HavingInstruction(ConditionNode Condition) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}