using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IConsolidationEngine
{
    Task<ConsolidationResult> RunConsolidationAsync(
        int groupId, string reportingPeriod, DateOnly snapshotDate,
        int userId, CancellationToken ct = default);

    Task<ConsolidationResult?> GetRunResultAsync(
        long runId, int groupId, CancellationToken ct = default);

    Task<IReadOnlyList<ConsolidationSubsidiaryResult>> GetSubsidiarySnapshotsAsync(
        long runId, int groupId, CancellationToken ct = default);

    Task<IReadOnlyList<ConsolidationAdjustmentDto>> GetAdjustmentsAsync(
        long runId, int groupId, CancellationToken ct = default);

    Task AddManualAdjustmentAsync(
        long runId, int groupId, ConsolidationAdjustmentInput adjustment,
        int userId, CancellationToken ct = default);
}
