using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// What the four query readers say and spell alike about a quantified comparison (decision
/// 119), so that one place says it: the record a reader files when the pair of operator and
/// quantifier folds into the <c>IN</c> or <c>NOT IN</c> the representation already carries
/// (<see cref="ComparisonCondition.Quantified"/>), and the spelling of a quantifier in a
/// record.
/// </summary>
public static class QuantifiedComparisons
{
    /// <summary>The quantifier as SQL spells it, for records and for the SQL-like targets.</summary>
    public static string Spelled(Quantifier quantifier) => quantifier == Quantifier.All ? "ALL" : "ANY";

    /// <summary>
    /// The convention record of a pair that folds into <c>IN</c> or <c>NOT IN</c> - the same
    /// rows, a second spelling the representation does not keep, the way rule Q14 rewrites
    /// <c>BETWEEN</c> - or null where the quantifier stays.
    /// </summary>
    public static string? RewriteNote(ComparisonOperator op, Quantifier quantifier)
    {
        if (ComparisonCondition.IsIn(op, quantifier))
        {
            return "The quantified comparison = ANY (subquery) was rewritten as IN (subquery), which selects the same rows.";
        }

        if (ComparisonCondition.IsNotIn(op, quantifier))
        {
            return "The quantified comparison <> ALL (subquery) was rewritten as NOT IN (subquery), which selects the same rows.";
        }

        return null;
    }
}
