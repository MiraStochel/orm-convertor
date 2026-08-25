using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Model;
using Model.QueryInstructions;

namespace DapperWrappers;

/// <summary>
/// Emits SQL, which is the only query form Dapper has (decision 022). Two artifacts leave
/// here: the runnable C# method and the bare SQL, so that consumers which want the query
/// itself do not have to dig it back out of the generated code (decision 025).
/// </summary>
public class DapperSqlQueryBuilder : AbstractQueryBuilder
{
    private readonly IQueryVisitor visitor;

    public DapperSqlQueryBuilder()
    {
        // The visitor reports into this builder's channel, the same wiring the other two
        // targets do inside BuildSource; Dapper's visitor is stateless, so it is built once
        // (decision 053).
        visitor = new DapperSqlQueryVisitor((kind, reason, feature) => Report(kind, reason, feature));
    }

    public override TargetFrameworkDescriptor Descriptor => DapperDescriptor.Instance;

    protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact)
    {
        artifact.Source.Append("FROM ").Append(clauses.From.Accept(visitor));

        // Dapper materializes into a type the query itself never names. The entities of the
        // conversion are the first answer - the orchestration hands them over before
        // generating, the same inverse move the two name-based targets make - and only where
        // no entity is mapped to the table does the name come from the table itself, which is
        // a convention of ours and is reported as one.
        var map = EntityFor(clauses.From.Table);
        var table = clauses.From.Table.Split('.').LastOrDefault();

        if (map is not null)
        {
            artifact.ResultEntity = map.Entity.Name;
        }
        else if (!string.IsNullOrEmpty(table))
        {
            artifact.ResultEntity = EntityTableNaming.EntityNameFor(table);
            Report(
                ConversionRecordKind.Convention,
                $"The result type '{artifact.ResultEntity}' was derived from the table name '{clauses.From.Table}'; the query does not name it.",
                QueryFeature.Projection,
                entity: artifact.ResultEntity);
        }
    }

    protected override void BuildJoins(QueryClauses clauses, QueryArtifact artifact)
    {
        foreach (var join in clauses.Joins)
        {
            if (artifact.Joins.Length > 0)
            {
                artifact.Joins.AppendLine();
            }

            artifact.Joins.Append(join.Accept(visitor));
        }
    }

    protected override void BuildFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.Filter is null)
        {
            return;
        }

        artifact.Filter.Append("WHERE ").Append(clauses.Filter.Accept(visitor));
    }

    protected override void BuildGrouping(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.GroupBys.Count == 0)
        {
            return;
        }

        artifact.Grouping
            .Append("GROUP BY ")
            .Append(string.Join(", ", clauses.GroupBys.Select(g => g.Accept(visitor))));
    }

    protected override void BuildPostFilter(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.PostFilter is null)
        {
            return;
        }

        artifact.PostFilter.Append("HAVING ").Append(clauses.PostFilter.Accept(visitor));
    }

    protected override void BuildOrdering(QueryClauses clauses, QueryArtifact artifact)
    {
        if (clauses.OrderBys.Count == 0)
        {
            return;
        }

        artifact.Ordering
            .Append("ORDER BY ")
            .Append(string.Join(", ", clauses.OrderBys.Select(o => o.Accept(visitor))));
    }

    protected override void BuildProjection(QueryClauses clauses, QueryArtifact artifact)
    {
        artifact.Projection.Append("SELECT ");

        // Rule Q3: no projection means the whole entity is materialized.
        artifact.Projection.Append(clauses.ProjectsWholeEntity
            ? "*"
            : string.Join(", ", clauses.Projections.Select(p => p.Accept(visitor))));
    }

    protected override List<ConversionSource> FinalizeQuery(QueryClauses clauses, QueryArtifact artifact)
        => Emit(RenderSelect(artifact), artifact.ResultEntity);

    /// <summary>
    /// SQL clause order, which is the relational evaluation order with the projection moved
    /// to the front. The steps ran in the other order; joining the slots here is what lets
    /// one template also serve a LINQ target (decision 023).
    /// </summary>
    private static string RenderSelect(QueryArtifact artifact)
    {
        var parts = new[]
        {
            artifact.Projection,
            artifact.Source,
            artifact.Joins,
            artifact.Filter,
            artifact.Grouping,
            artifact.PostFilter,
            artifact.Ordering,
        };

        return string.Join("\n", parts.Where(p => p.Length > 0).Select(p => p.ToString()));
    }

    protected override List<ConversionSource> BuildSetOperation(SetOperationInstruction instruction)
    {
        var sql = RenderSetOperation(instruction, out var resultEntity);
        return sql is null ? [] : Emit(sql, resultEntity);
    }

    private string? RenderSetOperation(SetOperationInstruction instruction, out string? resultEntity)
    {
        var left = RenderOperand(instruction.Left, out _);
        var right = RenderOperand(instruction.Right, out resultEntity);

        if (left is null || right is null)
        {
            return null;
        }

        var keyword = visitor.Visit(instruction);
        return keyword.Length == 0 ? null : $"{left}\n\n{keyword}\n\n{right}";
    }

    /// <summary>
    /// One operand of a set operation: a SELECT run through the seven steps, or a nested set
    /// operation rendered recursively. The nested case is parenthesized, because T-SQL
    /// evaluates INTERSECT before UNION and EXCEPT and same-precedence operators left to
    /// right, so a nested operand written bare could regroup into a different row set.
    /// </summary>
    private string? RenderOperand(SubQueryInstruction operand, out string? resultEntity)
    {
        var body = Unwrap(operand.Instructions);

        if (body.Count == 1 && body[0] is SetOperationInstruction nested)
        {
            var inner = RenderSetOperation(nested, out resultEntity);
            return inner is null ? null : $"(\n{inner}\n)";
        }

        var clauses = Normalize(body);
        if (clauses is null)
        {
            resultEntity = null;
            return null;
        }

        var artifact = Compose(clauses);
        resultEntity = artifact.ResultEntity;
        return RenderSelect(artifact);
    }

    private static List<ConversionSource> Emit(string sql, string? resultEntity)
    {
        var entity = resultEntity ?? "object";
        var indented = string.Join("\n", sql.Split('\n').Select(line => "        " + line));

        var method =
            $$""""
            public static List<{{entity}}> Query(IDbConnection connection)
            {
                return connection.Query<{{entity}}>(
                    """
            {{indented}}
                    """).ToList();
            }
            """";

        return
        [
            new() { Content = method, ContentType = ConversionContentType.CSharpQuery },
            new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ];
    }
}
