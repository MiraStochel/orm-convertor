namespace Model.QueryInstructions;

/// <summary>
/// A named intermediate result of the whole query (decision 112): the rows its body
/// returns, under a name a row source - the FROM of a scope, the right side of a join -
/// refers to as it refers to a table. T-SQL's WITH is read into one directly, a derived
/// table into one named by its alias, and a LINQ chain composed over a grouped projection
/// or a slice into one named by the variable or lambda parameter that holds its rows.
///
/// A definition belongs to the query, not to the scope the source wrote it in: it sees
/// nothing of the query around it, so a derived table nested anywhere is lifted to the one
/// list the query carries, in the order it was read, which is the order of dependencies.
/// The columns are the body's projections; the builder template describes them as a row
/// that exists only inside the query, so that every mechanism that types or names a column
/// of an entity does the same for a column of a definition.
/// </summary>
public sealed record WithInstruction(string Name, SubQueryInstruction Body) : QueryInstruction
{
    // Rendering a definition means normalizing its body and composing it through the eight
    // steps, which is builder work, as for a subquery operand (decisions 061 and 112).
    public override string Accept(IQueryVisitor visitor) => string.Empty;
}
