using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EFCoreWrappers;

/// <summary>
/// The text EF Core is handed with a native query (decision 113), as T-SQL the shared reader
/// takes: an interpolated string - <c>SqlQuery</c>, <c>FromSql</c>, <c>FromSqlInterpolated</c>,
/// the form the escape path writes - with every hole a parameter of the name of the value it
/// captures, and the composite format of <c>SqlQueryRaw</c> and <c>FromSqlRaw</c> with every
/// <c>{n}</c> a parameter named after the value passed in its place. A value computed where it
/// stands - an expression in a hole, a format or an alignment, an argument that is not a plain
/// name - has no parameter of the model to be (decision 083), and a <c>@</c> the text writes
/// itself is a variable EF Core binds to nothing; both are refused by name.
/// </summary>
internal static class EFCoreNativeSqlReading
{
    /// <summary>The T-SQL of an interpolated or a plain string argument, or null with the reason.</summary>
    public static string? Interpolated(ExpressionSyntax argument, out string? unread)
    {
        unread = null;

        if (argument is LiteralExpressionSyntax)
        {
            var text = LinqText(argument);
            return text is null ? NotALiteral(out unread) : Checked(text, [], out unread);
        }

        if (argument is not InterpolatedStringExpressionSyntax interpolated)
        {
            return NotALiteral(out unread);
        }

        var sql = new StringBuilder();
        var holes = new List<int>();

        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    sql.Append(text.TextToken.ValueText);
                    break;

                case InterpolationSyntax { Expression: IdentifierNameSyntax name, AlignmentClause: null, FormatClause: null }:
                    holes.Add(sql.Length);
                    sql.Append('@').Append(name.Identifier.Text);
                    break;

                case InterpolationSyntax hole:
                    unread = $"the hole {{{hole.Expression}}} of the interpolated text holds a value computed where it stands, not a name the query could take as a parameter";
                    return null;
            }
        }

        return Checked(sql.ToString(), holes, out unread);
    }

    /// <summary>
    /// The T-SQL of the composite format SqlQueryRaw and FromSqlRaw take: every <c>{n}</c> the
    /// parameter named after the value passed in its place, a doubled brace one brace.
    /// </summary>
    public static string? Raw(SeparatedSyntaxList<ArgumentSyntax> arguments, out string? unread)
    {
        unread = null;

        if (LinqText(arguments[0].Expression) is not { } format)
        {
            return NotALiteral(out unread);
        }

        var values = arguments.Skip(1).Select(argument => argument.Expression).ToList();
        var sql = new StringBuilder(format.Length);
        var holes = new List<int>();

        for (var i = 0; i < format.Length; i++)
        {
            var character = format[i];

            if ((character == '{' || character == '}') && i + 1 < format.Length && format[i + 1] == character)
            {
                sql.Append(character);
                i++;
                continue;
            }

            if (character != '{')
            {
                sql.Append(character);
                continue;
            }

            var close = format.IndexOf('}', i);
            if (close < 0
                || !int.TryParse(format.AsSpan(i + 1, close - i - 1), out var index)
                || index < 0 || index >= values.Count)
            {
                unread = $"the format item at position {i} of the text is not a number of one of the values passed with it";
                return null;
            }

            if (values[index] is not IdentifierNameSyntax name)
            {
                unread = $"the value passed for {{{index}}} is {values[index]}, a value computed where it stands, not a name the query could take as a parameter";
                return null;
            }

            holes.Add(sql.Length);
            sql.Append('@').Append(name.Identifier.Text);
            i = close;
        }

        return Checked(sql.ToString(), holes, out unread);
    }

    private static string? LinqText(ExpressionSyntax expression)
        => expression is LiteralExpressionSyntax literal && literal.Token.Value is string text ? text : null;

    private static string? NotALiteral(out string? unread)
    {
        unread = "the text is not a string literal - it is composed at run time";
        return null;
    }

    /// <summary>
    /// The text, where every <c>@</c> outside a string literal is one the reading put there
    /// for a parameter; null with the reason where the text writes a variable of its own.
    /// </summary>
    private static string? Checked(string sql, List<int> holes, out string? unread)
    {
        unread = null;
        var insideLiteral = false;

        for (var i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '\'')
            {
                insideLiteral = !insideLiteral;
            }
            else if (!insideLiteral && sql[i] == '@' && !holes.Contains(i))
            {
                var end = i + 1;
                while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] is '_' or '@'))
                {
                    end++;
                }

                unread = $"the text writes the T-SQL variable {sql[i..end]}, which EF Core binds to nothing";
                return null;
            }
        }

        return sql;
    }
}
