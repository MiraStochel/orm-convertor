using AbstractWrappers.Diagnostics;
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
    public required List<ConversionSource> Sources { get; init; }

    public required List<ConversionRecord> Records { get; init; }
}
