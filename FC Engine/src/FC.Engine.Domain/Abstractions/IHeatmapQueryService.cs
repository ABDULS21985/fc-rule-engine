using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Read-model query service for the regulator portal: sector heatmap,
/// institution EWI history, and correlation matrix.
/// </summary>
public interface IHeatmapQueryService
{
    Task<IReadOnlyList<HeatmapCell>> GetSectorHeatmapAsync(
        string regulatorCode,
        string periodCode,
        string? institutionTypeFilter,
        CancellationToken ct = default);

    Task<IReadOnlyList<EWITriggerRow>> GetInstitutionEWIHistoryAsync(
        int institutionId,
        string regulatorCode,
        int periods,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a Pearson correlation matrix of CAR time-series for all
    /// institutions of the given type over the last 24 months.
    /// </summary>
    Task<double[][]> GetCorrelationMatrixAsync(
        string regulatorCode,
        string institutionType,
        string periodCode,
        CancellationToken ct = default);
}
