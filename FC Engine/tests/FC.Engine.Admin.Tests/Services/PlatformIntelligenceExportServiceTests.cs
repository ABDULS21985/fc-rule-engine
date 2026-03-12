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
}
