using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// After policy enactment, tracks predicted vs actual impact over time.
/// Runs monthly as a background job.
/// </summary>
public interface IHistoricalImpactTracker
{
    Task RunTrackingCycleAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PredictedVsActual>> GetTrackingHistoryAsync(
        long decisionId,
        int regulatorId,
        CancellationToken ct = default);

    Task<decimal> GetAccuracyScoreAsync(
        long decisionId,
        int regulatorId,
        CancellationToken ct = default);
}
