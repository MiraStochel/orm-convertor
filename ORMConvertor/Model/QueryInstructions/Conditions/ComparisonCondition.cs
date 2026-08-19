namespace Model.QueryInstructions.Conditions;

/// <summary>
/// A comparison of two operands (decision 024). The nine loose strings this used to carry
/// became two <see cref="QueryOperand"/>s, so both sides are the same shape and a constant
/// arrives typed rather than as whatever text the source happened to write.
///
/// For <see cref="ComparisonOperator.IsNull"/> and <see cref="ComparisonOperator.IsNotNull"/>
/// the right operand is unused and null (decision 002) — a null test is an operator, not a
/// comparison against a constant.
/// </summary>
public sealed record ComparisonCondition(
    QueryOperand Left,
    ComparisonOperator Operator,
    QueryOperand? Right = null
) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
