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
/// <param name="CatalogState">State of the catalog connection during the completion
/// phase. The connection lives in server configuration and the interface only shows its
/// state (decision 030), so this field is how a user learns whether the translation had
/// the catalog at all.</param>
/// <param name="CatalogReadMilliseconds">Duration of the catalog completion phase
/// (decision 015), reported separately from translation time (S3); null when the phase
/// had nothing to do.</param>
public record ConvertResponse(
    Guid RunId,
    string ToolVersion,
    ORMEnum SourceFramework,
    string SourceFrameworkVersion,
    ORMEnum TargetFramework,
    string TargetFrameworkVersion,
    List<ConversionSource> Sources,
    List<ConversionRecord> Records,
    CatalogConnectionState CatalogState,
    double? CatalogReadMilliseconds = null);
