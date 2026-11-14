using System.Collections.Concurrent;
using AbstractWrappers;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace TransactSql;

/// <summary>
/// How a name is written so that T-SQL reads it as a name. A table, a column, an alias or a
/// common table expression spelled like a reserved word of the language - <c>User</c>,
/// <c>Order</c>, <c>Rule</c>, <c>Set</c> - goes out in square brackets, and so does a name
/// that is no identifier at all (a dotted alias the source delimited); every other name stays
/// exactly as the source spelled it. Which words are reserved is ScriptDom's own list, read
/// through its lexer: a name it tokenizes as an identifier it reads bare in every position,
/// a name it tokenizes as a keyword it refuses in at least one (measured on the pinned
/// release, <c>Combined/KeywordNameTest</c>). The bracketed name reads back as the bare one,
/// so a query the tool wrote is a query the tool reads.
/// </summary>
public static class SqlNames
{
    private static readonly ConcurrentDictionary<string, bool> Reserved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether ScriptDom's lexer reads the name as a keyword rather than as an identifier.</summary>
    public static bool IsReserved(string name) => Reserved.GetOrAdd(name, static n =>
    {
        var tokens = new TSql160Parser(initialQuotedIdentifiers: true)
            .GetTokenStream(new StringReader(n), out _)
            .Where(token => token.TokenType != TSqlTokenType.EndOfFile)
            .ToList();
        return tokens.Count != 1 || tokens[0].TokenType != TSqlTokenType.Identifier;
    });

    /// <summary>The name as T-SQL reads it: bare where it is a regular identifier and no reserved word, otherwise delimited.</summary>
    public static string Written(string name)
        => Identifiers.IsRegular(name) && !IsReserved(name) ? name : Delimited(name);

    /// <summary>A possibly schema-qualified table name (<c>Sales.Orders</c>), each part written on its own.</summary>
    public static string WrittenQualified(string name)
        => name.Contains('.') ? string.Join(".", name.Split('.').Select(Written)) : Written(name);

    /// <summary>A name in T-SQL's delimiters, a closing bracket inside it doubled.</summary>
    public static string Delimited(string name) => $"[{name.Replace("]", "]]")}]";

    /// <summary>
    /// The index just past the delimited name that opens with the bracket at
    /// <paramref name="start"/>, a doubled closing bracket read as a character of the name;
    /// the end of the text where the name is not closed.
    /// </summary>
    public static int EndOfDelimited(string sql, int start)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var closing = ClosingBracket(sql, start);
        return closing < 0 ? sql.Length : closing + 1;
    }

    /// <summary>
    /// The statement with every delimited name respelled: <paramref name="spelling"/> takes
    /// what stands between the brackets, as the text holds it, and gives what is to stand
    /// there. String literals are skipped, so that a bracket inside a value is no name, and
    /// inside a name an apostrophe opens no literal. For a host that reads characters of a
    /// name as its own (decision 217) - the delimited name is the only place where the shared
    /// writer puts a character it did not choose outside a literal.
    /// </summary>
    public static string RespellDelimited(string sql, Func<string, string> spelling)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(spelling);

        var respelled = new System.Text.StringBuilder(sql.Length);
        var insideLiteral = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var character = sql[i];

            if (character == '\'')
            {
                insideLiteral = !insideLiteral;
            }

            var closing = !insideLiteral && character == '[' ? ClosingBracket(sql, i) : -1;
            if (closing < 0)
            {
                respelled.Append(character);
                continue;
            }

            respelled.Append('[').Append(spelling(sql[(i + 1)..closing])).Append(']');
            i = closing;
        }

        return respelled.ToString();
    }

    /// <summary>The index of the bracket that closes the name opened at <paramref name="start"/>, or -1.</summary>
    private static int ClosingBracket(string sql, int start)
    {
        for (var i = start + 1; i < sql.Length; i++)
        {
            if (sql[i] != ']')
            {
                continue;
            }

            if (i + 1 < sql.Length && sql[i + 1] == ']')
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }
}
