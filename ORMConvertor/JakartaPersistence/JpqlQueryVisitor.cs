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
public sealed class JpqlQueryVisitor(
    Dictionary<string, EntityMap> entities,
    string sourceAlias,
    Action<ConversionRecordKind, string, QueryFeature?> report,
    Func<SubQueryInstruction, ComparisonOperator, string?> renderSubQuery) : IQueryVisitor
{
    public string Visit(FromInstruction instr) => instr.Alias ?? instr.Table;

    public string Visit(ProjectInstruction instr)
    {
        var value = Column(instr.Table, instr.Attribute, instr.Function);
        return instr.Alias is null ? value : $"{value} as {instr.Alias}";
    }

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    public string Visit(GroupByInstruction instr) => Column(instr.Table, instr.Attribute, null);

    public string Visit(OrderByInstruction instr)
        => $"{Column(instr.Table, instr.Attribute, null)} {(instr.Asc ? "asc" : "desc")}";

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

        return $"{keyword} {entity} {alias} on {instr.OnCondition.Accept(this)}";
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

        return $"{operand} {Operator(cond.Operator)} {Operand(cond.Right)}";
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
        => operand.IsValueList
            ? $"({string.Join(", ", operand.Values!.Select(Literal))})"
            : operand.IsParameter
                ? Parameter(operand.Parameter!, operand.Function)
                : operand.IsConstant
                    ? Wrap(Literal(operand.Constant!), operand.Function)
                    : Column(operand.Table, operand.Property!, operand.Function);

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

    private static string Wrap(string value, string? function)
        => function is null ? value : $"{function.ToLowerInvariant()}({value})";

    private string Column(string? alias, string attribute, string? function)
    {
        // count(*) is not JPQL; the standard counts the identification variable.
        if (function is not null && attribute == "*")
        {
            return $"{function.ToLowerInvariant()}({alias ?? sourceAlias})";
        }

        var path = alias is null ? Property(null, attribute) : $"{alias}.{Property(alias, attribute)}";
        return Wrap(path, function);
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
