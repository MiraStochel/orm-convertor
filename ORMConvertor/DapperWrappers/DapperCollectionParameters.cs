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
/// </summary>
internal static class DapperCollectionParameters
{
    /// <summary>
    /// The text with every bare parameter after IN put in parentheses, and in
    /// <paramref name="facts"/> the collection flag for each of them; the text unchanged and
    /// an empty map where there is none. Null when the text was refused: the same name
    /// standing bare after one IN and in parentheses after another is one name for two
    /// bindings, which the gate of the builder template would refuse if it could see it -
    /// but the facts travel per name, so a parenthesized occurrence of a name stated as a
    /// list would read as a list too, and the refusal has to be made here.
    /// </summary>
    public static string? PeelOff(
        string sql,
        Action<ConversionRecordKind, string, QueryFeature?> report,
        out IReadOnlyDictionary<string, SqlParameterFacts> facts)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(report);

        var stated = new Dictionary<string, SqlParameterFacts>(StringComparer.Ordinal);
        facts = stated;

        using var reader = new StringReader(sql);
        var tokens = new TSql160Parser(initialQuotedIdentifiers: true).GetTokenStream(reader, out var errors);

        // A text the lexer cannot read is left to the grammar, which reports the error with
        // the line and column S7 asks the interface to show.
        if (errors.Count > 0)
        {
            return sql;
        }

        var bare = new List<TSqlParserToken>();
        var parenthesized = new HashSet<string>(StringComparer.Ordinal);

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

            if (tokens[next].TokenType == TSqlTokenType.Variable)
            {
                bare.Add(tokens[next]);
                continue;
            }

            // IN (@x): Dapper's single value, read as the list of one bound value it is. Noted
            // only so that a name standing both ways can be refused below.
            if (tokens[next].TokenType == TSqlTokenType.LeftParenthesis)
            {
                var variable = SkipTrivia(tokens, next + 1);
                if (variable < tokens.Count && tokens[variable].TokenType == TSqlTokenType.Variable)
                {
                    var close = SkipTrivia(tokens, variable + 1);
                    if (close < tokens.Count && tokens[close].TokenType == TSqlTokenType.RightParenthesis)
                    {
                        parenthesized.Add(NameOf(tokens[variable]));
                    }
                }
            }
        }

        foreach (var token in bare)
        {
            var name = NameOf(token);

            if (parenthesized.Contains(name))
            {
                report(
                    ConversionRecordKind.Failure,
                    $"The parameter '{token.Text}' stands bare after one IN, which is the list Dapper expands, and in parentheses after another, which is a single value to Dapper; that is one name for two bindings; no artifact was generated.",
                    QueryFeature.QueryParameter);
                return null;
            }

            stated[name] = new SqlParameterFacts(IsCollection: true);
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

    /// <summary>The parameter's undecorated name, the way the shared reader keys it.</summary>
    private static string NameOf(TSqlParserToken variable) => variable.Text.TrimStart('@');

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
