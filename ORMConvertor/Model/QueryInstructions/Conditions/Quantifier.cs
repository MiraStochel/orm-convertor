namespace Model.QueryInstructions.Conditions;

/// <summary>
/// The quantifier of a comparison against the rows of a subquery (decision 119):
/// <c>x &gt; ALL (SELECT …)</c> holds when the comparison holds against every value the
/// subquery returns - and over no value at all -, <c>x &gt; ANY (SELECT …)</c> when it holds
/// against at least one. SQL's <c>SOME</c> is a second spelling of <c>ANY</c> and is read as
/// it; the representation carries one name per meaning, the way it carries <c>IN</c> once for
/// <c>= ANY</c> and <c>NOT IN</c> once for <c>&lt;&gt; ALL</c>
/// (<see cref="ComparisonCondition.Quantified"/>).
/// </summary>
public enum Quantifier
{
    All = 1,
    Any = 2,
}
