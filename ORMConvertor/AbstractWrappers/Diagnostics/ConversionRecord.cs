using AbstractWrappers.Descriptors;
using Model;

namespace AbstractWrappers.Diagnostics;

/// <summary>
/// One structured diagnostic record of a conversion, returned next to the artifacts rather
/// than thrown or logged (decision 010). Carries what F11 asks for: the target framework,
/// the artifact, the entity and property concerned, the category of the mapping fact, and
/// the reason. A record type so that the orchestration can attribute a finished record to
/// an input unit by copy (decision 066).
/// </summary>
public sealed record ConversionRecord
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

    /// <summary>
    /// The input unit the record came from: the unit's client-given name, or "unit N" with
    /// the unit's 1-based position in the request where no name was sent. Carried only by
    /// records whose origin is the reading of one unit; a record about the merged entity,
    /// the completion phase or the run names no unit, because several units may
    /// legitimately have declared the entity (decision 066).
    /// </summary>
    public string? Unit { get; init; }

    public required string Reason { get; init; }
}
