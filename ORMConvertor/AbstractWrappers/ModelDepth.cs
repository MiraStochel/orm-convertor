using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// The depth of the model of one query (decision 191): on the deepest path through it, how many
/// computed values - an operation, a call, a CASE, a window, a list aggregate -, negations and
/// set operations stand inside one another. These are the chains the cap of decision 092 does
/// not see, because the text writes them without a bracket - <c>a + b + c</c>, a CASE in the
/// ELSE of a CASE, <c>NOT NOT</c>, <c>UNION</c> after <c>UNION</c> -, while the model holds each
/// as deep as it is long, and every walk after the reading descends that deep. A comparison and
/// AND and OR add nothing: the first has no chain, the others are flat lists. A subquery adds
/// nothing either - its brackets are what decision 092 counts -, but the path goes on through
/// it, so a chain inside a subquery adds to the chain around it.
///
/// Measured with a stack of its own rather than by recursion, because the input it is there to
/// refuse is exactly the one a recursion would not survive.
/// </summary>
public static class ModelDepth
{
    /// <summary>The depth of the deepest path through the instructions and the definitions they read.</summary>
    public static int Of(IEnumerable<QueryInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);

        var deepest = 0;
        var pending = new Stack<(object Node, int Depth)>();
        foreach (var instruction in instructions)
        {
            pending.Push((instruction, 0));
        }

        while (pending.Count > 0)
        {
            var (node, depth) = pending.Pop();
            deepest = Math.Max(deepest, depth);

            switch (node)
            {
                case ProjectInstruction projection:
                    pending.Push((projection.Operand, depth));
                    break;
                case OrderByInstruction order:
                    pending.Push((order.Operand, depth));
                    break;
                case GroupByInstruction grouping:
                    pending.Push((grouping.Key, depth));
                    break;
                case SelectInstruction filter:
                    pending.Push((filter.Condition, depth));
                    break;
                case HavingInstruction postFilter:
                    pending.Push((postFilter.Condition, depth));
                    break;
                case JoinInstruction join:
                    pending.Push((join.OnCondition, depth));
                    break;
                case SetOperationInstruction set:
                    pending.Push((set.Left, depth + 1));
                    pending.Push((set.Right, depth + 1));
                    break;
                case SubQueryInstruction subQuery:
                    foreach (var inner in subQuery.Instructions)
                    {
                        pending.Push((inner, depth));
                    }

                    break;
                case WithInstruction definition:
                    pending.Push((definition.Body, depth));
                    break;
                case NotCondition negation:
                    pending.Push((negation.Operand, depth + 1));
                    break;
                case LogicalCondition logical:
                    foreach (var operand in logical.Operands)
                    {
                        pending.Push((operand, depth));
                    }

                    break;
                case ComparisonCondition comparison:
                    pending.Push((comparison.Left, depth));
                    if (comparison.Right is { } right)
                    {
                        pending.Push((right, depth));
                    }

                    break;
                case QueryOperand operand:
                    if (operand.Expression is { } expression)
                    {
                        pending.Push((expression, depth + 1));
                    }

                    if (operand.SubQuery is { } nested)
                    {
                        pending.Push((nested, depth));
                    }

                    foreach (var value in operand.Values ?? [])
                    {
                        pending.Push((value, depth));
                    }

                    break;
                case QueryExpression computed:
                    foreach (var leaf in computed.Leaves())
                    {
                        pending.Push((leaf, depth));
                    }

                    foreach (var branch in computed.Branches ?? [])
                    {
                        pending.Push((branch.When, depth));
                    }

                    break;
            }
        }

        return deepest;
    }

    /// <summary>
    /// The sentence a query deeper than the cap is refused with. The model keeps no position,
    /// so unlike the refusal of decision 092 it names the depth rather than a line and a column.
    /// </summary>
    public static string Reason(int depth, ParseLimits limits)
        => $"The query nests {depth} operations, CASEs, negations or set operations inside one another, "
            + $"deeper than the {limits.MaxNestingDepth} levels this instance reads; nothing was translated.";
}
