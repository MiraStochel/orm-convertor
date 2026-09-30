using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// What the four visitors spell alike when they write an expression (decision 107), kept
/// here so that no language project has to reference another for it (S1): the grouping of
/// a binary expression, which every one of the four languages resolves with the same
/// precedence of <c>* / %</c> over <c>+ -</c>, and the chain of replacements that makes a
/// pattern value literal, which differs between the languages only in the name of the
/// replace function.
/// </summary>
public static class ExpressionSpelling
{
    /// <summary>
    /// Whether one side of a binary expression has to be parenthesized to keep its grouping:
    /// a nested operation of lower precedence, or one of the same precedence on the right,
    /// where <c>a - (b - c)</c> is not <c>a - b - c</c>. An aggregate over an expression is
    /// a call and binds tighter than any operator.
    /// </summary>
    public static bool NeedsParentheses(QueryOperand side, QueryExpression parent, bool rightSide)
    {
        if (!side.IsExpression || side.IsAggregate || !side.Expression!.IsBinary)
        {
            return false;
        }

        var inner = Precedence(side.Expression.Operator!.Value);
        var outer = Precedence(parent.Operator!.Value);
        return inner < outer || (inner == outer && rightSide);
    }

    private static int Precedence(ExpressionOperator op) => op switch
    {
        ExpressionOperator.Multiply or ExpressionOperator.Divide or ExpressionOperator.Modulo => 2,
        _ => 1,
    };

    /// <summary>
    /// The value with every wildcard of SQL Server made literal (decision 107): the escape
    /// character first, so that one already in the value stays literal, then <c>%</c>,
    /// <c>_</c> and <c>[</c>, each put behind the escape. Written as the replace function of
    /// the language the caller names, over the escape the comparison declares.
    /// </summary>
    public static string EscapePattern(string value, string escape, string replace)
    {
        var quoted = escape.Replace("'", "''");
        var text = $"{replace}({value}, '{quoted}', '{quoted}{quoted}')";
        foreach (var wildcard in new[] { "%", "_", "[" })
        {
            text = $"{replace}({text}, '{wildcard}', '{quoted}{wildcard}')";
        }

        return text;
    }
}
