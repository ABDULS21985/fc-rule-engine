using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FC.Engine.Infrastructure.Services;

namespace FC.Engine.Admin.Services;

public sealed class PlatformIntelligenceExportService
{
    private readonly IPlatformIntelligenceWorkspaceLoader _workspaceLoader;
    private readonly DashboardBriefingPackBuilder _dashboardBriefingPackBuilder;
    private readonly DataTableExportService _exportService;

    public PlatformIntelligenceExportService(
        IPlatformIntelligenceWorkspaceLoader workspaceLoader,
        DashboardBriefingPackBuilder dashboardBriefingPackBuilder,
        DataTableExportService exportService)
    {
        _workspaceLoader = workspaceLoader;
        _dashboardBriefingPackBuilder = dashboardBriefingPackBuilder;
        _exportService = exportService;
    }

    public async Task<IntelligenceExportFile> ExportOverviewCsvAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        return CreateOverviewCsv(workspace);
    }

    public async Task<IntelligenceExportFile> ExportOverviewPdfAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        return CreateOverviewPdf(workspace);
    }

    public async Task<IntelligenceExportFile?> ExportDashboardBriefingPackCsvAsync(
        string lens,
        int? institutionId,
        CancellationToken ct = default)
    {
        var state = await LoadOrMaterializeDashboardBriefingPackAsync(lens, institutionId, ct);
        if (state is null)
        {
            return null;
        }

        return new IntelligenceExportFile(
            BuildDashboardPackFileName(lens, institutionId, "csv"),
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                state.Sections,
                [
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Code", x => x.SectionCode),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Name", x => x.SectionName),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Coverage", x => x.Coverage),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Signal", x => x.Signal),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Commentary", x => x.Commentary),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Recommended Action", x => x.RecommendedAction),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Materialized At", x => x.MaterializedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                ]));
    }

    public async Task<IntelligenceExportFile?> ExportDashboardBriefingPackPdfAsync(
        string lens,
        int? institutionId,
        CancellationToken ct = default)
    {
        var state = await LoadOrMaterializeDashboardBriefingPackAsync(lens, institutionId, ct);
        if (state is null)
        {
            return null;
        }

        return new IntelligenceExportFile(
            BuildDashboardPackFileName(lens, institutionId, "pdf"),
            "application/pdf",
            _exportService.ExportPdf(
                state.Sections,
                [
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Code", x => x.SectionCode),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Name", x => x.SectionName),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Coverage", x => x.Coverage),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Signal", x => x.Signal),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Commentary", x => x.Commentary),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Recommended Action", x => x.RecommendedAction)
                ],
                BuildDashboardPackTitle(lens, institutionId)));
    }

    public async Task<IntelligenceExportFile> ExportKnowledgeDossierCsvAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        return CreateKnowledgeDossierCsv(workspace);
    }

    public async Task<IntelligenceExportFile> ExportCapitalPackCsvAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        return CreateCapitalPackCsv(workspace);
    }

    public async Task<IntelligenceExportFile> ExportSanctionsPackCsvAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        return CreateSanctionsPackCsv(workspace);
    }

    public async Task<IntelligenceExportFile> ExportResiliencePackCsvAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        return CreateResiliencePackCsv(workspace);
    }

    public async Task<IntelligenceExportFile> ExportModelRiskPackCsvAsync(CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        return CreateModelRiskPackCsv(workspace);
    }

    public async Task<IntelligenceExportFile?> ExportBundleAsync(
        string lens,
        int? institutionId,
        CancellationToken ct = default)
    {
        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        var dashboardPack = await LoadOrMaterializeDashboardBriefingPackAsync(lens, institutionId, ct);
        if (dashboardPack is null)
        {
            return null;
        }

        var files = new List<IntelligenceExportFile>
        {
            CreateOverviewCsv(workspace),
            CreateOverviewPdf(workspace),
            CreateDashboardPackCsv(dashboardPack, lens, institutionId),
            CreateDashboardPackPdf(dashboardPack, lens, institutionId),
            CreateKnowledgeDossierCsv(workspace),
            CreateCapitalPackCsv(workspace),
            CreateSanctionsPackCsv(workspace),
            CreateResiliencePackCsv(workspace),
            CreateModelRiskPackCsv(workspace)
        };

        var manifest = new IntelligenceBundleManifest
        {
            Lens = lens,
            InstitutionId = institutionId,
            GeneratedAtUtc = workspace.GeneratedAt,
            RefreshStatus = workspace.Refresh.Status,
            Files = files
                .Select(x => new IntelligenceBundleManifestFile
                {
                    FileName = x.FileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.Content.Length
                })
                .ToList()
        };

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.FileName, CompressionLevel.Fastest);
                using var entryStream = entry.Open();
                await entryStream.WriteAsync(file.Content, ct);
            }

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
            await using var manifestStream = manifestEntry.Open();
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                new JsonSerializerOptions { WriteIndented = true });
            await manifestStream.WriteAsync(manifestBytes, ct);
        }

        return new IntelligenceExportFile(
            BuildBundleFileName(lens, institutionId),
            "application/zip",
            stream.ToArray());
    }

    private async Task<DashboardBriefingPackCatalogState?> LoadOrMaterializeDashboardBriefingPackAsync(
        string lens,
        int? institutionId,
        CancellationToken ct)
    {
        var state = await _workspaceLoader.GetDashboardBriefingPackCatalogStateAsync(lens, institutionId, ct);
        if (state.Sections.Count > 0)
        {
            return state;
        }

        var workspace = await _workspaceLoader.GetWorkspaceAsync(ct);
        var screeningSession = await _workspaceLoader.GetSanctionsScreeningSessionStateAsync(ct);
        var workflowState = await _workspaceLoader.GetSanctionsWorkflowStateAsync(ct);
        var strDraftCatalog = await _workspaceLoader.GetSanctionsStrDraftCatalogStateAsync(ct);
        var sections = _dashboardBriefingPackBuilder.Build(workspace, lens, institutionId, screeningSession, workflowState, strDraftCatalog);

        if (lens.Equals("executive", StringComparison.OrdinalIgnoreCase) && sections.Count == 0)
        {
            return null;
        }

        return await _workspaceLoader.MaterializeDashboardBriefingPackAsync(lens, institutionId, sections, ct);
    }

    private static List<OverviewExportRow> BuildOverviewRows(PlatformIntelligenceWorkspace workspace) =>
    [
        new("Workspace", "Generated At", workspace.GeneratedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), "UTC timestamp for the current intelligence workspace."),
        new("Refresh", "Status", workspace.Refresh.Status, workspace.Refresh.Commentary),
        new("Refresh", "Recent Runs", workspace.Refresh.RecentRuns.Count.ToString(CultureInfo.InvariantCulture), $"Recent success {workspace.Refresh.RecentSuccessCount}; failures {workspace.Refresh.RecentFailureCount}."),
        new("Operations", "Interventions", workspace.Interventions.Count.ToString(CultureInfo.InvariantCulture), "Prioritized cross-track intervention queue size."),
        new("Operations", "Timeline Events", workspace.ActivityTimeline.Count.ToString(CultureInfo.InvariantCulture), "Recent cross-domain activity events in the supervisory timeline."),
        new("Institutions", "Institution Scorecards", workspace.InstitutionScorecards.Count.ToString(CultureInfo.InvariantCulture), "Institutions currently ranked in the supervisory pressure model."),
        new("Rollout", "Pending Entitlements", workspace.Rollout.PendingEntitlementCount.ToString(CultureInfo.InvariantCulture), $"{workspace.Rollout.PendingTenantCount} tenant(s) currently need reconciliation."),
        new("Knowledge", "Obligations", workspace.KnowledgeGraph.ObligationCount.ToString(CultureInfo.InvariantCulture), $"{workspace.KnowledgeGraph.RequirementCount} requirement(s) mapped into the graph."),
        new("Capital", "Watchlist Institutions", workspace.Capital.CapitalWatchlistCount.ToString(CultureInfo.InvariantCulture), $"{workspace.Capital.ReturnPackAttentionCount} capital pack section(s) currently need attention."),
        new("Sanctions", "Watchlist Sources", workspace.Sanctions.SourceCount.ToString(CultureInfo.InvariantCulture), $"{workspace.Sanctions.ReturnPackAttentionCount} sanctions pack section(s) currently need attention."),
        new("Resilience", "Open Incidents", workspace.Resilience.OpenIncidentCount.ToString(CultureInfo.InvariantCulture), $"{workspace.Resilience.ReturnPackAttentionCount} resilience pack sheet(s) currently need attention."),
        new("Model Risk", "Due Validations", workspace.ModelRisk.DueValidationCount.ToString(CultureInfo.InvariantCulture), $"{workspace.ModelRisk.ReturnPackAttentionCount} model-risk pack sheet(s) currently need attention.")
    ];

    private IntelligenceExportFile CreateOverviewCsv(PlatformIntelligenceWorkspace workspace)
    {
        var rows = BuildOverviewRows(workspace);
        return new IntelligenceExportFile(
            "platform-intelligence-overview.csv",
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                rows,
                [
                    new ExportColumnDef<OverviewExportRow>("Section", x => x.Section),
                    new ExportColumnDef<OverviewExportRow>("Metric", x => x.Metric),
                    new ExportColumnDef<OverviewExportRow>("Value", x => x.Value),
                    new ExportColumnDef<OverviewExportRow>("Commentary", x => x.Commentary)
                ]));
    }

    private IntelligenceExportFile CreateOverviewPdf(PlatformIntelligenceWorkspace workspace)
    {
        var rows = BuildOverviewRows(workspace);
        return new IntelligenceExportFile(
            "platform-intelligence-board-brief.pdf",
            "application/pdf",
            _exportService.ExportPdf(
                rows,
                [
                    new ExportColumnDef<OverviewExportRow>("Section", x => x.Section),
                    new ExportColumnDef<OverviewExportRow>("Metric", x => x.Metric),
                    new ExportColumnDef<OverviewExportRow>("Value", x => x.Value),
                    new ExportColumnDef<OverviewExportRow>("Commentary", x => x.Commentary)
                ],
                "Platform Intelligence Board Brief"));
    }

    private IntelligenceExportFile CreateDashboardPackCsv(
        DashboardBriefingPackCatalogState state,
        string lens,
        int? institutionId) =>
        new(
            BuildDashboardPackFileName(lens, institutionId, "csv"),
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                state.Sections,
                [
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Code", x => x.SectionCode),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Name", x => x.SectionName),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Coverage", x => x.Coverage),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Signal", x => x.Signal),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Commentary", x => x.Commentary),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Recommended Action", x => x.RecommendedAction),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Materialized At", x => x.MaterializedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                ]));

    private IntelligenceExportFile CreateDashboardPackPdf(
        DashboardBriefingPackCatalogState state,
        string lens,
        int? institutionId) =>
        new(
            BuildDashboardPackFileName(lens, institutionId, "pdf"),
            "application/pdf",
            _exportService.ExportPdf(
                state.Sections,
                [
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Code", x => x.SectionCode),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Section Name", x => x.SectionName),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Coverage", x => x.Coverage),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Signal", x => x.Signal),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Commentary", x => x.Commentary),
                    new ExportColumnDef<DashboardBriefingPackSectionState>("Recommended Action", x => x.RecommendedAction)
                ],
                BuildDashboardPackTitle(lens, institutionId)));

    private IntelligenceExportFile CreateKnowledgeDossierCsv(PlatformIntelligenceWorkspace workspace) =>
        new(
            "knowledge-graph-dossier.csv",
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                workspace.KnowledgeGraph.DossierPack,
                [
                    new ExportColumnDef<KnowledgeGraphDossierRow>("Section Code", x => x.SectionCode),
                    new ExportColumnDef<KnowledgeGraphDossierRow>("Section Name", x => x.SectionName),
                    new ExportColumnDef<KnowledgeGraphDossierRow>("Row Count", x => x.RowCount.ToString(CultureInfo.InvariantCulture)),
                    new ExportColumnDef<KnowledgeGraphDossierRow>("Signal", x => x.Signal),
                    new ExportColumnDef<KnowledgeGraphDossierRow>("Coverage", x => x.Coverage),
                    new ExportColumnDef<KnowledgeGraphDossierRow>("Commentary", x => x.Commentary),
                    new ExportColumnDef<KnowledgeGraphDossierRow>("Recommended Action", x => x.RecommendedAction)
                ]));

    private IntelligenceExportFile CreateCapitalPackCsv(PlatformIntelligenceWorkspace workspace) =>
        new(
            "capital-supervisory-pack.csv",
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                workspace.Capital.ReturnPack,
                [
                    new ExportColumnDef<CapitalPackSectionState>("Section Code", x => x.SectionCode),
                    new ExportColumnDef<CapitalPackSectionState>("Section Name", x => x.SectionName),
                    new ExportColumnDef<CapitalPackSectionState>("Row Count", x => x.RowCount.ToString(CultureInfo.InvariantCulture)),
                    new ExportColumnDef<CapitalPackSectionState>("Signal", x => x.Signal),
                    new ExportColumnDef<CapitalPackSectionState>("Coverage", x => x.Coverage),
                    new ExportColumnDef<CapitalPackSectionState>("Commentary", x => x.Commentary),
                    new ExportColumnDef<CapitalPackSectionState>("Recommended Action", x => x.RecommendedAction)
                ]));

    private IntelligenceExportFile CreateSanctionsPackCsv(PlatformIntelligenceWorkspace workspace) =>
        new(
            "sanctions-supervisory-pack.csv",
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                workspace.Sanctions.ReturnPack,
                [
                    new ExportColumnDef<SanctionsPackSectionState>("Section Code", x => x.SectionCode),
                    new ExportColumnDef<SanctionsPackSectionState>("Section Name", x => x.SectionName),
                    new ExportColumnDef<SanctionsPackSectionState>("Row Count", x => x.RowCount.ToString(CultureInfo.InvariantCulture)),
                    new ExportColumnDef<SanctionsPackSectionState>("Signal", x => x.Signal),
                    new ExportColumnDef<SanctionsPackSectionState>("Coverage", x => x.Coverage),
                    new ExportColumnDef<SanctionsPackSectionState>("Commentary", x => x.Commentary),
                    new ExportColumnDef<SanctionsPackSectionState>("Recommended Action", x => x.RecommendedAction)
                ]));

    private IntelligenceExportFile CreateResiliencePackCsv(PlatformIntelligenceWorkspace workspace) =>
        new(
            "ops-resilience-pack.csv",
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                workspace.Resilience.ReturnPack,
                [
                    new ExportColumnDef<OpsResilienceSheetRow>("Sheet Code", x => x.SheetCode),
                    new ExportColumnDef<OpsResilienceSheetRow>("Sheet Name", x => x.SheetName),
                    new ExportColumnDef<OpsResilienceSheetRow>("Row Count", x => x.RowCount.ToString(CultureInfo.InvariantCulture)),
                    new ExportColumnDef<OpsResilienceSheetRow>("Signal", x => x.Signal),
                    new ExportColumnDef<OpsResilienceSheetRow>("Coverage", x => x.Coverage),
                    new ExportColumnDef<OpsResilienceSheetRow>("Commentary", x => x.Commentary),
                    new ExportColumnDef<OpsResilienceSheetRow>("Recommended Action", x => x.RecommendedAction)
                ]));

    private IntelligenceExportFile CreateModelRiskPackCsv(PlatformIntelligenceWorkspace workspace) =>
        new(
            "model-risk-pack.csv",
            "text/csv;charset=utf-8",
            _exportService.ExportCsv(
                workspace.ModelRisk.ReturnPack,
                [
                    new ExportColumnDef<ModelRiskSheetRow>("Sheet Code", x => x.SheetCode),
                    new ExportColumnDef<ModelRiskSheetRow>("Sheet Name", x => x.SheetName),
                    new ExportColumnDef<ModelRiskSheetRow>("Row Count", x => x.RowCount.ToString(CultureInfo.InvariantCulture)),
                    new ExportColumnDef<ModelRiskSheetRow>("Signal", x => x.Signal),
                    new ExportColumnDef<ModelRiskSheetRow>("Coverage", x => x.Coverage),
                    new ExportColumnDef<ModelRiskSheetRow>("Commentary", x => x.Commentary),
                    new ExportColumnDef<ModelRiskSheetRow>("Recommended Action", x => x.RecommendedAction)
                ]));

    private static string BuildDashboardPackFileName(string lens, int? institutionId, string extension)
    {
        var slug = lens.Trim().ToLowerInvariant();
        return slug == "executive" && institutionId.HasValue
            ? $"stakeholder-briefing-pack-{slug}-{institutionId.Value}.{extension}"
            : $"stakeholder-briefing-pack-{slug}.{extension}";
    }

    private static string BuildBundleFileName(string lens, int? institutionId)
    {
        var slug = lens.Trim().ToLowerInvariant();
        return slug == "executive" && institutionId.HasValue
            ? $"platform-intelligence-bundle-{slug}-{institutionId.Value}.zip"
            : $"platform-intelligence-bundle-{slug}.zip";
    }

    private static string BuildDashboardPackTitle(string lens, int? institutionId)
    {
        var title = lens.Trim().ToLowerInvariant() switch
        {
            "governor" => "Governor Briefing Pack",
            "deputy" => "Deputy Governor Briefing Pack",
            "director" => "Director / Examiner Briefing Pack",
            "executive" => institutionId.HasValue
                ? $"Institution Executive Briefing Pack #{institutionId.Value}"
                : "Institution Executive Briefing Pack",
            _ => "Stakeholder Briefing Pack"
        };

        return $"Platform Intelligence {title}";
    }

    private sealed record OverviewExportRow(string Section, string Metric, string Value, string Commentary);

    private sealed class IntelligenceBundleManifest
    {
        public string Lens { get; set; } = string.Empty;
        public int? InstitutionId { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public string RefreshStatus { get; set; } = string.Empty;
        public List<IntelligenceBundleManifestFile> Files { get; set; } = [];
    }

    private sealed class IntelligenceBundleManifestFile
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public int SizeBytes { get; set; }
    }
}

public sealed record IntelligenceExportFile(string FileName, string ContentType, byte[] Content);
