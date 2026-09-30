using Common.Naming;
using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace NHibernateWrappers;

/// <summary>
/// Writes query instructions as HQL (decision 022). HQL is shaped like SQL, so unlike the
/// LINQ visitor this one needs no lexical scope — but it names entities and properties
/// rather than tables and columns, so every reference goes through the mapping IR.
/// </summary>
/// <param name="typing">The typed view of the query's expressions the builder's gate filled (decision 107): which <c>+</c> stands over a string, because HQL spells a concatenation with a word of its own.</param>
public sealed class NHibernateHqlQueryVisitor(
    Dictionary<string, EntityMap> entities,
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
            JoinKind.Inner => "inner join",
            JoinKind.Left => "left join",
            JoinKind.Right => "right join",
            _ => null,
        };

        if (keyword is null)
        {
            // An inner join in its place would return fewer rows than the source's full
            // outer join, and HQL 5.7.0 has no set operation to compose a faithful one
            // from the way the EF Core builder does (decision 065).
            report(
                ConversionRecordKind.Failure,
                "HQL in NHibernate 5.7.0 has no full outer join and no set operation to compose one from; no artifact was generated.",
                QueryFeature.JoinKind);
            return string.Empty;
        }

        // A table no entity maps to takes the name the one naming convention derives
        // (decision 050), as the source step does for the from clause and as the JPQL
        // visitor does here; the bare table name used to stand in its place, which named an
        // entity no mapping declares.
        var entity = EntityName(instr.RightTableAlias ?? instr.RightTable) ?? EntityTableNaming.EntityNameFor(instr.RightTable);
        var alias = instr.RightTableAlias ?? Bare(instr.RightTable).ToLowerInvariant();

        // NHibernate 5 supports entity joins, where the predicate is given with `with`
        // rather than being implied by an association.
        return $"{keyword} {entity} {alias} with {instr.OnCondition.Accept(this)}";
    }

    public string Visit(SetOperationInstruction instr)
    {
        report(
            ConversionRecordKind.Loss,
            "HQL in NHibernate 5.7.0 has no set operations; the query was not generated.",
            QueryFeature.SetOperation);
        return string.Empty;
    }

    public string Visit(ComparisonCondition cond)
    {
        // EXISTS carries its subquery as the left operand, the way IS NULL carries its
        // column (decisions 002 and 061). The nested scope is the builder's to render;
        // null means it refused, and the refusal is already on the channel.
        if (cond.Operator == ComparisonOperator.Exists)
        {
            var sub = renderSubQuery(cond.Left.SubQuery!, cond.Operator);
            return sub is null ? string.Empty : $"exists ({sub})";
        }

        if (cond.Left.IsSubQuery || cond.Right?.IsSubQuery == true)
        {
            return SubQueryComparison(cond);
        }

        var left = Operand(cond.Left);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{left} is null";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{left} is not null";
        }

        if (cond.Right is null)
        {
            // Unreachable: the template refuses such a tree before any step runs
            // (decision 053). The "1 = 1" that used to stand here was not a loss but a
            // different query - it returns every row the source filtered out.
            report(ConversionRecordKind.Failure, $"Operator {cond.Operator} has no right operand; the query was not generated.", QueryFeature.Filtering);
            return string.Empty;
        }

        patternEscape = cond.Operator == ComparisonOperator.Like ? cond.Escape : null;
        var right = Operand(cond.Right);
        patternEscape = null;

        return $"{left} {Operator(cond.Operator)} {right}{Escape(cond)}";
    }

    /// <summary>The escape clause of a like (decision 102); the template holds it to like and to one character.</summary>
    private static string Escape(ComparisonCondition cond)
        => cond.Escape is null ? string.Empty : $" escape '{cond.Escape.Replace("'", "''")}'";

    /// <summary>
    /// A comparison one of whose sides is a subquery (decision 061): IN and the scalar
    /// operators alike write the nested select in parentheses in the operand's place.
    /// </summary>
    private string SubQueryComparison(ComparisonCondition cond)
    {
        var left = OperandOrSubQuery(cond.Left, cond.Operator);
        var right = OperandOrSubQuery(cond.Right!, cond.Operator);
        if (left is null || right is null)
        {
            return string.Empty;
        }

        return $"{left} {Operator(cond.Operator)} {right}";
    }

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
            operand is LogicalCondition
                ? $"({operand.Accept(this)})"
                : operand.Accept(this));

        return string.Join($" {keyword} ", parts);
    }

    public string Visit(NotCondition cond) => $"not ({cond.Operand.Accept(this)})";

    /// <summary>
    /// The comparison operators HQL spells, exhaustively. No catch-all branch: it used to
    /// answer "in" to anything unmapped, so a value the target has no form for would come
    /// out as a different operator instead of as a refusal (decision 053).
    /// </summary>
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
                report(ConversionRecordKind.Failure, $"Operator {op} has no HQL form; the query was not generated.", QueryFeature.Filtering);
                return string.Empty;
        }
    }

    private string Operand(QueryOperand operand)
    {
        // The values IN enumerates (decision 074), each spelled as a lone constant is,
        // a parameter among them (decision 102) as a lone parameter is.
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
    /// A parameter in HQL (decision 083): always the named form, because HQL's positional
    /// one is a bare ? whose order the text decides, and a query rewritten into another
    /// clause order would bind different values. A collection parameter is parenthesized,
    /// which is the only shape NHibernate's grammar takes after IN.
    /// </summary>
    private static string Parameter(QueryParameter parameter, string? function)
    {
        var placeholder = $":{QueryParameterNaming.IdentifierFor(parameter)}";
        return parameter.IsCollection ? $"({placeholder})" : Wrap(placeholder, function);
    }

    private static string Wrap(string value, string? function, bool distinct = false)
        => function is null ? value : $"{function.ToLowerInvariant()}({(distinct ? "distinct " : string.Empty)}{value})";

    /// <param name="distinct">Whether the aggregate ranges over the distinct values of the column (decision 102).</param>
    private string Column(string? alias, string attribute, string? function, bool distinct = false)
    {
        // count(*) is the one aggregate whose argument is not a property.
        if (function is not null && attribute == "*")
        {
            return $"{function.ToLowerInvariant()}(*)";
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

    private static string Bare(string table) => table.Split('.').LastOrDefault() ?? table;

    /* ---- expressions (decision 107) --------------------------------------------------- */

    /// <summary>
    /// An expression in HQL's spelling (decision 107): <c>concat(a, b, …)</c> for a
    /// concatenation - the nesting the model carries flattened, which is the target's
    /// spelling and not the model -, the arithmetic operators as written and <c>mod(a, b)</c>
    /// for the modulo, the functions of the vocabulary in lower case, <c>current_timestamp()</c>
    /// with its parentheses, and a searched case.
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

            var (left, right) = (expression.Left!, expression.Right!);
            var text = $"{Side(left, expression, rightSide: false)} {symbol} {Side(right, expression, rightSide: true)}";

            // NHibernate 5.7.0 does not type an arithmetic operation over a decimal and a
            // whole number as a decimal: `Quantity * UnitPrice` comes back as an int with the
            // fraction cut off, whichever side the decimal stands on - a different value, not
            // a poorer one (decision 053). Found by the fourth level over the arithmetic row.
            // The value is cast to the decimal the gate typed it as, which is the target's
            // spelling of the type the other five targets give it anyway, and said in a record.
            if (typing.ScalarOf(expression) == ScalarType.Decimal
                && (IsWholeNumber(LeafScalar(left)) || IsWholeNumber(LeafScalar(right))))
            {
                report(
                    ConversionRecordKind.Convention,
                    $"HQL in NHibernate 5.7.0 does not type '{expression}' over a decimal and a whole number as a decimal, so the value was cast to decimal, which keeps its fraction.",
                    QueryFeature.Expression);
                return $"cast({text} as decimal)";
            }

            return text;
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
                QueryFunction.Year => $"year({arguments[0]})",
                QueryFunction.Month => $"month({arguments[0]})",
                QueryFunction.Day => $"day({arguments[0]})",
                QueryFunction.CurrentTimestamp => "current_timestamp()",
                QueryFunction.EscapePattern => ExpressionSpelling.EscapePattern(arguments[0], patternEscape ?? "!", "replace"),
                _ => throw new ArgumentOutOfRangeException(nameof(expression), expression.Function, null),
            };
        }

        var branches = string.Join(" ", expression.Branches!.Select(b => $"when {b.When.Accept(this)} then {Operand(b.Then)}"));
        var otherwise = expression.Else is null ? string.Empty : $" else {Operand(expression.Else)}";
        return $"case {branches}{otherwise} end";
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
    /// The scalar of a leaf as this visitor can know it: a constant's own, a column's from the
    /// mapping behind its alias, an expression's from the typed view (decision 107); null
    /// for a parameter, a subquery or an aggregate, whose type NHibernate resolves itself.
    /// </summary>
    private ScalarType? LeafScalar(QueryOperand operand)
    {
        if (operand.IsAggregate)
        {
            return null;
        }

        if (operand.IsConstant)
        {
            return operand.Constant!.Type;
        }

        if (operand.IsExpression)
        {
            return typing.ScalarOf(operand.Expression!);
        }

        if (operand.IsColumn && operand.Table is not null && entities.TryGetValue(operand.Table, out var map))
        {
            return map.PropertyMaps
                .FirstOrDefault(p => string.Equals(p.ColumnName ?? p.Property.Name, operand.Property, StringComparison.OrdinalIgnoreCase))
                ?.Property.Type is { Category: LangTypeCategory.Scalar } type
                ? type.ScalarType
                : null;
        }

        return null;
    }

    private static bool IsWholeNumber(ScalarType? scalar)
        => scalar is ScalarType.Byte or ScalarType.Short or ScalarType.Int or ScalarType.Long;

    /// <summary>
    /// Writes a constant the way HQL wants it (decision 024). Strings and dates are quoted,
    /// numbers are not, and no suffix survives from whichever language the source was in.
    /// </summary>
    private static string Literal(QueryConstant constant) => constant.Type switch
    {
        ScalarType.String or ScalarType.Char or ScalarType.Guid
            => $"'{constant.Text.Replace("'", "''")}'",
        // HQL reads every temporal literal from a quoted string (decision 071).
        ScalarType.DateTime or ScalarType.Date or ScalarType.TimeOfDay
            or ScalarType.DateTimeOffset or ScalarType.Duration => $"'{constant.Text}'",
        ScalarType.Bool => constant.Text.ToLowerInvariant(),
        _ => constant.Text,
    };
}
