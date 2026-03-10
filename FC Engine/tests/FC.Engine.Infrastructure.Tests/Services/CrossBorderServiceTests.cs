using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services.CrossBorder;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FC.Engine.Infrastructure.Tests.Services;

public class CrossBorderServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }

    private static IHarmonisationAuditLogger CreateAudit(MetadataDbContext db) =>
        new HarmonisationAuditLogger(db);

    private static CurrencyConversionEngine CreateFxEngine(MetadataDbContext db) =>
        new(db, CreateAudit(db), NullLogger<CurrencyConversionEngine>.Instance);

    private static EquivalenceMappingService CreateMappingService(MetadataDbContext db) =>
        new(db, CreateAudit(db), NullLogger<EquivalenceMappingService>.Instance);

    private static ConsolidationEngine CreateConsolidationEngine(MetadataDbContext db) =>
        new(db, CreateFxEngine(db), CreateAudit(db), NullLogger<ConsolidationEngine>.Instance);

    private static CrossBorderDataFlowEngine CreateDataFlowEngine(MetadataDbContext db) =>
        new(db, CreateFxEngine(db), CreateAudit(db), NullLogger<CrossBorderDataFlowEngine>.Instance);

    private static DivergenceDetectionService CreateDivergenceService(MetadataDbContext db) =>
        new(db, CreateAudit(db), NullLogger<DivergenceDetectionService>.Instance);

    private static async Task SeedJurisdictions(MetadataDbContext db)
    {
        db.RegulatoryJurisdictions.AddRange(
            new RegulatoryJurisdiction
            {
                JurisdictionCode = "NG", CountryName = "Nigeria", RegulatorCode = "CBN",
                RegulatorName = "Central Bank of Nigeria", CurrencyCode = "NGN", CurrencySymbol = "₦",
                TimeZoneId = "Africa/Lagos", RegulatoryFramework = "Basel III", EcowasRegion = true
            },
            new RegulatoryJurisdiction
            {
                JurisdictionCode = "GH", CountryName = "Ghana", RegulatorCode = "BOG",
                RegulatorName = "Bank of Ghana", CurrencyCode = "GHS", CurrencySymbol = "GH₵",
                TimeZoneId = "Africa/Accra", RegulatoryFramework = "Basel III", EcowasRegion = true
            },
            new RegulatoryJurisdiction
            {
                JurisdictionCode = "KE", CountryName = "Kenya", RegulatorCode = "CBK",
                RegulatorName = "Central Bank of Kenya", CurrencyCode = "KES", CurrencySymbol = "KSh",
                TimeZoneId = "Africa/Nairobi", RegulatoryFramework = "Basel III"
            });
        await db.SaveChangesAsync();
    }

    private static async Task<FinancialGroup> SeedGroupWithSubsidiaries(MetadataDbContext db)
    {
        var group = new FinancialGroup
        {
            GroupCode = "ACCESS_GRP", GroupName = "Access Holdings Plc",
            HeadquarterJurisdiction = "NG", BaseCurrency = "NGN"
        };
        db.FinancialGroups.Add(group);
        await db.SaveChangesAsync();

        db.GroupSubsidiaries.AddRange(
            new GroupSubsidiary
            {
                GroupId = group.Id, InstitutionId = 1, JurisdictionCode = "NG",
                SubsidiaryCode = "ACCESS_NG", SubsidiaryName = "Access Bank Nigeria",
                EntityType = "Commercial Bank", LocalCurrency = "NGN",
                OwnershipPercentage = 100m, ConsolidationMethod = ConsolidationMethod.Full
            },
            new GroupSubsidiary
            {
                GroupId = group.Id, InstitutionId = 2, JurisdictionCode = "GH",
                SubsidiaryCode = "ACCESS_GH", SubsidiaryName = "Access Bank Ghana",
                EntityType = "Commercial Bank", LocalCurrency = "GHS",
                OwnershipPercentage = 100m, ConsolidationMethod = ConsolidationMethod.Full
            },
            new GroupSubsidiary
            {
                GroupId = group.Id, InstitutionId = 3, JurisdictionCode = "KE",
                SubsidiaryCode = "ACCESS_KE", SubsidiaryName = "Access Bank Kenya",
                EntityType = "Commercial Bank", LocalCurrency = "KES",
                OwnershipPercentage = 75m, ConsolidationMethod = ConsolidationMethod.Proportional
            });
        await db.SaveChangesAsync();

        return group;
    }

    private static async Task SeedFxRates(MetadataDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.CrossBorderFxRates.AddRange(
            new CrossBorderFxRate
            {
                BaseCurrency = "USD", QuoteCurrency = "NGN", RateDate = today,
                Rate = 1550m, InverseRate = Math.Round(1m / 1550m, 8),
                RateSource = "CBN", RateType = FxRateType.PeriodEnd
            },
            new CrossBorderFxRate
            {
                BaseCurrency = "USD", QuoteCurrency = "GHS", RateDate = today,
                Rate = 14.5m, InverseRate = Math.Round(1m / 14.5m, 8),
                RateSource = "BOG", RateType = FxRateType.PeriodEnd
            },
            new CrossBorderFxRate
            {
                BaseCurrency = "USD", QuoteCurrency = "KES", RateDate = today,
                Rate = 155m, InverseRate = Math.Round(1m / 155m, 8),
                RateSource = "CBK", RateType = FxRateType.PeriodEnd
            });
        await db.SaveChangesAsync();
    }

    // ── Currency Conversion Tests ────────────────────────────────────

    [Fact]
    public async Task CurrencyConversion_DirectRate_ReturnsCorrectConversion()
    {
        await using var db = CreateDb(nameof(CurrencyConversion_DirectRate_ReturnsCorrectConversion));
        await SeedFxRates(db);
        var sut = CreateFxEngine(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await sut.ConvertAsync(1000m, "USD", "NGN", today);

        result.ConvertedValue.Should().Be(1_550_000m);
        result.FxRate.Should().Be(1550m);
    }

    [Fact]
    public async Task CurrencyConversion_InverseRate_ReturnsCorrectConversion()
    {
        await using var db = CreateDb(nameof(CurrencyConversion_InverseRate_ReturnsCorrectConversion));
        await SeedFxRates(db);
        var sut = CreateFxEngine(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var result = await sut.ConvertAsync(1_550_000m, "NGN", "USD", today);

        result.ConvertedValue.Should().BeApproximately(1000m, 1m);
    }

    [Fact]
    public async Task CurrencyConversion_TriangulationViaUSD_Works()
    {
        await using var db = CreateDb(nameof(CurrencyConversion_TriangulationViaUSD_Works));
        await SeedFxRates(db);
        var sut = CreateFxEngine(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // GHS -> NGN via USD: 100 GHS -> USD -> NGN
        var result = await sut.ConvertAsync(100m, "GHS", "NGN", today);

        // 100 GHS / 14.5 = ~6.896 USD * 1550 = ~10,689.66 NGN
        result.ConvertedValue.Should().BeGreaterThan(10_000m);
        result.SourceCurrency.Should().Be("GHS");
        result.TargetCurrency.Should().Be("NGN");
    }

    [Fact]
    public async Task CurrencyConversion_UpsertRate_CreatesNewRate()
    {
        await using var db = CreateDb(nameof(CurrencyConversion_UpsertRate_CreatesNewRate));
        var sut = CreateFxEngine(db);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await sut.UpsertRateAsync("USD", "ZAR", today, 18.5m, "SARB", FxRateType.PeriodEnd, userId: 1);

        var rate = await sut.GetRateAsync("USD", "ZAR", today);
        rate.Should().Be(18.5m);
    }

    // ── Equivalence Mapping Tests ────────────────────────────────────

    [Fact]
    public async Task EquivalenceMapping_CreateAndRetrieve_Works()
    {
        await using var db = CreateDb(nameof(EquivalenceMapping_CreateAndRetrieve_Works));
        await SeedJurisdictions(db);
        var sut = CreateMappingService(db);

        var id = await sut.CreateMappingAsync(
            "TEST_CAR", "Test CAR Mapping", "CapitalAdequacy", "Test",
            new List<EquivalenceEntryInput>
            {
                new()
                {
                    JurisdictionCode = "NG", RegulatorCode = "CBN",
                    LocalParameterCode = "CAR_NG", LocalParameterName = "Capital Adequacy Ratio",
                    LocalThreshold = 15m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                },
                new()
                {
                    JurisdictionCode = "GH", RegulatorCode = "BOG",
                    LocalParameterCode = "CAR_GH", LocalParameterName = "Capital Adequacy Ratio",
                    LocalThreshold = 13m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                }
            }, userId: 1);

        id.Should().BeGreaterThan(0);

        var retrieved = await sut.GetMappingAsync(id);
        retrieved.Should().NotBeNull();
        retrieved!.MappingCode.Should().Be("TEST_CAR");
        retrieved.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task EquivalenceMapping_GetCrossBorderComparison_ReturnsThresholds()
    {
        await using var db = CreateDb(nameof(EquivalenceMapping_GetCrossBorderComparison_ReturnsThresholds));
        await SeedJurisdictions(db);
        var sut = CreateMappingService(db);

        await sut.CreateMappingAsync(
            "CMP_CAR", "CAR Comparison", "CapitalAdequacy", null,
            new List<EquivalenceEntryInput>
            {
                new()
                {
                    JurisdictionCode = "NG", RegulatorCode = "CBN",
                    LocalParameterCode = "CAR_NG", LocalParameterName = "CAR",
                    LocalThreshold = 15m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                },
                new()
                {
                    JurisdictionCode = "GH", RegulatorCode = "BOG",
                    LocalParameterCode = "CAR_GH", LocalParameterName = "CAR",
                    LocalThreshold = 13m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                }
            }, userId: 1);

        var thresholds = await sut.GetCrossBorderComparisonAsync("CMP_CAR");

        thresholds.Should().HaveCount(2);
        thresholds.Should().Contain(t => t.JurisdictionCode == "NG" && t.Threshold == 15m);
        thresholds.Should().Contain(t => t.JurisdictionCode == "GH" && t.Threshold == 13m);
    }

    // ── Consolidation Engine Tests ───────────────────────────────────

    [Fact]
    public async Task ConsolidationEngine_RunConsolidation_CreatesRunWithSnapshots()
    {
        await using var db = CreateDb(nameof(ConsolidationEngine_RunConsolidation_CreatesRunWithSnapshots));
        await SeedJurisdictions(db);
        var group = await SeedGroupWithSubsidiaries(db);
        await SeedFxRates(db);

        var sut = CreateConsolidationEngine(db);

        var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await sut.RunConsolidationAsync(group.Id, "2025-Q4", snapshotDate, userId: 1);

        result.Should().NotBeNull();
        result.Status.Should().Be(ConsolidationRunStatus.Completed);
        result.TotalSubsidiaries.Should().Be(3);
        result.SubsidiariesCollected.Should().Be(3);
        result.ConsolidatedTotalAssets.Should().BeGreaterThan(0);
        result.ConsolidatedCAR.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ConsolidationEngine_GetRunResult_ReturnsRun()
    {
        await using var db = CreateDb(nameof(ConsolidationEngine_GetRunResult_ReturnsRun));
        await SeedJurisdictions(db);
        var group = await SeedGroupWithSubsidiaries(db);
        await SeedFxRates(db);

        var sut = CreateConsolidationEngine(db);

        var snapshotDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await sut.RunConsolidationAsync(group.Id, "2025-Q4", snapshotDate, userId: 1);

        var fetched = await sut.GetRunResultAsync(result.RunId, group.Id);

        fetched.Should().NotBeNull();
        fetched!.ReportingPeriod.Should().Be("2025-Q4");
        fetched.Status.Should().Be(ConsolidationRunStatus.Completed);
    }

    // ── Data Flow Tests ──────────────────────────────────────────────

    [Fact]
    public async Task DataFlowEngine_DefineAndExecuteFlow_Works()
    {
        await using var db = CreateDb(nameof(DataFlowEngine_DefineAndExecuteFlow_Works));
        await SeedJurisdictions(db);
        var group = await SeedGroupWithSubsidiaries(db);
        await SeedFxRates(db);

        var sut = CreateDataFlowEngine(db);

        var flowId = await sut.DefineFlowAsync(group.Id, new DataFlowDefinition
        {
            FlowCode = "NG_GH_ASSETS", FlowName = "Nigeria to Ghana Total Assets",
            SourceJurisdiction = "NG", SourceReturnCode = "CBN_300", SourceLineCode = "TOTAL_ASSETS",
            TargetJurisdiction = "GH", TargetReturnCode = "BOG_BS", TargetLineCode = "TOTAL_ASSETS",
            Transformation = DataFlowTransformation.Direct,
            RequiresCurrencyConversion = true
        }, userId: 1);

        flowId.Should().BeGreaterThan(0);

        var flows = await sut.ListFlowsAsync(group.Id, null, null);
        flows.Should().HaveCount(1);
        flows[0].FlowCode.Should().Be("NG_GH_ASSETS");

        var results = await sut.ExecuteFlowsAsync(group.Id, "2025-Q4");
        results.Should().HaveCount(1);
        results[0].Status.Should().Be("SUCCESS");
    }

    // ── Divergence Detection Tests ───────────────────────────────────

    [Fact]
    public async Task DivergenceDetection_DetectsThresholdDivergence()
    {
        await using var db = CreateDb(nameof(DivergenceDetection_DetectsThresholdDivergence));
        await SeedJurisdictions(db);
        var mappingService = CreateMappingService(db);

        // Create a mapping with threshold gap > 2 percentage points (15% vs 13% = 2% gap, not > 2)
        // Need gap > 2, so use 15% vs 10%
        await mappingService.CreateMappingAsync(
            "DIV_CAR", "CAR Divergence Test", "CapitalAdequacy", null,
            new List<EquivalenceEntryInput>
            {
                new()
                {
                    JurisdictionCode = "NG", RegulatorCode = "CBN",
                    LocalParameterCode = "CAR_NG", LocalParameterName = "CAR",
                    LocalThreshold = 15m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                },
                new()
                {
                    JurisdictionCode = "GH", RegulatorCode = "BOG",
                    LocalParameterCode = "CAR_GH", LocalParameterName = "CAR",
                    LocalThreshold = 10m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                }
            }, userId: 1);

        var sut = CreateDivergenceService(db);
        var alerts = await sut.DetectDivergencesAsync();

        // Should detect threshold divergence (15% vs 10% = 5% gap > 2%)
        alerts.Should().NotBeEmpty();
        alerts.Should().Contain(a => a.Type == DivergenceType.ThresholdChange);
    }

    [Fact]
    public async Task DivergenceDetection_AcknowledgeAndResolve_Works()
    {
        await using var db = CreateDb(nameof(DivergenceDetection_AcknowledgeAndResolve_Works));
        await SeedJurisdictions(db);
        var mappingService = CreateMappingService(db);

        await mappingService.CreateMappingAsync(
            "ACK_CAR", "CAR Ack Test", "CapitalAdequacy", null,
            new List<EquivalenceEntryInput>
            {
                new()
                {
                    JurisdictionCode = "NG", RegulatorCode = "CBN",
                    LocalParameterCode = "CAR_NG", LocalParameterName = "CAR",
                    LocalThreshold = 15m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                },
                new()
                {
                    JurisdictionCode = "GH", RegulatorCode = "BOG",
                    LocalParameterCode = "CAR_GH", LocalParameterName = "CAR",
                    LocalThreshold = 10m, ThresholdUnit = "PERCENTAGE",
                    CalculationBasis = "RWA", RegulatoryFramework = "Basel III"
                }
            }, userId: 1);

        var sut = CreateDivergenceService(db);
        var alerts = await sut.DetectDivergencesAsync();
        var divergenceId = alerts.First().DivergenceId;

        await sut.AcknowledgeDivergenceAsync(divergenceId, userId: 1);

        var open = await sut.GetOpenDivergencesAsync(null, null);
        open.Should().Contain(a => a.DivergenceId == divergenceId);

        await sut.ResolveDivergenceAsync(divergenceId, "Thresholds harmonised", userId: 1);

        var afterResolve = await sut.GetOpenDivergencesAsync(null, null);
        afterResolve.Should().NotContain(a => a.DivergenceId == divergenceId);
    }

    // ── Pan-African Dashboard Tests ──────────────────────────────────

    [Fact]
    public async Task PanAfricanDashboard_GetGroupOverview_ReturnsData()
    {
        await using var db = CreateDb(nameof(PanAfricanDashboard_GetGroupOverview_ReturnsData));
        await SeedJurisdictions(db);
        var group = await SeedGroupWithSubsidiaries(db);

        var sut = new PanAfricanDashboardService(db);

        var overview = await sut.GetGroupOverviewAsync(group.Id);

        overview.Should().NotBeNull();
        overview!.GroupCode.Should().Be("ACCESS_GRP");
        overview.TotalSubsidiaries.Should().Be(3);
        overview.TotalJurisdictions.Should().Be(3);
        overview.ByJurisdiction.Should().HaveCount(3);
    }

    [Fact]
    public async Task PanAfricanDashboard_GetDeadlineCalendar_ReturnsDeadlines()
    {
        await using var db = CreateDb(nameof(PanAfricanDashboard_GetDeadlineCalendar_ReturnsDeadlines));
        await SeedJurisdictions(db);
        var group = await SeedGroupWithSubsidiaries(db);

        var now = DateTimeOffset.UtcNow;
        db.RegulatoryDeadlines.Add(new RegulatoryDeadline
        {
            JurisdictionCode = "NG", RegulatorCode = "CBN",
            ReturnCode = "CBN_300", ReturnName = "Prudential Returns",
            ReportingPeriod = "2025-Q4", DeadlineUtc = now.AddDays(30),
            LocalTimeZone = "Africa/Lagos", Frequency = "Quarterly",
            Status = DeadlineStatus.Upcoming
        });
        await db.SaveChangesAsync();

        var sut = new PanAfricanDashboardService(db);
        var from = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60));

        var deadlines = await sut.GetDeadlineCalendarAsync(group.Id, from, to);

        deadlines.Should().HaveCount(1);
        deadlines[0].ReturnCode.Should().Be("CBN_300");
        deadlines[0].DaysUntilDeadline.Should().BeGreaterThan(0);
    }

    // ── AfCFTA Tracking Tests ────────────────────────────────────────

    [Fact]
    public async Task AfcftaTracking_ListAndUpdateStatus_Works()
    {
        await using var db = CreateDb(nameof(AfcftaTracking_ListAndUpdateStatus_Works));

        db.AfcftaProtocolTracking.Add(new AfcftaProtocolTracking
        {
            ProtocolCode = "AFCFTA_FIN_001",
            ProtocolName = "Financial Services Mutual Recognition",
            Category = "Financial Services",
            Status = AfcftaProtocolStatus.Proposed,
            ParticipatingJurisdictions = "NG,GH,KE,ZA,EG",
            Description = "Mutual recognition of banking licences across AfCFTA member states"
        });
        await db.SaveChangesAsync();

        var sut = new AfcftaTrackingService(db, CreateAudit(db));

        var protocols = await sut.ListProtocolsAsync();
        protocols.Should().HaveCount(1);
        protocols[0].ProtocolCode.Should().Be("AFCFTA_FIN_001");
        protocols[0].ParticipatingJurisdictions.Should().HaveCount(5);

        await sut.UpdateProtocolStatusAsync("AFCFTA_FIN_001", AfcftaProtocolStatus.Negotiating, userId: 1);

        var updated = await sut.GetProtocolAsync("AFCFTA_FIN_001");
        updated.Should().NotBeNull();
        updated!.Status.Should().Be(AfcftaProtocolStatus.Negotiating);
    }

    // ── Audit Logger Tests ───────────────────────────────────────────

    [Fact]
    public async Task AuditLogger_LogAsync_PersistsEntry()
    {
        await using var db = CreateDb(nameof(AuditLogger_LogAsync_PersistsEntry));
        var sut = new HarmonisationAuditLogger(db);
        var correlationId = Guid.NewGuid();

        await sut.LogAsync(1, "NG", correlationId, "TEST_ACTION",
            new { Detail = "Test detail" }, userId: 42);

        var entries = await db.HarmonisationAuditLogs.ToListAsync();
        entries.Should().HaveCount(1);
        entries[0].Action.Should().Be("TEST_ACTION");
        entries[0].GroupId.Should().Be(1);
        entries[0].PerformedByUserId.Should().Be(42);
        entries[0].CorrelationId.Should().Be(correlationId);
    }
}
