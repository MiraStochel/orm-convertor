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
/// Only round and curly brackets are counted, because only those are unambiguous in all five
/// languages. Java's <c>&lt;</c> and <c>&gt;</c> are a comparison in an initializer and a type
/// argument in a declaration, so generic nesting is counted where the context is known - in
/// the Java reader itself - against the same number.
/// </summary>
public static class NestingDepthGuard
{
    /// <summary>
    /// The first token at which the input nests deeper than the cap allows, or null when it
    /// never does. An unmatched closing bracket lowers the depth no further than zero, so a
    /// syntactically broken input is still measured rather than made to look shallow; saying
    /// that it is broken is the parser's job, not this one's.
    /// </summary>
    public static SourceToken? FirstBeyond(IEnumerable<SourceToken> tokens, ParseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(limits);

        if (!limits.CapsNesting)
        {
            return null;
        }

        var depth = 0;

        foreach (var token in tokens)
        {
            switch (token.Text)
            {
                case "(":
                case "{":
                    if (++depth > limits.MaxNestingDepth)
                    {
                        return token;
                    }

                    break;

                case ")":
                case "}":
                    if (depth > 0)
                    {
                        depth--;
                    }

                    break;
            }
        }

        return null;
    }

    /// <summary>
    /// The one sentence all five languages refuse with, so that the reason reads the same
    /// whichever parser met it, and carries the line and the column S7 asks the UI to show.
    /// </summary>
    public static string Reason(SourceToken token, ParseLimits limits)
        => $"The input nests deeper at line {token.Line}, column {token.Column} than the "
            + $"{limits.MaxNestingDepth} levels this instance reads; nothing was translated.";
}
