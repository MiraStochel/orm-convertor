namespace Model.QueryInstructions;

public sealed record SubQueryInstruction(List<QueryInstruction> Instructions) : QueryInstruction
{
    // Rendering a nested scope means normalization, the eight steps and the target's own
    // clause order, which is builder work rather than a visitor's (decision 061) - the same
    // split set operations and pagination already have.
    public override string Accept(IQueryVisitor v) => string.Empty;
}
