using Model.QueryInstructions.Conditions;

namespace Model.QueryInstructions;

/// <summary>
/// One projected value with an optional alias (decision 107). The operand is the same
/// <see cref="QueryOperand"/> a comparison has on either side, so the projection stands
/// over whatever an operand can be: a column, optionally under an aggregate function with
/// its DISTINCT modifier (decision 102: the modifier sits beside the function, which now
/// sits on the operand), the whole row of one range variable as a column whose property is
/// <c>*</c> under the variable's alias - <c>SELECT t.*</c>, <c>select r</c> after a join,
/// <c>Select(x =&gt; x.r)</c> -, a constant, or an expression. The loose quintuple this
/// used to be was the very shape decision 024 took out of the comparison, and an expression
/// had no place in it. A query without any projection materializes the whole joined row
/// (rule Q3); a whole-row projection names which of the rows it materializes.
///
/// An expression without an alias has no name any by-name target could read the column
/// under, and a name invented by the tool is forbidden (decision 028), so the builder
/// template refuses it; a column needs none, being named already.
///
/// The alias is carried undecorated, the way a constant is. <paramref name="DelimitedAlias"/>
/// says that the source set it off with delimiters - <c>AS "product.productId"</c>,
/// <c>AS [Order]</c> - because it is no identifier of its language: a SQL target delimits it
/// again in its own spelling, and a target whose language names a column as a member of
/// its own - an anonymous type, a result variable - has no spelling for a name that is no
/// identifier and refuses it.
/// </summary>
public sealed record ProjectInstruction(
    QueryOperand Operand,
    string? Alias = null,
    bool DelimitedAlias = false
) : QueryInstruction
{
    /// <summary>Whether the projection takes the whole row of one range variable: a column <c>*</c> under an alias, under no aggregate.</summary>
    public bool IsWholeRow => Operand is { IsColumn: true, IsAggregate: false, Property: "*", Table: not null };

    public override string Accept(IQueryVisitor visitor) => visitor.Visit(this);
}
