using AbstractWrappers.Descriptors;
using Model;

namespace AbstractWrappers.Diagnostics;

/// <summary>
/// One structured diagnostic record of a conversion, returned next to the artifacts rather
/// than thrown or logged (decision 010). Carries what F11 asks for: the target framework,
/// the artifact, the entity and property concerned, the category of the mapping fact, and
/// the reason.
/// </summary>
public sealed class ConversionRecord
{
    public required ConversionRecordKind Kind { get; init; }

    /// <summary>Target framework of the conversion the record belongs to.</summary>
    public required ORMEnum Framework { get; init; }

    /// <summary>
    /// The artifact concerned; null when the record concerns the entity as a whole rather
    /// than one of its artifacts.
    /// </summary>
    public ConversionContentType? Artifact { get; init; }

    public string? Entity { get; init; }

    public string? Property { get; init; }

    /// <summary>
    /// Category of the mapping fact; null for facts outside the descriptor's vocabulary,
    /// such as the language type of a property.
    /// </summary>
    public MappingFactCategory? Category { get; init; }

    /// <summary>
    /// Query capability the record concerns; null for records of the entity branch. A query
    /// loss is about an instruction, not a mapping fact, so it needs its own vocabulary
    /// (decision 022).
    /// </summary>
    public QueryFeature? Feature { get; init; }

    public required string Reason { get; init; }
}
