using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IForeSightService
{
    Task<ForeSightDashboardData> GetTenantDashboardAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<ForeSightPredictionSummary>> GetPredictionsAsync(Guid tenantId, string? modelCode = null, CancellationToken ct = default);
    Task<IReadOnlyList<ForeSightAlertItem>> GetAlertsAsync(Guid tenantId, bool unreadOnly = true, CancellationToken ct = default);
    Task MarkAlertReadAsync(int alertId, string userId, CancellationToken ct = default);
    Task DismissAlertAsync(int alertId, string userId, CancellationToken ct = default);
    Task RunAllPredictionsAsync(Guid tenantId, string performedBy = "FORESIGHT", CancellationToken ct = default);
    Task<IReadOnlyList<RegulatoryActionForecast>> GetRegulatoryRiskRankingAsync(string regulatorCode, string? licenceType = null, CancellationToken ct = default);
    Task<IReadOnlyList<ChurnRiskAssessment>> GetChurnRiskDashboardAsync(CancellationToken ct = default);
    Task<byte[]> ExportFilingRiskReportAsync(Guid tenantId, CancellationToken ct = default);
}
