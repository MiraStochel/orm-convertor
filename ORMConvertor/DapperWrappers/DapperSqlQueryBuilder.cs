using Common.Naming;
using AbstractWrappers.Descriptors;
using Model;
using TransactSql;

namespace DapperWrappers;

/// <summary>
/// Dapper's query builder, which is the whole of what Dapper adds to T-SQL: the descriptor
/// and the wrapping of a finished SELECT into artifacts. Clause order, pagination, set
/// operations and subqueries are properties of the language and live in
/// <see cref="AbstractSqlQueryBuilder"/> (decision 082).
///
/// Two artifacts leave here, because SQL is the only query form Dapper has (decision 022):
/// the runnable C# method and the bare SQL, so that consumers which want the query itself
/// do not have to dig it back out of the generated code (decision 025).
/// </summary>
public class DapperSqlQueryBuilder : AbstractSqlQueryBuilder
{
    public override TargetFrameworkDescriptor Descriptor => DapperDescriptor.Instance;

    protected override List<ConversionSource> Emit(string sql, string? resultEntity)
    {
        var entity = resultEntity ?? "object";
        var indented = string.Join("\n", sql.Split('\n').Select(line => "        " + line));

        // Dapper binds from an anonymous object whose members are the placeholders of the
        // text, so the binding is the parameter list spelled once more (decision 083).
        var binding = Parameters.Count == 0
            ? string.Empty
            : $", new {{ {string.Join(", ", Parameters.Select(QueryParameterNaming.IdentifierFor))} }}";

        var method =
            $$""""
            public static List<{{entity}}> {{MethodName}}(IDbConnection connection{{CSharpParameters()}})
            {
                return connection.Query<{{entity}}>(
                    """
            {{indented}}
                    """{{binding}}).ToList();
            }
            """";

        return
        [
            new() { Content = method, ContentType = ConversionContentType.CSharpQuery },
            new() { Content = sql, ContentType = ConversionContentType.SqlQuery },
        ];
    }
}
