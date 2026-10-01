using Model.AbstractRepresentation.Enums;

namespace Model.QueryInstructions.Conditions;

/// <summary>
/// One branch of a searched <c>CASE</c>: the condition and the value the expression takes
/// when it holds. The condition is the same <see cref="ConditionNode"/> a filter is, so the
/// same visitor code writes it and the parameter gate descends into it.
/// </summary>
public sealed record CaseBranch(ConditionNode When, QueryOperand Then);

/// <summary>
/// One key of the ordering inside a window or a list aggregate (decision 113): an operand and
/// its direction - the same pair an <see cref="OrderByInstruction"/> is, without being an
/// instruction of the query, because it orders the rows of one computation, not the result.
/// </summary>
public sealed record OrderingKey(QueryOperand Operand, bool Ascending = true)
{
    public override string ToString() => $"{Operand} {(Ascending ? "asc" : "desc")}";
}

/// <summary>
/// A value the query computes (decision 107): the content of the sixth operand shape,
/// <see cref="QueryOperand.Expression"/>. A closed type with five factories, like the
/// operand it lives in - a binary operation, a call of a function from the closed
/// vocabulary <see cref="QueryFunction"/>, a searched <c>CASE</c>, and since decision 113 a
/// ranking function over a window and an aggregate that joins values into a list -, whose
/// leaves are operands again, so that nesting is recursion and a visitor writes nothing it
/// does not write already.
///
/// The expression carries no scalar of its own. A constant carries one because the parser
/// read it off the literal; the scalar of an expression follows from the mapping
/// representation, which only the builder has (decision 083), so it is derived there,
/// bottom-up from the leaves, and kept as a typed view beside the tree rather than written
/// into it. The one scalar a call does carry is the one a conversion converts into, which is
/// what the source wrote and not a derivation.
///
/// A list of values and a collection parameter cannot be a leaf - no target writes either
/// in the position of a computed value - and the factories refuse them, the way the factory
/// of a list refuses what no target writes among its values (decision 074).
/// </summary>
public sealed class QueryExpression
{
    private QueryExpression(
        ExpressionOperator? op = null,
        QueryOperand? left = null,
        QueryOperand? right = null,
        QueryFunction? function = null,
        IReadOnlyList<QueryOperand>? arguments = null,
        DateUnit? unit = null,
        ScalarType? castTo = null,
        IReadOnlyList<CaseBranch>? branches = null,
        QueryOperand? otherwise = null,
        RankingFunction? ranking = null,
        IReadOnlyList<QueryOperand>? partitions = null,
        IReadOnlyList<OrderingKey>? ordering = null,
        QueryOperand? listed = null,
        string? separator = null)
    {
        Operator = op;
        Left = left;
        Right = right;
        Function = function;
        Arguments = arguments;
        Unit = unit;
        CastTo = castTo;
        Branches = branches;
        Else = otherwise;
        Ranking = ranking;
        Partitions = partitions;
        Ordering = ordering;
        Listed = listed;
        Separator = separator;
    }

    /// <summary>The operator of a binary expression; null for the other shapes.</summary>
    public ExpressionOperator? Operator { get; }

    public QueryOperand? Left { get; }

    public QueryOperand? Right { get; }

    /// <summary>The function a call names; null for the other shapes.</summary>
    public QueryFunction? Function { get; }

    /// <summary>The arguments of a call, in order; null for the other shapes.</summary>
    public IReadOnlyList<QueryOperand>? Arguments { get; }

    /// <summary>The unit of a <see cref="QueryFunction.DateAdd"/> or a <see cref="QueryFunction.DateDiff"/> (decision 113); null for every other call and shape.</summary>
    public DateUnit? Unit { get; }

    /// <summary>The scalar a <see cref="QueryFunction.Cast"/> converts into (decision 113); null for every other call and shape.</summary>
    public ScalarType? CastTo { get; }

    /// <summary>The branches of a searched CASE, in order; null for the other shapes.</summary>
    public IReadOnlyList<CaseBranch>? Branches { get; }

    /// <summary>
    /// The value a CASE takes when no branch holds; null when the source wrote no ELSE, in
    /// which case SQL, HQL and JPQL yield NULL and the model does not validate.
    /// </summary>
    public QueryOperand? Else { get; }

    /// <summary>The ranking function of a window (decision 113); null for the other shapes.</summary>
    public RankingFunction? Ranking { get; }

    /// <summary>The partitions of a window, possibly none (decision 113); null for the other shapes.</summary>
    public IReadOnlyList<QueryOperand>? Partitions { get; }

    /// <summary>
    /// The ordering of a window, never empty, or of a list aggregate, possibly empty
    /// (decision 113); null for the other shapes.
    /// </summary>
    public IReadOnlyList<OrderingKey>? Ordering { get; }

    /// <summary>The value a list aggregate joins into its list (decision 113); null for the other shapes.</summary>
    public QueryOperand? Listed { get; }

    /// <summary>The text a list aggregate puts between two values (decision 113); null for the other shapes.</summary>
    public string? Separator { get; }

    public bool IsBinary => Operator is not null;

    public bool IsCall => Function is not null;

    public bool IsCase => Branches is not null;

    public bool IsWindow => Ranking is not null;

    public bool IsListAggregate => Separator is not null;

    /// <summary>
    /// The scalars a <see cref="QueryFunction.Cast"/> converts into (decision 113): the five
    /// Jakarta Persistence 3.2 names a conversion by, which T-SQL writes as <c>INT</c>,
    /// <c>BIGINT</c>, <c>REAL</c>, <c>FLOAT</c> and <c>NVARCHAR(MAX)</c> - types without a
    /// length or a precision that could change the value. A decimal has no such spelling: bare
    /// <c>DECIMAL</c> is <c>DECIMAL(18,0)</c>, which cuts the fraction off.
    /// </summary>
    public static IReadOnlySet<ScalarType> Castable { get; } = new HashSet<ScalarType>
    {
        ScalarType.Int, ScalarType.Long, ScalarType.Float, ScalarType.Double, ScalarType.String,
    };

    /// <summary>
    /// A binary operation. Binary rather than n-ary: <c>concat(a, b, c)</c> is read as a
    /// nesting and a visitor whose language has an n-ary spelling may flatten it again -
    /// that is the target's spelling (decision 022), not the model.
    /// </summary>
    public static QueryExpression Binary(ExpressionOperator op, QueryOperand left, QueryOperand right)
    {
        Leaf(left, nameof(left));
        Leaf(right, nameof(right));
        return new QueryExpression(op, left, right);
    }

    /// <summary>
    /// A call of a function of the vocabulary. The arity is a property of the function and
    /// the factory holds it, so that a call no target could write is not writable; so are the
    /// unit, which a date function requires and every other refuses, and the scalar of a
    /// conversion, which a cast requires - one of <see cref="Castable"/> - and every other
    /// function refuses (decision 113).
    /// </summary>
    public static QueryExpression Call(
        QueryFunction function,
        IReadOnlyList<QueryOperand> arguments,
        DateUnit? unit = null,
        ScalarType? castTo = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var (least, most) = Arity(function);
        if (arguments.Count < least || arguments.Count > most)
        {
            throw new ArgumentException(
                least == most
                    ? $"{function} takes {least} argument(s), not {arguments.Count}."
                    : $"{function} takes at least {least} arguments, not {arguments.Count}.",
                nameof(arguments));
        }

        var dated = function is QueryFunction.DateAdd or QueryFunction.DateDiff;
        if (dated != unit.HasValue)
        {
            throw new ArgumentException(
                dated ? $"{function} takes a unit." : $"{function} takes no unit; only DateAdd and DateDiff do.",
                nameof(unit));
        }

        if (function == QueryFunction.Cast)
        {
            if (castTo is not { } scalar || !Castable.Contains(scalar))
            {
                throw new ArgumentException(
                    $"Cast converts into one of {string.Join(", ", Castable)}, not into {castTo?.ToString() ?? "nothing"}.",
                    nameof(castTo));
            }
        }
        else if (castTo is not null)
        {
            throw new ArgumentException($"{function} converts into nothing; only Cast does.", nameof(castTo));
        }

        foreach (var argument in arguments)
        {
            Leaf(argument, nameof(arguments));
        }

        return new QueryExpression(function: function, arguments: arguments, unit: unit, castTo: castTo);
    }

    /// <summary>
    /// A searched CASE with at least one branch. A simple <c>CASE x WHEN v THEN …</c> is read
    /// as the searched form with an equality per branch - an exact rewrite, the kind of
    /// reading decision 103 gives a LINQ query expression.
    /// </summary>
    public static QueryExpression Case(IReadOnlyList<CaseBranch> branches, QueryOperand? otherwise)
    {
        ArgumentNullException.ThrowIfNull(branches);
        if (branches.Count == 0)
        {
            throw new ArgumentException("A CASE carries at least one branch.", nameof(branches));
        }

        foreach (var branch in branches)
        {
            ArgumentNullException.ThrowIfNull(branch, nameof(branches));
            Leaf(branch.Then, nameof(branches));
        }

        if (otherwise is not null)
        {
            Leaf(otherwise, nameof(otherwise));
        }

        return new QueryExpression(branches: branches, otherwise: otherwise);
    }

    /// <summary>
    /// A ranking function over a window (decision 113): <c>ROW_NUMBER() OVER (PARTITION BY …
    /// ORDER BY …)</c>. The ordering is required, because SQL Server requires it of all three
    /// functions; the partitions may be none. Where it may stand - only in a projection - is
    /// the rule of the builder template, which the model does not validate.
    /// </summary>
    public static QueryExpression Window(RankingFunction function, IReadOnlyList<QueryOperand> partitions, IReadOnlyList<OrderingKey> ordering)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentNullException.ThrowIfNull(ordering);
        if (ordering.Count == 0)
        {
            throw new ArgumentException("A ranking function orders the rows of its window; the ordering carries at least one key.", nameof(ordering));
        }

        foreach (var partition in partitions)
        {
            Leaf(partition, nameof(partitions));
        }

        foreach (var key in ordering)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(ordering));
            Leaf(key.Operand, nameof(ordering));
        }

        return new QueryExpression(ranking: function, partitions: partitions, ordering: ordering);
    }

    /// <summary>
    /// An aggregate that joins the values of a group into one text, the separator between two
    /// of them, in the order the ordering states - T-SQL's <c>STRING_AGG(x, ', ') WITHIN GROUP
    /// (ORDER BY …)</c> (decision 113). An aggregate as much as COUNT is, so the builder
    /// template holds it to the rules of grouping; a factory rather than the string an
    /// aggregate function travels as, because every language names it differently and it
    /// carries a separator and an ordering besides.
    /// </summary>
    public static QueryExpression ListAggregate(QueryOperand value, string separator, IReadOnlyList<OrderingKey> ordering)
    {
        Leaf(value, nameof(value));
        ArgumentNullException.ThrowIfNull(separator);
        ArgumentNullException.ThrowIfNull(ordering);

        foreach (var key in ordering)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(ordering));
            Leaf(key.Operand, nameof(ordering));
        }

        return new QueryExpression(listed: value, separator: separator, ordering: ordering);
    }

    /// <summary>The smallest and largest number of arguments a function of the vocabulary takes.</summary>
    public static (int Least, int Most) Arity(QueryFunction function) => function switch
    {
        QueryFunction.Upper or QueryFunction.Lower or QueryFunction.Trim or QueryFunction.Length
            or QueryFunction.Abs or QueryFunction.Year or QueryFunction.Month or QueryFunction.Day
            or QueryFunction.EscapePattern or QueryFunction.Sqrt or QueryFunction.Cast => (1, 1),
        QueryFunction.DateAdd or QueryFunction.DateDiff or QueryFunction.Round => (2, 2),
        QueryFunction.Substring => (3, 3),
        QueryFunction.Coalesce => (2, int.MaxValue),
        QueryFunction.CurrentTimestamp => (0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, null),
    };

    /// <summary>
    /// Every operand this expression stands over, in order, one level deep: the sides of an
    /// operation, the arguments of a call, the values of a CASE, the partitions and the
    /// ordering of a window, the value and the ordering of a list aggregate.
    /// </summary>
    public IEnumerable<QueryOperand> Leaves()
    {
        if (IsBinary)
        {
            yield return Left!;
            yield return Right!;
        }
        else if (IsCall)
        {
            foreach (var argument in Arguments!)
            {
                yield return argument;
            }
        }
        else if (IsCase)
        {
            foreach (var branch in Branches!)
            {
                yield return branch.Then;
            }

            if (Else is not null)
            {
                yield return Else;
            }
        }
        else if (IsWindow)
        {
            foreach (var partition in Partitions!)
            {
                yield return partition;
            }

            foreach (var key in Ordering!)
            {
                yield return key.Operand;
            }
        }
        else
        {
            yield return Listed!;

            foreach (var key in Ordering!)
            {
                yield return key.Operand;
            }
        }
    }

    /// <summary>
    /// The same expression over other leaves and other conditions, or null where a function
    /// gives null for one of them: what a walk of the builder template uses to type a literal
    /// inside an expression without the shape changing (decision 113). The leaves are replaced
    /// in the order <see cref="Leaves"/> yields them; an unchanged tree comes back as itself.
    /// </summary>
    public QueryExpression? Rebuilt(Func<QueryOperand, QueryOperand?> leaf, Func<ConditionNode, ConditionNode?> condition)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ArgumentNullException.ThrowIfNull(condition);

        var original = Leaves().ToList();
        var leaves = new List<QueryOperand>(original.Count);
        foreach (var operand in original)
        {
            if (leaf(operand) is not { } mapped)
            {
                return null;
            }

            leaves.Add(mapped);
        }

        var whens = new List<ConditionNode>();
        foreach (var branch in Branches ?? [])
        {
            if (condition(branch.When) is not { } mapped)
            {
                return null;
            }

            whens.Add(mapped);
        }

        if (leaves.SequenceEqual(original, ReferenceEqualityComparer.Instance)
            && whens.SequenceEqual((Branches ?? []).Select(b => b.When), ReferenceEqualityComparer.Instance))
        {
            return this;
        }

        if (IsBinary)
        {
            return Binary(Operator!.Value, leaves[0], leaves[1]);
        }

        if (IsCall)
        {
            return Call(Function!.Value, leaves, Unit, CastTo);
        }

        if (IsCase)
        {
            var branches = Branches!.Select((b, i) => new CaseBranch(whens[i], leaves[i])).ToList();
            return Case(branches, Else is null ? null : leaves[^1]);
        }

        if (IsWindow)
        {
            var partitions = leaves.Take(Partitions!.Count).ToList();
            var ordering = Ordering!.Select((k, i) => k with { Operand = leaves[Partitions.Count + i] }).ToList();
            return Window(Ranking!.Value, partitions, ordering);
        }

        return ListAggregate(leaves[0], Separator!, Ordering!.Select((k, i) => k with { Operand = leaves[1 + i] }).ToList());
    }

    private static void Leaf(QueryOperand operand, string parameter)
    {
        ArgumentNullException.ThrowIfNull(operand, parameter);
        if (operand.IsValueList || operand.Parameter?.IsCollection == true)
        {
            throw new ArgumentException("A list of values or a collection parameter cannot be a leaf of an expression; no target writes one in that position.", parameter);
        }
    }

    public override string ToString()
    {
        if (IsBinary)
        {
            return $"({Left} {Operator} {Right})";
        }

        if (IsCall)
        {
            var arguments = Arguments!.Select(a => a.ToString());
            return Function switch
            {
                QueryFunction.DateAdd or QueryFunction.DateDiff => $"{Function}({Unit}, {string.Join(", ", arguments)})",
                QueryFunction.Cast => $"Cast({Arguments![0]} as {CastTo})",
                _ => $"{Function}({string.Join(", ", arguments)})",
            };
        }

        if (IsCase)
        {
            var branches = string.Join(" ", Branches!.Select(b => $"when {b.When} then {b.Then}"));
            return Else is null ? $"case {branches} end" : $"case {branches} else {Else} end";
        }

        var ordered = string.Join(", ", Ordering!.Select(k => k.ToString()));

        if (IsWindow)
        {
            var partitioned = Partitions!.Count == 0 ? string.Empty : $"partition by {string.Join(", ", Partitions.Select(p => p.ToString()))} ";
            return $"{Ranking}() over ({partitioned}order by {ordered})";
        }

        return Ordering!.Count == 0
            ? $"ListAggregate({Listed}, '{Separator}')"
            : $"ListAggregate({Listed}, '{Separator}' order by {ordered})";
    }
}
