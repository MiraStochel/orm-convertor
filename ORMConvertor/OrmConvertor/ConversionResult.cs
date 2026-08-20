using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;

namespace OrmConvertor;

/// <summary>
/// What a conversion returns: the generated artifacts and the diagnostic records that arose
/// while producing them. Records are data, not exceptions - a conversion that lost or
/// refused something still returns normally and says so here (decision 010). Exceptions
/// stay reserved for program errors: an unsupported framework, input that cannot be parsed.
/// </summary>
public sealed class ConversionResult
{
    /// <summary>
    /// Identifier of this run (S6). Fresh on every call, so two runs over the same input
    /// stay distinguishable wherever the result is stored or logged.
    /// </summary>
    public required Guid RunId { get; init; }

    public required ORMEnum SourceFramework { get; init; }

    /// <summary>
    /// Framework release the source was read against, taken from its descriptor
    /// (decision 013) - the record cannot claim a version the parser did not assume.
    /// </summary>
    public required string SourceFrameworkVersion { get; init; }

    public required ORMEnum TargetFramework { get; init; }

    /// <summary>
    /// Framework release the artifacts are valid against, taken from the target's
    /// descriptor (decision 013) - the record cannot claim a version the generator
    /// did not use.
    /// </summary>
    public required string TargetFrameworkVersion { get; init; }

    public required List<ConversionSource> Sources { get; init; }

    public required List<ConversionRecord> Records { get; init; }

    /// <summary>
    /// State of the catalog connection during the completion phase. The connection lives
    /// in server configuration and the interface only shows its state (decision 030); the
    /// records carry the same fact, but only as one entry among many, so the caller gets
    /// it here as a field of its own.
    /// </summary>
    public required CatalogConnectionState CatalogState { get; init; }

    /// <summary>
    /// How long the catalog completion phase took (decision 015), reported separately
    /// from translation time as S3 asks. Null when the phase had nothing to do - an
    /// empty demand or no configured connection means zero queries.
    /// </summary>
    public TimeSpan? CatalogReadTime { get; init; }
}
