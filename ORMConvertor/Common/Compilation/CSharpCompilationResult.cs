using Microsoft.CodeAnalysis;

namespace Common.Compilation;

/// <summary>
/// Outcome of one compilation of generated C#. The diagnostics are returned, not thrown,
/// because for the verification of generated artifacts (decision 016) a failed compilation
/// is a result to report, not an error of the program.
/// </summary>
public sealed class CSharpCompilationResult
{
    public required bool Success { get; init; }

    /// <summary>Everything the compiler said, warnings included.</summary>
    public required IReadOnlyList<Diagnostic> Diagnostics { get; init; }

    /// <summary>The emitted assembly image, or <c>null</c> when the compilation failed.</summary>
    public byte[]? Assembly { get; init; }

    /// <summary>Portable PDB matching <see cref="Assembly"/>, or <c>null</c> on failure.</summary>
    public byte[]? Symbols { get; init; }

    public IEnumerable<Diagnostic> Errors
        => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
}
