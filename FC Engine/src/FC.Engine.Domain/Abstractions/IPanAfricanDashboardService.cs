using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IPanAfricanDashboardService
{
    Task<GroupComplianceOverview?> GetGroupOverviewAsync(
        int groupId, CancellationToken ct = default);

    Task<IReadOnlyList<SubsidiaryComplianceSnapshot>> GetSubsidiarySnapshotsAsync(
        int groupId, string? reportingPeriod, CancellationToken ct = default);

    Task<IReadOnlyList<RegulatoryDeadlineDto>> GetDeadlineCalendarAsync(
        int groupId, DateOnly fromDate, DateOnly toDate,
        CancellationToken ct = default);

    Task<CrossBorderRiskMetrics?> GetConsolidatedRiskMetricsAsync(
        int groupId, string reportingPeriod, CancellationToken ct = default);
}
