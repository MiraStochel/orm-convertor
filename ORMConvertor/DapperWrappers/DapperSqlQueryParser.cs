using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Model;
using Model.AbstractRepresentation;
using TransactSql;

namespace DapperWrappers;

/// <summary>
/// Reads a Dapper query. Two stages, because a Dapper query in the wild is T-SQL inside C#:
/// Roslyn finds the Query&lt;T&gt; call and takes its string literal, then the shared T-SQL
/// reader turns that string into instructions (decisions 026 and 082). A bare SQL source
/// skips the first stage, and which of the two it is, is the content type the unit declares
/// (decision 047).
///
/// What is left here is the first stage alone: getting hold of the text is reading Dapper's
/// own artifact, whereas reading the text is reading a language three frameworks share, so
/// the grammar lives in <see cref="SqlQueryReader"/>. Depending on that reader is not
/// depending on Dapper, which is what S1 forbids.
/// </summary>
public class DapperSqlQueryParser(Func<AbstractQueryBuilder> queryBuilders) : IQueryParser
{
    /// <summary>
    /// The builder of the query being read. Assigned at the start of every Parse from the
    /// factory the orchestration supplied: one query, one fresh builder (decision 081). The
    /// parser may not make one itself - a builder belongs to the target framework and this
    /// parser to the source (S1) - and it is never touched outside a Parse call.
    /// </summary>
    private AbstractQueryBuilder queryBuilder = default!;

    private static readonly string[] DapperMethods =
    [
        "Query", "QueryAsync",
        "QueryFirst", "QueryFirstAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "ExecuteScalar", "ExecuteScalarAsync",
    ];

    public bool CanParse(ConversionContentType contentType) => contentType is
        ConversionContentType.SqlQuery or ConversionContentType.CSharpQuery;

    /// <summary>
    /// Which of the two stages the unit enters is decided by the language it declares, not
    /// by what its text looks like: a bare SELECT that happens to mention a table called
    /// QueryLog used to be taken for a C# snippet and refused for carrying no Dapper call
    /// (decisions 025 and 047).
    /// </summary>
    public IReadOnlyCollection<AbstractQueryBuilder> Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null)
    {
        queryBuilder = queryBuilders();

        // The builder leaves on every path, refused ones included: it holds the records of
        // what went wrong, and only the parser can say that this unit yielded a query at all
        // (decision 081).
        var sql = contentType == ConversionContentType.CSharpQuery ? ExtractSql(source) : source;
        if (sql is null)
        {
            return [queryBuilder];
        }

        new SqlQueryReader(queryBuilder, Report).Read(sql);

        return [queryBuilder];
    }

    /// <summary>
    /// Pulls the SQL out of a Dapper call. The literal is read through the token's value, so
    /// verbatim strings, raw string literals and escapes have already been resolved by
    /// Roslyn rather than being unwound by hand.
    /// </summary>
    private string? ExtractSql(string source)
    {
        var tree = CSharpSyntaxTree.ParseText("public class Snippet\n{\n" + source + "\n}\n");
        var root = tree.GetCompilationUnitRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member
                || !DapperMethods.Contains(member.Name.Identifier.Text))
            {
                continue;
            }

            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is LiteralExpressionSyntax literal
                    && literal.RawKind == (int)SyntaxKind.StringLiteralExpression)
                {
                    return literal.Token.ValueText;
                }
            }

            Report(
                ConversionRecordKind.Incompleteness,
                "The Dapper call does not pass the SQL as a string literal, so the query could not be read.");
            return null;
        }

        Report(ConversionRecordKind.Failure, "No Dapper query call was found in the source.");
        return null;
    }

    /// <summary>
    /// The channel the shared reader reports over. The record names the language that was
    /// read, not the unit that carried it, which is the shape every query parser of the
    /// solution uses - HQL for an hbm.xml &lt;query&gt;, SQL here.
    /// </summary>
    private void Report(ConversionRecordKind kind, string reason, QueryFeature? feature = null)
        => queryBuilder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = queryBuilder.Descriptor.Framework,
            Artifact = ConversionContentType.SqlQuery,
            Feature = feature,
            Reason = reason,
        });
}
