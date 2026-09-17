using System.Globalization;
using System.Text;

namespace JavaEntityParsing;

public enum JavaTokenKind
{
    Identifier,
    Number,
    String,
    Char,
    Symbol,
    End,
}

/// <summary>
/// One token of a Java source. <see cref="Offset"/> and <see cref="Length"/> address the
/// original text, so that a reader can hand back the exact spelling of an initializer or
/// an annotation argument instead of a re-joined approximation.
/// </summary>
public readonly record struct JavaToken(JavaTokenKind Kind, string Text, int Line, int Column, int Offset, int Length);

/// <summary>
/// A shape the reader does not understand. Carries a line and a column, which is what S7
/// asks the interface to show and what the Dapper parser gets from TSql160Parser; the
/// parser may fail to understand, never understand differently (decisions 062 and 076).
/// </summary>
public sealed class JavaSyntaxError(int line, int column, string message) : Exception(message)
{
    public int Line { get; } = line;

    public int Column { get; } = column;
}

/// <summary>
/// The lexical grammar of Java, whole (decision 076): comments, string and character
/// literals with their escapes, text blocks, unicode escapes and numeric literals in every
/// spelling. Whole on purpose, while the reader above it covers only a subset of the
/// syntax: a method body the reader skips by brace matching may hold a brace inside a
/// string or a comment, and only a complete lexer keeps such a brace from counting.
/// </summary>
public static class JavaLexer
{
    public static List<JavaToken> Lex(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var text = TranslateUnicodeEscapes(source);
        var tokens = new List<JavaToken>();
        int line = 1, column = 1, i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (c == '\n')
            {
                line++;
                column = 1;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                column++;
                i++;
                continue;
            }

            int startLine = line, startColumn = column, start = i;

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                    column++;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                column += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    Step(text, ref i, ref line, ref column);
                }

                if (i >= text.Length)
                {
                    throw new JavaSyntaxError(startLine, startColumn, "unterminated comment");
                }

                i += 2;
                column += 2;
                continue;
            }

            if (c == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
            {
                var value = LexTextBlock(text, ref i, ref line, ref column, startLine, startColumn);
                tokens.Add(new JavaToken(JavaTokenKind.String, value, startLine, startColumn, start, i - start));
                continue;
            }

            if (c == '"')
            {
                var value = LexQuoted(text, '"', ref i, ref line, ref column, startLine, startColumn, "string literal");
                tokens.Add(new JavaToken(JavaTokenKind.String, value, startLine, startColumn, start, i - start));
                continue;
            }

            if (c == '\'')
            {
                var value = LexQuoted(text, '\'', ref i, ref line, ref column, startLine, startColumn, "character literal");
                tokens.Add(new JavaToken(JavaTokenKind.Char, value, startLine, startColumn, start, i - start));
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
            {
                LexNumber(text, ref i, ref column);
                tokens.Add(new JavaToken(JavaTokenKind.Number, text[start..i], startLine, startColumn, start, i - start));
                continue;
            }

            if (IsIdentifierStart(c))
            {
                while (i < text.Length && IsIdentifierPart(text[i]))
                {
                    i++;
                    column++;
                }

                tokens.Add(new JavaToken(JavaTokenKind.Identifier, text[start..i], startLine, startColumn, start, i - start));
                continue;
            }

            // Three- and two-character operators the reader tells apart from their prefixes;
            // everything else is one character, generics included, so that ">>" closing two
            // type arguments never has to be split again.
            var symbol = i + 2 < text.Length && text.Substring(i, 3) == "..." ? "..."
                : i + 1 < text.Length && text.Substring(i, 2) is "::" or "->" ? text.Substring(i, 2)
                : c.ToString();

            if (symbol.Length == 1 && !IsSymbol(c))
            {
                throw new JavaSyntaxError(startLine, startColumn, $"unexpected character '{c}'");
            }

            tokens.Add(new JavaToken(JavaTokenKind.Symbol, symbol, startLine, startColumn, start, symbol.Length));
            i += symbol.Length;
            column += symbol.Length;
        }

        tokens.Add(new JavaToken(JavaTokenKind.End, string.Empty, line, column, text.Length, 0));
        return tokens;
    }

    private static bool IsSymbol(char c) => "@(){}[];,.=<>+-*/%!?:&|^~".Contains(c);

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '$';

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';

    private static void Step(string text, ref int i, ref int line, ref int column)
    {
        if (text[i] == '\n')
        {
            line++;
            column = 1;
        }
        else
        {
            column++;
        }

        i++;
    }

    /// <summary>
    /// Every numeric spelling Java admits - decimal, hexadecimal, octal and binary, with
    /// underscores, fractions, exponents and type suffixes. The text is kept as written;
    /// what the number means is the reader's question, and only for initializers.
    /// </summary>
    private static void LexNumber(string text, ref int i, ref int column)
    {
        bool hex = text[i] == '0' && i + 1 < text.Length && (text[i + 1] is 'x' or 'X');
        bool binary = text[i] == '0' && i + 1 < text.Length && (text[i + 1] is 'b' or 'B');

        if (hex || binary)
        {
            i += 2;
            column += 2;
        }

        while (i < text.Length)
        {
            char c = text[i];
            bool digit = hex ? char.IsAsciiHexDigit(c) : char.IsAsciiDigit(c);

            if (digit || c == '_' || c == '.'
                || (!hex && (c is 'e' or 'E'))
                || (hex && (c is 'p' or 'P'))
                || ((c is '+' or '-') && i > 0 && (text[i - 1] is 'e' or 'E' or 'p' or 'P'))
                || c is 'l' or 'L' or 'f' or 'F' or 'd' or 'D')
            {
                i++;
                column++;
                continue;
            }

            break;
        }
    }

    private static string LexQuoted(
        string text, char quote, ref int i, ref int line, ref int column, int startLine, int startColumn, string what)
    {
        var value = new StringBuilder();
        i++;
        column++;

        while (true)
        {
            if (i >= text.Length || text[i] == '\n')
            {
                throw new JavaSyntaxError(startLine, startColumn, $"unterminated {what}");
            }

            if (text[i] == quote)
            {
                i++;
                column++;
                return value.ToString();
            }

            if (text[i] == '\\')
            {
                value.Append(ReadEscape(text, ref i, ref column, startLine, startColumn));
                continue;
            }

            value.Append(text[i]);
            i++;
            column++;
        }
    }

    private static string ReadEscape(string text, ref int i, ref int column, int startLine, int startColumn)
    {
        if (i + 1 >= text.Length)
        {
            throw new JavaSyntaxError(startLine, startColumn, "unterminated escape sequence");
        }

        char e = text[i + 1];
        i += 2;
        column += 2;

        switch (e)
        {
            case 'n': return "\n";
            case 't': return "\t";
            case 'r': return "\r";
            case 'b': return "\b";
            case 'f': return "\f";
            case 's': return " ";
            case '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7':
            {
                var octal = new StringBuilder().Append(e);
                while (octal.Length < 3 && i < text.Length && text[i] is >= '0' and <= '7')
                {
                    octal.Append(text[i]);
                    i++;
                    column++;
                }

                return ((char)Convert.ToInt32(octal.ToString(), 8)).ToString();
            }
            case '\\' or '\'' or '"': return e.ToString();
            case '\n':
                // A line continuation inside a text block: the escape and the line break
                // both disappear.
                return string.Empty;
            default:
                throw new JavaSyntaxError(startLine, startColumn, $"unknown escape sequence '\\{e}'");
        }
    }

    /// <summary>
    /// A text block (JLS 3.10.6): the content between the delimiters with the incidental
    /// indentation stripped - the common leading whitespace of the non-blank lines and of
    /// the closing delimiter's line - trailing whitespace removed and escapes translated.
    /// The generated query methods use text blocks, so the parser reading them back has to
    /// get the same string the JVM would.
    /// </summary>
    private static string LexTextBlock(
        string text, ref int i, ref int line, ref int column, int startLine, int startColumn)
    {
        i += 3;
        column += 3;

        // The opening delimiter is followed by optional whitespace and a mandatory line break.
        while (i < text.Length && text[i] is ' ' or '\t' or '\f')
        {
            i++;
            column++;
        }

        if (i >= text.Length || text[i] != '\n')
        {
            if (i + 1 < text.Length && text[i] == '\r' && text[i + 1] == '\n')
            {
                i++;
            }
            else
            {
                throw new JavaSyntaxError(startLine, startColumn, "a text block's opening delimiter has to end its line");
            }
        }

        i++;
        line++;
        column = 1;

        var raw = new StringBuilder();
        while (true)
        {
            if (i >= text.Length)
            {
                throw new JavaSyntaxError(startLine, startColumn, "unterminated text block");
            }

            if (text[i] == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
            {
                i += 3;
                column += 3;
                break;
            }

            if (text[i] == '\\')
            {
                // Escapes are translated after the indentation is stripped, so the raw
                // text keeps them; the pair of characters is copied as it stands.
                raw.Append(text[i]);
                i++;
                column++;
                if (i < text.Length)
                {
                    raw.Append(text[i]);
                    Step(text, ref i, ref line, ref column);
                }

                continue;
            }

            raw.Append(text[i]);
            Step(text, ref i, ref line, ref column);
        }

        var lines = raw.ToString().Replace("\r\n", "\n").Split('\n');

        // The last line holds the closing delimiter's indentation when the delimiter stood
        // on its own line; it counts for the common indentation and contributes no content.
        var significant = lines
            .Select((l, index) => (Line: l, Last: index == lines.Length - 1))
            .Where(x => x.Last || x.Line.Trim().Length > 0)
            .Select(x => x.Line.Length - x.Line.TrimStart(' ', '\t').Length)
            .DefaultIfEmpty(0)
            .Min();

        var content = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var l = lines[index];
            var stripped = l.Length >= significant ? l[significant..] : l.TrimStart(' ', '\t');
            stripped = stripped.TrimEnd(' ', '\t', '\f');

            if (index == lines.Length - 1)
            {
                // Content before the delimiter on its line stays; a delimiter on its own
                // line adds nothing, and the line break before it is the block's last.
                if (stripped.Length > 0)
                {
                    content.Append(stripped);
                }
                else if (content.Length > 0)
                {
                    content.Length--;
                }

                break;
            }

            content.Append(stripped).Append('\n');
        }

        return TranslateEscapes(content.ToString(), startLine, startColumn);
    }

    private static string TranslateEscapes(string text, int line, int column)
    {
        var result = new StringBuilder();
        int i = 0, dummy = 0;

        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                result.Append(ReadEscape(text, ref i, ref dummy, line, column));
                continue;
            }

            result.Append(text[i]);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// JLS 3.3: unicode escapes are translated before any other lexical step. A backslash
    /// is eligible only when preceded by an even number of backslashes, and more than one
    /// 'u' is allowed.
    /// </summary>
    private static string TranslateUnicodeEscapes(string source)
    {
        if (!source.Contains("\\u", StringComparison.Ordinal))
        {
            return source;
        }

        var result = new StringBuilder(source.Length);
        int i = 0, backslashes = 0;

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '\\' && backslashes % 2 == 0 && i + 1 < source.Length && source[i + 1] == 'u')
            {
                int j = i + 1;
                while (j < source.Length && source[j] == 'u')
                {
                    j++;
                }

                if (j + 4 <= source.Length
                    && int.TryParse(source.AsSpan(j, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                {
                    result.Append((char)code);
                    i = j + 4;
                    backslashes = 0;
                    continue;
                }
            }

            backslashes = c == '\\' ? backslashes + 1 : 0;
            result.Append(c);
            i++;
        }

        return result.ToString();
    }
}
