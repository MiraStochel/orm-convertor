namespace Model.QueryInstructions.Conditions;

/// <summary>
/// One branch of a searched <c>CASE</c>: the condition and the value the expression takes
/// when it holds. The condition is the same <see cref="ConditionNode"/> a filter is, so the
/// same visitor code writes it and the parameter gate descends into it.
/// </summary>
public sealed record CaseBranch(ConditionNode When, QueryOperand Then);

/// <summary>
/// A value the query computes (decision 107): the content of the sixth operand shape,
/// <see cref="QueryOperand.Expression"/>. A closed type with three factories, like the
/// operand it lives in - a binary operation, a call of a function from the closed
/// vocabulary <see cref="QueryFunction"/>, and a searched <c>CASE</c> -, whose leaves are
/// operands again, so that nesting is recursion and a visitor writes nothing it does not
/// write already.
///
/// The expression carries no scalar of its own. A constant carries one because the parser
/// read it off the literal; the scalar of an expression follows from the mapping
/// representation, which only the builder has (decision 083), so it is derived there,
/// bottom-up from the leaves, and kept as a typed view beside the tree rather than written
/// into it.
///
/// A list of values and a collection parameter cannot be a leaf - no target writes either
/// in the position of a computed value - and the factories refuse them, the way the factory
/// of a list refuses what no target writes among its values (decision 074).
/// </summary>
public sealed class QueryExpression
{
    private QueryExpression(
        ExpressionOperator? op,
        QueryOperand? left,
        QueryOperand? right,
        QueryFunction? function,
        IReadOnlyList<QueryOperand>? arguments,
        IReadOnlyList<CaseBranch>? branches,
        QueryOperand? otherwise)
    {
        Operator = op;
        Left = left;
        Right = right;
        Function = function;
        Arguments = arguments;
        Branches = branches;
        Else = otherwise;
    }

    /// <summary>The operator of a binary expression; null for the other two shapes.</summary>
    public ExpressionOperator? Operator { get; }

    public QueryOperand? Left { get; }

    public QueryOperand? Right { get; }

    /// <summary>The function a call names; null for the other two shapes.</summary>
    public QueryFunction? Function { get; }

    /// <summary>The arguments of a call, in order; null for the other two shapes.</summary>
    public IReadOnlyList<QueryOperand>? Arguments { get; }

    /// <summary>The branches of a searched CASE, in order; null for the other two shapes.</summary>
    public IReadOnlyList<CaseBranch>? Branches { get; }

    /// <summary>
    /// The value a CASE takes when no branch holds; null when the source wrote no ELSE, in
    /// which case SQL, HQL and JPQL yield NULL and the model does not validate.
    /// </summary>
    public QueryOperand? Else { get; }

    public bool IsBinary => Operator is not null;

    public bool IsCall => Function is not null;

    public bool IsCase => Branches is not null;

    /// <summary>
    /// A binary operation. Binary rather than n-ary: <c>concat(a, b, c)</c> is read as a
    /// nesting and a visitor whose language has an n-ary spelling may flatten it again -
    /// that is the target's spelling (decision 022), not the model.
    /// </summary>
    public static QueryExpression Binary(ExpressionOperator op, QueryOperand left, QueryOperand right)
    {
        Leaf(left, nameof(left));
        Leaf(right, nameof(right));
        return new QueryExpression(op, left, right, null, null, null, null);
    }

    /// <summary>
    /// A call of a function of the vocabulary. The arity is a property of the function and
    /// the factory holds it, so that a call no target could write is not writable.
    /// </summary>
    public static QueryExpression Call(QueryFunction function, IReadOnlyList<QueryOperand> arguments)
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

        foreach (var argument in arguments)
        {
            Leaf(argument, nameof(arguments));
        }

        return new QueryExpression(null, null, null, function, arguments, null, null);
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

        return new QueryExpression(null, null, null, null, null, branches, otherwise);
    }

    /// <summary>The smallest and largest number of arguments a function of the vocabulary takes.</summary>
    public static (int Least, int Most) Arity(QueryFunction function) => function switch
    {
        QueryFunction.Upper or QueryFunction.Lower or QueryFunction.Trim or QueryFunction.Length
            or QueryFunction.Abs or QueryFunction.Year or QueryFunction.Month or QueryFunction.Day
            or QueryFunction.EscapePattern => (1, 1),
        QueryFunction.Substring => (3, 3),
        QueryFunction.Coalesce => (2, int.MaxValue),
        QueryFunction.CurrentTimestamp => (0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(function), function, null),
    };

    /// <summary>Every operand this expression stands over, in order, one level deep.</summary>
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
        else
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
            return $"{Function}({string.Join(", ", Arguments!.Select(a => a.ToString()))})";
        }

        var branches = string.Join(" ", Branches!.Select(b => $"when {b.When} then {b.Then}"));
        return Else is null ? $"case {branches} end" : $"case {branches} else {Else} end";
    }
}
