using AbstractWrappers;
using Model;
using Model.Exceptions;
using Model.QueryInstructions;
using Model.QueryInstructions.Conditions;
using System.Text;

namespace DapperWrappers;

public class DapperSqlQueryBuilder : AbstractQueryBuilder
{
    private readonly IQueryVisitor visitor = new DapperSQLQueryVisitor();

    private string? sourceEntity;
    private string sourceName = "connection";

    public override List<ConversionSource> Build()
    {
        var firstInstruction = instructions[0];

        string query;

        if (firstInstruction is SetOperationInstruction setOp)
        {
            var left = BuildSelectQuery(setOp.Left);
            var right = BuildSelectQuery(setOp.Right);
            query = $"{left.TrimEnd()}\n\n{visitor.Visit(setOp)}\n\n{right}";
        }
        else if (firstInstruction is SubQueryInstruction subQuery)
        {
            query = BuildSelectQuery(subQuery);
        }
        else
        {
            throw new NotSupportedException();
        }

        query = query.Trim();
        var indent = new string(' ', 8);
        var indentedQuery = string.Join("\n", query.Split('\n').Select(line => indent + line));

        // Indentation fix :(
        var template =
@"public List<{0}> Query() 
{{
    return {2}.Query<{0}>(
        """"""
        {1}
        """""",    
    ).ToList();
}}
"; ;

        var finalMethod = string.Format(template.Trim(), sourceEntity, indentedQuery.TrimStart(), sourceName);
        return [
            new() {
                Content = finalMethod,
                ContentType = ConversionContentType.CSharpQuery
            }
        ];
    }

    private string BuildSelectQuery(SubQueryInstruction subQuery)
    {
        // Own builder for nesting
        StringBuilder sqlBuilder = new();

        var inst = subQuery.Instructions;

        BuildProjectionPart(inst, sqlBuilder);

        BuildFromPart(inst, sqlBuilder);

        BuildJoinPart(inst, sqlBuilder);

        BuildWherePart(inst, sqlBuilder);

        BuildGroupByPart(inst, sqlBuilder);

        BuildHavingPart(inst, sqlBuilder);

        BuildOrderByPart(inst, sqlBuilder);

        return sqlBuilder.ToString();
    }

    private void BuildProjectionPart(List<QueryInstruction> instructions, StringBuilder sqlBuilder)
    {
        sqlBuilder.Append("SELECT ");

        var projections = instructions.OfType<ProjectInstruction>().ToList();

        if (projections.Count == 0)
        {
            sqlBuilder.Append('*');
            sqlBuilder.AppendLine();
            return;
        }

        foreach (var projection in projections)
        {
            sqlBuilder.Append(projection.Accept(visitor) + ", ");
        }

        sqlBuilder.Remove(sqlBuilder.Length - 2, 2); // Remove trailing comma and space
        sqlBuilder.AppendLine();
    }

    private void BuildFromPart(List<QueryInstruction> instructions, StringBuilder sqlBuilder)
    {
        var fromInstructions = instructions.OfType<FromInstruction>().ToList();

        if (fromInstructions.Count != 1)
        {
            throw new QueryBuilderException("None or too many query sources.");
        }

        sourceEntity = fromInstructions.First().Table.Split('.').LastOrDefault();
        if (sourceEntity != null && sourceEntity.EndsWith('s'))
        {
            sourceEntity = sourceEntity[..^1];
        }

        sqlBuilder.Append("FROM ");
        sqlBuilder.Append(fromInstructions.First().Accept(visitor));
        sqlBuilder.AppendLine();
    }

    private void BuildJoinPart(List<QueryInstruction> instructions, StringBuilder sqlBuilder)
    {
        var joinInstructions = instructions.OfType<JoinInstruction>().ToList();

        foreach (var join in joinInstructions)
        {
            sqlBuilder.AppendLine(join.Accept(visitor));
        }
    }

    private void BuildWherePart(List<QueryInstruction> instructions, StringBuilder sqlBuilder)
    {
        var selectInstructions = instructions.OfType<SelectInstruction>().ToList();

        if (selectInstructions.Count == 0)
        {
            return;
        }

        // Multiple Where() calls are combined by conjunction (rule Q4 from the paper).
        var condition = selectInstructions.Count == 1
            ? selectInstructions[0].Condition
            : new LogicalCondition(LogicalOperator.And, selectInstructions.Select(s => s.Condition).ToList());

        sqlBuilder.Append("WHERE ");
        sqlBuilder.Append(condition.Accept(visitor));
        sqlBuilder.AppendLine();
    }

    private void BuildOrderByPart(List<QueryInstruction> instructions, StringBuilder sqlBuilder)
    {
        var orderByInstructions = instructions.OfType<OrderByInstruction>().ToList();

        if (orderByInstructions.Count == 0)
        {
            return;
        }

        sqlBuilder.Append("ORDER BY ");
        foreach (var orderBy in orderByInstructions)
        {
            sqlBuilder.Append(orderBy.Accept(visitor) + ", ");
        }
        sqlBuilder.Remove(sqlBuilder.Length - 2, 2); // Remove trailing comma and space
        sqlBuilder.AppendLine();
    }

    private void BuildGroupByPart(List<QueryInstruction> instructions, StringBuilder sqlBuilder)
    {
        var groupByInstructions = instructions.OfType<GroupByInstruction>().ToList();

        if (groupByInstructions.Count == 0)
        {
            return;
        }

        sqlBuilder.Append("GROUP BY ");
        foreach (var groupBy in groupByInstructions)
        {
            sqlBuilder.Append(groupBy.Accept(visitor) + ", ");
        }
        sqlBuilder.Remove(sqlBuilder.Length - 2, 2); // Remove trailing comma and space
        sqlBuilder.AppendLine();
    }

    private void BuildHavingPart(List<QueryInstruction> instructions, StringBuilder sqlBuilder)
    {
        var havingInstructions = instructions.OfType<HavingInstruction>().ToList();
        if (havingInstructions.Count == 0)
        {
            return;
        }

        var condition = havingInstructions.Count == 1
            ? havingInstructions[0].Condition
            : new LogicalCondition(LogicalOperator.And, havingInstructions.Select(h => h.Condition).ToList());

        sqlBuilder.Append("HAVING ");
        sqlBuilder.Append(condition.Accept(visitor));
        sqlBuilder.AppendLine();
    }
}