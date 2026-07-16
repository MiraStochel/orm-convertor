namespace Model.QueryInstructions.Conditions;

public abstract record ConditionNode
{
    public abstract string Accept(IQueryVisitor visitor);
}