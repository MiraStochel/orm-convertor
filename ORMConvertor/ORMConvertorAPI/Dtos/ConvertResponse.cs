using AbstractWrappers.Diagnostics;
using Model;

namespace ORMConvertorAPI.Dtos;

/// <param name="RunId">Identifier of the conversion run (S6), fresh on every call.</param>
/// <param name="SourceFrameworkVersion">Framework release the source was read against,
/// from the source framework's descriptor (decision 013).</param>
/// <param name="TargetFrameworkVersion">Framework release the artifacts are valid
/// against, from the target framework's descriptor (decision 013).</param>
/// <param name="CatalogReadMilliseconds">Duration of the catalog completion phase
/// (decision 015), reported separately from translation time (S3); null when the phase
/// had nothing to do.</param>
public record ConvertResponse(
    Guid RunId,
    ORMEnum SourceFramework,
    string SourceFrameworkVersion,
    ORMEnum TargetFramework,
    string TargetFrameworkVersion,
    List<ConversionSource> Sources,
    List<ConversionRecord> Records,
    double? CatalogReadMilliseconds = null);
