using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Aggregates sector-wide prudential metrics into systemic risk indicators.
/// Computes breach counts, systemic risk score (0–100), and systemic band classification.
/// </summary>
public interface ISystemicRiskAggregator
{
    Task<SystemicRiskIndicators> AggregateAsync(
        string regulatorCode,
        string institutionType,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);
}
