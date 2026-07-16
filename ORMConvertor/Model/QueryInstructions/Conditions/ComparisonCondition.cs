namespace Model.QueryInstructions.Conditions;

public sealed record ComparisonCondition(
    string? LeftTable,
    string? LeftProperty,
    string? LeftConstant,
    string? LeftFunction,
    ComparisonOperator Operator,
    string? RightTable,
    string? RightProperty,
    string? RightConstant,
    string? RightFunction
) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}