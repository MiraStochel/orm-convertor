using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// The recorded instruction list sorted into clauses, with the rules that hold regardless
/// of the target already applied (decision 023): multiple filters conjoined per rule Q4,
/// an absent projection left absent per rule Q3.
///
/// Every builder consumes this instead of filtering the flat list itself, so rule Q4 lives
/// in one place rather than twice per builder.
/// </summary>
public sealed class QueryClauses
{
    public required FromInstruction From { get; init; }

    public required IReadOnlyList<ProjectInstruction> Projections { get; init; }

    public required IReadOnlyList<JoinInstruction> Joins { get; init; }

    /// <summary>WHERE, already conjoined from every recorded filter (rule Q4).</summary>
    public ConditionNode? Filter { get; init; }

    public required IReadOnlyList<GroupByInstruction> GroupBys { get; init; }

    /// <summary>HAVING, already conjoined (rule Q4).</summary>
    public ConditionNode? PostFilter { get; init; }

    public required IReadOnlyList<OrderByInstruction> OrderBys { get; init; }

    /// <summary>
    /// Rows skipped before the slice, when the query paginates (decision 060) - a number
    /// the source stated or a parameter the caller binds (decision 085).
    /// </summary>
    public RowCount? Offset { get; init; }

    /// <summary>
    /// Rows the slice is limited to, when the query paginates (decision 060) - a number the
    /// source stated or a parameter the caller binds (decision 085).
    /// </summary>
    public RowCount? Limit { get; init; }

    /// <summary>
    /// True when the (sub)query collapses duplicate rows of its final projection
    /// (decision 073). A property of the whole projection, which is why it is one flag here
    /// rather than a mark on each ProjectInstruction; the projection step of each builder
    /// writes the word its target spells for it.
    /// </summary>
    public bool Distinct { get; init; }

    /// <summary>True when any projection aggregates - by one of the five functions or, since decision 113, by a list aggregate -, at any depth of its expression.</summary>
    public bool HasAggregates => Projections.Any(p => OperandStructure.ContainsAggregate(p.Operand));

    /// <summary>
    /// True when the query materializes a whole entity rather than a row of named columns:
    /// nobody named columns (rule Q3), or the one projection takes the whole row of one range
    /// variable (<see cref="ProjectInstruction.IsWholeRow"/>). Kept as a question about the
    /// clauses rather than a decision each builder re-derives.
    /// </summary>
    public bool ProjectsWholeEntity => Projections.Count == 0 || Projections is [{ IsWholeRow: true }];

    /// <summary>The alias of the source: its own, or the table where the source wrote none.</summary>
    public string SourceAlias => From.Alias ?? From.Table;

    /// <summary>
    /// The range variable whose whole row the query materializes: the source where nobody
    /// named columns - the whole joined row under its first alias -, the one variable a
    /// whole-row projection names, null for a row of named columns.
    /// </summary>
    public string? MaterializedAlias => Projections.Count == 0
        ? SourceAlias
        : Projections is [{ IsWholeRow: true } whole] ? whole.Operand.Table : null;

    /// <summary>
    /// The table - or the intermediate result - behind <see cref="MaterializedAlias"/>, which
    /// is what the entity the query materializes into is resolved from; null for a row of
    /// named columns.
    /// </summary>
    public string? MaterializedTable
    {
        get
        {
            if (MaterializedAlias is not { } alias)
            {
                return null;
            }

            if (string.Equals(alias, SourceAlias, StringComparison.OrdinalIgnoreCase))
            {
                return From.Table;
            }

            return Joins.FirstOrDefault(j => string.Equals(j.RightTableAlias ?? j.RightTable, alias, StringComparison.OrdinalIgnoreCase))?.RightTable
                   ?? From.Table;
        }
    }

    /// <summary>True when the whole row materialized is the source's own, under which no Select, no select clause and a bare <c>*</c> say the same thing.</summary>
    public bool MaterializesTheSource => MaterializedAlias is { } alias && string.Equals(alias, SourceAlias, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the projection names the whole row of one range variable (<c>o.*</c>) rather
    /// than leaving the projection out. Without joins the two are the same; with joins only
    /// the absence is the whole joined row, and the named row - the source's own included -
    /// is that one member of it.
    /// </summary>
    public bool ProjectsOneWholeRow => Projections is [{ IsWholeRow: true }];
}
