using Model.AbstractRepresentation.Enums;
using Model.Exceptions;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using Model.QueryInstructions.Enums;

namespace DapperWrappers;

public class DapperSQLQueryVisitor : IQueryVisitor
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
            throw new QueryBuilderException($"Operator {cond.Operator} needs a right operand.");
        }

        string right = BuildOperand(cond.Right);
        return $"{left} {MapOperator(cond.Operator)} {right}";
    }

    public string Visit(LogicalCondition cond)
    {
        if (cond.Operands.Count == 0)
        {
            throw new QueryBuilderException("LogicalCondition must have at least one operand.");
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

    private static string MapOperator(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "<>",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.LessThanOrEqual => "<=",
        ComparisonOperator.Like => "LIKE",
        ComparisonOperator.In => "IN",
        _ => throw new QueryBuilderException($"Unsupported ComparisonOperator: {op}")
    };

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
    /// the shape of the text. A value whose type nobody recognised goes out verbatim - that
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
        return instr.OperationType switch
        {
            SetOperationType.Union => "UNION",
            SetOperationType.UnionAll => "UNION ALL",
            SetOperationType.Intersect => "INTERSECT",
            SetOperationType.Except => "EXCEPT",
            _ => throw new QueryBuilderException($"Unsupported SetOperationType: {instr.OperationType}")
        };
    }
}
