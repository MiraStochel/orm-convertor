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
///
/// <paramref name="Quantifier"/> makes the comparison a quantified one (decision 119):
/// <c>Left Operator ALL (subquery)</c> or <c>Left Operator ANY (subquery)</c>, where the
/// right operand is the subquery and the operator one of the six relational ones. It sits on
/// the comparison for the same reason the escape does - it says how the two sides are
/// compared, not what either side is. Null for an ordinary comparison; the builder template
/// refuses a quantifier on any other shape. Instances with a quantifier come from
/// <see cref="Quantified"/>, which folds the two spellings SQL gives <c>IN</c> and
/// <c>NOT IN</c> back into those.
/// </summary>
public sealed record ComparisonCondition(
    QueryOperand Left,
    ComparisonOperator Operator,
    QueryOperand? Right = null,
    string? Escape = null,
    Quantifier? Quantifier = null
) : ConditionNode
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);

    /// <summary>
    /// A comparison against the rows of a subquery under a quantifier (decision 119), in the
    /// representation's one shape per meaning: <c>= ANY</c> is <c>IN</c> and <c>&lt;&gt; ALL</c>
    /// is <c>NOT IN</c>, exactly, in three-valued logic as in two-valued, so those two come
    /// back as the <see cref="ComparisonOperator.In"/> tree the targets already write - the way
    /// rule Q14 reads <c>BETWEEN</c> as a pair of comparisons rather than carrying a second
    /// spelling of one fact. The other ten combinations keep the quantifier.
    /// </summary>
    /// <param name="left">The value compared, read in the enclosing scope.</param>
    /// <param name="op">A relational operator; any other is the caller's error.</param>
    /// <param name="quantifier">ALL or ANY; a source's SOME is ANY.</param>
    /// <param name="subQuery">The subquery operand (<see cref="QueryOperand.Nested"/>).</param>
    public static ConditionNode Quantified(QueryOperand left, ComparisonOperator op, Quantifier quantifier, QueryOperand subQuery)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(subQuery);
        if (!op.IsRelational())
        {
            throw new ArgumentOutOfRangeException(nameof(op), op, "A quantifier stands on a relational comparison only.");
        }

        if (!subQuery.IsSubQuery)
        {
            throw new ArgumentException("A quantifier compares against a subquery.", nameof(subQuery));
        }

        if (IsIn(op, quantifier))
        {
            return new ComparisonCondition(left, ComparisonOperator.In, subQuery);
        }

        if (IsNotIn(op, quantifier))
        {
            return new NotCondition(new ComparisonCondition(left, ComparisonOperator.In, subQuery));
        }

        return new ComparisonCondition(left, op, subQuery, Quantifier: quantifier);
    }

    /// <summary>Whether the pair spells <c>IN</c>: <c>= ANY</c>.</summary>
    public static bool IsIn(ComparisonOperator op, Quantifier quantifier)
        => op == ComparisonOperator.Equal && quantifier == Conditions.Quantifier.Any;

    /// <summary>Whether the pair spells <c>NOT IN</c>: <c>&lt;&gt; ALL</c>.</summary>
    public static bool IsNotIn(ComparisonOperator op, Quantifier quantifier)
        => op == ComparisonOperator.NotEqual && quantifier == Conditions.Quantifier.All;

    /// <summary>
    /// The quantified comparison that holds exactly when this one does not, by De Morgan over
    /// the rows of the subquery - <c>NOT (x &gt; ALL S)</c> is <c>x &lt;= ANY S</c>,
    /// <c>NOT (x &gt; ANY S)</c> is <c>x &lt;= ALL S</c> - and exactly in three-valued logic:
    /// both sides are unknown over the same rows. A writer whose language has no quantifier
    /// and whose negation does not keep the unknown value uses it instead of negating the
    /// rewritten form (decision 119). Defined for a quantified comparison only.
    /// </summary>
    public ComparisonCondition NegatedQuantified()
    {
        if (Quantifier is not { } quantifier)
        {
            throw new InvalidOperationException("Only a quantified comparison has a quantified negation.");
        }

        var flipped = quantifier == Conditions.Quantifier.All ? Conditions.Quantifier.Any : Conditions.Quantifier.All;
        return this with { Operator = Operator.Negated(), Quantifier = flipped };
    }
}
