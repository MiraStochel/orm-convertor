using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace Model.QueryInstructions;

public sealed record JoinInstruction(
    JoinKind Kind,
    string LeftTable,
    string RightTable,
    string? RightTableAlias,
    ConditionNode OnCondition
) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}