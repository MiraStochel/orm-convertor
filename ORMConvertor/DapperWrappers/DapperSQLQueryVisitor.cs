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
        string left = BuildOperand(cond.LeftTable, cond.LeftProperty, cond.LeftConstant, cond.LeftFunction);

        if (cond.Operator == ComparisonOperator.IsNull)
        {
            return $"{left} IS NULL";
        }

        if (cond.Operator == ComparisonOperator.IsNotNull)
        {
            return $"{left} IS NOT NULL";
        }

        string right = BuildOperand(cond.RightTable, cond.RightProperty, cond.RightConstant, cond.RightFunction);
        return $"{left} {MapOperator(cond.Operator)} {right}";
    }

    public string Visit(LogicalCondition cond)
    {
        if (cond.Operands.Count == 0)
        {
            throw new QueryBuilderException("LogicalCondition must have at least one operand.");
        }

        string keyword = cond.Operator == LogicalOperator.And ? "AND" : "OR";

        // Vnořený logický uzel se vždy obaluje závorkami, aby AND obsahující OR
        // (a naopak) nezměnil význam dotazu.
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

        return $"{joinType} {rightTable} ON {instr.LeftTable}.{instr.LeftProperty} = {(instr.RightTableAlias ?? instr.RightTable)}.{instr.RightProperty}";
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

    private static string BuildOperand(
        string? table,
        string? property,
        string? constant,
        string? function
    )
    {
        if (property != null)
        {
            string column = table != null ? $"{table}.{property}" : property;
            return function != null
                ? $"{function}({column})"
                : column;
        }
        else if (constant != null)
        {
            string value = constant.Replace('"', '\'');
            return function != null
                ? $"{function}({value})"
                : value;
        }
        else
        {
            throw new QueryBuilderException("Condition operand must be a table.column or a constant.");
        }
    }

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