namespace Model.QueryInstructions.Conditions;

/// <summary>
/// One side of a comparison (decision 024). Either a column reference — a property with an
/// optional table qualifier — or a constant, optionally wrapped in an aggregate function;
/// or a nested subquery (decision 061), which is the third operand shape the paper's
/// condition-tree grammar names; or a list of values (decision 074), the fourth shape,
/// which stands only as the right side of IN; or a parameter (decision 083), the fifth -
/// a value the query names and the caller supplies. Since decision 102 an aggregate
/// function on a column may carry the DISTINCT modifier, and the values of a list may be
/// scalar parameters as well as constants.
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
        bool distinct,
        SubQueryInstruction? subQuery,
        IReadOnlyList<QueryOperand>? values,
        QueryParameter? parameter)
    {
        Table = table;
        Property = property;
        Constant = constant;
        Function = function;
        Distinct = distinct;
        SubQuery = subQuery;
        Values = values;
        Parameter = parameter;
    }

    /// <summary>Table or alias qualifying <see cref="Property"/>; null when unqualified.</summary>
    public string? Table { get; }

    public string? Property { get; }

    public QueryConstant? Constant { get; }

    /// <summary>Aggregate function applied to the operand, e.g. COUNT or SUM; null when none.</summary>
    public string? Function { get; }

    /// <summary>
    /// Whether <see cref="Function"/> aggregates over the distinct values of its argument -
    /// <c>COUNT(DISTINCT x)</c> (decision 102). A modifier of the function, so the factory
    /// refuses it without one; it is not the DISTINCT of the query scope (decision 073).
    /// </summary>
    public bool Distinct { get; }

    /// <summary>Nested subquery standing as the operand (decision 061); null otherwise.</summary>
    public SubQueryInstruction? SubQuery { get; }

    /// <summary>
    /// The values IN enumerates (decision 074); null otherwise. Each element is a constant
    /// keeping the scalar the parser read it with, or, since decision 102, a scalar
    /// parameter the caller binds - the factory admits nothing else. The list has no scalar
    /// of its own: the model carries what the source wrote, the builder template decides
    /// what a target can take, and a parameter among the values takes its scalar from the
    /// left side of IN, never from its neighbours. Never empty: <c>IN ()</c> has no form
    /// in any target.
    /// </summary>
    public IReadOnlyList<QueryOperand>? Values { get; }

    /// <summary>
    /// The parameter standing in the operand's place (decision 083); null otherwise. One
    /// field, the way a constant is one field, rather than four loose ones: what holds only
    /// of a parameter - a name against an order, collectionness - stays together, and
    /// <see cref="IQueryVisitor"/> does not change, which is the surface S1 promises to
    /// keep stable for the seventh framework.
    /// </summary>
    public QueryParameter? Parameter { get; }

    public bool IsColumn => Property is not null;

    public bool IsConstant => Constant is not null;

    public bool IsSubQuery => SubQuery is not null;

    public bool IsValueList => Values is not null;

    public bool IsParameter => Parameter is not null;

    /// <summary>
    /// A column, optionally under an aggregate function, optionally over the distinct values
    /// of the column (decision 102). The modifier without a function is an unwritable
    /// combination, as a constant beside a property is.
    /// </summary>
    public static QueryOperand Column(string? table, string property, string? function = null, bool distinct = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        if (distinct && function is null)
        {
            throw new ArgumentException("DISTINCT modifies an aggregate function; an operand without one cannot carry it.", nameof(distinct));
        }

        return new QueryOperand(table, property, null, function, distinct, null, null, null);
    }

    public static QueryOperand Value(QueryConstant constant, string? function = null)
    {
        ArgumentNullException.ThrowIfNull(constant);
        return new QueryOperand(null, null, constant, function, false, null, null, null);
    }

    public static QueryOperand Nested(SubQueryInstruction subQuery)
    {
        ArgumentNullException.ThrowIfNull(subQuery);
        return new QueryOperand(null, null, null, null, false, subQuery, null, null);
    }

    /// <summary>
    /// The values IN enumerates (decision 074): constants, and since decision 102 scalar
    /// parameters among them. Anything else - a column, a subquery, a nested list, an
    /// aggregate, a collection parameter - is refused here rather than by a check
    /// downstream, so that an invalid list is not writable.
    /// </summary>
    public static QueryOperand ValueList(IReadOnlyList<QueryOperand> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("A list of values carries at least one value.", nameof(values));
        }

        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(values));
            var admitted = value.Function is null
                && (value.IsConstant || (value.IsParameter && !value.Parameter!.IsCollection));
            if (!admitted)
            {
                throw new ArgumentException("A list of values carries constants and scalar parameters only.", nameof(values));
            }
        }

        return new QueryOperand(null, null, null, null, false, null, values, null);
    }

    /// <summary>
    /// A parameter in the operand's place (decision 083) - the value the caller binds.
    /// Named for the verb every target spells rather than for the field it fills, because
    /// the field is <see cref="Parameter"/>, after <see cref="Constant"/>, and a factory
    /// cannot share a name with a property. Takes an aggregate function for the same reason
    /// a constant does: a parameter stands wherever a constant stands.
    /// </summary>
    public static QueryOperand Bound(QueryParameter parameter, string? function = null)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return new QueryOperand(null, null, null, function, false, null, null, parameter);
    }

    public override string ToString() => IsSubQuery
        ? "(subquery)"
        : IsValueList
            ? $"({string.Join(", ", Values!.Select(v => v.ToString()))})"
            : IsParameter
                ? Parameter!.ToString()
                : IsColumn
                    ? (Table is null ? Property! : $"{Table}.{Property}")
                    : Constant!.Text;
}
