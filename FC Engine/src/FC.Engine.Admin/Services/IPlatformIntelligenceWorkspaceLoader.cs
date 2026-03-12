using FC.Engine.Infrastructure.Services;

namespace FC.Engine.Admin.Services;

public interface IPlatformIntelligenceWorkspaceLoader
{
    Task<PlatformIntelligenceWorkspace> GetWorkspaceAsync(CancellationToken ct = default);
    Task<SanctionsScreeningSessionState> GetSanctionsScreeningSessionStateAsync(CancellationToken ct = default);
    Task<SanctionsWorkflowState> GetSanctionsWorkflowStateAsync(CancellationToken ct = default);
    Task<SanctionsStrDraftCatalogState> GetSanctionsStrDraftCatalogStateAsync(CancellationToken ct = default);
    Task<DashboardBriefingPackCatalogState> GetDashboardBriefingPackCatalogStateAsync(
        string lens,
        int? institutionId,
        CancellationToken ct = default);
    Task<DashboardBriefingPackCatalogState> MaterializeDashboardBriefingPackAsync(
        string lens,
        int? institutionId,
        IReadOnlyList<DashboardBriefingPackSectionInput> sections,
        CancellationToken ct = default);
}
