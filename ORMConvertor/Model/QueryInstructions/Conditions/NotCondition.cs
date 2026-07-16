namespace Model.QueryInstructions.Conditions;

public sealed record NotCondition(ConditionNode Operand) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}