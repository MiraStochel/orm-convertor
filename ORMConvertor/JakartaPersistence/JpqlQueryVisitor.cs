using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace JakartaPersistence;

/// <summary>
/// Writes query instructions as JPQL (decision 077). Shaped like the HQL visitor of
/// NHibernate - JPQL names entities and attributes rather than tables and columns, so
/// every reference goes through the mapping IR - and standard where JPQL 3.2 has a form:
/// temporal literals in the JDBC escape syntax the specification prescribes, count over
/// the alias instead of count(*). The entity join with on is the one form outside the
/// standard, which both implementations add (decision 077).
/// </summary>
/// <param name="typing">The typed view of the query's expressions the builder's gate filled (decision 107): which <c>+</c> stands over a string, because JPQL spells a concatenation with a word of its own.</param>
public sealed class JpqlQueryVisitor(
    Dictionary<string, EntityMap> entities,
    string sourceAlias,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery,
    ExpressionTyping typing) : IQueryVisitor
{
    /// <summary>The escape character of the like whose pattern is being written (decision 107); null outside a pattern.</summary>
    private string? patternEscape;

    public string Visit(FromInstruction instr) => instr.Alias ?? instr.Table;

    public string Visit(ProjectInstruction instr)
    {
        var value = Operand(instr.Operand);
        return instr.Alias is null ? value : $"{value} as {instr.Alias}";
    }

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    public string Visit(GroupByInstruction instr) => Column(instr.Table, instr.Attribute, null);

    public string Visit(OrderByInstruction instr)
        => $"{Operand(instr.Operand)} {(instr.Asc ? "asc" : "desc")}";

    public string Visit(JoinInstruction instr)
    {
        var keyword = instr.Kind switch
        {
            JoinKind.Inner => "join",
            JoinKind.Left => "left join",
            JoinKind.Right => "right join",
            JoinKind.Full => "full join",
            _ => throw new ArgumentOutOfRangeException(nameof(instr), instr.Kind, null),
        };

        // A table no entity maps to takes the name the one naming convention derives
        // (decision 050), as the source step does for the from clause.
        var entity = EntityName(instr.RightTableAlias ?? instr.RightTable) ?? EntityTableNaming.EntityNameFor(instr.RightTable);
        var alias = instr.RightTableAlias ?? entity.ToLowerInvariant();

        // A condition of several conjuncts goes in parentheses: EclipseLink 5.0 reads an
        // unparenthesized `on a = b and c = d join …` on to the next clause instead of
        // stopping at the next join and refuses it as "the right expression is not a valid
        // expression", whereas the parenthesized form is the same JPQL expression and
        // Hibernate reads it as well. Found by the Java suite over the deeply nested query.
        var condition = instr.OnCondition is LogicalCondition
            ? $"({instr.OnCondition.Accept(this)})"
            : instr.OnCondition.Accept(this);

        return $"{keyword} {entity} {alias} on {condition}";
    }

    public string Visit(SetOperationInstruction instr) => string.Empty; // composed by the builder

    public string Visit(ComparisonCondition cond)
    {
        if (cond.Operator == ComparisonOperator.Exists)
        {
            var sub = renderSubQuery(cond.Left.SubQuery!, cond.Operator);
            return sub is null ? string.Empty : $"exists ({sub})";
        }

        if (cond.Left.IsSubQuery || cond.Right?.IsSubQuery == true)
        {
            var left = OperandOrSubQuery(cond.Left, cond.Operator);
            var right = OperandOrSubQuery(cond.Right!, cond.Operator);
            return left is null || right is null ? string.Empty : $"{left} {Operator(cond.Operator)} {right}";
        }

        var operand = Operand(cond.Left);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{operand} is null";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{operand} is not null";
        }

        if (cond.Right is null)
        {
            report(ConversionRecordKind.Failure, $"Operator {cond.Operator} has no right operand; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        patternEscape = cond.Operator == ComparisonOperator.Like ? cond.Escape : null;
        var rightText = Operand(cond.Right);
        patternEscape = null;

        return $"{operand} {Operator(cond.Operator)} {rightText}{Escape(cond)}";
    }

    /// <summary>The escape clause of a like (decision 102); the template holds it to like and to one character.</summary>
    private static string Escape(ComparisonCondition cond)
        => cond.Escape is null ? string.Empty : $" escape '{cond.Escape.Replace("'", "''")}'";

    private string? OperandOrSubQuery(QueryOperand operand, ComparisonOperator op)
    {
        if (!operand.IsSubQuery)
        {
            return Operand(operand);
        }

        var sub = renderSubQuery(operand.SubQuery!, op);
        return sub is null ? null : $"({sub})";
    }

    public string Visit(LogicalCondition cond)
    {
        var keyword = cond.Operator == LogicalOperator.And ? "and" : "or";
        var parts = cond.Operands.Select(operand =>
            operand is LogicalCondition ? $"({operand.Accept(this)})" : operand.Accept(this));

        return string.Join($" {keyword} ", parts);
    }

    public string Visit(NotCondition cond) => $"not ({cond.Operand.Accept(this)})";

    private string Operator(ComparisonOperator op)
    {
        switch (op)
        {
            case ComparisonOperator.Equal: return "=";
            case ComparisonOperator.NotEqual: return "<>";
            case ComparisonOperator.GreaterThan: return ">";
            case ComparisonOperator.GreaterThanOrEqual: return ">=";
            case ComparisonOperator.LessThan: return "<";
            case ComparisonOperator.LessThanOrEqual: return "<=";
            case ComparisonOperator.Like: return "like";
            case ComparisonOperator.In: return "in";
            default:
                report(ConversionRecordKind.Failure, $"Operator {op} has no JPQL form; the query was not generated.", QueryFeature.Filtering);
                return string.Empty;
        }
    }

    private string Operand(QueryOperand operand)
    {
        if (operand.IsValueList)
        {
            return $"({string.Join(", ", operand.Values!.Select(Operand))})";
        }

        if (operand.IsParameter)
        {
            return Parameter(operand.Parameter!, operand.Function);
        }

        if (operand.IsConstant)
        {
            return Wrap(Literal(operand.Constant!), operand.Function);
        }

        // A subquery in a scalar position - a leaf of an expression (decision 107).
        if (operand.IsSubQuery)
        {
            var sub = renderSubQuery(operand.SubQuery!, ComparisonOperator.Equal);
            return sub is null ? string.Empty : $"({sub})";
        }

        if (operand.IsExpression)
        {
            return Wrap(Expression(operand.Expression!), operand.Function, operand.Distinct);
        }

        return Column(operand.Table, operand.Property!, operand.Function, operand.Distinct);
    }

    /// <summary>
    /// A parameter in JPQL (decision 083). The one target language with a positional form,
    /// so a positional parameter keeps its order here instead of being renamed. A collection
    /// parameter is written without parentheses: the grammar of Jakarta Persistence 3.2
    /// puts a collection-valued input parameter in IN's place itself, and parentheses there
    /// would make it one item of a list.
    /// </summary>
    private static string Parameter(QueryParameter parameter, string? function)
    {
        var placeholder = parameter.IsPositional
            ? $"?{parameter.Position}"
            : $":{parameter.Name}";

        return parameter.IsCollection ? placeholder : Wrap(placeholder, function);
    }

    private static string Wrap(string value, string? function, bool distinct = false)
        => function is null ? value : $"{function.ToLowerInvariant()}({(distinct ? "distinct " : string.Empty)}{value})";

    /// <param name="distinct">Whether the aggregate ranges over the distinct values of the column (decision 102).</param>
    private string Column(string? alias, string attribute, string? function, bool distinct = false)
    {
        // count(*) is not JPQL; the standard counts the identification variable.
        if (function is not null && attribute == "*")
        {
            return $"{function.ToLowerInvariant()}({alias ?? sourceAlias})";
        }

        var path = alias is null ? Property(null, attribute) : $"{alias}.{Property(alias, attribute)}";
        return Wrap(path, function, distinct);
    }

    public string Property(string? alias, string column)
    {
        var map = alias is not null && entities.TryGetValue(alias, out var found) ? found : null;
        return map?.PropertyMaps
                   .FirstOrDefault(p => string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase))
                   ?.Property.Name
               ?? column;
    }

    public string? EntityName(string alias)
        => entities.TryGetValue(alias, out var map) ? map.Entity.Name : null;

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// An expression in the spelling of Jakarta Persistence 3.2 (decision 107):
    /// <c>concat(a, b, …)</c> for a concatenation, flattened; the arithmetic operators and
    /// <c>mod(a, b)</c>; the functions of the vocabulary in lower case, the parts of a date
    /// through the standard <c>extract</c> - not Hibernate's <c>year()</c>, so that EclipseLink
    /// reads it too -, <c>current_timestamp</c> without parentheses; and a searched case,
    /// whose else the grammar demands, so a CASE without one writes <c>else null</c>.
    /// </summary>
    private string Expression(QueryExpression expression)
    {
        if (expression.IsBinary)
        {
            if (typing.IsConcatenation(expression))
            {
                return $"concat({string.Join(", ", Concatenated(expression).Select(Operand))})";
            }

            if (expression.Operator == ExpressionOperator.Modulo)
            {
                return $"mod({Operand(expression.Left!)}, {Operand(expression.Right!)})";
            }

            var symbol = expression.Operator!.Value switch
            {
                ExpressionOperator.Add => "+",
                ExpressionOperator.Subtract => "-",
                ExpressionOperator.Multiply => "*",
                ExpressionOperator.Divide => "/",
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Operator, null),
            };

            return $"{Side(expression.Left!, expression, rightSide: false)} {symbol} {Side(expression.Right!, expression, rightSide: true)}";
        }

        if (expression.IsCall)
        {
            var arguments = expression.Arguments!.Select(Operand).ToList();
            return expression.Function!.Value switch
            {
                QueryFunction.Upper => $"upper({arguments[0]})",
                QueryFunction.Lower => $"lower({arguments[0]})",
                QueryFunction.Trim => $"trim({arguments[0]})",
                QueryFunction.Substring => $"substring({arguments[0]}, {arguments[1]}, {arguments[2]})",
                QueryFunction.Length => $"length({arguments[0]})",
                QueryFunction.Coalesce => $"coalesce({string.Join(", ", arguments)})",
                QueryFunction.Abs => $"abs({arguments[0]})",
                QueryFunction.Year => $"extract(year from {arguments[0]})",
                QueryFunction.Month => $"extract(month from {arguments[0]})",
                QueryFunction.Day => $"extract(day from {arguments[0]})",
                QueryFunction.CurrentTimestamp => "current_timestamp",
                QueryFunction.EscapePattern => ExpressionSpelling.EscapePattern(arguments[0], patternEscape ?? "!", "replace"),
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Function, null),
            };
        }

        var branches = string.Join(" ", expression.Branches!.Select(b => $"when {b.When.Accept(this)} then {Operand(b.Then)}"));
        var otherwise = expression.Else is null ? "null" : Operand(expression.Else);
        return $"case {branches} else {otherwise} end";
    }

    /// <summary>The operands of a concatenation, its nested concatenations flattened into one argument list.</summary>
    private IEnumerable<QueryOperand> Concatenated(QueryExpression concatenation)
    {
        foreach (var side in new[] { concatenation.Left!, concatenation.Right! })
        {
            if (side is { IsExpression: true, IsAggregate: false } && side.Expression!.IsBinary && typing.IsConcatenation(side.Expression))
            {
                foreach (var inner in Concatenated(side.Expression))
                {
                    yield return inner;
                }
            }
            else
            {
                yield return side;
            }
        }
    }

    private string Side(QueryOperand side, QueryExpression parent, bool rightSide)
    {
        var text = Operand(side);
        return ExpressionSpelling.NeedsParentheses(side, parent, rightSide) ? $"({text})" : text;
    }

    /// <summary>
    /// A constant the way JPQL wants it (decision 024): strings quoted, numbers bare, and
    /// the temporal families in the JDBC escape syntax the specification names for date,
    /// time and timestamp literals (Jakarta Persistence 3.2 §4.6.1).
    /// </summary>
    public static string Literal(QueryConstant constant) => constant.Type switch
    {
        ScalarType.String or ScalarType.Char or ScalarType.Guid => $"'{constant.Text.Replace("'", "''")}'",
        ScalarType.Date => $"{{d '{constant.Text}'}}",
        ScalarType.TimeOfDay => $"{{t '{constant.Text}'}}",
        ScalarType.DateTime => $"{{ts '{constant.Text}'}}",
        ScalarType.DateTimeOffset or ScalarType.Duration => $"'{constant.Text}'",
        ScalarType.Bool => constant.Text.ToLowerInvariant(),
        _ => constant.Text,
    };
}
