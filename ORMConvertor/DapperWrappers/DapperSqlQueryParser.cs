using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
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
///
/// Between the two stages stands the one word Dapper adds to the language: a bare parameter
/// after IN is its list parameter, which <see cref="DapperCollectionParameters"/> peels off
/// the text before the grammar sees it (decision 106), on both routes in.
///
/// The two routes differ in one more fact, and it is Dapper's to state (decision 108). A
/// bare SQL unit is a script, whose every SELECT is a query of its own with a builder of its
/// own, numbered by its position when there are several; the literal of a Dapper call is one
/// command, of which Query&lt;T&gt; maps the first result set alone, so a second SELECT in it
/// is refused.
/// </summary>
public class DapperSqlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    SourceSqlDialect? declaredSourceDialect = null) : IQueryParser
{
    private static readonly string[] DapperMethods =
    [
        "Query", "QueryAsync",
        "QueryFirst", "QueryFirstAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "ExecuteScalar", "ExecuteScalarAsync",
    ];

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

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
        // The unit's first builder, fresh from the factory the orchestration supplied
        // (decision 081): the parser may not make one itself - a builder belongs to the target
        // framework and this parser to the source (S1). It carries whatever refuses the text
        // as a whole, and it leaves on every path, refused ones included: it holds the records
        // of what went wrong, and only the parser can say that this unit yielded a query at all.
        var first = queryBuilders();
        var report = ChannelOf(first);

        var sql = contentType == ConversionContentType.CSharpQuery ? ExtractSql(source, report) : source;
        if (sql is null)
        {
            return [first];
        }

        // Dapper's own spelling of a list parameter, IN @ids, is not T-SQL; it is rewritten to
        // the form the grammar reads and the fact that the parameter binds a list travels
        // beside the text (decision 106), the way the MyBatis wrapper carries a <foreach>.
        var text = DapperCollectionParameters.PeelOff(sql, report, out var statedParameters);
        if (text is null)
        {
            return [first];
        }

        var selects = SqlText.Selects(
            text,
            report,
            declaredSourceDialect,
            Limits,
            isScript: contentType == ConversionContentType.SqlQuery);

        if (selects is null)
        {
            return [first];
        }

        // One query, one fresh builder and one reader (decision 081), so a SELECT the reading
        // refuses refuses itself alone. The number is given before the reading, to every
        // SELECT of the text, so that a refused query does not renumber its neighbours - and a
        // later version that reads it does not either (decision 108). A single SELECT keeps
        // the fixed name, as there is nothing to tell it from.
        var builders = new List<AbstractQueryBuilder>(selects.Count);

        for (var i = 0; i < selects.Count; i++)
        {
            var builder = i == 0 ? first : queryBuilders();

            if (selects.Count > 1)
            {
                builder.QueryName = QueryMethodNaming.Positional(i + 1);
            }

            new SqlQueryReader(builder, ChannelOf(builder), declaredSourceDialect, statedParameters, Limits).Read(selects[i]);
            builders.Add(builder);
        }

        return builders;
    }

    /// <summary>
    /// Pulls the SQL out of a Dapper call. The literal is read through the token's value, so
    /// verbatim strings, raw string literals and escapes have already been resolved by
    /// Roslyn rather than being unwound by hand.
    /// </summary>
    private static string? ExtractSql(string source, Action<ConversionRecordKind, string, QueryFeature?> report)
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

            report(
                ConversionRecordKind.Incompleteness,
                "The Dapper call does not pass the SQL as a string literal, so the query could not be read.",
                null);
            return null;
        }

        report(ConversionRecordKind.Failure, "No Dapper query call was found in the source.", null);
        return null;
    }

    /// <summary>
    /// The channel the shared reading reports over into one builder. The record names the
    /// language that was read, not the unit that carried it, which is the shape every query
    /// parser of the solution uses - HQL for an hbm.xml &lt;query&gt;, SQL here.
    /// </summary>
    private static Action<ConversionRecordKind, string, QueryFeature?> ChannelOf(AbstractQueryBuilder builder)
        => (kind, reason, feature) => builder.Report(new ConversionRecord
        {
            Kind = kind,
            Framework = builder.Descriptor.Framework,
            Artifact = ConversionContentType.SqlQuery,
            Feature = feature,
            Reason = reason,
        });
}
