namespace AbstractWrappers;

/// <summary>
/// A range of characters in a text the tool read or wrote: the offset of its first character
/// and its length (decision 125). Offsets count characters of the unit as the client sent it,
/// or of the artifact as the tool returns it, so that the client can find the range without
/// knowing anything about how the text was parsed.
/// </summary>
public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;

    /// <summary>The range from one offset to another, exclusive.</summary>
    public static TextRange Between(int start, int end) => new(start, Math.Max(0, end - start));
}
