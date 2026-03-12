using System.IO.Compression;
using System.Text;
using FC.Engine.Admin.Services;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace FC.Engine.Admin.Tests.Services;

public class PlatformIntelligenceExportServiceTests
{
    [Fact]
    public async Task ExportOverviewCsvAsync_Returns_Core_Overview_Rows()
    {
        var loader = new Mock<IPlatformIntelligenceWorkspaceLoader>();
        loader
            .Setup(x => x.GetWorkspaceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformIntelligenceWorkspace
            {
                GeneratedAt = new DateTime(2026, 3, 12, 10, 30, 0, DateTimeKind.Utc),
                Refresh = new PlatformIntelligenceRefreshSnapshot
                {
                    Status = "Healthy",
                    Commentary = "Background refresh is current."
                },
                Interventions = [new InterventionQueueRow(), new InterventionQueueRow()],
                ActivityTimeline = [new ActivityTimelineRow()],
                InstitutionScorecards = [new InstitutionScorecardRow()],
                Rollout = new MarketplaceRolloutSnapshot
                {
                    PendingEntitlementCount = 3,
                    PendingTenantCount = 2
                },
                KnowledgeGraph = new KnowledgeGraphSnapshot
                {
                    ObligationCount = 14,
                    RequirementCount = 31
                },
                Capital = new CapitalManagementSnapshot
                {
                    CapitalWatchlistCount = 4,
                    ReturnPackAttentionCount = 2
                },
                Sanctions = new SanctionsSnapshot
                {
                    SourceCount = 6,
                    ReturnPackAttentionCount = 1
                },
                Resilience = new OperationalResilienceSnapshot
                {
                    OpenIncidentCount = 5,
                    ReturnPackAttentionCount = 3
                },
                ModelRisk = new ModelRiskSnapshot
                {
                    DueValidationCount = 7,
                    ReturnPackAttentionCount = 2
                }
            });

        var sut = new PlatformIntelligenceExportService(loader.Object, new DashboardBriefingPackBuilder(), new DataTableExportService());

        var file = await sut.ExportOverviewCsvAsync();
        var csv = Encoding.UTF8.GetString(file.Content);

        file.FileName.Should().Be("platform-intelligence-overview.csv");
        file.ContentType.Should().Be("text/csv;charset=utf-8");
        csv.Should().Contain("Refresh,Status,Healthy");
        csv.Should().Contain("Rollout,Pending Entitlements,3");
        csv.Should().Contain("Knowledge,Obligations,14");
        csv.Should().Contain("Model Risk,Due Validations,7");
    }

    [Fact]
    public async Task ExportDashboardBriefingPackCsvAsync_Materializes_When_Catalog_Is_Empty()
    {
        var loader = new Mock<IPlatformIntelligenceWorkspaceLoader>();
        loader
            .Setup(x => x.GetDashboardBriefingPackCatalogStateAsync("governor", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardBriefingPackCatalogState
            {
                Lens = "governor"
            });
        loader
            .Setup(x => x.GetWorkspaceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformIntelligenceWorkspace
            {
                Refresh = new PlatformIntelligenceRefreshSnapshot
                {
                    CatalogFreshness =
                    [
                        new PlatformIntelligenceCatalogFreshnessRow
                        {
                            Area = "Knowledge",
                            Artifact = "Graph catalog",
                            Status = "Current",
                            ThresholdLabel = "120 mins",
                            Commentary = "Fresh"
                        }
                    ]
                }
            });
        loader
            .Setup(x => x.GetSanctionsScreeningSessionStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SanctionsScreeningSessionState());
        loader
            .Setup(x => x.GetSanctionsWorkflowStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SanctionsWorkflowState());
        loader
            .Setup(x => x.GetSanctionsStrDraftCatalogStateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SanctionsStrDraftCatalogState());
        loader
            .Setup(x => x.MaterializeDashboardBriefingPackAsync(
                "governor",
                null,
                It.IsAny<IReadOnlyList<DashboardBriefingPackSectionInput>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string lens, int? institutionId, IReadOnlyList<DashboardBriefingPackSectionInput> sections, CancellationToken _) =>
                new DashboardBriefingPackCatalogState
                {
                    Lens = lens,
                    InstitutionId = institutionId,
                    MaterializedAt = new DateTime(2026, 3, 12, 11, 0, 0, DateTimeKind.Utc),
                    Sections = sections.Select(x => new DashboardBriefingPackSectionState
                    {
                        Lens = lens,
                        InstitutionId = institutionId,
                        SectionCode = x.SectionCode,
                        SectionName = x.SectionName,
                        Coverage = x.Coverage,
                        Signal = x.Signal,
                        Commentary = x.Commentary,
                        RecommendedAction = x.RecommendedAction,
                        MaterializedAt = new DateTime(2026, 3, 12, 11, 0, 0, DateTimeKind.Utc)
                    }).ToList()
                });

        var sut = new PlatformIntelligenceExportService(loader.Object, new DashboardBriefingPackBuilder(), new DataTableExportService());

        var file = await sut.ExportDashboardBriefingPackCsvAsync("governor", null);
        var csv = Encoding.UTF8.GetString(file!.Content);

        file.FileName.Should().Be("stakeholder-briefing-pack-governor.csv");
        csv.Should().Contain("Section Code,Section Name,Coverage,Signal");
        csv.Should().Contain("GOV-01");
        loader.Verify(x => x.MaterializeDashboardBriefingPackAsync(
            "governor",
            null,
            It.IsAny<IReadOnlyList<DashboardBriefingPackSectionInput>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExportDashboardBriefingPackPdfAsync_Returns_Pdf_From_Persisted_State()
    {
        var loader = new Mock<IPlatformIntelligenceWorkspaceLoader>();
        loader
            .Setup(x => x.GetDashboardBriefingPackCatalogStateAsync("deputy", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardBriefingPackCatalogState
            {
                Lens = "deputy",
                MaterializedAt = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc),
                Sections =
                [
                    new DashboardBriefingPackSectionState
                    {
                        Lens = "deputy",
                        SectionCode = "DPY-01",
                        SectionName = "Operating Queue",
                        Coverage = "12 queued action(s)",
                        Signal = "Watch",
                        Commentary = "The queue remains elevated.",
                        RecommendedAction = "Sequence the queue by due date.",
                        MaterializedAt = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc)
                    }
                ]
            });

        var sut = new PlatformIntelligenceExportService(loader.Object, new DashboardBriefingPackBuilder(), new DataTableExportService());

        var file = await sut.ExportDashboardBriefingPackPdfAsync("deputy", null);

        file.Should().NotBeNull();
        file!.FileName.Should().Be("stakeholder-briefing-pack-deputy.pdf");
        file.ContentType.Should().Be("application/pdf");
        Encoding.ASCII.GetString(file.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task ExportBundleAsync_Returns_Zip_With_Expected_Entries_And_Manifest()
    {
        var loader = new Mock<IPlatformIntelligenceWorkspaceLoader>();
        loader
            .Setup(x => x.GetWorkspaceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformIntelligenceWorkspace
            {
                GeneratedAt = new DateTime(2026, 3, 12, 13, 0, 0, DateTimeKind.Utc),
                Refresh = new PlatformIntelligenceRefreshSnapshot
                {
                    Status = "Healthy",
                    Commentary = "Background refresh is current."
                },
                Interventions = [new InterventionQueueRow()],
                ActivityTimeline = [new ActivityTimelineRow()],
                InstitutionScorecards = [new InstitutionScorecardRow()],
                Rollout = new MarketplaceRolloutSnapshot
                {
                    PendingEntitlementCount = 1,
                    PendingTenantCount = 1
                },
                KnowledgeGraph = new KnowledgeGraphSnapshot
                {
                    ObligationCount = 8,
                    RequirementCount = 13,
                    DossierPack =
                    [
                        new KnowledgeGraphDossierRow
                        {
                            SectionCode = "KG-01",
                            SectionName = "Ontology Coverage",
                            RowCount = 2,
                            Signal = "Current",
                            Coverage = "6 regulators",
                            Commentary = "Coverage is current.",
                            RecommendedAction = "Maintain."
                        }
                    ]
                },
                Capital = new CapitalManagementSnapshot
                {
                    CapitalWatchlistCount = 2,
                    ReturnPackAttentionCount = 1,
                    ReturnPack =
                    [
                        new CapitalPackSectionState
                        {
                            SectionCode = "CAP-01",
                            SectionName = "Capital Watchlist",
                            RowCount = 2,
                            Signal = "Watch",
                            Coverage = "2 institutions",
                            Commentary = "Watchlist is elevated.",
                            RecommendedAction = "Review.",
                            MaterializedAt = new DateTime(2026, 3, 12, 13, 1, 0, DateTimeKind.Utc)
                        }
                    ]
                },
                Sanctions = new SanctionsSnapshot
                {
                    SourceCount = 6,
                    ReturnPackAttentionCount = 1,
                    ReturnPack =
                    [
                        new SanctionsPackSectionState
                        {
                            SectionCode = "SAN-01",
                            SectionName = "Watchlist Integration",
                            RowCount = 6,
                            Signal = "Current",
                            Coverage = "6 sources",
                            Commentary = "Coverage is current.",
                            RecommendedAction = "Maintain.",
                            MaterializedAt = new DateTime(2026, 3, 12, 13, 1, 0, DateTimeKind.Utc)
                        }
                    ]
                },
                Resilience = new OperationalResilienceSnapshot
                {
                    OpenIncidentCount = 1,
                    ReturnPackAttentionCount = 1,
                    ReturnPack =
                    [
                        new OpsResilienceSheetRow
                        {
                            SheetCode = "OPS-01",
                            SheetName = "Important Business Services",
                            RowCount = 3,
                            Signal = "Watch",
                            Coverage = "3 mapped services",
                            Commentary = "Coverage is partial.",
                            RecommendedAction = "Expand mapping."
                        }
                    ]
                },
                ModelRisk = new ModelRiskSnapshot
                {
                    DueValidationCount = 1,
                    ReturnPackAttentionCount = 1,
                    ReturnPack =
                    [
                        new ModelRiskSheetRow
                        {
                            SheetCode = "MRM-01",
                            SheetName = "Model Inventory",
                            RowCount = 4,
                            Signal = "Current",
                            Coverage = "4 governed models",
                            Commentary = "Inventory is current.",
                            RecommendedAction = "Maintain."
                        }
                    ]
                }
            });
        loader
            .Setup(x => x.GetDashboardBriefingPackCatalogStateAsync("governor", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardBriefingPackCatalogState
            {
                Lens = "governor",
                Sections =
                [
                    new DashboardBriefingPackSectionState
                    {
                        Lens = "governor",
                        SectionCode = "GOV-01",
                        SectionName = "Systemic Posture",
                        Coverage = "1 critical intervention",
                        Signal = "Watch",
                        Commentary = "Pressure remains elevated.",
                        RecommendedAction = "Review critical items.",
                        MaterializedAt = new DateTime(2026, 3, 12, 13, 5, 0, DateTimeKind.Utc)
                    }
                ]
            });

        var sut = new PlatformIntelligenceExportService(loader.Object, new DashboardBriefingPackBuilder(), new DataTableExportService());

        var file = await sut.ExportBundleAsync("governor", null);

        file.Should().NotBeNull();
        file!.FileName.Should().Be("platform-intelligence-bundle-governor.zip");
        file.ContentType.Should().Be("application/zip");

        using var stream = new MemoryStream(file.Content);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = archive.Entries.Select(x => x.FullName).OrderBy(x => x).ToList();

        entries.Should().Contain("platform-intelligence-overview.csv");
        entries.Should().Contain("platform-intelligence-board-brief.pdf");
        entries.Should().Contain("stakeholder-briefing-pack-governor.csv");
        entries.Should().Contain("stakeholder-briefing-pack-governor.pdf");
        entries.Should().Contain("knowledge-graph-dossier.csv");
        entries.Should().Contain("capital-supervisory-pack.csv");
        entries.Should().Contain("sanctions-supervisory-pack.csv");
        entries.Should().Contain("ops-resilience-pack.csv");
        entries.Should().Contain("model-risk-pack.csv");
        entries.Should().Contain("manifest.json");

        using var manifestReader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
        var manifest = await manifestReader.ReadToEndAsync();
        manifest.Should().Contain("\"Lens\": \"governor\"");
        manifest.Should().Contain("\"FileName\": \"platform-intelligence-overview.csv\"");
    }
}
