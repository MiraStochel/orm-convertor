namespace Model;

public class ConversionSource
{
    public required ConversionContentType ContentType { get; init; }

    public required string Content { get; init; }

    /// <summary>
    /// Optional label of an input unit as the client knows it - typically the file name.
    /// The tool treats it as opaque: records reference the unit by it (decision 066),
    /// nothing is derived from it. Generated artifacts do not carry one; pairing output
    /// with input is a separate open item.
    /// </summary>
    public string? Name { get; init; }
}
