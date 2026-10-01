using Common.Naming;
using Model.QueryInstructions.Conditions;

namespace TransactSql;

/// <summary>
/// The parameters of a statement the shared writer composed, respelled for a framework that
/// does not write T-SQL's <c>@name</c> (decisions 083 and 113): MyBatis's <c>#{name}</c>, the
/// <c>:name</c> of NHibernate's native query, the <c>?1</c> of JPA's, the interpolation hole
/// of EF Core's. One routine for all of them, because what has to be got right is the same
/// everywhere - a placeholder is a name the method declares, never an <c>@</c> inside a
/// string literal and never a name the query does not bind.
/// </summary>
public static class SqlPlaceholders
{
    /// <summary>
    /// The statement with every placeholder of <paramref name="parameters"/> replaced by its
    /// spelling. A collection parameter arrives as the writer leaves it, bare after IN - the
    /// shape Dapper expands - so the spelling decides the parentheses as well. String literals
    /// are skipped, so that a value holding an <c>@</c> is not read as a name.
    /// </summary>
    public static string Respell(string sql, IReadOnlyList<QueryParameter> parameters, Func<QueryParameter, string> spelling)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(spelling);

        var substituted = new System.Text.StringBuilder(sql.Length);
        var insideLiteral = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var character = sql[i];

            if (character == '\'')
            {
                insideLiteral = !insideLiteral;
                substituted.Append(character);
                continue;
            }

            if (insideLiteral || character != '@' || i + 1 >= sql.Length || !IsNameStart(sql[i + 1]))
            {
                substituted.Append(character);
                continue;
            }

            var end = i + 1;
            while (end < sql.Length && IsNameCharacter(sql[end]))
            {
                end++;
            }

            var name = sql[(i + 1)..end];
            var parameter = parameters.FirstOrDefault(p => QueryParameterNaming.IdentifierFor(p) == name);

            if (parameter is null)
            {
                substituted.Append(character);
                continue;
            }

            substituted.Append(spelling(parameter));
            i = end - 1;
        }

        return substituted.ToString();
    }

    /// <summary>
    /// The way back (decision 113): the text a host hands its API for native SQL, with the
    /// placeholders of that API respelled as the T-SQL variables the shared reader takes -
    /// the <c>:name</c> of NHibernate and of JPA as <c>@name</c>, the <c>?1</c> of JPA as
    /// <c>@p1</c> with its position stated in <paramref name="facts"/> -, string literals
    /// skipped. The wrapper respells before the grammar sees the text, as it puts Dapper's
    /// list parameter in parentheses (decision 106), and the grammar is not taught a second
    /// spelling (decision 082).
    ///
    /// Null, with the reason in <paramref name="unread"/>, where the text holds what would be
    /// read as something it is not: an <c>@</c> variable of its own, which the API binds to
    /// nothing and the reading would make a parameter of; a <c>?</c> without a number, whose
    /// position the text does not state.
    /// </summary>
    public static string? FromHost(string text, out Dictionary<string, SqlParameterFacts> facts, out string? unread)
    {
        ArgumentNullException.ThrowIfNull(text);

        facts = new Dictionary<string, SqlParameterFacts>(StringComparer.Ordinal);
        unread = null;

        var respelled = new System.Text.StringBuilder(text.Length);
        var insideLiteral = false;

        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];

            if (character == '\'')
            {
                insideLiteral = !insideLiteral;
                respelled.Append(character);
                continue;
            }

            if (insideLiteral)
            {
                respelled.Append(character);
                continue;
            }

            // A colon before a name, and not one of a pair: T-SQL has no :: of its own, and a
            // colon after a name - a label - is not followed by one.
            if (character == ':' && i + 1 < text.Length && IsNameStart(text[i + 1])
                && (i == 0 || text[i - 1] != ':'))
            {
                var end = NameEnd(text, i + 1);
                respelled.Append('@').Append(text, i + 1, end - i - 1);
                i = end - 1;
                continue;
            }

            if (character == '?')
            {
                var end = i + 1;
                while (end < text.Length && char.IsAsciiDigit(text[end]))
                {
                    end++;
                }

                if (end == i + 1)
                {
                    unread = "a ? without a number, whose position the text does not state";
                    return null;
                }

                var position = int.Parse(text.AsSpan(i + 1, end - i - 1), System.Globalization.CultureInfo.InvariantCulture);
                facts[$"p{position}"] = new SqlParameterFacts(Position: position);
                respelled.Append("@p").Append(position.ToString(System.Globalization.CultureInfo.InvariantCulture));
                i = end - 1;
                continue;
            }

            if (character == '@' && i + 1 < text.Length && (IsNameStart(text[i + 1]) || text[i + 1] == '@'))
            {
                var start = text[i + 1] == '@' ? i + 2 : i + 1;
                unread = $"the T-SQL variable {text[i..NameEnd(text, start)]}, which the API binds to nothing";
                return null;
            }

            respelled.Append(character);
        }

        return respelled.ToString();
    }

    private static int NameEnd(string text, int start)
    {
        var end = start;
        while (end < text.Length && IsNameCharacter(text[end]))
        {
            end++;
        }

        return end;
    }

    private static bool IsNameStart(char character) => char.IsLetter(character) || character == '_';

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character == '_';
}
