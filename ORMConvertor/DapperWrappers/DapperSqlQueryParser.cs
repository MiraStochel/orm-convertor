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
///
/// The call says more than its text, and none of it passes in silence (decisions 048 and
/// 109). Each argument is told by its name, or by its position in the overload Dapper
/// declares, and answered by what it states: the parameter object and the transaction are
/// values of the caller, not facts of the query; buffered and commandTimeout say how Dapper
/// runs the command and are each a loss; the map and splitOn of a multi-mapping say how a
/// row becomes objects, which every target derives again, and are a loss too. One argument
/// changes what the text is: with commandType StoredProcedure Dapper sends it as the name of
/// a procedure, so the query is refused by name rather than read as the query it is not.
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
    /// The parameters of the SqlMapper overloads that take the SQL as a string, in their order
    /// after the connection the call is made on (Dapper 2.1.79): every method has these, the
    /// synchronous Query adds buffered, and an overload whose first argument is a Type has it
    /// ahead of the SQL.
    /// </summary>
    private static readonly string[] PlainParameters = ["sql", "param", "transaction", "commandTimeout", "commandType"];

    private static readonly string[] BufferedParameters = ["sql", "param", "transaction", "buffered", "commandTimeout", "commandType"];

    /// <summary>The multi-mapping overloads of Query and QueryAsync: the map ahead of the parameter object, and in one of them the types ahead of the map.</summary>
    private static readonly string[] MultiMappingParameters = ["sql", "map", "param", "transaction", "buffered", "splitOn", "commandTimeout", "commandType"];

    private static readonly string[] MultiMappingByTypesParameters = ["sql", "types", "map", "param", "transaction", "buffered", "splitOn", "commandTimeout", "commandType"];

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
            : [new Handover(source, IsScript: true, Refusal: null, Losses: [])];

        // Every query of the unit, each with a builder fresh from the factory the
        // orchestration supplied (decision 081): the parser may not make one itself - a
        // builder belongs to the target framework and this parser to the source (S1). A
        // handover refused as a whole is one query holding the records of what went wrong,
        // and it leaves with the rest: only the parser can say that this unit yielded a query.
        var queries = new List<(AbstractQueryBuilder Builder, SqlSelect? Select, IReadOnlyList<DapperCollectionParameters.Occurrence>? Occurrences)>();

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
            // to the form the grammar reads, and where each such parameter stood is kept for the
            // facts to be handed out per query below (decision 106), the way the MyBatis wrapper
            // carries a <foreach>.
            var text = DapperCollectionParameters.PeelOff(handover.Sql!, out var occurrences);
            var selects = SqlText.Selects(text, report, declaredSourceDialect, Limits, handover.IsScript);

            if (selects is null)
            {
                queries.Add((builder, null, null));
                continue;
            }

            // What the call states beside its text, it states of every query it hands over.
            for (var i = 0; i < selects.Count; i++)
            {
                var query = i == 0 ? builder : queryBuilders();

                foreach (var (reason, feature) in handover.Losses)
                {
                    ChannelOf(query)(ConversionRecordKind.Loss, reason, feature);
                }

                queries.Add((query, selects[i], occurrences));
            }
        }

        // One query, one fresh builder and one reader (decision 081), so a query the reading
        // refuses refuses itself alone. The number is given before the reading, to every query
        // of the unit, so that a refused query does not renumber its neighbours - and a later
        // version that reads it does not either (decisions 108 and 109). A single query keeps
        // the fixed name, as there is nothing to tell it from.
        for (var i = 0; i < queries.Count; i++)
        {
            var (builder, select, occurrences) = queries[i];

            if (queries.Count > 1)
            {
                builder.QueryName = QueryMethodNaming.Positional(i + 1);
            }

            if (select is null)
            {
                continue;
            }

            // The facts of this SELECT alone: in a script, a list parameter of one query says
            // nothing about a parameter of the same name in another.
            var report = ChannelOf(builder);
            if (DapperCollectionParameters.FactsOf(select, occurrences!, report) is { } facts)
            {
                new SqlQueryReader(builder, report, declaredSourceDialect, facts, Limits).Read(select);
            }
        }

        return [.. queries.Select(query => query.Builder)];
    }

    /// <summary>
    /// A place where the source hands Dapper a text: the SQL with whether it is a script, or
    /// the reason it could not be taken, for the query it is to report on, and the losses of
    /// what the call states beside the text.
    /// </summary>
    private sealed record Handover(
        string? Sql,
        bool IsScript,
        (ConversionRecordKind Kind, string Reason)? Refusal,
        IReadOnlyList<(string Reason, QueryFeature? Feature)> Losses);

    /// <summary>
    /// Every Dapper call of the unit, in the order of the text (decision 109). The SQL is the
    /// argument in the place of sql - by name, or by position in the overload (see
    /// <see cref="ParametersOf"/>) -, and it is read through the token's value, so verbatim
    /// strings, raw string literals and escapes have already been resolved by Roslyn rather
    /// than being unwound by hand. A call with no argument at all is ADO.NET's own
    /// ExecuteReader or ExecuteScalar on a command, not Dapper's, which always takes the SQL.
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
            var parameters = ParametersOf(member, arguments);

            ExpressionSyntax? sql = null;
            (ConversionRecordKind Kind, string Reason)? refusal = null;
            var losses = new List<(string Reason, QueryFeature? Feature)>();
            var mapping = new List<string>();

            for (var i = 0; i < arguments.Count; i++)
            {
                var role = arguments[i].NameColon?.Name.Identifier.Text ?? (i < parameters.Length ? parameters[i] : null);
                var value = arguments[i].Expression;

                switch (role)
                {
                    case "sql":
                        sql = value;
                        break;

                    // The result type, the values of the parameters and the transaction the
                    // command joins are the caller's, not facts of the query.
                    case "type" or "param" or "transaction":
                        break;

                    case "buffered" when value.IsKind(SyntaxKind.TrueLiteralExpression):
                    case "commandTimeout" when value.IsKind(SyntaxKind.NullLiteralExpression):
                        break;

                    case "buffered":
                        losses.Add((
                            $"The Dapper call passes buffered: {value}, which says whether Dapper reads every row before handing the "
                                + "result over - how the result is materialized, not which rows the query reads; the query "
                                + "representation has no place for it and it was dropped.",
                            null));
                        break;

                    case "commandTimeout":
                        losses.Add((
                            $"The Dapper call passes commandTimeout: {value}, which says how long the command may run - how the "
                                + "query is executed, not which rows it reads; the query representation has no place for it and "
                                + "it was dropped.",
                            null));
                        break;

                    case "commandType":
                        refusal ??= CommandTypeRefusal(value);
                        break;

                    case "map" or "splitOn" or "types":
                        mapping.Add(role);
                        break;

                    default:
                        losses.Add((
                            $"The Dapper call passes {(role is null ? "the argument" : $"{role}:")} {value}, which the reading does "
                                + "not know; it was dropped, which changes nothing the SQL text says about its rows.",
                            null));
                        break;
                }
            }

            if (mapping.Count > 0)
            {
                losses.Add((
                    $"The Dapper call maps each row onto several objects ({string.Join(", ", mapping)}), which states how its rows "
                        + "are materialized and which the query representation does not carry; it was dropped and every target "
                        + "derives the row again.",
                    QueryFeature.Projection));
            }

            if (refusal is null && !(sql is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)))
            {
                refusal = (
                    ConversionRecordKind.Incompleteness,
                    "The Dapper call does not pass the SQL as a string literal, so the query could not be read.");
            }

            handovers.Add(refusal is null
                ? new Handover(((LiteralExpressionSyntax)sql!).Token.ValueText, ScriptMethods.Contains(member.Name.Identifier.Text), null, losses)
                : new Handover(null, false, refusal, []));
        }

        return handovers;
    }

    /// <summary>
    /// The parameters of the overload a call is made to, as far as its syntax tells them
    /// apart (<see cref="PlainParameters"/>): a Type as the first argument puts the type ahead
    /// of the SQL; a Query with three type arguments or more, a lambda or a list of types after
    /// the SQL, or an argument named map, splitOn or types is a multi-mapping. An argument the
    /// overload has no place for at its position is one the reading does not know.
    /// </summary>
    private static string[] ParametersOf(MemberAccessExpressionSyntax member, SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        var name = member.Name.Identifier.Text;
        var typed = arguments[0] is { NameColon: null, Expression: TypeOfExpressionSyntax };
        var afterSql = arguments.ElementAtOrDefault(typed ? 2 : 1) is { NameColon: null } argument ? argument.Expression : null;

        var parameters =
            name is "Query" or "QueryAsync"
            && (member.Name is GenericNameSyntax { TypeArgumentList.Arguments.Count: >= 3 }
                || afterSql is AnonymousFunctionExpressionSyntax
                || IsTypeList(afterSql)
                || arguments.Any(a => a.NameColon?.Name.Identifier.Text is "map" or "splitOn" or "types"))
                ? IsTypeList(afterSql) || arguments.Any(a => a.NameColon?.Name.Identifier.Text == "types")
                    ? MultiMappingByTypesParameters
                    : MultiMappingParameters
                : name == "Query" ? BufferedParameters : PlainParameters;

        return typed ? ["type", .. parameters] : parameters;
    }

    /// <summary>An array or a collection expression of types, the types argument of a multi-mapping.</summary>
    private static bool IsTypeList(ExpressionSyntax? expression) => expression switch
    {
        ArrayCreationExpressionSyntax array => array.Initializer?.Expressions.FirstOrDefault() is TypeOfExpressionSyntax,
        ImplicitArrayCreationExpressionSyntax array => array.Initializer.Expressions.FirstOrDefault() is TypeOfExpressionSyntax,
        CollectionExpressionSyntax collection => collection.Elements.FirstOrDefault() is ExpressionElementSyntax { Expression: TypeOfExpressionSyntax },
        _ => false,
    };

    /// <summary>
    /// The command type of a call, which decides what Dapper sends the text as. Text, stated
    /// or left null, is the query the text states. StoredProcedure has Dapper send the text as
    /// the name of a procedure to call, and TableDirect as the name of a table, so neither is
    /// a query, and the call is refused by name rather than read as one: a SELECT sent so is
    /// refused by the server, so translating it would make a working query out of a failing
    /// call. A value the code computes decides that at run time, which leaves nothing to read
    /// either. Null where the command type leaves the text a query.
    /// </summary>
    private static (ConversionRecordKind Kind, string Reason)? CommandTypeRefusal(ExpressionSyntax value)
    {
        var member = value is MemberAccessExpressionSyntax access
            && access.Expression is IdentifierNameSyntax { Identifier.Text: "CommandType" } or MemberAccessExpressionSyntax { Name.Identifier.Text: "CommandType" }
                ? access.Name.Identifier.Text
                : null;

        var reason = member switch
        {
            _ when value.IsKind(SyntaxKind.NullLiteralExpression) => null,
            "Text" => null,
            "StoredProcedure" => $"The Dapper call passes commandType: {value}, so Dapper sends its text as the name of a stored "
                + "procedure to call rather than as a query; a call of a procedure is no query the tool translates, so no artifact "
                + "was generated.",
            "TableDirect" => $"The Dapper call passes commandType: {value}, so its text is sent as the name of a table to read "
                + "rather than as a query; no artifact was generated.",
            _ => $"The Dapper call passes commandType: {value}, a value computed at run time, which decides whether Dapper sends "
                + "the text as a query or as the name of a stored procedure; what the text is cannot be read off the unit, so no "
                + "artifact was generated.",
        };

        return reason is null ? null : (ConversionRecordKind.Failure, reason);
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
