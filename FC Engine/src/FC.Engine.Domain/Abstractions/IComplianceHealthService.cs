using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IComplianceHealthService
{
    Task<ComplianceHealthScore> GetCurrentScore(Guid tenantId, CancellationToken ct = default);
    Task<ChsDashboardData> GetDashboard(Guid tenantId, CancellationToken ct = default);
    Task<ChsTrendData> GetTrend(Guid tenantId, int periods = 12, CancellationToken ct = default);
    Task<ChsPeerComparison> GetPeerComparison(Guid tenantId, CancellationToken ct = default);
    Task<List<ChsAlert>> GetAlerts(Guid tenantId, CancellationToken ct = default);

    Task<SectorChsSummary> GetSectorSummary(string regulatorCode, CancellationToken ct = default);
    Task<List<ChsWatchListItem>> GetWatchList(string regulatorCode, CancellationToken ct = default);
    Task<List<ChsHeatmapItem>> GetSectorHeatmap(string regulatorCode, CancellationToken ct = default);
}
