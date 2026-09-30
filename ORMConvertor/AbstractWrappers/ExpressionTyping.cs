using System.Runtime.CompilerServices;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// The typed view of the expressions of one query (decision 107): what the builder's gate
/// derived about each <see cref="QueryExpression"/> from the mapping representation, kept
/// beside the tree the parser handed over rather than written into it - the same division
/// decision 083 made for parameters, whose resolved scalars go onto the builder's own list.
///
/// Two facts per expression. Its scalar, where the gate could derive one, which the LINQ
/// visitor needs to spell the ELSE of a CASE without one (<c>(T?)null</c>). And whether an
/// <c>Add</c> the reader could not tell from a concatenation stands over a string, which the
/// HQL and JPQL visitors need because their languages spell the two with different words;
/// T-SQL and C# write <c>+</c> either way and never ask.
///
/// Keyed by reference: two expressions of the same text in two places of one query are two
/// nodes and may be typed differently by their surroundings.
/// </summary>
public sealed class ExpressionTyping
{
    private readonly Dictionary<QueryExpression, ScalarType?> scalars = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<QueryExpression> concatenations = new(ReferenceEqualityComparer.Instance);

    public void Clear()
    {
        scalars.Clear();
        concatenations.Clear();
    }

    /// <summary>Records what the gate derived for one expression.</summary>
    public void Record(QueryExpression expression, ScalarType? scalar, bool concatenates)
    {
        scalars[expression] = scalar;
        if (concatenates)
        {
            concatenations.Add(expression);
        }
        else
        {
            concatenations.Remove(expression);
        }
    }

    /// <summary>The scalar of the expression, or null where none follows from the mapping or the gate has not seen it.</summary>
    public ScalarType? ScalarOf(QueryExpression expression)
        => scalars.GetValueOrDefault(expression);

    /// <summary>
    /// Whether the expression concatenates strings: a <see cref="ExpressionOperator.Concat"/>
    /// by its operator, or an <see cref="ExpressionOperator.Add"/> the gate typed over a
    /// string side (decision 107).
    /// </summary>
    public bool IsConcatenation(QueryExpression expression)
        => expression.Operator == ExpressionOperator.Concat || concatenations.Contains(expression);
}
