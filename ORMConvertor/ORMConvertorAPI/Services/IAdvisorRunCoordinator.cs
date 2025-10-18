using ORMConvertorAPI.Dtos.Advisor;

namespace ORMConvertorAPI.Services;

/// <summary>
/// Minimal contract for running the advisor pipeline end-to-end.
/// </summary>
public interface IAdvisorRunCoordinator
{
    Task<AdvisorRunResult> RunAsync(
        AdvisorRunRequest request,
        CancellationToken cancellationToken = default);
}
