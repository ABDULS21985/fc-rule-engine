using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Executes policy impact simulations across all supervised entities' return data.
/// Each run is immutable once completed.
/// </summary>
public interface IImpactAssessmentEngine
{
    Task<ImpactAssessmentResult> RunAssessmentAsync(
        long scenarioId,
        int regulatorId,
        int userId,
        CancellationToken ct = default);

    Task<ImpactAssessmentResult> GetRunResultAsync(
        long runId,
        int regulatorId,
        CancellationToken ct = default);

    Task<PagedResult<EntityImpactSummary>> GetEntityResultsAsync(
        long runId,
        int regulatorId,
        ImpactCategory? categoryFilter,
        string? entityTypeFilter,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<ScenarioComparisonResult> CompareRunsAsync(
        IReadOnlyList<long> runIds,
        int regulatorId,
        CancellationToken ct = default);
}
