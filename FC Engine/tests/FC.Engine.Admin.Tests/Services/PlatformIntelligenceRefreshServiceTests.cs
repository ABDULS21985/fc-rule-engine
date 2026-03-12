using FC.Engine.Admin.Services;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace FC.Engine.Admin.Tests.Services;

public class PlatformIntelligenceRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_Returns_Workspace_Catalog_Timestamps_And_Counts()
    {
        var generatedAt = new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc);
        var knowledgeAt = generatedAt.AddMinutes(-10);
        var capitalAt = generatedAt.AddMinutes(-8);
        var sanctionsAt = generatedAt.AddMinutes(-6);
        var strDraftsAt = generatedAt.AddMinutes(-5);
        var resilienceAt = generatedAt.AddMinutes(-4);
        var modelRiskAt = generatedAt.AddMinutes(-3);
        var rolloutAt = generatedAt.AddMinutes(-2);

        var loader = new Mock<IPlatformIntelligenceWorkspaceLoader>();
        loader
            .Setup(x => x.GetWorkspaceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformIntelligenceWorkspace
            {
                GeneratedAt = generatedAt,
                InstitutionScorecards = [new InstitutionScorecardRow(), new InstitutionScorecardRow()],
                Interventions = [new InterventionQueueRow(), new InterventionQueueRow(), new InterventionQueueRow()],
                ActivityTimeline = [new ActivityTimelineRow()],
                Rollout = new MarketplaceRolloutSnapshot
                {
                    CatalogMaterializedAt = rolloutAt
                },
                KnowledgeGraph = new KnowledgeGraphSnapshot
                {
                    CatalogMaterializedAt = knowledgeAt,
                    DossierMaterializedAt = knowledgeAt.AddMinutes(1)
                },
                Capital = new CapitalManagementSnapshot
                {
                    ReturnPackMaterializedAt = capitalAt
                },
                Sanctions = new SanctionsSnapshot
                {
                    ReturnPackMaterializedAt = sanctionsAt,
                    StrDraftCatalogMaterializedAt = strDraftsAt
                },
                Resilience = new OperationalResilienceSnapshot
                {
                    ReturnPackMaterializedAt = resilienceAt
                },
                ModelRisk = new ModelRiskSnapshot
                {
                    ReturnPackMaterializedAt = modelRiskAt
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
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IReadOnlyList<DashboardBriefingPackSectionInput>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string lens, int? institutionId, IReadOnlyList<DashboardBriefingPackSectionInput> sections, CancellationToken _) =>
                new DashboardBriefingPackCatalogState
                {
                    Lens = lens,
                    InstitutionId = institutionId,
                    MaterializedAt = generatedAt,
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
                        MaterializedAt = generatedAt
                    }).ToList()
                });

        var sut = new PlatformIntelligenceRefreshService(loader.Object, new DashboardBriefingPackBuilder());

        var result = await sut.RefreshAsync();

        result.GeneratedAt.Should().Be(generatedAt);
        result.InstitutionCount.Should().Be(2);
        result.InterventionCount.Should().Be(3);
        result.TimelineCount.Should().Be(1);
        result.RolloutCatalogMaterializedAt.Should().Be(rolloutAt);
        result.KnowledgeCatalogMaterializedAt.Should().Be(knowledgeAt);
        result.CapitalPackMaterializedAt.Should().Be(capitalAt);
        result.SanctionsPackMaterializedAt.Should().Be(sanctionsAt);
        result.SanctionsStrDraftCatalogMaterializedAt.Should().Be(strDraftsAt);
        result.ResiliencePackMaterializedAt.Should().Be(resilienceAt);
        result.ModelRiskPackMaterializedAt.Should().Be(modelRiskAt);
        result.DashboardPacksMaterialized.Should().Be(3);

        loader.Verify(x => x.MaterializeDashboardBriefingPackAsync(
            "governor",
            null,
            It.Is<IReadOnlyList<DashboardBriefingPackSectionInput>>(sections => sections.Count == 5),
            It.IsAny<CancellationToken>()), Times.Once);
        loader.Verify(x => x.MaterializeDashboardBriefingPackAsync(
            "director",
            null,
            It.Is<IReadOnlyList<DashboardBriefingPackSectionInput>>(sections => sections.Count == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_Allows_Empty_Workspace_Collections()
    {
        var loader = new Mock<IPlatformIntelligenceWorkspaceLoader>();
        loader
            .Setup(x => x.GetWorkspaceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformIntelligenceWorkspace
            {
                GeneratedAt = new DateTime(2026, 3, 12, 10, 0, 0, DateTimeKind.Utc)
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
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IReadOnlyList<DashboardBriefingPackSectionInput>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardBriefingPackCatalogState());

        var sut = new PlatformIntelligenceRefreshService(loader.Object, new DashboardBriefingPackBuilder());

        var result = await sut.RefreshAsync();

        result.InstitutionCount.Should().Be(0);
        result.InterventionCount.Should().Be(0);
        result.TimelineCount.Should().Be(0);
        result.DashboardPacksMaterialized.Should().Be(3);
        result.KnowledgeCatalogMaterializedAt.Should().BeNull();
        result.SanctionsStrDraftCatalogMaterializedAt.Should().BeNull();
    }

    [Fact]
    public async Task RefreshAsync_Materializes_Executive_Packs_For_Institution_Profiles()
    {
        var loader = new Mock<IPlatformIntelligenceWorkspaceLoader>();
        loader
            .Setup(x => x.GetWorkspaceAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformIntelligenceWorkspace
            {
                GeneratedAt = new DateTime(2026, 3, 12, 11, 0, 0, DateTimeKind.Utc),
                InstitutionScorecards =
                [
                    new InstitutionScorecardRow
                    {
                        InstitutionId = 44,
                        InstitutionName = "Example BDC",
                        LicenceType = "BDC",
                        Priority = "High",
                        OverdueObligations = 1,
                        DueSoonObligations = 2,
                        OpenResilienceIncidents = 1,
                        ModelReviewItems = 1
                    }
                ],
                InstitutionDetails =
                [
                    new InstitutionIntelligenceDetail
                    {
                        InstitutionId = 44,
                        InstitutionName = "Example BDC",
                        LicenceType = "BDC",
                        OverdueObligations = 1,
                        DueSoonObligations = 2,
                        CapitalScore = 52m,
                        CapitalAlert = "Capital buffer is tightening.",
                        OpenResilienceIncidents = 1,
                        ModelReviewItems = 1,
                        TopObligations =
                        [
                            new KnowledgeGraphInstitutionObligationRow
                            {
                                ReturnCode = "BDC_FXV",
                                Status = "Overdue",
                                PeriodLabel = "Mar 2026",
                                NextDeadline = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)
                            }
                        ]
                    }
                ]
            });
        loader.Setup(x => x.GetSanctionsScreeningSessionStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SanctionsScreeningSessionState());
        loader.Setup(x => x.GetSanctionsWorkflowStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SanctionsWorkflowState());
        loader.Setup(x => x.GetSanctionsStrDraftCatalogStateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SanctionsStrDraftCatalogState());
        loader
            .Setup(x => x.MaterializeDashboardBriefingPackAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<IReadOnlyList<DashboardBriefingPackSectionInput>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardBriefingPackCatalogState());

        var sut = new PlatformIntelligenceRefreshService(loader.Object, new DashboardBriefingPackBuilder());

        var result = await sut.RefreshAsync();

        result.DashboardPacksMaterialized.Should().Be(4);
        loader.Verify(x => x.MaterializeDashboardBriefingPackAsync(
            "executive",
            44,
            It.Is<IReadOnlyList<DashboardBriefingPackSectionInput>>(sections =>
                sections.Count == 5 && sections.Any(row => row.SectionCode == "EXE-01")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
