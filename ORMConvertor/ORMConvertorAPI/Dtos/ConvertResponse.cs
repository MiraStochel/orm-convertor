using AbstractWrappers.Diagnostics;
using Model;

namespace ORMConvertorAPI.Dtos;

public record ConvertResponse(List<ConversionSource> Sources, List<ConversionRecord> Records);
