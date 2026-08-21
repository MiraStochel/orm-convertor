using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
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
    private readonly IQueryVisitor visitor = new DapperSqlQueryVisitor();

    public override TargetFrameworkDescriptor Descriptor => DapperDescriptor.Instance;

    protected override void BuildSource(QueryClauses clauses, QueryArtifact artifact)
    {
        artifact.Source.Append("FROM ").Append(clauses.From.Accept(visitor));

        // Dapper materialises into a type the query itself never names, so the entity is
        // derived from the table - a convention of ours, and reported as one.
        var table = clauses.From.Table.Split('.').LastOrDefault();
        if (!string.IsNullOrEmpty(table))
        {
            artifact.ResultEntity = table.EndsWith('s') ? table[..^1] : table;
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

        // Rule Q3: no projection means the whole entity is materialised.
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
        var left = Normalize(instruction.Left.Instructions);
        var right = Normalize(instruction.Right.Instructions);

        if (left is null || right is null)
        {
            return [];
        }

        var leftSql = RenderSelect(Compose(left));
        var rightArtifact = Compose(right);
        var rightSql = RenderSelect(rightArtifact);

        var sql = $"{leftSql}\n\n{visitor.Visit(instruction)}\n\n{rightSql}";
        return Emit(sql, rightArtifact.ResultEntity);
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
