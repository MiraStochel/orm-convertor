using Model;

namespace ORMConvertorAPI.Dtos.Advisor;

/// <summary>
/// Minimal advisor response containing the recommended framework selection.
/// </summary>
public record AdvisorRunResult(
    int Objective,
    IReadOnlyList<ORMEnum> SelectedFrameworks,
    IReadOnlyDictionary<string, ORMEnum> QueryAssignments
);
