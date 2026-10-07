using Model.AbstractRepresentation;
using Model.QueryInstructions.Conditions;

namespace LinqBuilding;

/// <summary>
/// The lexical scope a LINQ chain is being written in. A LINQ lambda names one parameter,
/// and what that parameter holds changes as the chain grows: the source row at first, a
/// transparent tuple after a join, a grouping after GroupBy. Rendering a column therefore
/// needs this state, which is why the LINQ visitor carries it and the SQL one does not.
/// </summary>
public sealed class LinqScope
{
    /// <summary>Name of the current lambda parameter.</summary>
    public string Param { get; set; } = "c";

    /// <summary>True once a join has made the parameter hold a tuple of rows.</summary>
    public bool Composite { get; set; }

    public bool Grouped { get; set; }

    /// <summary>
    /// The keys of the grouping, each with the member of the key object that names it - none
    /// for a single key, which is <c>g.Key</c> itself (decision 113).
    /// </summary>
    public IReadOnlyList<LinqGroupKey> GroupKeys { get; set; } = [];

    /// <summary>Parameter used inside an aggregate lambda, which ranges over group elements.</summary>
    public string ElementParam { get; set; } = "e";

    public Dictionary<string, EntityMap> Entities { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Aliases this scope itself declares - the source and each join, mapped or not. What a
    /// nested subquery's scope does not declare it looks up in the enclosing scope, which is
    /// how a correlated reference finds the outer lambda's parameter (decision 061).
    /// </summary>
    public HashSet<string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Aliases on the side of an outer join that may find no match - the joined row of a left
    /// join, the rows before a right one, both of a full one. A column of such a row is null
    /// wherever the join found no match, whatever type its entity declares.
    /// </summary>
    public HashSet<string> OptionalAliases { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string Row(string? alias) => Composite && alias is not null ? $"{Param}.{alias}" : Param;

    public string ElementRow(string? alias) => Composite && alias is not null ? $"{ElementParam}.{alias}" : ElementParam;
}

/// <summary>
/// One key of a LINQ grouping (decision 113): the value grouped by, a column or an
/// expression, and the member of the anonymous key object it goes by - null where the key is
/// the only one and <c>g.Key</c> is the value itself.
/// </summary>
public sealed record LinqGroupKey(QueryOperand Key, string? Member);
