namespace Model.QueryInstructions.Conditions;

/// <summary>
/// The two symmetries of the six relational operators, which the readers and writers of a
/// quantified comparison lean on (decision 119). Defined once, here, so that no reader
/// spells its own table of them.
/// </summary>
public static class ComparisonOperatorExtensions
{
    /// <summary>
    /// Whether the operator is one of the six relational comparisons - the only operators a
    /// quantifier stands on; LIKE, IN, the null tests and EXISTS are predicates of their own.
    /// </summary>
    public static bool IsRelational(this ComparisonOperator op) => op is
        ComparisonOperator.Equal or ComparisonOperator.NotEqual
        or ComparisonOperator.GreaterThan or ComparisonOperator.GreaterThanOrEqual
        or ComparisonOperator.LessThan or ComparisonOperator.LessThanOrEqual;

    /// <summary>
    /// The operator that holds exactly when this one does not, in SQL's three-valued logic
    /// as much as in C#: <c>&gt;</c> and <c>&lt;=</c>, <c>=</c> and <c>&lt;&gt;</c>. Defined
    /// for the relational operators only.
    /// </summary>
    public static ComparisonOperator Negated(this ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => ComparisonOperator.NotEqual,
        ComparisonOperator.NotEqual => ComparisonOperator.Equal,
        ComparisonOperator.GreaterThan => ComparisonOperator.LessThanOrEqual,
        ComparisonOperator.GreaterThanOrEqual => ComparisonOperator.LessThan,
        ComparisonOperator.LessThan => ComparisonOperator.GreaterThanOrEqual,
        ComparisonOperator.LessThanOrEqual => ComparisonOperator.GreaterThan,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Only a relational operator has a negation."),
    };

    /// <summary>
    /// The operator of the same comparison read from the other side - <c>a &gt; b</c> is
    /// <c>b &lt; a</c> -, which is how a reader turns a comparison whose subquery side was
    /// written on the left into the representation's one shape, subquery on the right.
    /// Defined for the relational operators only.
    /// </summary>
    public static ComparisonOperator Mirrored(this ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => ComparisonOperator.Equal,
        ComparisonOperator.NotEqual => ComparisonOperator.NotEqual,
        ComparisonOperator.GreaterThan => ComparisonOperator.LessThan,
        ComparisonOperator.GreaterThanOrEqual => ComparisonOperator.LessThanOrEqual,
        ComparisonOperator.LessThan => ComparisonOperator.GreaterThan,
        ComparisonOperator.LessThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Only a relational operator has a mirror image."),
    };
}
