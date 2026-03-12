using FC.Engine.Infrastructure.Services;

namespace FC.Engine.Admin.Services;

public sealed class PlatformIntelligenceRefreshService
{
    private readonly IPlatformIntelligenceWorkspaceLoader _workspaceLoader;
    private readonly DashboardBriefingPackBuilder _dashboardBriefingPackBuilder;

    public PlatformIntelligenceRefreshService(
        IPlatformIntelligenceWorkspaceLoader workspaceLoader,
        DashboardBriefingPackBuilder dashboardBriefingPackBuilder)
    {
        _workspaceLoader = workspaceLoader;
        _dashboardBriefingPackBuilder = dashboardBriefingPackBuilder;
    }

    public async Task<PlatformIntelligenceRefreshResult> RefreshAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        var screeningSessionState = await _workspaceLoader.GetSanctionsScreeningSessionStateAsync(ct);
        var workflowState = await _workspaceLoader.GetSanctionsWorkflowStateAsync(ct);
        var strDraftCatalogState = await _workspaceLoader.GetSanctionsStrDraftCatalogStateAsync(ct);

        var dashboardPacksMaterialized = await MaterializeDashboardBriefingPacksAsync(
            workspace,
            screeningSessionState,
            workflowState,
            strDraftCatalogState,
            ct);

        return new PlatformIntelligenceRefreshResult
        {
            GeneratedAt = workspace.GeneratedAt,
            InstitutionCount = workspace.InstitutionScorecards.Count,
            InterventionCount = workspace.Interventions.Count,
            TimelineCount = workspace.ActivityTimeline.Count,
            DashboardPacksMaterialized = dashboardPacksMaterialized,
            RolloutCatalogMaterializedAt = workspace.Rollout.CatalogMaterializedAt,
            KnowledgeCatalogMaterializedAt = workspace.KnowledgeGraph.CatalogMaterializedAt,
            KnowledgeDossierMaterializedAt = workspace.KnowledgeGraph.DossierMaterializedAt,
            CapitalPackMaterializedAt = workspace.Capital.ReturnPackMaterializedAt,
            SanctionsPackMaterializedAt = workspace.Sanctions.ReturnPackMaterializedAt,
            SanctionsStrDraftCatalogMaterializedAt = workspace.Sanctions.StrDraftCatalogMaterializedAt,
            ResiliencePackMaterializedAt = workspace.Resilience.ReturnPackMaterializedAt,
            ModelRiskPackMaterializedAt = workspace.ModelRisk.ReturnPackMaterializedAt
        };
    }

    private async Task<int> MaterializeDashboardBriefingPacksAsync(
        PlatformIntelligenceWorkspace workspace,
        SanctionsScreeningSessionState screeningSessionState,
        SanctionsWorkflowState workflowState,
        SanctionsStrDraftCatalogState strDraftCatalogState,
        CancellationToken ct)
    {
        var materialized = 0;

        foreach (var lens in new[] { "governor", "deputy", "director" })
        {
            var sections = _dashboardBriefingPackBuilder.Build(
                workspace,
                lens,
                institutionId: null,
                screeningSessionState,
                workflowState,
                strDraftCatalogState);

            await _workspaceLoader.MaterializeDashboardBriefingPackAsync(lens, null, sections, ct);
            materialized++;
        }

        foreach (var institution in workspace.InstitutionDetails)
        {
            var sections = _dashboardBriefingPackBuilder.Build(
                workspace,
                "executive",
                institution.InstitutionId,
                screeningSessionState,
                workflowState,
                strDraftCatalogState);

            await _workspaceLoader.MaterializeDashboardBriefingPackAsync("executive", institution.InstitutionId, sections, ct);
            materialized++;
        }

        return materialized;
    }
}

public sealed class PlatformIntelligenceRefreshResult
{
    public DateTime GeneratedAt { get; set; }
    public int InstitutionCount { get; set; }
    public int InterventionCount { get; set; }
    public int TimelineCount { get; set; }
    public int DashboardPacksMaterialized { get; set; }
    public DateTime? RolloutCatalogMaterializedAt { get; set; }
    public DateTime? KnowledgeCatalogMaterializedAt { get; set; }
    public DateTime? KnowledgeDossierMaterializedAt { get; set; }
    public DateTime? CapitalPackMaterializedAt { get; set; }
    public DateTime? SanctionsPackMaterializedAt { get; set; }
    public DateTime? SanctionsStrDraftCatalogMaterializedAt { get; set; }
    public DateTime? ResiliencePackMaterializedAt { get; set; }
    public DateTime? ModelRiskPackMaterializedAt { get; set; }
}
