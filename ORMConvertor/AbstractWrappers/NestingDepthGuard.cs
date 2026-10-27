namespace AbstractWrappers;

/// <summary>
/// One token of an input, reduced to what the nesting guard needs to see. Every parser has a
/// token type of its own and none of them is shared; this is the little they have in common,
/// so that the guard can be shared even though the lexers are not (decision 092).
/// </summary>
public readonly record struct SourceToken(string Text, int Line, int Column);

/// <summary>
/// The depth cap of decision 092, enforced over a token stream before any descent begins.
///
/// Why over tokens rather than inside the recursion: two of the five languages are read by
/// grammars whose recursion is not ours to count - <c>TSql160Parser</c> and Roslyn - but both
/// offer their lexer separately, and lexing is a loop in all five. It is therefore the only
/// place where one rule can hold for all of them, and a guard that asks before the descent
/// cannot be evaded by a shape of the descent nobody foresaw.
///
/// Round and curly brackets are counted in all five languages, because only those are
/// unambiguous in all of them. Java's <c>&lt;</c> and <c>&gt;</c> are a comparison in an
/// initializer and a type argument in a declaration, so generic nesting is counted where the
/// context is known - in the Java reader itself - against the same number.
///
/// Since decision 191 two more nestings count that the text writes without a bracket and a
/// grammar descends recursively all the same: a <c>CASE</c> up to its <c>END</c> in the three
/// query languages - T-SQL's grammar overflowed on CASE nested in ELSE, past the reach of any
/// guard of ours -, where <c>case</c> is not also a label of a switch as in C# and Java; and
/// a run of prefix operators in all five - <c>NOT NOT …</c>, <c>!!…</c>, <c>- - …</c> -, each
/// one level deeper than the one before, while the run lasts.
/// </summary>
public static class NestingDepthGuard
{
    /// <summary>
    /// The first token at which the input nests deeper than the cap allows, or null when it
    /// never does. An unmatched closing bracket lowers the depth no further than zero, so a
    /// syntactically broken input is still measured rather than made to look shallow; saying
    /// that it is broken is the parser's job, not this one's.
    /// </summary>
    /// <param name="caseNests">Whether <c>CASE</c> opens a level that <c>END</c> closes: true for SQL, HQL and JPQL.</param>
    public static SourceToken? FirstBeyond(IEnumerable<SourceToken> tokens, ParseLimits limits, bool caseNests = false)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(limits);

        if (!limits.CapsNesting)
        {
            return null;
        }

        var brackets = 0;
        var cases = 0;
        var run = 0;

        foreach (var token in tokens)
        {
            if (IsPrefixOperator(token.Text))
            {
                if (brackets + cases + ++run > limits.MaxNestingDepth)
                {
                    return token;
                }

                continue;
            }

            run = 0;

            switch (token.Text)
            {
                case "(":
                case "{":
                    if (++brackets + cases > limits.MaxNestingDepth)
                    {
                        return token;
                    }

                    break;

                case ")":
                case "}":
                    if (brackets > 0)
                    {
                        brackets--;
                    }

                    break;

                default:
                    if (!caseNests)
                    {
                        break;
                    }

                    if (string.Equals(token.Text, "case", StringComparison.OrdinalIgnoreCase))
                    {
                        if (brackets + ++cases > limits.MaxNestingDepth)
                        {
                            return token;
                        }
                    }
                    else if (string.Equals(token.Text, "end", StringComparison.OrdinalIgnoreCase) && cases > 0)
                    {
                        cases--;
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>A token that applies to the operand after it: a negation, a sign, a bitwise complement. A token without text - ScriptDom's end of file - is none.</summary>
    private static bool IsPrefixOperator(string? text)
        => text is "!" or "-" or "+" or "~" || string.Equals(text, "not", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one sentence all five languages refuse with, so that the reason reads the same
    /// whichever parser met it, and carries the line and the column S7 asks the UI to show.
    /// </summary>
    public static string Reason(SourceToken token, ParseLimits limits)
        => $"The input nests deeper at line {token.Line}, column {token.Column} than the "
            + $"{limits.MaxNestingDepth} levels this instance reads; nothing was translated.";
}
