namespace Model.QueryInstructions.Conditions;

/// <summary>
/// One side of a comparison (decision 024). Either a column reference — a property with an
/// optional table qualifier — or a constant, optionally wrapped in an aggregate function;
/// or a nested subquery (decision 061), which is the third operand shape the paper's
/// condition-tree grammar names; or a list of values (decision 074), the fourth shape,
/// which stands only as the right side of IN.
///
/// Both sides of a comparison are this same type, so they cannot drift apart the way two
/// parallel quadruples of loose strings did. Instances come only from the factory methods,
/// which is what keeps an invalid combination (a constant and a property at once)
/// unwritable — the same device <see cref="Model.AbstractRepresentation.LangType"/> uses.
/// </summary>
public sealed class QueryOperand
{
    private QueryOperand(
        string? table,
        string? property,
        QueryConstant? constant,
        string? function,
        SubQueryInstruction? subQuery,
        IReadOnlyList<QueryConstant>? values)
    {
        Table = table;
        Property = property;
        Constant = constant;
        Function = function;
        SubQuery = subQuery;
        Values = values;
    }

    /// <summary>Table or alias qualifying <see cref="Property"/>; null when unqualified.</summary>
    public string? Table { get; }

    public string? Property { get; }

    public QueryConstant? Constant { get; }

    /// <summary>Aggregate function applied to the operand, e.g. COUNT or SUM; null when none.</summary>
    public string? Function { get; }

    /// <summary>Nested subquery standing as the operand (decision 061); null otherwise.</summary>
    public SubQueryInstruction? SubQuery { get; }

    /// <summary>
    /// The values IN enumerates (decision 074); null otherwise. Each constant keeps the
    /// scalar the parser read it with - the list has no scalar of its own, so the model
    /// carries what the source wrote and the builder template decides what a target can
    /// take. Never empty: <c>IN ()</c> has no form in any target.
    /// </summary>
    public IReadOnlyList<QueryConstant>? Values { get; }

    public bool IsColumn => Property is not null;

    public bool IsConstant => Constant is not null;

    public bool IsSubQuery => SubQuery is not null;

    public bool IsValueList => Values is not null;

    public static QueryOperand Column(string? table, string property, string? function = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return new QueryOperand(table, property, null, function, null, null);
    }

    public static QueryOperand Value(QueryConstant constant, string? function = null)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return new QueryOperand(null, null, constant, function, null, null);
    }

    public static QueryOperand Nested(SubQueryInstruction subQuery)
    {
        ArgumentNullException.ThrowIfNull(subQuery);
        return new QueryOperand(null, null, null, null, subQuery, null);
    }

    public static QueryOperand ValueList(IReadOnlyList<QueryConstant> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("A list of values carries at least one value.", nameof(values));
        }

        return new QueryOperand(null, null, null, null, null, values);
    }

    public override string ToString() => IsSubQuery
        ? "(subquery)"
        : IsValueList
            ? $"({string.Join(", ", Values!.Select(v => v.Text))})"
            : IsColumn
                ? (Table is null ? Property! : $"{Table}.{Property}")
                : Constant!.Text;
}
