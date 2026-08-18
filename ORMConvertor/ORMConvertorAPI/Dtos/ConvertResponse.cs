using AbstractWrappers.Diagnostics;
using Model;

namespace ORMConvertorAPI.Dtos;

/// <param name="CatalogReadMilliseconds">Duration of the catalog completion phase
/// (decision 015), reported separately from translation time (S3); null when the phase
/// had nothing to do.</param>
public record ConvertResponse(
    List<ConversionSource> Sources,
    List<ConversionRecord> Records,
    double? CatalogReadMilliseconds = null);
