using Model;

namespace ORMConvertorAPI.Dtos.Advisor;

/// <summary>
/// Advisor response containing the recommended framework selection and
/// collected benchmark measurements for each query/framework pair.
/// </summary>
public record AdvisorRunResult(
    IReadOnlyList<ORMEnum> SelectedFrameworks,
    IReadOnlyDictionary<string, ORMEnum> QueryAssignments,
    IReadOnlyDictionary<string, IReadOnlyDictionary<ORMEnum, BenchmarkMeasurementDto>> Measurements
);

/// <summary>
/// Lightweight DTO for benchmark results used in the API contract.
/// </summary>
public sealed record BenchmarkMeasurementDto(double MeanDurationMilliseconds, long AllocatedBytes);
