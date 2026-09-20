using System.Globalization;
using Model.QueryInstructions.Conditions;

namespace Model.QueryInstructions;

/// <summary>
/// One of the two row counts of a pagination: the number of rows to skip, or the number of
/// rows to take (decision 085). Either the query states it, or it leaves it to the caller
/// and states a parameter instead - and those are the only two shapes there are, which is
/// why this is a type of its own rather than a <see cref="QueryOperand"/>.
///
/// A column, a subquery, a list of values and an aggregate function are the other four
/// shapes an operand has, and no target writes any of them as a row count; carrying an
/// operand here would make them writable and leave a rule to forbid at run time what a type
/// can refuse outright - the device the model has used since decision 024. Carrying the
/// number as a <see cref="QueryConstant"/> would cost as much in the other direction: a
/// constant holds its value as text with an optional scalar, so every builder would parse
/// the count back out of a string that could say anything.
/// </summary>
public sealed class RowCount
{
    private RowCount(long? value, QueryParameter? parameter)
    {
        Value = value;
        Parameter = parameter;
    }

    /// <summary>The number of rows the query itself states; null when it binds a parameter.</summary>
    public long? Value { get; }

    /// <summary>The parameter the caller binds the count to; null when the query states a number.</summary>
    public QueryParameter? Parameter { get; }

    public bool IsParameter => Parameter is not null;

    /// <summary>
    /// A count the query states. Non-negative, because a negative slice has no meaning in
    /// any source and none of the four parsers can produce one - the factory refuses it the
    /// way the factory of a list of values refuses an empty list (decision 074), so that no
    /// check downstream has to.
    /// </summary>
    public static RowCount Literal(long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return new RowCount(value, null);
    }

    /// <summary>
    /// A count the caller binds. The scalar of the parameter is not the parser's to state:
    /// a row count is a whole number whatever the source wrote around it, so the builder
    /// template types it from the clause rather than from a comparison (decision 085).
    /// </summary>
    public static RowCount Bound(QueryParameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        return new RowCount(null, parameter);
    }

    public override string ToString()
        => IsParameter ? Parameter!.ToString() : Value!.Value.ToString(CultureInfo.InvariantCulture);
}
