namespace Tests.Differential;

/// <summary>
/// The rows both ecosystems render to prove they render alike (decision 089). The list is
/// code rather than data on purpose: every entry has to be expressible in both languages,
/// and a value written into a file would only prove that both suites can read a file.
///
/// The counterpart is <c>RendererConformance.java</c>; the expected text is
/// <c>renderer-conformance.txt</c> beside the schema script, and it is checked in rather
/// than recorded, so neither suite can move the target by re-running itself.
///
/// Each entry is one row. Most carry a single field, because what is being pinned down is
/// one rule each; the last one carries five, because the separator and a null among real
/// values are rules of their own.
/// </summary>
internal static class RendererConformance
{
    /// <summary>The settings the conformance rows are rendered with - the defaults of decision 089.</summary>
    public static ResultRow.RenderSettings Settings => new();

    public static IReadOnlyList<object?[]> Rows =>
    [
        [null],
        [true],
        [false],
        [0],
        [-42],
        [9007199254740993L],

        // Exact decimals: the stated scale is kept even when the value has fewer digits,
        // and a tie is resolved half to even in both directions - the digit before the tie
        // is even in the first case and odd in the second.
        [0m],
        [1250.5m],
        [1.0000005m],
        [1.0000015m],

        // Approximate numbers: trailing zeros go, so that one ecosystem cannot write 3.0
        // where the other writes 3. Every value here is exact in binary floating point, so
        // the case under test is the rendering and not the arithmetic.
        [3.0d],
        [2.5d],
        [0.5d],
        [-1.75d],
        [0.0d],

        ["plain"],
        ["a\"b\\c\t\n\r"],
        [string.Empty],

        [new DateTime(2026, 1, 2, 3, 4, 5, 678)],
        [new DateOnly(2024, 6, 10)],

        [1, "Zither", 1250.5m, null, true],
    ];
}
