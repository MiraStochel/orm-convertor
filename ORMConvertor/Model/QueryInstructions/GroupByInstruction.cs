using Model.QueryInstructions.Conditions;

namespace Model.QueryInstructions;

/// <summary>
/// One grouping key (decision 113). The key is a <see cref="QueryOperand"/> - a column, or an
/// expression such as <c>YEAR(o.PlacedAt)</c> -, the way decision 107 stood the projection and
/// the ordering key over an operand; the position that decision left out is closed. What a
/// grouped query may name beside its keys - a column only inside a subtree equal to a key, or
/// under an aggregate - is the rule of SQL itself, and the builder template holds it.
/// </summary>
public sealed record GroupByInstruction(QueryOperand Key) : QueryInstruction
{
    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
