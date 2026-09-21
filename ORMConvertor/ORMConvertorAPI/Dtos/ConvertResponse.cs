using AbstractWrappers;
using AbstractWrappers.Diagnostics;
using DatabaseCatalog;
using Model;

namespace ORMConvertorAPI.Dtos;

/// <param name="RunId">Identifier of the conversion run (S6), fresh on every call.</param>
/// <param name="ToolVersion">Version of ORMConvertor itself, read from the assembly. S6
/// asks the run record to carry versions and S2 makes determinism conditional on the
/// version of the tool, so the record names the tool alongside both frameworks.</param>
/// <param name="SourceFrameworkVersion">Framework release the source was read against,
/// from the source framework's descriptor (decision 013).</param>
/// <param name="TargetFrameworkVersion">Framework release the artifacts are valid
/// against, from the target framework's descriptor (decision 013).</param>
/// <param name="TargetDatabaseDialect">Database system the artifacts are written for,
/// from the target framework's descriptor (decision 086). It is what the literal column
/// types in the output hold against, and until now the caller had to assume it.</param>
/// <param name="DeclaredSourceDialect">What the source declared about the dialect of its
/// own literal SQL (decision 088), or null where it declared nothing. The two are different
/// facts - read as T-SQL because the source said so, read as T-SQL because nobody said
/// anything - and the run record is where they are told apart (S6).</param>
/// <param name="CatalogState">State of the catalog connection during the completion
/// phase. The connection lives in server configuration and the interface only shows its
/// state (decision 030), so this field is how a user learns whether the translation had
/// the catalog at all.</param>
/// <param name="CatalogReadMilliseconds">Duration of the catalog completion phase
/// (decision 015), reported separately from translation time (S3); null when the phase
/// had nothing to do.</param>
/// <param name="MaxNestingDepth">How deep this instance let the parsers read, or 0 where
/// the operator switched the cap off (decision 092). It stands beside the tool version for
/// the same reason that one does: S2 promises determinism for the same version of the tool,
/// and a movable cap is the second thing the answer depends on.</param>
public record ConvertResponse(
    Guid RunId,
    string ToolVersion,
    ORMEnum SourceFramework,
    string SourceFrameworkVersion,
    ORMEnum TargetFramework,
    string TargetFrameworkVersion,
    DatabaseDialect TargetDatabaseDialect,
    List<ConversionSource> Sources,
    List<ConversionRecord> Records,
    CatalogConnectionState CatalogState,
    double? CatalogReadMilliseconds = null,
    SourceSqlDialect? DeclaredSourceDialect = null,
    int MaxNestingDepth = ParseLimits.DefaultMaxNestingDepth);
