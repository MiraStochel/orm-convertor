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
public sealed class NHibernateHqlQueryVisitor(
    Dictionary<string, EntityMap> entities,
    Action<ConversionRecordKind, string, QueryFeature?> report) : IQueryVisitor
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
            JoinKind.Inner => "inner join",
            JoinKind.Left => "left join",
            JoinKind.Right => "right join",
            _ => null,
        };

        if (keyword is null)
        {
            report(
                ConversionRecordKind.Loss,
                "HQL has no full outer join; an inner join was generated instead.",
                QueryFeature.JoinKind);
            keyword = "inner join";
        }

        var entity = EntityName(instr.RightTableAlias ?? instr.RightTable) ?? Bare(instr.RightTable);
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
            report(ConversionRecordKind.Loss, $"Operator {cond.Operator} has no right operand; the comparison was dropped.", QueryFeature.Filtering);
            return "1 = 1";
        }

        return $"{left} {Operator(cond.Operator)} {Operand(cond.Right)}";
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

    private static string Operator(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "<>",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.LessThanOrEqual => "<=",
        ComparisonOperator.Like => "like",
        _ => "in",
    };

    private string Operand(QueryOperand operand)
        => operand.IsConstant
            ? Wrap(Literal(operand.Constant!), operand.Function)
            : Column(operand.Table, operand.Property!, operand.Function);

    private static string Wrap(string value, string? function)
        => function is null ? value : $"{function.ToLowerInvariant()}({value})";

    private string Column(string? alias, string attribute, string? function)
    {
        // count(*) is the one aggregate whose argument is not a property.
        if (function is not null && attribute == "*")
        {
            return $"{function.ToLowerInvariant()}(*)";
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

    private static string Bare(string table) => table.Split('.').LastOrDefault() ?? table;

    /// <summary>
    /// Writes a constant the way HQL wants it (decision 024). Strings and dates are quoted,
    /// numbers are not, and no suffix survives from whichever language the source was in.
    /// </summary>
    private static string Literal(QueryConstant constant) => constant.Type switch
    {
        ScalarType.String or ScalarType.Char or ScalarType.Guid
            => $"'{constant.Text.Replace("'", "''")}'",
        ScalarType.DateTime => $"'{constant.Text}'",
        ScalarType.Bool => constant.Text.ToLowerInvariant(),
        _ => constant.Text,
    };
}
