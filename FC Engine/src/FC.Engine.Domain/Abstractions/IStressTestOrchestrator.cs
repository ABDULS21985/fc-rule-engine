using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IStressTestOrchestrator
{
    Task<StressTestRunSummary> RunAsync(
        string regulatorCode,
        int    scenarioId,
        string periodCode,
        string timeHorizon,
        int    initiatedByUserId,
        CancellationToken ct = default);

    Task<StressTestRunSummary?> GetRunSummaryAsync(
        Guid runGuid,
        CancellationToken ct = default);

    Task<IReadOnlyList<EntityShockResult>> GetEntityResultsAsync(
        long runId,
        string? institutionTypeFilter,
        CancellationToken ct = default);
}
