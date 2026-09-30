namespace Model.QueryInstructions.Conditions;

/// <summary>
/// The binary operators of a query expression (decision 107). Concatenation is an operator
/// of its own and not <see cref="Add"/> over strings, because HQL, JPQL and C# spell it with
/// a word of their own (<c>concat</c>, <c>string.Concat</c>) and the model carries what the
/// source said. T-SQL and C# overload <c>+</c>, so a reader of either language reads it as
/// <see cref="Concat"/> where a side is a string literal or an expression whose scalar is
/// statically a string, and as <see cref="Add"/> otherwise; where an <see cref="Add"/> turns
/// out to stand over a string column, the builder's gate, which has the mapping, says so,
/// and the visitors whose language tells the two apart write the concatenation.
/// </summary>
public enum ExpressionOperator
{
    Concat = 1,
    Add = 2,
    Subtract = 3,
    Multiply = 4,
    Divide = 5,
    Modulo = 6,
}
