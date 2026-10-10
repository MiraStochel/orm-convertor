using Common.Naming;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace TransactSql;

/// <summary>
/// Writes query instructions as T-SQL (decision 022). Shared by every framework that emits
/// SQL rather than a query language of its own (decision 082), so it belongs to the
/// language project and not to one wrapper.
///
/// Carries the same report channel as the visitors of the other languages: a shape the
/// target cannot render is a record, not an exception - exceptions stay reserved for errors
/// of the program, and a condition tree a foreign parser produced is not one
/// (decisions 010 and 053).
/// </summary>
/// <param name="typing">The typed view of the query's expressions the builder's gate filled (decision 107); T-SQL spells <c>+</c> for a concatenation and an addition alike, so this visitor reads nothing from it today and takes it so that the four visitors have one shape.</param>
public class SqlQueryVisitor(
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
    ExpressionTyping typing) : IQueryVisitor
{
    /// <summary>
    /// The escape character of the LIKE whose pattern is being written, so that an
    /// <see cref="QueryFunction.EscapePattern"/> inside the pattern escapes with the character
    /// the comparison declares (decision 107). Null outside a pattern.
    /// </summary>
    private string? patternEscape;

    public string Visit(FromInstruction instr)
    {
        var alias = instr.Alias is null ? string.Empty : $" AS {instr.Alias}";
        return $"{instr.Table}{alias}";
    }

    /// <summary>
    /// A projected value with its alias (decision 107). COUNT(*) is the one aggregate whose
    /// argument is not a column, so it is the one the table alias must not qualify:
    /// `COUNT(c.*)` is not T-SQL at all, which the operand rendering below knows.
    /// </summary>
    public string Visit(ProjectInstruction instr)
    {
        var alias = instr.Alias is null ? string.Empty : $" AS {instr.Alias}";
        return $"{BuildOperand(instr.Operand)}{alias}";
    }

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    public string Visit(ComparisonCondition cond)
    {
        // EXISTS carries its subquery as the left operand, the way IS NULL carries its
        // column (decisions 002 and 061). The nested scope is the builder's to render;
        // null means it refused, and the refusal is already on the channel.
        if (cond.Operator == ComparisonOperator.Exists)
        {
            var sub = renderSubQuery(cond.Left.SubQuery!, cond.Operator);
            return sub is null ? string.Empty : $"EXISTS ({sub})";
        }

        if (cond.Left.IsSubQuery || cond.Right?.IsSubQuery == true)
        {
            return SubQueryComparison(cond);
        }

        string left = BuildOperand(cond.Left);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{left} IS NULL";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{left} IS NOT NULL";
        }

        if (cond.Right is null)
        {
            // Unreachable: the template refuses such a tree before any step runs
            // (decision 053). Reported rather than thrown, so that one unrenderable query
            // no longer takes the whole conversion down with it.
            report(ConversionRecordKind.Failure, $"Operator {cond.Operator} has no right operand; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        patternEscape = cond.Operator == ComparisonOperator.Like ? cond.Escape : null;
        string right = BuildOperand(cond.Right);
        patternEscape = null;

        // The escape character of a LIKE goes out as the clause T-SQL spells (decision 102);
        // the template has already held it to LIKE and to one character.
        var escape = cond.Escape is null ? string.Empty : $" ESCAPE '{cond.Escape.Replace("'", "''")}'";
        return $"{left} {MapOperator(cond.Operator)} {right}{escape}";
    }

    /// <summary>
    /// A comparison one of whose sides is a subquery (decision 061): IN and the scalar
    /// operators alike write the nested SELECT in parentheses in the operand's place. A
    /// quantified comparison (decision 119) writes its quantifier between the operator and
    /// the subquery, as T-SQL spells it: <c>x &gt; ALL (SELECT …)</c>.
    /// </summary>
    private string SubQueryComparison(ComparisonCondition cond)
    {
        var left = OperandOrSubQuery(cond.Left, cond.Operator);
        var right = OperandOrSubQuery(cond.Right!, cond.Operator);
        if (left is null || right is null)
        {
            return string.Empty;
        }

        var quantifier = cond.Quantifier is { } q ? QuantifiedComparisons.Spelled(q) + " " : string.Empty;
        return $"{left} {MapOperator(cond.Operator)} {quantifier}{right}";
    }

    private string? OperandOrSubQuery(QueryOperand operand, ComparisonOperator op)
    {
        if (!operand.IsSubQuery)
        {
            return BuildOperand(operand);
        }

        var sub = renderSubQuery(operand.SubQuery!, op);
        return sub is null ? null : $"({sub})";
    }

    public string Visit(LogicalCondition cond)
    {
        if (cond.Operands.Count == 0)
        {
            // Unreachable for the same reason as above.
            report(ConversionRecordKind.Failure, "A logical condition carries no operand; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        string keyword = cond.Operator == LogicalOperator.And ? "AND" : "OR";

        // A nested logical node is always wrapped in parentheses so that an AND containing an OR
        // (and vice versa) does not change the meaning of the query.
        var parts = cond.Operands.Select(operand =>
            operand is LogicalCondition
                ? $"({operand.Accept(this)})"
                : operand.Accept(this));

        return string.Join($" {keyword} ", parts);
    }

    public string Visit(NotCondition cond)
    {
        return $"NOT ({cond.Operand.Accept(this)})";
    }

    /// <summary>
    /// The comparison operators SQL spells. A value outside the set is a refusal, not an
    /// exception: the artifact does not come out and the record says why (decision 053).
    /// </summary>
    private string MapOperator(ComparisonOperator op)
    {
        switch (op)
        {
            case ComparisonOperator.Equal: return "=";
            case ComparisonOperator.NotEqual: return "<>";
            case ComparisonOperator.GreaterThan: return ">";
            case ComparisonOperator.GreaterThanOrEqual: return ">=";
            case ComparisonOperator.LessThan: return "<";
            case ComparisonOperator.LessThanOrEqual: return "<=";
            case ComparisonOperator.Like: return "LIKE";
            case ComparisonOperator.In: return "IN";
            default:
                report(ConversionRecordKind.Failure, $"Operator {op} has no SQL form; the query was not generated.", QueryFeature.Filtering);
                return string.Empty;
        }
    }

    public string Visit(JoinInstruction instr)
    {
        string joinType = instr.Kind switch
        {
            JoinKind.Inner => "INNER JOIN",
            JoinKind.Left => "LEFT JOIN",
            JoinKind.Right => "RIGHT JOIN",
            JoinKind.Full => "FULL JOIN",
            _ => "JOIN"
        };

        var rightTable = instr.RightTableAlias is null
            ? instr.RightTable
            : $"{instr.RightTable} {instr.RightTableAlias}";

        return $"{joinType} {rightTable} ON {instr.OnCondition.Accept(this)}";
    }

    /// <summary>An ordering key with its direction (decision 107): a column, an aggregate, an expression or a projection alias.</summary>
    public string Visit(OrderByInstruction instr)
    {
        string direction = instr.Asc ? "ASC" : "DESC";
        return $"{BuildOperand(instr.Operand)} {direction}";
    }

    /// <summary>A grouping key (decision 113): a column, or an expression written as in the projection that repeats it.</summary>
    public string Visit(GroupByInstruction instr) => BuildOperand(instr.Key);

    private string BuildOperand(QueryOperand operand)
    {
        // The values IN enumerates (decision 074): each one spelled the way a lone constant
        // is, so quoting and suffixes come from the scalar, not from the source text; a
        // parameter among them (decision 102) is spelled the way a lone parameter is.
        if (operand.IsValueList)
        {
            return $"({string.Join(", ", operand.Values!.Select(BuildOperand))})";
        }

        // A subquery in a scalar position - a leaf of an expression (decision 107) - is the
        // nested SELECT in parentheses, as it is beside a scalar operator.
        if (operand.IsSubQuery)
        {
            var sub = renderSubQuery(operand.SubQuery!, ComparisonOperator.Equal);
            return sub is null ? string.Empty : $"({sub})";
        }

        // T-SQL decorates a parameter with @ and has no positional form, so a positional one
        // arrives here already named after its order (decision 083). A collection parameter
        // is written bare, without parentheses: that is the shape Dapper expands into a list
        // before the statement reaches the server.
        // COUNT(*) in operand position - a HAVING over the row count - is the same one
        // aggregate whose argument is no column, and the alias must not qualify it here any
        // more than in the projection above: `COUNT(o.*)` is not T-SQL.
        var text = operand.IsParameter
            ? $"@{QueryParameterNaming.IdentifierFor(operand.Parameter!)}"
            : operand.IsExpression
                ? Expression(operand.Expression!)
                : operand.IsColumn
                    ? (operand.Table is null || operand.Property == "*" ? operand.Property! : $"{operand.Table}.{operand.Property}")
                    : Literal(operand.Constant!);

        return operand.Function is null
            ? text
            : $"{operand.Function}({(operand.Distinct ? "DISTINCT " : string.Empty)}{text})";
    }

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// An expression in T-SQL's spelling (decision 107): the operators as written, <c>+</c>
    /// for a concatenation as for an addition, the functions of the vocabulary under their
    /// T-SQL names, and a searched CASE; since decision 113 the date functions with their unit
    /// as the keyword T-SQL takes, a conversion into the type of the dialect, a ranking
    /// function over its window and STRING_AGG with its ordering.
    /// </summary>
    private string Expression(QueryExpression expression)
    {
        if (expression.IsWindow)
        {
            var partitions = expression.Partitions!.Count == 0
                ? string.Empty
                : $"PARTITION BY {string.Join(", ", expression.Partitions.Select(BuildOperand))} ";
            var function = expression.Ranking switch
            {
                RankingFunction.RowNumber => "ROW_NUMBER",
                RankingFunction.Rank => "RANK",
                RankingFunction.DenseRank => "DENSE_RANK",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Ranking, null),
            };

            return $"{function}() OVER ({partitions}ORDER BY {Ordered(expression.Ordering!)})";
        }

        if (expression.IsListAggregate)
        {
            var within = expression.Ordering!.Count == 0 ? string.Empty : $" WITHIN GROUP (ORDER BY {Ordered(expression.Ordering)})";
            return $"STRING_AGG({BuildOperand(expression.Listed!)}, '{expression.Separator!.Replace("'", "''")}'){within}";
        }

        if (expression.IsBinary)
        {
            var symbol = expression.Operator!.Value switch
            {
                ExpressionOperator.Concat or ExpressionOperator.Add => "+",
                ExpressionOperator.Subtract => "-",
                ExpressionOperator.Multiply => "*",
                ExpressionOperator.Divide => "/",
                ExpressionOperator.Modulo => "%",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Operator, null),
            };

            return $"{Side(expression.Left!, expression, rightSide: false)} {symbol} {Side(expression.Right!, expression, rightSide: true)}";
        }

        if (expression.IsCall)
        {
            var arguments = expression.Arguments!.Select(BuildOperand).ToList();
            return expression.Function!.Value switch
            {
                QueryFunction.Upper => $"UPPER({arguments[0]})",
                QueryFunction.Lower => $"LOWER({arguments[0]})",
                QueryFunction.Trim => $"TRIM({arguments[0]})",
                QueryFunction.Substring => $"SUBSTRING({arguments[0]}, {arguments[1]}, {arguments[2]})",
                QueryFunction.Length => $"LEN({arguments[0]})",
                QueryFunction.Coalesce => $"COALESCE({string.Join(", ", arguments)})",
                QueryFunction.Abs => $"ABS({arguments[0]})",
                QueryFunction.Year => $"YEAR({arguments[0]})",
                QueryFunction.Month => $"MONTH({arguments[0]})",
                QueryFunction.Day => $"DAY({arguments[0]})",
                QueryFunction.CurrentTimestamp => "CURRENT_TIMESTAMP",
                QueryFunction.EscapePattern => ExpressionSpelling.EscapePattern(arguments[0], patternEscape ?? "!", "REPLACE"),
                QueryFunction.DateAdd => $"DATEADD({DatePart(expression.Unit!.Value)}, {arguments[0]}, {arguments[1]})",
                QueryFunction.DateDiff => $"DATEDIFF({DatePart(expression.Unit!.Value)}, {arguments[0]}, {arguments[1]})",
                QueryFunction.Round => $"ROUND({arguments[0]}, {arguments[1]})",
                QueryFunction.Sqrt => $"SQRT({arguments[0]})",
                QueryFunction.Cast => $"CAST({arguments[0]} AS {CastType(expression.CastTo!.Value)})",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Function, null),
            };
        }

        var branches = string.Join(" ", expression.Branches!.Select(b => $"WHEN {b.When.Accept(this)} THEN {BuildOperand(b.Then)}"));
        var otherwise = expression.Else is null ? string.Empty : $" ELSE {BuildOperand(expression.Else)}";
        return $"CASE {branches}{otherwise} END";
    }

    /// <summary>The keys of a window or of a list aggregate, each with its direction (decision 113).</summary>
    private string Ordered(IReadOnlyList<OrderingKey> ordering)
        => string.Join(", ", ordering.Select(key => $"{BuildOperand(key.Operand)} {(key.Ascending ? "ASC" : "DESC")}"));

    /// <summary>The datepart keyword of T-SQL for a unit of the vocabulary (decision 113).</summary>
    public static string DatePart(DateUnit unit) => unit switch
    {
        DateUnit.Year => "year",
        DateUnit.Month => "month",
        DateUnit.Day => "day",
        DateUnit.Hour => "hour",
        DateUnit.Minute => "minute",
        DateUnit.Second => "second",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>
    /// The type of the dialect a conversion writes (decision 113): the names of decision 086
    /// for the scalar, without a length or a precision that could change the value - text as
    /// <c>NVARCHAR(MAX)</c>, the floating-point numbers as <c>REAL</c> and <c>FLOAT</c>. The
    /// reader takes exactly these back.
    /// </summary>
    public static string CastType(ScalarType scalar) => scalar switch
    {
        ScalarType.Int => "INT",
        ScalarType.Long => "BIGINT",
        ScalarType.Float => "REAL",
        ScalarType.Double => "FLOAT",
        ScalarType.String => "NVARCHAR(MAX)",
        _ => throw new ArgumentOutOfRangeException(nameof(scalar), scalar, null),
    };

    /// <summary>
    /// One side of a binary expression, parenthesized where the grammar would regroup it
    /// otherwise: a nested operation of lower precedence, or one of the same precedence on
    /// the right, where <c>a - (b - c)</c> is not <c>a - b - c</c>.
    /// </summary>
    private string Side(QueryOperand side, QueryExpression parent, bool rightSide)
    {
        var text = BuildOperand(side);
        return ExpressionSpelling.NeedsParentheses(side, parent, rightSide) ? $"({text})" : text;
    }

    /// <summary>
    /// Writes a constant the way T-SQL wants it (decision 024). The model carries the value
    /// undecorated, so quoting is decided here from the scalar type rather than guessed from
    /// the shape of the text. A value whose type nobody recognized goes out verbatim - that
    /// is what the parser already reported as a gap.
    /// </summary>
    private static string Literal(QueryConstant constant) => constant.Type switch
    {
        null => constant.Text,
        // The temporal scalars are quoted like DateTime: T-SQL reads a date, a time, a
        // datetimeoffset and a time-typed interval from a string literal (decision 071).
        // A byte array is not - a 0x… literal is already the SQL spelling.
        ScalarType.String or ScalarType.Char or ScalarType.Guid or ScalarType.DateTime
            or ScalarType.Date or ScalarType.TimeOfDay or ScalarType.DateTimeOffset or ScalarType.Duration
            => $"'{constant.Text.Replace("'", "''")}'",
        ScalarType.Bool => string.Equals(constant.Text, "true", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
        _ => constant.Text,
    };

    public string Visit(SetOperationInstruction instr)
    {
        switch (instr.OperationType)
        {
            case SetOperationType.Union: return "UNION";
            case SetOperationType.UnionAll: return "UNION ALL";
            case SetOperationType.Intersect: return "INTERSECT";
            case SetOperationType.Except: return "EXCEPT";
            default:
                // ExceptAll has no T-SQL keyword; writing EXCEPT for it would silently
                // deduplicate rows the source kept (decision 053, the same trap
                // architecture.md §4.4 names for the EF Core side).
                report(
                    ConversionRecordKind.Failure,
                    $"The set operation {instr.OperationType} has no SQL form; the query was not generated.",
                    QueryFeature.SetOperation);
                return string.Empty;
        }
    }
}
