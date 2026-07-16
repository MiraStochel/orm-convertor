using Model.QueryInstructions.Conditions;

namespace Model.QueryInstructions;

public sealed record SelectInstruction(ConditionNode Condition) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}