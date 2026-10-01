using System.Text;
using AbstractWrappers.Descriptors;
using AbstractWrappers.Diagnostics;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using TransactSql;

namespace DapperWrappers;

/// <summary>
/// The one word Dapper adds to T-SQL, peeled off the text before the grammar sees it
/// (decision 106). Dapper documents a list parameter as a bare parameter after IN -
/// <c>WHERE Id IN @ids</c> - and expands it into <c>(@ids1, @ids2, …)</c> before the
/// statement reaches the server; a parameter in parentheses, <c>IN (@ids)</c>, is one value
/// to Dapper, and a list bound to it fails. The shared grammar is a grammar of T-SQL and
/// knows neither: the bare form is a syntax error to it, and the parenthesized one is a
/// list of one bound value, which is what it is read as (decision 102).
///
/// So the bare form is recognized here, over the token stream of the grammar's own lexer -
/// the same stream the nesting guard of decision 092 reads - and rewritten to the
/// parenthesized form the grammar accepts, while the fact that the parameter binds a list
/// travels beside the text as <see cref="SqlParameterFacts"/>. It is the step the MyBatis
/// wrapper takes for a canonical <c>&lt;foreach&gt;</c> (decision 084), one token shorter:
/// that wrapper replaces the whole tag by <c>(@ids)</c> and sets the flag, this one adds
/// the parentheses and sets the flag. Recognized over tokens rather than text so that a
/// string literal, which is one token, is never touched, and a comment between IN and the
/// parameter does not hide the form.
///
/// Nothing is read off the C# call around the SQL: the facts about a Dapper parameter are
/// facts of its SQL text, and both routes into the wrapper - a bare SqlQuery unit and the
/// literal pulled out of a Dapper call in a C# unit - go through this same step.
///
/// The step stands over the whole text, before the grammar splits it into statements, and
/// the facts belong to one query. A bare unit and the text of QueryMultiple are scripts
/// whose every SELECT is a query of its own, with a method and a parameter of its own
/// (decisions 108 and 109), so <c>IN @ids</c> in one SELECT says nothing about <c>@ids</c>
/// in the next. The peeling therefore keeps where each parameter after IN stood, and the
/// facts are handed out per SELECT, from the occurrences within its span.
/// </summary>
internal static class DapperCollectionParameters
{
    /// <summary>
    /// The text with every bare parameter after IN put in parentheses, and in
    /// <paramref name="occurrences"/> every parameter that stands alone after IN - bare or
    /// in parentheses -, at its offset in the text returned; the text unchanged where there
    /// is no bare one. A text the lexer cannot read comes back unchanged and with no
    /// occurrence: the grammar reports the error with the line and column S7 asks the
    /// interface to show.
    /// </summary>
    public static string PeelOff(string sql, out IReadOnlyList<Occurrence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var found = new List<Occurrence>();
        occurrences = found;

        using var reader = new StringReader(sql);
        var tokens = new TSql160Parser(initialQuotedIdentifiers: true).GetTokenStream(reader, out var errors);

        if (errors.Count > 0)
        {
            return sql;
        }

        var bare = new List<TSqlParserToken>();

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].TokenType != TSqlTokenType.In)
            {
                continue;
            }

            var next = SkipTrivia(tokens, i + 1);
            if (next >= tokens.Count)
            {
                break;
            }

            // Every bare parameter before this one moves it two characters on, by the
            // parentheses put around that one; this one's own opening parenthesis moves it one more.
            if (tokens[next].TokenType == TSqlTokenType.Variable)
            {
                found.Add(new Occurrence(tokens[next].Text, tokens[next].Offset + (2 * bare.Count) + 1, IsBare: true));
                bare.Add(tokens[next]);
                continue;
            }

            // IN (@x): Dapper's single value, read as the list of one bound value it is. Noted
            // only so that a name standing both ways in one query can be refused (FactsOf).
            if (tokens[next].TokenType == TSqlTokenType.LeftParenthesis)
            {
                var variable = SkipTrivia(tokens, next + 1);
                if (variable < tokens.Count && tokens[variable].TokenType == TSqlTokenType.Variable)
                {
                    var close = SkipTrivia(tokens, variable + 1);
                    if (close < tokens.Count && tokens[close].TokenType == TSqlTokenType.RightParenthesis)
                    {
                        found.Add(new Occurrence(tokens[variable].Text, tokens[variable].Offset + (2 * bare.Count), IsBare: false));
                    }
                }
            }
        }

        if (bare.Count == 0)
        {
            return sql;
        }

        // Rewritten from the end, so that the offsets of the earlier tokens stay valid.
        var text = new StringBuilder(sql);
        foreach (var token in bare.OrderByDescending(token => token.Offset))
        {
            text.Insert(token.Offset + token.Text.Length, ')');
            text.Insert(token.Offset, '(');
        }

        return text.ToString();
    }

    /// <summary>
    /// The facts of one SELECT of the peeled text: the collection flag for every bare
    /// parameter within its span, and nothing about a parameter that stands only in another
    /// query of the script. Null when the query was refused: the same name standing bare
    /// after one IN and in parentheses after another is one name for two bindings, which the
    /// gate of the builder template would refuse if it could see it - but the facts travel
    /// per name, so a parenthesized occurrence of a name stated as a list would read as a
    /// list too, and the refusal has to be made here. It is made per query, for the same
    /// reason the facts are given per query.
    /// </summary>
    public static IReadOnlyDictionary<string, SqlParameterFacts>? FactsOf(
        SqlSelect select,
        IReadOnlyList<Occurrence> occurrences,
        Action<ConversionRecordKind, string, QueryFeature?> report)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(occurrences);
        ArgumentNullException.ThrowIfNull(report);

        var within = occurrences.Where(occurrence => select.Spans(occurrence.Offset)).ToList();
        var parenthesized = within.Where(occurrence => !occurrence.IsBare).Select(occurrence => occurrence.Name).ToHashSet(StringComparer.Ordinal);
        var facts = new Dictionary<string, SqlParameterFacts>(StringComparer.Ordinal);

        foreach (var occurrence in within.Where(occurrence => occurrence.IsBare))
        {
            if (parenthesized.Contains(occurrence.Name))
            {
                report(
                    ConversionRecordKind.Failure,
                    $"The parameter '{occurrence.Spelling}' stands bare after one IN, which is the list Dapper expands, and in parentheses after another, which is a single value to Dapper; that is one name for two bindings; no artifact was generated.",
                    QueryFeature.QueryParameter);
                return null;
            }

            facts[occurrence.Name] = new SqlParameterFacts(IsCollection: true);
        }

        return facts;
    }

    /// <summary>
    /// A parameter standing alone after IN, as the source spelled it, at the offset of its
    /// name in the peeled text - inside the parentheses, where the peeling added them.
    /// </summary>
    public readonly record struct Occurrence(string Spelling, int Offset, bool IsBare)
    {
        /// <summary>The parameter's undecorated name, the way the shared reader keys it.</summary>
        public string Name => Spelling.TrimStart('@');
    }

    /// <summary>The index of the first token from <paramref name="index"/> on that is neither whitespace nor a comment.</summary>
    private static int SkipTrivia(IList<TSqlParserToken> tokens, int index)
    {
        while (index < tokens.Count
            && tokens[index].TokenType is TSqlTokenType.WhiteSpace
                or TSqlTokenType.SingleLineComment
                or TSqlTokenType.MultilineComment)
        {
            index++;
        }

        return index;
    }
}
