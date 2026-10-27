using System.Runtime.CompilerServices;

namespace AbstractWrappers;

/// <summary>
/// The dam of decision 191 for the descent no count can stop in time: a reader that walks the
/// tree of a grammar recursively - the T-SQL reader over ScriptDom's fragments, the LINQ reader
/// over Roslyn's syntax, the HQL and JPQL parsers over their own tokens - descends as deep as a
/// chain is long before the model exists to be measured. Where the stack is nearly spent, the
/// reader throws <see cref="InsufficientExecutionStackException"/> instead of overflowing, which
/// .NET cannot catch, and the orchestration turns it into the refusal of the unit.
/// </summary>
public static class ReaderStack
{
    /// <summary>Throws <see cref="InsufficientExecutionStackException"/> where the rest of the stack would not carry one more level of the descent.</summary>
    public static void Ensure() => RuntimeHelpers.EnsureSufficientExecutionStack();

    /// <summary>The sentence a unit is refused with when its reading ran out of stack - the reader's, or a grammar's that guards itself the same way, as Roslyn does.</summary>
    public const string Reason =
        "The input nests deeper than this instance can read without running out of stack; nothing of the unit was translated.";
}
