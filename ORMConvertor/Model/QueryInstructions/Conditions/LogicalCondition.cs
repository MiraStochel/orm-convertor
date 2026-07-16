namespace Model.QueryInstructions.Conditions;

public sealed record LogicalCondition(
    LogicalOperator Operator,
    IReadOnlyList<ConditionNode> Operands
) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}