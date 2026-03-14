using FC.Engine.Domain.Models;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

[Collection("RegulatorIqIntegration")]
public sealed class RegulatorIntelligenceServiceIntegrationTests : IClassFixture<RegulatorIqFixture>
{
    private readonly RegulatorIqFixture _fixture;

    public RegulatorIntelligenceServiceIntegrationTests(RegulatorIqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetEntityProfileAsync_AggregatesExistingServicesAndDirectData()
    {
        var service = _fixture.CreateIntelligenceService();

        var profile = await service.GetEntityProfileAsync(_fixture.AccessBankTenantId, "CBN");

        profile.InstitutionName.Should().Be("Access Bank Plc");
        profile.LicenceCategory.Should().Be("DMB");
        profile.ComplianceHealth.Should().NotBeNull();
        profile.Anomaly.Should().NotBeNull();
        profile.Anomaly!.QualityScore.Should().Be(68m);
        profile.FilingRisk.Should().NotBeNull();
        profile.FilingRisk!.RiskBand.Should().Be("HIGH");
        profile.CamelsScore.Should().NotBeNull();
        profile.FilingTimeliness.Should().NotBeNull();
        profile.FilingTimeliness!.LateFilings.Should().BeGreaterThan(0);
        profile.SanctionsExposure.Should().NotBeNull();
        profile.SanctionsExposure!.MatchCount.Should().Be(1);
        profile.DataSourcesUsed.Should().Contain(["RG-07", "RG-32", "AI-01", "AI-04", "RG-12", "RG-48"]);
    }

    [Fact]
    public async Task GetSectorSummaryAsync_ComposesSectorServicesAndDirectAggregates()
    {
        var service = _fixture.CreateIntelligenceService();

        var summary = await service.GetSectorSummaryAsync("CBN", "DMB", "2026-Q1");

        summary.EntityCount.Should().Be(5);
        summary.AverageCarRatio.Should().BeGreaterThan(17m);
        summary.CarDistribution.Should().NotBeNull();
        summary.CarDistribution!.PeriodCode.Should().Be("2026-Q1");
        summary.RegulatoryRiskRanking.Should().HaveCountGreaterOrEqualTo(2);
        summary.AnomalyHotspots.Should().Contain(x => x.InstitutionName == "Access Bank Plc");
        summary.DataSourcesUsed.Should().Contain(["RG-07", "RG-32", "AI-01", "AI-04", "RG-12", "RG-36"]);
    }

    [Fact]
    public async Task RankEntitiesByMetricAsync_UsesJsonValueAcrossTenants()
    {
        var service = _fixture.CreateIntelligenceService();

        var ranking = await service.RankEntitiesByMetricAsync("carratio", "CBN", "DMB", "2026-Q1", 10);

        ranking.Should().HaveCount(2);
        ranking[0].InstitutionName.Should().Be("Zenith Bank Plc");
        ranking[0].MetricValue.Should().Be(19.1m);
        ranking[1].InstitutionName.Should().Be("Access Bank Plc");
        ranking[1].MetricValue.Should().Be(16.4m);
    }

    [Fact]
    public async Task GenerateExaminationBriefingAsync_ReturnsProfilePeerContextAndTrends()
    {
        var service = _fixture.CreateIntelligenceService();

        var briefing = await service.GenerateExaminationBriefingAsync(_fixture.AccessBankTenantId, "CBN");

        briefing.InstitutionName.Should().Be("Access Bank Plc");
        briefing.Profile.Should().NotBeNull();
        briefing.PeerContext.EntityCount.Should().Be(5);
        briefing.Trends.Should().Contain(x => x.MetricCode == "carratio");
        briefing.FocusAreas.Should().NotBeEmpty();
    }
}
