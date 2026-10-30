using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// The cap on the text of one query (decision 194): one number, <see cref="ParseLimits.MaxQueryTextLength"/>,
/// held at every place where the text of a query multiplies and over the finished artifact.
/// Two spellings of decision 156 write an operand twice - a comparison tested for NULL writes
/// the operand it tests beside the comparison, and the reader of EF Core reads a compensated
/// <c>!=</c> as <c>&lt;&gt; OR IS NULL</c> -, so an operand that nests another such comparison
/// doubles at every level, and the include of MyBatis inlines a fragment as many times as it is
/// named; a kilobyte of input then writes megabytes within a dozen levels and gigabytes within
/// two dozen. Measured 2026-10-30, before the decision.
/// </summary>
public static class QueryTextCap
{
    /// <summary>
    /// How many nodes of the model a target writes: every instruction, condition, operand and
    /// expression, a shared node counted each time it is reached, because every writer writes
    /// it each time. A reader that compensates shares the operand it tests, so the model is a
    /// graph that is small in memory and large as written; the count follows the writing. Every
    /// node writes at least one character in every language, so a model with more nodes than
    /// the cap writes a longer text than the cap in every target, and is refused before any walk
    /// descends into it - the walks after the reading follow the writing too.
    ///
    /// Memoized by reference, so the count takes one step per distinct node however many times
    /// it is shared, and clamped at one past <paramref name="ceiling"/>, so it never overflows.
    /// Measured with a stack of its own rather than by recursion, like <see cref="ModelDepth"/>.
    /// </summary>
    public static long WrittenNodes(IEnumerable<QueryInstruction> instructions, long ceiling)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var limit = Math.Max(ceiling, 0) + 1;
        var sizes = new Dictionary<object, long>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(object Node, bool Summing)>();
        var roots = instructions.ToList();
        foreach (var root in roots)
        {
            pending.Push((root, false));
        }

        while (pending.Count > 0)
        {
            var (node, summing) = pending.Pop();
            if (summing)
            {
                long size = 1;
                foreach (var child in Children(node))
                {
                    size = Math.Min(limit, size + sizes[child]);
                }

                sizes[node] = size;
                continue;
            }

            if (sizes.ContainsKey(node))
            {
                continue;
            }

            // Marked before its children are pushed, so that a node reached twice on the way
            // down is expanded once; its size is filled in when the summing phase pops it.
            sizes[node] = 0;
            pending.Push((node, true));
            foreach (var child in Children(node))
            {
                pending.Push((child, false));
            }
        }

        long total = 0;
        foreach (var root in roots)
        {
            total = Math.Min(limit, total + sizes[root]);
        }

        return total;
    }

    private static IEnumerable<object> Children(object node)
    {
        switch (node)
        {
            case ProjectInstruction projection:
                yield return projection.Operand;
                break;
            case OrderByInstruction order:
                yield return order.Operand;
                break;
            case GroupByInstruction grouping:
                yield return grouping.Key;
                break;
            case SelectInstruction filter:
                yield return filter.Condition;
                break;
            case HavingInstruction postFilter:
                yield return postFilter.Condition;
                break;
            case JoinInstruction { OnCondition: { } joinCondition }:
                yield return joinCondition;
                break;
            case SetOperationInstruction set:
                yield return set.Left;
                yield return set.Right;
                break;
            case SubQueryInstruction subQuery:
                foreach (var inner in subQuery.Instructions)
                {
                    yield return inner;
                }

                break;
            case WithInstruction definition:
                yield return definition.Body;
                break;
            case NotCondition negation:
                yield return negation.Operand;
                break;
            case LogicalCondition logical:
                foreach (var operand in logical.Operands)
                {
                    yield return operand;
                }

                break;
            case ComparisonCondition comparison:
                yield return comparison.Left;
                if (comparison.Right is { } right)
                {
                    yield return right;
                }

                break;
            case QueryOperand operand:
                if (operand.Expression is { } expression)
                {
                    yield return expression;
                }

                if (operand.SubQuery is { } nested)
                {
                    yield return nested;
                }

                foreach (var value in operand.Values ?? [])
                {
                    yield return value;
                }

                break;
            case QueryExpression computed:
                foreach (var leaf in computed.Leaves())
                {
                    yield return leaf;
                }

                foreach (var branch in computed.Branches ?? [])
                {
                    yield return branch.When;
                }

                break;
        }
    }

    /// <summary>The sentence a query whose model writes more nodes than the cap is refused with, right after the reading.</summary>
    public static string ModelReason(long nodes, ParseLimits limits)
        => $"The query would be written with more than {limits.MaxQueryTextLength} characters in any target - its model has "
            + $"{(nodes > limits.MaxQueryTextLength ? "more than " : string.Empty)}{Math.Min(nodes, limits.MaxQueryTextLength)} nodes as written, "
            + "because a comparison that tests a value for NULL writes that value twice and the value nests such a comparison again - "
            + "longer than the text of one query this instance writes; nothing was translated.";

    /// <summary>The sentence a text that crossed the cap while being written or expanded is refused with.</summary>
    public static string TextReason(string what, int length, ParseLimits limits)
        => $"{what} has {length} characters, longer than the {limits.MaxQueryTextLength} characters of one query this instance writes; "
            + "no artifact was generated.";
}
