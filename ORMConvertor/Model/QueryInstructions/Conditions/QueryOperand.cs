namespace Model.QueryInstructions.Conditions;

/// <summary>
/// One side of a comparison (decision 024). Either a column reference — a property with an
/// optional table qualifier — or a constant, optionally wrapped in an aggregate function;
/// or a nested subquery (decision 061), which is the third operand shape the paper's
/// condition-tree grammar names.
///
/// Both sides of a comparison are this same type, so they cannot drift apart the way two
/// parallel quadruples of loose strings did. Instances come only from the factory methods,
/// which is what keeps an invalid combination (a constant and a property at once)
/// unwritable — the same device <see cref="Model.AbstractRepresentation.LangType"/> uses.
/// </summary>
public sealed class QueryOperand
{
    private QueryOperand(string? table, string? property, QueryConstant? constant, string? function, SubQueryInstruction? subQuery)
    {
        Table = table;
        Property = property;
        Constant = constant;
        Function = function;
        SubQuery = subQuery;
    }

    /// <summary>Table or alias qualifying <see cref="Property"/>; null when unqualified.</summary>
    public string? Table { get; }

    public string? Property { get; }

    public QueryConstant? Constant { get; }

    /// <summary>Aggregate function applied to the operand, e.g. COUNT or SUM; null when none.</summary>
    public string? Function { get; }

    /// <summary>Nested subquery standing as the operand (decision 061); null otherwise.</summary>
    public SubQueryInstruction? SubQuery { get; }

    public bool IsColumn => Property is not null;

    public bool IsConstant => Constant is not null;

    public bool IsSubQuery => SubQuery is not null;

    public static QueryOperand Column(string? table, string property, string? function = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return new QueryOperand(table, property, null, function, null);
    }

    public static QueryOperand Value(QueryConstant constant, string? function = null)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return new QueryOperand(null, null, constant, function, null);
    }

    public static QueryOperand Nested(SubQueryInstruction subQuery)
    {
        ArgumentNullException.ThrowIfNull(subQuery);
        return new QueryOperand(null, null, null, null, subQuery);
    }

    public override string ToString() => IsSubQuery
        ? "(subquery)"
        : IsColumn
            ? (Table is null ? Property! : $"{Table}.{Property}")
            : Constant!.Text;
}
