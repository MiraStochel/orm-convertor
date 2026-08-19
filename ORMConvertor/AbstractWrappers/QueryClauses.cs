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

    /// <summary>True when any projection carries an aggregate function.</summary>
    public bool HasAggregates => Projections.Any(p => p.Function is not null);

    /// <summary>
    /// True when the query materialises whole entities because nobody named columns
    /// (rule Q3). Kept as a question about the clauses rather than a decision each builder
    /// re-derives.
    /// </summary>
    public bool ProjectsWholeEntity => Projections.Count == 0;
}
