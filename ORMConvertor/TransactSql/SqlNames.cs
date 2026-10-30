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
}
