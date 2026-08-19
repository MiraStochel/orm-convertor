using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace EFCoreWrappers;

/// <summary>
/// The lexical scope a LINQ chain is being written in. A LINQ lambda names one parameter,
/// and what that parameter holds changes as the chain grows: the source row at first, a
/// transparent tuple after a join, a grouping after GroupBy. Rendering a column therefore
/// needs this state, which is why the LINQ visitor carries it and the SQL one does not.
/// </summary>
public sealed class LinqScope
{
    /// <summary>Name of the current lambda parameter.</summary>
    public string Param { get; set; } = "c";

    /// <summary>True once a join has made the parameter hold a tuple of rows.</summary>
    public bool Composite { get; set; }

    public bool Grouped { get; set; }

    public IReadOnlyList<GroupByInstruction> GroupKeys { get; set; } = [];

    /// <summary>Parameter used inside an aggregate lambda, which ranges over group elements.</summary>
    public string ElementParam { get; set; } = "e";

    public Dictionary<string, EntityMap> Entities { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string Row(string? alias) => Composite && alias is not null ? $"{Param}.{alias}" : Param;

    public string ElementRow(string? alias) => Composite && alias is not null ? $"{ElementParam}.{alias}" : ElementParam;
}

/// <summary>
/// Writes query instructions as LINQ (decision 022). Unlike the SQL visitor this one is
/// stateful: it reads <see cref="LinqScope"/> to know what the current lambda parameter
/// stands for.
/// </summary>
public sealed class EFCoreLinqQueryVisitor(LinqScope scope, Action<ConversionRecordKind, string, QueryFeature?> report)
    : IQueryVisitor
{
    public LinqScope Scope { get; } = scope;

    public string Visit(FromInstruction instr) => Scope.Param;

    public string Visit(ProjectInstruction instr)
        => Column(instr.Table, instr.Attribute, instr.Function);

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    public string Visit(GroupByInstruction instr) => Column(instr.Table, instr.Attribute, null);

    public string Visit(OrderByInstruction instr) => Column(instr.Table, instr.Attribute, null);

    public string Visit(JoinInstruction instr) => instr.OnCondition.Accept(this);

    public string Visit(SetOperationInstruction instr) => instr.OperationType switch
    {
        SetOperationType.Union => "Union",
        SetOperationType.UnionAll => "Concat",
        SetOperationType.Intersect => "Intersect",
        _ => "Except",
    };

    public string Visit(ComparisonCondition cond)
    {
        var left = Operand(cond.Left);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{left} == null";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{left} != null";
        }

        if (cond.Right is null)
        {
            report(ConversionRecordKind.Loss, $"Operator {cond.Operator} has no right operand; the comparison was dropped.", QueryFeature.Filtering);
            return "true";
        }

        // LINQ has no LIKE or IN operator; the nearest expressions are method calls, and
        // they read differently enough to be worth reporting rather than pretending.
        if (cond.Operator == ComparisonOperator.Like)
        {
            report(ConversionRecordKind.Convention, "A LIKE comparison was written as string.Contains, which anchors differently.", QueryFeature.Filtering);
            return $"{left}.Contains({Operand(cond.Right)})";
        }

        if (cond.Operator == ComparisonOperator.In)
        {
            report(ConversionRecordKind.Loss, "An IN comparison has no LINQ form the query representation can carry; it was dropped.", QueryFeature.Filtering);
            return "true";
        }

        return $"{left} {Operator(cond.Operator)} {Operand(cond.Right)}";
    }

    public string Visit(LogicalCondition cond)
    {
        var keyword = cond.Operator == LogicalOperator.And ? "&&" : "||";

        var parts = cond.Operands.Select(operand =>
            operand is LogicalCondition
                ? $"({operand.Accept(this)})"
                : operand.Accept(this));

        return string.Join($" {keyword} ", parts);
    }

    public string Visit(NotCondition cond) => $"!({cond.Operand.Accept(this)})";

    private static string Operator(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "==",
        ComparisonOperator.NotEqual => "!=",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        ComparisonOperator.LessThan => "<",
        _ => "<=",
    };

    public string Operand(QueryOperand operand)
        => operand.IsConstant ? Literal(operand.Constant!) : Column(operand.Table, operand.Property!, operand.Function);

    /// <summary>
    /// Renders a column reference in the current scope: a plain member access, a group key,
    /// or an aggregate over the group's elements.
    /// </summary>
    public string Column(string? alias, string attribute, string? function)
    {
        if (function is not null)
        {
            return Aggregate(alias, attribute, function);
        }

        if (Scope.Grouped)
        {
            var key = GroupKeyPath(alias, attribute);
            if (key is not null)
            {
                return key;
            }

            report(
                ConversionRecordKind.Loss,
                $"Column {attribute} is neither a grouping key nor an aggregate, so it cannot be read after GroupBy; it was dropped.",
                QueryFeature.Projection);
            return $"{Scope.Param}.Key";
        }

        return $"{Scope.Row(alias)}.{Property(alias, attribute)}";
    }

    private string Aggregate(string? alias, string attribute, string function)
    {
        if (!Scope.Grouped)
        {
            report(
                ConversionRecordKind.Loss,
                $"{function} appears without a grouping, which LINQ cannot express inside a query; the aggregate was dropped.",
                QueryFeature.Aggregation);
            return $"{Scope.Row(alias)}.{Property(alias, attribute)}";
        }

        if (function == "COUNT")
        {
            if (attribute != "*")
            {
                report(
                    ConversionRecordKind.Convention,
                    $"COUNT({attribute}) was written as Count(), which counts rows rather than non-null values.",
                    QueryFeature.Aggregation);
            }

            return $"{Scope.Param}.Count()";
        }

        var method = function switch
        {
            "SUM" => "Sum",
            "MIN" => "Min",
            "MAX" => "Max",
            "AVG" => "Average",
            _ => null,
        };

        if (method is null)
        {
            report(
                ConversionRecordKind.Loss,
                $"Aggregate function {function} has no LINQ counterpart; it was dropped.",
                QueryFeature.Aggregation);
            return $"{Scope.Param}.Count()";
        }

        return $"{Scope.Param}.{method}({Scope.ElementParam} => {Scope.ElementRow(alias)}.{Property(alias, attribute)})";
    }

    /// <summary>
    /// The path to a grouping key, or null when the column is not one. A single key is
    /// reached through Key itself; a composite key through a member of it.
    /// </summary>
    private string? GroupKeyPath(string? alias, string attribute)
    {
        var index = -1;
        for (int i = 0; i < Scope.GroupKeys.Count; i++)
        {
            if (string.Equals(Scope.GroupKeys[i].Attribute, attribute, StringComparison.OrdinalIgnoreCase)
                && (alias is null || string.Equals(Scope.GroupKeys[i].Table, alias, StringComparison.OrdinalIgnoreCase)))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return null;
        }

        return Scope.GroupKeys.Count == 1
            ? $"{Scope.Param}.Key"
            : $"{Scope.Param}.Key.{Property(Scope.GroupKeys[index].Table, Scope.GroupKeys[index].Attribute)}";
    }

    public string Property(string? alias, string column)
    {
        var map = alias is not null && Scope.Entities.TryGetValue(alias, out var found) ? found : null;
        return map?.PropertyMaps
                   .FirstOrDefault(p => string.Equals(p.ColumnName ?? p.Property.Name, column, StringComparison.OrdinalIgnoreCase))
                   ?.Property.Name
               ?? column;
    }

    /// <summary>
    /// Writes a constant the way C# wants it (decision 024). The model carries the value
    /// undecorated, so the quoting and the numeric suffix are added here from the scalar
    /// type rather than carried over from whatever the source language wrote.
    /// </summary>
    private string Literal(QueryConstant constant) => constant.Type switch
    {
        ScalarType.String => $"\"{constant.Text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        ScalarType.Char => $"'{constant.Text}'",
        ScalarType.Bool => constant.Text.ToLowerInvariant(),
        ScalarType.Long => constant.Text + "L",
        ScalarType.Decimal => constant.Text + "m",
        ScalarType.Double => constant.Text + "d",
        ScalarType.Float => constant.Text + "f",
        ScalarType.DateTime => $"DateTime.Parse(\"{constant.Text}\")",
        ScalarType.Guid => $"Guid.Parse(\"{constant.Text}\")",
        _ => constant.Text,
    };
}
