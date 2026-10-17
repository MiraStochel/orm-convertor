using Model;

namespace ORMConvertorAPI.Dtos.Advisor;

/// <summary>
/// Advisor response containing the recommended framework selection, the collected
/// benchmark measurements for each query/framework pair, and the translations those
/// measurements were taken of (decision 059).
/// </summary>
public record AdvisorRunResult(
    IReadOnlyList<ORMEnum> SelectedFrameworks,
    IReadOnlyDictionary<string, ORMEnum> QueryAssignments,
    IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, BenchmarkMeasurementDto>> Measurements,
    IReadOnlyDictionary<ORMEnum, AdvisorTranslationsDto> Translations,
    BenchmarkPolicyDto Policy
);

/// <summary>
/// The iteration policy the run measured under (decision 145), for the record of the run:
/// the values are constants of the methodology, not of the request.
/// </summary>
public sealed record BenchmarkPolicyDto(
    double WarmupTolerance,
    int MaxWarmupIterations,
    int MinIterations,
    double TargetMilliseconds);

/// <summary>
/// One framework's translated artifacts exactly as the run compiled and measured them
/// (decision 059): the entity artifacts once - each query's conversion of the same entity
/// inputs emits the same ones - and each query's own artifacts under the id the
/// measurements use. Keyed like <c>Measurements</c>: every candidate framework, not only
/// the selected ones.
/// </summary>
public sealed record AdvisorTranslationsDto(
    IReadOnlyList<ConversionSource> Entities,
    IReadOnlyDictionary<string, IReadOnlyList<ConversionSource>> Queries
);

/// <summary>
/// One measured cell (decision 145): the median time of one execution, the mean bytes one
/// execution allocated, and the warm-up and measured executions behind them.
/// </summary>
public sealed record BenchmarkMeasurementDto(
    double MedianDurationMilliseconds,
    long AllocatedBytes,
    int WarmupIterations,
    int Iterations);
