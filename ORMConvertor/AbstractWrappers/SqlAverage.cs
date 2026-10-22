using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions.Conditions;

namespace AbstractWrappers;

/// <summary>
/// The average of the model is SQL's AVG (decision 161): it answers in the scalar of its
/// argument, and over a whole number with the quotient cut off. Five of the six target
/// languages average otherwise - LINQ and Hibernate's HQL average a whole number in floating
/// point wherever the average stands, HQL and JPQL hand every average over as a Double -, so a
/// writer converts its own average back into SQL's scalar and a reader reads the average of its
/// language as the SQL it computes. What the two sides share is kept here, so that no language
/// project answers it in its own way (S1): which conversion restores SQL's average, the scalar
/// of an averaged value as a reader knows it from the mapping, and how an average read in
/// floating point or under such a conversion is said in the model.
/// </summary>
public static class SqlAverage
{
    /// <summary>The precision SQL Server gives an average over a decimal: AVG over decimal(p, s) is decimal(38, max(s, 6)).</summary>
    public const int DecimalPrecision = 38;

    /// <summary>The least scale SQL Server gives an average over a decimal.</summary>
    public const int MinimumDecimalScale = 6;

    /// <summary>
    /// What a writer converts its average into so that it answers what SQL's AVG answers over
    /// an argument of the scalar: a whole number into the integer SQL answers in - int up to
    /// int, as SQL Server averages tinyint and smallint in int, long over long -, a decimal
    /// into a decimal, a floating-point number into nothing, since every language averages it
    /// alike. Unknown where the argument has no scalar: then no conversion can be chosen.
    /// </summary>
    public static AverageRestoration RestorationOf(ScalarType? argument) => argument switch
    {
        ScalarType.Byte or ScalarType.Short or ScalarType.Int => AverageRestoration.IntoInt,
        ScalarType.Long => AverageRestoration.IntoLong,
        ScalarType.Decimal => AverageRestoration.IntoDecimal,
        ScalarType.Double or ScalarType.Float => AverageRestoration.None,
        _ => AverageRestoration.Unknown,
    };

    /// <summary>The scale of SQL's average over a decimal of the given scale; a scale the mapping does not state is taken as at most six places.</summary>
    public static int DecimalScale(int? argumentScale) => Math.Max(argumentScale ?? 0, MinimumDecimalScale);

    /// <summary>Whether the operand is SQL's average, AVG over a value.</summary>
    public static bool IsAverage(QueryOperand operand)
        => string.Equals(operand.Function, "AVG", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the expression is an average over a window (decision 161).</summary>
    public static bool IsAverage(QueryExpression expression)
        => string.Equals(expression.WindowAggregate, "AVG", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The scalar of a value as a reader knows it before the builder's gate has run - what an
    /// average ranges over, or a column of an intermediate result it may range over: a column's
    /// as the resolver finds it in the mapping or in the definition behind its alias, a
    /// constant's and a parameter's own, a conversion's target, an arithmetic operation's from
    /// both sides by the rule SQL and C# share - a floating-point side makes it floating point,
    /// else a decimal side a decimal, else it stays a whole number -, a count's and a ranking's
    /// a long, any other aggregate's, over a window or not, its argument's. Null where any part
    /// has no number the reader knows.
    /// </summary>
    public static ScalarType? ScalarOf(QueryOperand value, Func<QueryOperand, ColumnNumber?> column)
    {
        if (value.IsAggregate)
        {
            return string.Equals(value.Function, "COUNT", StringComparison.OrdinalIgnoreCase)
                ? ScalarType.Long
                : ScalarOf(value.Bare(), column);
        }

        if (value is { IsExpression: true, Expression.IsWindow: true })
        {
            var window = value.Expression!;
            return !window.IsWindowAggregate || string.Equals(window.WindowAggregate, "COUNT", StringComparison.OrdinalIgnoreCase)
                ? ScalarType.Long
                : ScalarOf(window.Argument!, column);
        }

        if (value.IsConstant)
        {
            return value.Constant!.Type;
        }

        if (value.IsParameter)
        {
            return value.Parameter!.Type;
        }

        if (value.IsColumn)
        {
            return column(value)?.Scalar;
        }

        if (!value.IsExpression)
        {
            return null;
        }

        var expression = value.Expression!;
        if (expression is { IsCall: true, Function: QueryFunction.Cast })
        {
            return expression.CastTo;
        }

        if (expression is { IsCall: true, Function: QueryFunction.Abs or QueryFunction.Round })
        {
            return ScalarOf(expression.Arguments![0], column);
        }

        if (expression.IsBinary && expression.Operator != ExpressionOperator.Concat)
        {
            return Numeric([ScalarOf(expression.Left!, column), ScalarOf(expression.Right!, column)]);
        }

        return null;
    }

    /// <summary>The scale of an averaged column as the mapping states it; null for anything else.</summary>
    public static int? ScaleOf(QueryOperand value, Func<QueryOperand, ColumnNumber?> column)
        => value is { IsColumn: true, IsAggregate: false } ? column(value)?.Scale : null;

    /// <summary>
    /// The average of a language that averages a whole number in floating point - C#'s Average,
    /// HQL's avg in Hibernate - as the model says it: over a whole number SQL's AVG over the
    /// value converted into a double, which is the SQL both write; over anything else SQL's AVG
    /// itself.
    /// </summary>
    public static QueryOperand InFloatingPoint(QueryOperand average, ScalarType? argument)
        => IsWhole(argument)
            ? QueryOperand.Computed(QueryExpression.Call(QueryFunction.Cast, [average.Bare()], castTo: ScalarType.Double), average.Function, average.Distinct)
            : average;

    /// <summary>The same over a window: the argument of the window's average converted into a double where it is a whole number.</summary>
    public static QueryExpression InFloatingPoint(QueryExpression window, ScalarType? argument)
        => IsWhole(argument)
            ? QueryExpression.Window(window.WindowAggregate!, QueryOperand.Computed(QueryExpression.Call(QueryFunction.Cast, [window.Argument!], castTo: ScalarType.Double)), window.Partitions!, window.Ordering!)
            : window;

    /// <summary>
    /// A conversion a source wrote over an average, read back as SQL's average where it restores
    /// it - the conversions the writers write: into int over a whole number up to int, into long
    /// over a long, into a decimal of at least the scale SQL gives the average over a decimal.
    /// The first two are SQL's average in every language - where the language averages in
    /// floating point, the conversion cuts the quotient off as SQL does, which is why an
    /// argument a reader converted into a double is taken back. Null where the conversion
    /// restores nothing and stays what it is.
    /// </summary>
    /// <param name="average">The average as read, possibly over a whole number converted into a double.</param>
    /// <param name="into">What the source converts the average into.</param>
    /// <param name="decimalScale">The scale of a conversion into a decimal; null for the others.</param>
    /// <param name="column">The mapping of a column, as the reader resolves it.</param>
    public static QueryOperand? Restored(QueryOperand average, AverageRestoration into, int? decimalScale, Func<QueryOperand, ColumnNumber?> column)
    {
        if (!IsAverage(average))
        {
            return null;
        }

        var argument = Restores(average.Bare(), into, decimalScale, column);
        return argument is null ? null : Averaged(argument, average.Distinct);
    }

    /// <summary>The same over a window: the window's average over the argument the conversion restores.</summary>
    public static QueryExpression? Restored(QueryExpression window, AverageRestoration into, int? decimalScale, Func<QueryOperand, ColumnNumber?> column)
    {
        if (!IsAverage(window))
        {
            return null;
        }

        var argument = Restores(window.Argument!, into, decimalScale, column);
        return argument is null ? null : QueryExpression.Window(window.WindowAggregate!, argument, window.Partitions!, window.Ordering!);
    }

    /// <summary>
    /// The argument SQL's average ranges over when the conversion restores it, or null: a whole
    /// number a reader converted into a double is the whole number again, since the conversion
    /// into an integer cuts the floating-point average off as SQL does.
    /// </summary>
    private static QueryOperand? Restores(QueryOperand argument, AverageRestoration into, int? decimalScale, Func<QueryOperand, ColumnNumber?> column)
    {
        if (argument is { IsExpression: true, IsAggregate: false, Expression: { IsCall: true, Function: QueryFunction.Cast, CastTo: ScalarType.Double } converted }
            && IsWhole(ScalarOf(converted.Arguments![0], column))
            && into is AverageRestoration.IntoInt or AverageRestoration.IntoLong)
        {
            argument = converted.Arguments[0];
        }

        var scalar = ScalarOf(argument, column);
        var restores = into switch
        {
            AverageRestoration.IntoInt => scalar is ScalarType.Byte or ScalarType.Short or ScalarType.Int,
            AverageRestoration.IntoLong => scalar is ScalarType.Long,
            AverageRestoration.IntoDecimal => scalar is ScalarType.Decimal && decimalScale >= DecimalScale(ScaleOf(argument, column)),
            _ => false,
        };

        return restores ? argument : null;
    }

    /// <summary>The average over a value, its DISTINCT kept.</summary>
    private static QueryOperand Averaged(QueryOperand value, bool distinct)
    {
        if (value.IsColumn)
        {
            return QueryOperand.Column(value.Table, value.Property!, "AVG", distinct);
        }

        if (value.IsConstant)
        {
            return QueryOperand.Value(value.Constant!, "AVG");
        }

        if (value.IsParameter)
        {
            return QueryOperand.Bound(value.Parameter!, "AVG");
        }

        return QueryOperand.Computed(value.Expression!, "AVG", distinct);
    }

    private static bool IsWhole(ScalarType? scalar)
        => scalar is ScalarType.Byte or ScalarType.Short or ScalarType.Int or ScalarType.Long;

    /// <summary>The number two or more numbers make together, or null where one is no number or unknown.</summary>
    private static ScalarType? Numeric(IReadOnlyList<ScalarType?> scalars)
    {
        if (scalars.Any(s => s is not (ScalarType.Byte or ScalarType.Short or ScalarType.Int or ScalarType.Long or ScalarType.Decimal or ScalarType.Double or ScalarType.Float)))
        {
            return null;
        }

        if (scalars.Any(s => s is ScalarType.Double or ScalarType.Float))
        {
            return ScalarType.Double;
        }

        if (scalars.Contains(ScalarType.Decimal))
        {
            return ScalarType.Decimal;
        }

        return scalars.Contains(ScalarType.Long) ? ScalarType.Long : ScalarType.Int;
    }
}

/// <summary>
/// The number a column holds as a reader knows it (decision 161): its scalar, and the scale
/// of a decimal where the mapping states one. A column of an entity takes both from its
/// property map; a column of an intermediate result has the scalar of the value its
/// definition projects and no scale.
/// </summary>
public readonly record struct ColumnNumber(ScalarType? Scalar, int? Scale)
{
    /// <summary>The number of a mapped column; null where nothing maps it.</summary>
    public static ColumnNumber? Of(PropertyMap? map)
        => map is null ? null : new ColumnNumber(map.Property.Type is { Category: LangTypeCategory.Scalar } type ? type.ScalarType : null, map.Scale);
}

/// <summary>What a writer converts its average into so that it answers what SQL's AVG answers (decision 161).</summary>
public enum AverageRestoration
{
    /// <summary>Nothing: the argument is a floating-point number, which every language averages alike.</summary>
    None = 1,

    /// <summary>int: the argument is a whole number up to int.</summary>
    IntoInt = 2,

    /// <summary>long: the argument is a long.</summary>
    IntoLong = 3,

    /// <summary>A decimal of SQL's precision and of the scale <see cref="SqlAverage.DecimalScale"/> gives.</summary>
    IntoDecimal = 4,

    /// <summary>No conversion can be chosen: the argument has no scalar.</summary>
    Unknown = 5,
}
