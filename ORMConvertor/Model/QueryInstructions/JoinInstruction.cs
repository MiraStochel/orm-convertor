using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace Model.QueryInstructions;

/// <summary>
/// A join of the right table onto the rows so far (rule Q6). <paramref name="OnCondition"/>
/// is null for a <see cref="JoinKind.Cross"/> join alone, which has no condition by
/// definition (decision 196); every other kind carries one.
/// </summary>
public sealed record JoinInstruction(
    JoinKind Kind,
    string LeftTable,
    string RightTable,
    string? RightTableAlias,
    ConditionNode? OnCondition
) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
