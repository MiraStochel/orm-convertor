using AbstractWrappers;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Common.Naming;
using Common.Reading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
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
/// own; the literal of a Dapper call is one command, of which Query&lt;T&gt; maps the first
/// result set alone, so a second SELECT in it is refused - except in QueryMultiple, which
/// maps every result set of its text and so hands over a script as much as a bare unit does.
///
/// A C# unit carries a query for every Dapper call in it, not for the first one alone
/// (decision 109): each call is one exchange with the server, read into a builder of its
/// own, and which calls those are is Dapper's API as a whole - Execute and ExecuteReader as
/// much as Query - rather than the part of it whose text the tool can translate. A call whose
/// text states a write is refused by name, the way the same statement in a bare unit is.
/// Queries of one unit that the source did not name are numbered by their position in the
/// text, the calls and the SELECTs of a script alike.
/// </summary>
public class DapperSqlQueryParser(
    Func<AbstractQueryBuilder> queryBuilders,
    SourceSqlDialect? declaredSourceDialect = null) : IQueryParser
{
    /// <summary>
    /// The extension methods of Dapper's SqlMapper that send SQL to the server (decision 109):
    /// every one of them is a place where the source hands over a query, whether its text
    /// reads, writes, or returns several result sets.
    /// </summary>
    private static readonly HashSet<string> DapperMethods = new(StringComparer.Ordinal)
    {
        "Query", "QueryAsync",
        "QueryFirst", "QueryFirstAsync", "QueryFirstOrDefault", "QueryFirstOrDefaultAsync",
        "QuerySingle", "QuerySingleAsync", "QuerySingleOrDefault", "QuerySingleOrDefaultAsync",
        "QueryMultiple", "QueryMultipleAsync", "QueryUnbufferedAsync",
        "Execute", "ExecuteAsync",
        "ExecuteScalar", "ExecuteScalarAsync",
        "ExecuteReader", "ExecuteReaderAsync",
    };

    /// <summary>
    /// The methods that map every result set of their text (README of Dapper, "Multiple
    /// Results"): their text is a script whose every SELECT is a query, like a bare unit.
    /// </summary>
    private static readonly HashSet<string> ScriptMethods = new(StringComparer.Ordinal)
    {
        "QueryMultiple", "QueryMultipleAsync",
    };

    /// <summary>
    /// The limits this parser reads its input under (decision 092). The orchestration sets
    /// them on every parser it creates; one constructed by hand - in a test - runs under the
    /// default, which is the cap the application uses unless its operator moved it.
    /// </summary>
    public ParseLimits Limits { get; set; } = ParseLimits.Default;

    public bool CanParse(ConversionContentType contentType) => contentType is
        ConversionContentType.SqlQuery or ConversionContentType.CSharp;

    /// <summary>
    /// Which of the two stages the unit enters is decided by the language it declares, not
    /// by what its text looks like: a bare SELECT that happens to mention a table called
    /// QueryLog used to be taken for a C# snippet and refused for carrying no Dapper call
    /// (decisions 025 and 047).
    ///
    /// A C# unit is a whole file or a fragment of one (decision 111), and the entity pass
    /// reads the same text for its classes. One without a Dapper call is therefore no error -
    /// a file holding an entity alone is an ordinary input - and yields no query; whether the
    /// unit yielded anything at all is asked of both passes together (decision 081). A text
    /// nested beyond the cap yields nothing here either, and says nothing: the entity pass,
    /// which read the same text first, has reported it.
    /// </summary>
    public IReadOnlyCollection<AbstractQueryBuilder> Parse(ConversionContentType contentType, string source, IReadOnlyList<EntityMap>? entityMaps = null)
    {
        var handovers = contentType == ConversionContentType.CSharp
            ? ExtractCalls(source, Limits)
            : [new Handover(source, IsScript: true, Refusal: null)];

        // Every query of the unit, each with a builder fresh from the factory the
        // orchestration supplied (decision 081): the parser may not make one itself - a
        // builder belongs to the target framework and this parser to the source (S1). A
        // handover refused as a whole is one query holding the records of what went wrong,
        // and it leaves with the rest: only the parser can say that this unit yielded a query.
        var queries = new List<(AbstractQueryBuilder Builder, SqlSelect? Select, IReadOnlyDictionary<string, SqlParameterFacts>? Facts)>();

        foreach (var handover in handovers)
        {
            var builder = queryBuilders();
            var report = ChannelOf(builder);

            if (handover.Refusal is { } refusal)
            {
                report(refusal.Kind, refusal.Reason, null);
                queries.Add((builder, null, null));
                continue;
            }

            // Dapper's own spelling of a list parameter, IN @ids, is not T-SQL; it is rewritten
            // to the form the grammar reads and the fact that the parameter binds a list travels
            // beside the text (decision 106), the way the MyBatis wrapper carries a <foreach>.
            var text = DapperCollectionParameters.PeelOff(handover.Sql!, report, out var statedParameters);
            var selects = text is null
                ? null
                : SqlText.Selects(text, report, declaredSourceDialect, Limits, handover.IsScript);

            if (selects is null)
            {
                queries.Add((builder, null, null));
                continue;
            }

            for (var i = 0; i < selects.Count; i++)
            {
                queries.Add((i == 0 ? builder : queryBuilders(), selects[i], statedParameters));
            }
        }

        // One query, one fresh builder and one reader (decision 081), so a query the reading
        // refuses refuses itself alone. The number is given before the reading, to every query
        // of the unit, so that a refused query does not renumber its neighbours - and a later
        // version that reads it does not either (decisions 108 and 109). A single query keeps
        // the fixed name, as there is nothing to tell it from.
        for (var i = 0; i < queries.Count; i++)
        {
            var (builder, select, facts) = queries[i];

            if (queries.Count > 1)
            {
                builder.QueryName = QueryMethodNaming.Positional(i + 1);
            }

            if (select is not null)
            {
                new SqlQueryReader(builder, ChannelOf(builder), declaredSourceDialect, facts!, Limits).Read(select);
            }
        }

        return [.. queries.Select(query => query.Builder)];
    }

    /// <summary>
    /// A place where the source hands Dapper a text: the SQL with whether it is a script, or
    /// the reason it could not be taken, for the query it is to report on.
    /// </summary>
    private sealed record Handover(string? Sql, bool IsScript, (ConversionRecordKind Kind, string Reason)? Refusal);

    /// <summary>
    /// Every Dapper call of the unit, in the order of the text (decision 109). The SQL is the
    /// argument named sql, or else the first positional one - the place every SqlMapper method
    /// takes it -, and it is read through the token's value, so verbatim strings, raw string
    /// literals and escapes have already been resolved by Roslyn rather than being unwound by
    /// hand. A call with no argument at all is ADO.NET's own ExecuteReader or ExecuteScalar on
    /// a command, not Dapper's, which always takes the SQL.
    /// </summary>
    private static List<Handover> ExtractCalls(string source, ParseLimits limits)
    {
        var handovers = new List<Handover>();

        // Before Roslyn, because building the tree is what the cap protects (decision 092).
        if (NestingDepthGuard.FirstBeyond(Tracked(source), limits) is not null)
        {
            return handovers;
        }

        foreach (var invocation in Calls(source))
        {
            var member = (MemberAccessExpressionSyntax)invocation.Expression;
            var arguments = invocation.ArgumentList.Arguments;
            var sql = arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.Text == "sql")
                ?? arguments.FirstOrDefault(argument => argument.NameColon is null);

            handovers.Add(sql?.Expression is LiteralExpressionSyntax literal
                && literal.RawKind == (int)SyntaxKind.StringLiteralExpression
                    ? new Handover(literal.Token.ValueText, ScriptMethods.Contains(member.Name.Identifier.Text), null)
                    : new Handover(null, false, (
                        ConversionRecordKind.Incompleteness,
                        "The Dapper call does not pass the SQL as a string literal, so the query could not be read.")));
        }

        return handovers;
    }

    /// <summary>
    /// The places where the unit hands Dapper a text (decisions 109 and 111): one node for
    /// every call <see cref="Parse"/> reads a query of, found by the same search. The entity
    /// pass of the wrapper asks it which classes hold the code around queries rather than an
    /// entity, and the answer has to be the one the query pass acts on. A text nested beyond
    /// the cap has none, because it is not parsed.
    /// </summary>
    public static IReadOnlyList<SyntaxNode> FindHandovers(string source, ParseLimits? limits = null)
        => NestingDepthGuard.FirstBeyond(Tracked(source), limits ?? ParseLimits.Default) is not null
            ? []
            : [.. Calls(source)];

    /// <summary>
    /// Every call of a SqlMapper method in the unit, in the order of the text: a member call
    /// by one of the names Dapper sends SQL under, with an argument - ADO.NET's own
    /// ExecuteReader or ExecuteScalar on a command takes none. A file is parsed as it stands
    /// and a fragment inside a class, which is what a method needs to be one (decision 111).
    /// </summary>
    private static IEnumerable<InvocationExpressionSyntax> Calls(string source)
        => CSharpUnit.Parse(source, fragment => "public class Snippet\n{\n" + fragment + "\n}\n")
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax member
                && DapperMethods.Contains(member.Name.Identifier.Text)
                && invocation.ArgumentList.Arguments.Count > 0);

    /// <summary>
    /// The C# text as the shared nesting guard reads it (decision 092): Roslyn's own lexer,
    /// which is a loop, projected onto text and position - the same projection the shared
    /// entity and LINQ readings make of the same text.
    /// </summary>
    private static IEnumerable<SourceToken> Tracked(string source)
    {
        var text = SourceText.From(source);

        foreach (var token in SyntaxFactory.ParseTokens(source))
        {
            var position = text.Lines.GetLinePosition(token.SpanStart);
            yield return new SourceToken(token.Text, position.Line + 1, position.Character + 1);
        }
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
