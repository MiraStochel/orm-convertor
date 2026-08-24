using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Model.AbstractRepresentation.Enums;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace DapperWrappers;

/// <summary>
/// Writes query instructions as SQL (decision 022). Carries the same report channel as the
/// other two visitors: a shape the target cannot render is a record, not an exception -
/// exceptions stay reserved for errors of the program, and a condition tree a foreign
/// parser produced is not one (decisions 010 and 053).
/// </summary>
public class DapperSqlQueryVisitor(Action<ConversionRecordKind, string, QueryFeature?> report) : IQueryVisitor
{
    public string Visit(FromInstruction instr)
    {
        var alias = instr.Alias is null ? string.Empty : $" AS {instr.Alias}";
        return $"{instr.Table}{alias}";
    }

    public string Visit(ProjectInstruction instr)
    {
        string value;
        if (instr.Function != null)
        {
            value = $"{instr.Function}({instr.Table}.{instr.Attribute})";
        }
        else
        {
            value = $"{instr.Table}.{instr.Attribute}";
        }

        var alias = instr.Alias is null ? string.Empty : $" AS {instr.Alias}";
        return $"{value}{alias}";
    }

    public string Visit(SelectInstruction instr) => instr.Condition.Accept(this);

    public string Visit(HavingInstruction instr) => instr.Condition.Accept(this);

    public string Visit(ComparisonCondition cond)
    {
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

        string right = BuildOperand(cond.Right);
        return $"{left} {MapOperator(cond.Operator)} {right}";
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

    public string Visit(OrderByInstruction instr)
    {
        string column = instr.Table != null
            ? $"{instr.Table}.{instr.Attribute}"
            : instr.Attribute;
        string direction = instr.Asc ? "ASC" : "DESC";
        return $"{column} {direction}";
    }

    public string Visit(GroupByInstruction instr)
    {
        return $"{instr.Table}.{instr.Attribute}";
    }

    private static string BuildOperand(QueryOperand operand)
    {
        var text = operand.IsColumn
            ? (operand.Table is null ? operand.Property! : $"{operand.Table}.{operand.Property}")
            : Literal(operand.Constant!);

        return operand.Function is null ? text : $"{operand.Function}({text})";
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
        ScalarType.String or ScalarType.Char or ScalarType.Guid or ScalarType.DateTime
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
