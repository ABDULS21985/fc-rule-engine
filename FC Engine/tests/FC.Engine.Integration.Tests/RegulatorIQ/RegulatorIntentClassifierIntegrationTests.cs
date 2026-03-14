using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

[Collection("RegulatorIqIntegration")]
public sealed class RegulatorIntentClassifierIntegrationTests : IClassFixture<RegulatorIqFixture>
{
    private readonly RegulatorIqFixture _fixture;

    public RegulatorIntentClassifierIntegrationTests(RegulatorIqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ClassifyAsync_ResolvesExactAlias_ToEntityProfile()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "Give me a full profile of Access Bank",
            CreateContext());

        result.IntentCode.Should().Be("ENTITY_PROFILE");
        result.ResolvedEntityIds.Should().Contain(_fixture.AccessBankTenantId);
        result.ResolvedEntityNames.Should().Contain("Access Bank Plc");
    }

    [Fact]
    public async Task ClassifyAsync_ResolvesFuzzyEntityName()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "Show me the full profile of Acess Bank",
            CreateContext());

        result.IntentCode.Should().Be("ENTITY_PROFILE");
        result.ResolvedEntityIds.Should().Contain(_fixture.AccessBankTenantId);
    }

    [Fact]
    public async Task ClassifyAsync_ResolvesComparativeEntities()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "Compare GTBank vs Zenith on CAR and NPL",
            CreateContext());

        result.IntentCode.Should().Be("ENTITY_COMPARE");
        result.ResolvedEntityIds.Should().Contain(_fixture.GtBankTenantId);
        result.ResolvedEntityIds.Should().Contain(_fixture.ZenithBankTenantId);
        result.FieldCode.Should().Be("carratio");
    }

    [Fact]
    public async Task ClassifyAsync_ExpandsCommercialBankGroup()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "Show sector NPL trend across all commercial banks over the last 8 quarters",
            CreateContext());

        result.IntentCode.Should().Be("SECTOR_TREND");
        result.LicenceCategory.Should().Be("DMB");
        result.FieldCode.Should().Be("nplratio");
        result.ResolvedEntityIds.Should().HaveCountGreaterOrEqualTo(5);
    }

    [Fact]
    public async Task ClassifyAsync_UsesConversationContext_ForPronouns()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "What about their NPL?",
            CreateContext(recentEntities:
            [
                (_fixture.AccessBankTenantId, "Access Bank Plc")
            ]));

        result.IntentCode.Should().Be("CURRENT_VALUE");
        result.FieldCode.Should().Be("nplratio");
        result.ResolvedEntityIds.Should().Contain(_fixture.AccessBankTenantId);
    }

    [Fact]
    public async Task ClassifyAsync_SectorHealthSummary_DoesNotTriggerEntityDisambiguation()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "Show sector health summary",
            CreateContext());

        result.IntentCode.Should().Be("SECTOR_SUMMARY");
        result.NeedsDisambiguation.Should().BeFalse();
        result.DisambiguationOptions.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ClassifyAsync_ExaminationContext_FilingStatusPrefersPinnedEntity()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "Show filing status",
            new RegulatorContext
            {
                RegulatorTenantId = Guid.NewGuid(),
                RegulatorCode = "CBN",
                RegulatorName = "Central Bank of Nigeria",
                CurrentExaminationEntityId = _fixture.AccessBankTenantId
            });

        result.IntentCode.Should().Be("FILING_STATUS");
        result.NeedsDisambiguation.Should().BeFalse();
        result.ResolvedEntityIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsDisambiguationOptions_ForAmbiguousEntity()
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(
            "Compare First vs Zenith on CAR",
            CreateContext());

        result.NeedsDisambiguation.Should().BeTrue();
        result.DisambiguationOptions.Should().NotBeNull();
        result.DisambiguationOptions.Should().Contain("First Bank Nigeria Limited");
        result.DisambiguationOptions.Should().Contain("First City Monument Bank Plc");
    }

    [Fact]
    public async Task ClassifyAsync_UsesLlmFallback_WhenDeterministicConfidenceIsLow()
    {
        var llm = new ConfiguredLlmService(new
        {
            intentCode = "ENTITY_PROFILE",
            confidence = 0.91m,
            entityNames = new[] { "Access Bank" },
            fieldCode = (string?)null,
            periodCode = (string?)null,
            licenceCategory = "DMB",
            needsDisambiguation = false,
            disambiguationOptions = Array.Empty<string>(),
            extractedParameters = new Dictionary<string, string>()
        });

        var classifier = _fixture.CreateIntentClassifier(llm);

        var result = await classifier.ClassifyAsync(
            "Give me the big picture on Acess",
            CreateContext());

        llm.StructuredCallCount.Should().Be(1);
        result.IntentCode.Should().Be("ENTITY_PROFILE");
        result.ResolvedEntityIds.Should().Contain(_fixture.AccessBankTenantId);
    }

    [Theory]
    [InlineData("Give me a full profile of Access Bank", "ENTITY_PROFILE")]
    [InlineData("Show sector NPL trend for commercial banks over the last 8 quarters", "SECTOR_TREND")]
    [InlineData("Top 5 banks by total assets", "TOP_N_RANKING")]
    [InlineData("Which banks have overdue returns?", "FILING_STATUS")]
    [InlineData("Rank institutions by filing timeliness", "FILING_DELINQUENCY")]
    [InlineData("Rank DMBs by compliance health score", "CHS_RANKING")]
    [InlineData("Show Access Bank CHS score", "CHS_ENTITY")]
    [InlineData("Which institutions have active EWIs?", "EWI_STATUS")]
    [InlineData("Show me the systemic risk dashboard", "SYSTEMIC_DASHBOARD")]
    [InlineData("What happens if Access Bank fails?", "CONTAGION_QUERY")]
    [InlineData("List available stress scenarios", "STRESS_SCENARIOS")]
    [InlineData("Show sanctions exposure for Access Bank", "SANCTIONS_EXPOSURE")]
    [InlineData("Generate an examination briefing for Access Bank", "EXAMINATION_BRIEF")]
    [InlineData("Show open supervisory actions", "SUPERVISORY_ACTIONS")]
    [InlineData("Show cross-border divergence for GTCO group", "CROSS_BORDER")]
    [InlineData("What is the impact of a CRR increase?", "POLICY_IMPACT")]
    [InlineData("Show validation hotspots across banks", "VALIDATION_HOTSPOT")]
    [InlineData("Show sector health summary", "SECTOR_SUMMARY")]
    [InlineData("What is average CAR across all commercial banks?", "SECTOR_AGGREGATE")]
    [InlineData("Compare GTBank vs Zenith on CAR", "ENTITY_COMPARE")]
    [InlineData("Rank institutions by anomaly pressure", "RISK_RANKING")]
    [InlineData("What does BSD/DIR/2024/003 require?", "REGULATORY_LOOKUP")]
    [InlineData("What if NPL doubles?", "SCENARIO")]
    [InlineData("What is Access Bank's CAR?", "CURRENT_VALUE")]
    [InlineData("Show Access Bank CAR trend", "TREND")]
    [InlineData("Compare Access Bank to peers on CAR", "COMPARISON_PEER")]
    [InlineData("Compare Access Bank CAR between Q4 2025 and Q1 2026", "COMPARISON_PERIOD")]
    [InlineData("When is the next filing due?", "DEADLINE")]
    [InlineData("Are we compliant?", "COMPLIANCE_STATUS")]
    [InlineData("Show anomaly status for the latest return", "ANOMALY_STATUS")]
    [InlineData("Search validation errors for CAR", "SEARCH")]
    [InlineData("What can I ask?", "HELP")]
    [InlineData("How does this work?", "HELP")]
    public async Task ClassifyAsync_RecognisesSupportedIntentCatalog(string query, string expectedIntent)
    {
        var classifier = _fixture.CreateIntentClassifier();

        var result = await classifier.ClassifyAsync(query, CreateContext());

        result.IntentCode.Should().Be(expectedIntent);
    }

    private static RegulatorContext CreateContext(
        List<(Guid TenantId, string Name)>? recentEntities = null)
    {
        return new RegulatorContext
        {
            RegulatorTenantId = Guid.NewGuid(),
            RegulatorCode = "CBN",
            RegulatorName = "Central Bank of Nigeria",
            RecentEntities = recentEntities ?? new List<(Guid TenantId, string Name)>(),
            RecentTurns = new List<(string Query, string Intent)>()
        };
    }

    private sealed class ConfiguredLlmService : ILlmService
    {
        private readonly string _json;

        public ConfiguredLlmService(object payload) => _json = JsonSerializer.Serialize(payload);

        public int StructuredCallCount { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse
            {
                Success = true,
                Content = _json,
                Model = "test-model"
            });

        public Task<T> CompleteStructuredAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class
        {
            StructuredCallCount++;
            return Task.FromResult(JsonSerializer.Deserialize<T>(_json)!);
        }
    }
}
