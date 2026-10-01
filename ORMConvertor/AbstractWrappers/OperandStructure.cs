using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// Questions about the structure of an operand that the builder template and the visitors
/// ask alike (decision 113): whether two operands are the same value written twice - what
/// the rule of grouping matches a projection against a key with, and what the LINQ target
/// writes <c>g.Key</c> for -, and whether an operand aggregates, ranks or joins a list. Kept
/// here so that no language project answers them in its own way (S1).
/// </summary>
public static class OperandStructure
{
    /// <summary>
    /// Whether two operands are the same value, written the same way: the same shape over the
    /// same parts, compared down to the leaves. A column without a qualifier matches the same
    /// column under any qualifier - T-SQL reads <c>YEAR(PlacedAt)</c> beside
    /// <c>GROUP BY YEAR(o.PlacedAt)</c> as the same value, and an ambiguous name is the
    /// database's to refuse. Names compare without regard to case, as SQL compares them; a
    /// subquery is the same only as itself.
    /// </summary>
    public static bool Same(QueryOperand? left, QueryOperand? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (!string.Equals(left.Function, right.Function, StringComparison.OrdinalIgnoreCase) || left.Distinct != right.Distinct)
        {
            return false;
        }

        if (left.IsColumn && right.IsColumn)
        {
            return string.Equals(left.Property, right.Property, StringComparison.OrdinalIgnoreCase)
                   && (left.Table is null || right.Table is null || string.Equals(left.Table, right.Table, StringComparison.OrdinalIgnoreCase));
        }

        if (left.IsConstant && right.IsConstant)
        {
            return left.Constant!.Type == right.Constant!.Type && string.Equals(left.Constant.Text, right.Constant.Text, StringComparison.Ordinal);
        }

        if (left.IsParameter && right.IsParameter)
        {
            return string.Equals(left.Parameter!.Name, right.Parameter!.Name, StringComparison.Ordinal)
                   && left.Parameter.Position == right.Parameter.Position;
        }

        if (left.IsExpression && right.IsExpression)
        {
            return Same(left.Expression!, right.Expression!);
        }

        return false;
    }

    private static bool Same(QueryExpression left, QueryExpression right)
    {
        if (left.Operator != right.Operator
            || left.Function != right.Function
            || left.Unit != right.Unit
            || left.CastTo != right.CastTo
            || left.Ranking != right.Ranking
            || left.IsCase != right.IsCase
            || !string.Equals(left.Separator, right.Separator, StringComparison.Ordinal))
        {
            return false;
        }

        var leftLeaves = left.Leaves().ToList();
        var rightLeaves = right.Leaves().ToList();
        if (leftLeaves.Count != rightLeaves.Count || !leftLeaves.Zip(rightLeaves).All(pair => Same(pair.First, pair.Second)))
        {
            return false;
        }

        if ((left.Else is null) != (right.Else is null) || (left.Partitions?.Count ?? 0) != (right.Partitions?.Count ?? 0))
        {
            return false;
        }

        if (left.Ordering is { } leftOrdering && !leftOrdering.Select(k => k.Ascending).SequenceEqual(right.Ordering!.Select(k => k.Ascending)))
        {
            return false;
        }

        return left.Branches is null || left.Branches.Zip(right.Branches!).All(pair => Same(pair.First.When, pair.Second.When));
    }

    /// <summary>Whether two conditions are the same, compared the way <see cref="Same(QueryOperand?, QueryOperand?)"/> compares operands.</summary>
    public static bool Same(ConditionNode left, ConditionNode right) => (left, right) switch
    {
        (ComparisonCondition a, ComparisonCondition b) => a.Operator == b.Operator
                                                          && string.Equals(a.Escape, b.Escape, StringComparison.Ordinal)
                                                          && Same(a.Left, b.Left)
                                                          && Same(a.Right, b.Right),
        (LogicalCondition a, LogicalCondition b) => a.Operator == b.Operator
                                                    && a.Operands.Count == b.Operands.Count
                                                    && a.Operands.Zip(b.Operands).All(pair => Same(pair.First, pair.Second)),
        (NotCondition a, NotCondition b) => Same(a.Operand, b.Operand),
        _ => false,
    };

    /// <summary>
    /// Whether the operand is an aggregate: one of the five functions over it, or since
    /// decision 113 a list aggregate - an aggregate as much as COUNT is, though it travels as
    /// an expression.
    /// </summary>
    public static bool Aggregates(QueryOperand operand)
        => operand.IsAggregate || (operand.IsExpression && operand.Expression!.IsListAggregate);

    /// <summary>Whether the operand aggregates anywhere short of a subquery - itself, or a leaf of its expression at any depth.</summary>
    public static bool ContainsAggregate(QueryOperand operand)
        => Aggregates(operand) || (operand.IsExpression && Inside(operand.Expression!).Any(ContainsAggregate));

    /// <summary>Whether a ranking function over a window stands anywhere in the operand short of a subquery (decision 113).</summary>
    public static bool ContainsWindow(QueryOperand operand)
        => operand.IsExpression && (operand.Expression!.IsWindow || Inside(operand.Expression).Any(ContainsWindow));

    /// <summary>Whether a list aggregate stands anywhere in the operand short of a subquery (decision 113).</summary>
    public static bool ContainsListAggregate(QueryOperand operand)
        => operand.IsExpression && (operand.Expression!.IsListAggregate || Inside(operand.Expression).Any(ContainsListAggregate));

    /// <summary>
    /// The operands one level inside an expression: its leaves, and the operands of the
    /// conditions of a CASE, which stand inside it as much as its values do.
    /// </summary>
    public static IEnumerable<QueryOperand> Inside(QueryExpression expression)
    {
        foreach (var leaf in expression.Leaves())
        {
            yield return leaf;
        }

        foreach (var branch in expression.Branches ?? [])
        {
            foreach (var operand in OperandsOf(branch.When))
            {
                yield return operand;
            }
        }
    }

    private static IEnumerable<QueryOperand> OperandsOf(ConditionNode node) => node switch
    {
        ComparisonCondition comparison => comparison.Right is null ? [comparison.Left] : [comparison.Left, comparison.Right],
        LogicalCondition logical => logical.Operands.SelectMany(OperandsOf),
        NotCondition negation => OperandsOf(negation.Operand),
        _ => [],
    };
}
