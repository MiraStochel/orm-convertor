namespace Model;

public class ConversionSource
{
    public required ConversionContentType ContentType { get; init; }

    public required string Content { get; init; }

    /// <summary>
    /// Optional label of an input unit as the client knows it - typically the file name.
    /// The tool treats it as opaque: records reference the unit by it (decision 066),
    /// nothing is derived from it.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// On a generated artifact: the simple name of the entity it was generated for; null on
    /// an input unit and on a query artifact, which belongs to no entity (decision 124).
    /// </summary>
    public string? Entity { get; init; }

    /// <summary>
    /// On a generated artifact: the input units it came from, referenced as records reference
    /// units (decision 066) - the units that declared or enriched the entity, or the one unit
    /// a query was read from. Empty for an entity no unit declared, such as a synthesized
    /// junction entity; null on an input unit. Stated by the orchestration and the builder
    /// base, never guessed from names (decision 124).
    /// </summary>
    public IReadOnlyList<string>? Units { get; init; }
}
