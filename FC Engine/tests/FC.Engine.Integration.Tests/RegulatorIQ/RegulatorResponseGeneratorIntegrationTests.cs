using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

[Collection("RegulatorIqIntegration")]
public sealed class RegulatorResponseGeneratorIntegrationTests : IClassFixture<RegulatorIqFixture>
{
    private readonly RegulatorIqFixture _fixture;

    public RegulatorResponseGeneratorIntegrationTests(RegulatorIqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GenerateAsync_EntityProfile_ReturnsProfileFlagsAndCitations()
    {
        var service = _fixture.CreateResponseGenerator();

        var response = await service.GenerateAsync(
            "Give me a full profile of Access Bank",
            new RegulatorIntentResult
            {
                IntentCode = "ENTITY_PROFILE",
                Confidence = 0.95m,
                ResolvedEntityIds = new List<Guid> { _fixture.AccessBankTenantId },
                ResolvedEntityNames = new List<string> { "Access Bank Plc" }
            },
            CreateContext());

        response.AnswerFormat.Should().Be("profile");
        response.ClassificationLevel.Should().Be("CONFIDENTIAL");
        response.EntitiesAccessed.Should().Contain(_fixture.AccessBankTenantId);
        response.DataSourcesUsed.Should().Contain(new[] { "RG-07", "RG-32", "AI-01", "AI-04", "RG-12" });
        response.Flags.Should().Contain(x => x.Message.Contains("NPL ratio exceeds", StringComparison.OrdinalIgnoreCase));

        var payload = response.StructuredData.Should().BeOfType<RegulatorProfileData>().Subject;
        payload.Profile.InstitutionName.Should().Be("Access Bank Plc");
        payload.Profile.KeyMetrics.Should().Contain(x => x.MetricCode == "carratio" && x.Value == 16.4m);
        response.Citations.Should().Contain(x => x.SourceModule == "RG-07");
        response.Citations.Should().Contain(x => x.SourceModule == "AI-01");
    }

    [Fact]
    public async Task GenerateAsync_EntityCompare_ReturnsComparisonMatrix_ForCarAndNpl()
    {
        var service = _fixture.CreateResponseGenerator();

        var response = await service.GenerateAsync(
            "Compare Access Bank vs Zenith on CAR and NPL",
            new RegulatorIntentResult
            {
                IntentCode = "ENTITY_COMPARE",
                Confidence = 0.95m,
                FieldCode = "carratio",
                ResolvedEntityIds = new List<Guid> { _fixture.AccessBankTenantId, _fixture.ZenithBankTenantId },
                ResolvedEntityNames = new List<string> { "Access Bank Plc", "Zenith Bank Plc" }
            },
            CreateContext());

        response.AnswerFormat.Should().Be("comparison");
        response.EntitiesAccessed.Should().Contain(new[] { _fixture.AccessBankTenantId, _fixture.ZenithBankTenantId });
        response.Flags.Should().Contain(x => x.Message.Contains("NPL ratio exceeds", StringComparison.OrdinalIgnoreCase));

        var payload = response.StructuredData.Should().BeOfType<RegulatorComparisonData>().Subject;
        payload.Rows.Select(x => x.MetricCode).Should().Contain(new[] { "carratio", "nplratio" });

        var carRow = payload.Rows.Single(x => x.MetricCode == "carratio");
        carRow.Values["Access Bank Plc"].Should().Be(16.4m);
        carRow.Values["Zenith Bank Plc"].Should().Be(19.1m);

        var nplRow = payload.Rows.Single(x => x.MetricCode == "nplratio");
        nplRow.Values["Access Bank Plc"].Should().Be(5.8m);
        nplRow.Values["Zenith Bank Plc"].Should().Be(4.1m);
    }

    [Fact]
    public async Task GenerateAsync_TopRanking_ReturnsRankedMetricRows()
    {
        var service = _fixture.CreateResponseGenerator();

        var response = await service.GenerateAsync(
            "Rank DMBs by CAR",
            new RegulatorIntentResult
            {
                IntentCode = "TOP_N_RANKING",
                Confidence = 0.92m,
                FieldCode = "carratio",
                LicenceCategory = "DMB",
                ExtractedParameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["limit"] = "10"
                }
            },
            CreateContext());

        response.AnswerFormat.Should().Be("ranking");
        response.DataSourcesUsed.Should().Contain("RG-07");

        var payload = response.StructuredData.Should().BeOfType<RegulatorRankingData>().Subject;
        payload.Items.Should().NotBeEmpty();
        payload.Items[0].InstitutionName.Should().Be("Zenith Bank Plc");
        payload.Items[0].Value.Should().Be(19.1m);
    }

    [Fact]
    public async Task GenerateAsync_SectorAggregate_ReturnsSectorStatistics()
    {
        var service = _fixture.CreateResponseGenerator();

        var response = await service.GenerateAsync(
            "What is average CAR across all commercial banks?",
            new RegulatorIntentResult
            {
                IntentCode = "SECTOR_AGGREGATE",
                Confidence = 0.91m,
                FieldCode = "carratio",
                LicenceCategory = "DMB",
                PeriodCode = "2026-Q1"
            },
            CreateContext());

        response.AnswerFormat.Should().Be("table");
        response.DataSourcesUsed.Should().Contain("RG-07");

        var payload = response.StructuredData.Should().BeOfType<RegulatorTableData>().Subject;
        payload.Rows.Should().ContainSingle();
        Convert.ToDecimal(payload.Rows[0]["average"]).Should().BeGreaterThan(17m);
        Convert.ToInt32(payload.Rows[0]["entity_count"]).Should().Be(2);
    }

    [Fact]
    public async Task GenerateAsync_SectorSummary_ReturnsCurrentSectorSnapshot()
    {
        var service = _fixture.CreateResponseGenerator();

        var response = await service.GenerateAsync(
            "Show sector health summary",
            new RegulatorIntentResult
            {
                IntentCode = "SECTOR_SUMMARY",
                Confidence = 0.97m,
                LicenceCategory = "DMB",
                PeriodCode = "2026-Q1"
            },
            CreateContext());

        response.AnswerFormat.Should().Be("table");
        response.ClassificationLevel.Should().Be("RESTRICTED");
        response.DataSourcesUsed.Should().Contain("RG-07");
        response.AnswerText.Should().Contain("Average CAR");
        response.AnswerText.Should().Contain("Average NPL");

        var payload = response.StructuredData.Should().BeOfType<RegulatorTableData>().Subject;
        payload.Rows.Should().ContainSingle();
        Convert.ToInt32(payload.Rows[0]["entity_count"]).Should().BeGreaterThanOrEqualTo(2);
        Convert.ToDecimal(payload.Rows[0]["average_car_ratio"]).Should().BeGreaterThan(17m);
        Convert.ToDecimal(payload.Rows[0]["average_npl_ratio"]).Should().BeGreaterThan(4m);
    }

    [Fact]
    public async Task GenerateAsync_SystemicDashboard_ReturnsConfidentialDashboard()
    {
        var service = _fixture.CreateResponseGenerator();

        var response = await service.GenerateAsync(
            "Show me the systemic risk dashboard",
            new RegulatorIntentResult
            {
                IntentCode = "SYSTEMIC_DASHBOARD",
                Confidence = 0.96m
            },
            CreateContext());

        response.AnswerFormat.Should().Be("chart_data");
        response.ClassificationLevel.Should().Be("CONFIDENTIAL");
        response.DataSourcesUsed.Should().Contain("RG-36");
        response.StructuredData.Should().BeOfType<SystemicRiskDashboard>();

        var payload = (SystemicRiskDashboard)response.StructuredData!;
        payload.Summary.TotalEntities.Should().BeGreaterThan(0);
        response.Flags.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateAsync_FilingStatus_InExaminationContext_FiltersToPinnedEntity()
    {
        var service = _fixture.CreateResponseGenerator();

        var response = await service.GenerateAsync(
            "Show filing status",
            new RegulatorIntentResult
            {
                IntentCode = "FILING_STATUS",
                Confidence = 0.95m
            },
            CreateContext(_fixture.AccessBankTenantId));

        response.AnswerFormat.Should().Be("table");
        response.EntitiesAccessed.Should().ContainSingle().Which.Should().Be(_fixture.AccessBankTenantId);
        response.AnswerText.Should().Contain("Access Bank Plc");

        var payload = response.StructuredData.Should().BeOfType<RegulatorTableData>().Subject;
        payload.Rows.Should().OnlyContain(row => Equals(row["institution_name"], "Access Bank Plc"));
    }

    private RegulatorContext CreateContext(Guid? currentExaminationEntityId = null)
    {
        return new RegulatorContext
        {
            RegulatorTenantId = _fixture.CbnRegulatorTenantId,
            RegulatorCode = "CBN",
            RegulatorName = "Central Bank of Nigeria",
            CurrentExaminationEntityId = currentExaminationEntityId,
            RecentEntities = new List<(Guid TenantId, string Name)>(),
            RecentTurns = new List<(string Query, string Intent)>()
        };
    }
}
