namespace Model.QueryInstructions.Conditions;

/// <summary>
/// A comparison of two operands (decision 024). The nine loose strings this used to carry
/// became two <see cref="QueryOperand"/>s, so both sides are the same shape and a constant
/// arrives typed rather than as whatever text the source happened to write.
///
/// For <see cref="ComparisonOperator.IsNull"/> and <see cref="ComparisonOperator.IsNotNull"/>
/// the right operand is unused and null (decision 002) — a null test is an operator, not a
/// comparison against a constant.
///
/// <paramref name="Escape"/> is the escape character of a <see cref="ComparisonOperator.Like"/>
/// (decision 102), carried undecorated - <c>!</c>, never <c>'!'</c> - the way a constant
/// carries its value. It sits on the comparison and not on the pattern operand because the
/// pattern may be a parameter and the escape applies all the same. Null for every other
/// comparison; the builder template refuses it under any other operator.
/// </summary>
public sealed record ComparisonCondition(
    QueryOperand Left,
    ComparisonOperator Operator,
    QueryOperand? Right = null,
    string? Escape = null
) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
