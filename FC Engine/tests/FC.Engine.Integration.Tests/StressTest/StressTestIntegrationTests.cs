using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using Xunit;

namespace FC.Engine.Integration.Tests.StressTest;

[Collection("StressTestIntegration")]
public sealed class StressTestIntegrationTests
    : IClassFixture<StressTestFixture>
{
    private readonly StressTestFixture _fx;
    public StressTestIntegrationTests(StressTestFixture fx) => _fx = fx;

    // ── Test 1: Oil collapse correctly reduces CAR ──────────────────────────
    [Fact]
    public void MacroShockTransmitter_OilCollapseScenario_ReducesCAR()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 1, InstitutionType: "DMB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 18.0m, NPL: 4.5m, LCR: 130.0m, NSFR: 115.0m, ROA: 1.8m,
            TotalAssets: 500_000m, TotalDeposits: 350_000m,
            OilSectorExposurePct: 25.0m,
            AgriExposurePct: 5.0m,
            FXLoansAssetPct: 15.0m,
            BondPortfolioAssetPct: 10.0m,
            TopDepositorConcentration: 25.0m);

        var parameters = new ResolvedShockParameters(
            ScenarioId: 4, InstitutionType: "DMB",
            GDPGrowthShock: -3.0m,
            OilPriceShockPct: -50.0m,
            FXDepreciationPct: 30.0m,
            InflationShockPp: 5.0m,
            InterestRateShockBps: 0,
            TradeVolumeShockPct: 0m,
            RemittanceShockPct: 0m,
            FDIShockPct: 0m,
            CarbonTaxUSDPerTon: 0m, StrandedAssetsPct: 0m,
            PhysicalRiskHazardCode: null,
            CARDeltaPerGDPPp: -0.32m,
            NPLDeltaPerGDPPp: 0.45m,
            LCRDeltaPerRateHike100: -0.08m,
            CARDeltaPerFXPct: -0.12m,
            NPLDeltaPerFXPct: 0.18m,
            CARDeltaPerOilPct: -0.10m,
            NPLDeltaPerOilPct: 0.25m,
            LCRDeltaPerCyber: 0m,
            DepositOutflowPctCyber: 0m);

        var result = _fx.ShockTransmitter.ApplyShock(snapshot, parameters);

        Assert.True(result.PostCAR < result.PreCAR,
            $"PostCAR {result.PostCAR:F2} should be less than PreCAR {result.PreCAR:F2}");
        Assert.True(result.PostNPL > result.PreNPL,
            "NPL should increase under oil price collapse");
        Assert.True(result.PostCAR - result.PreCAR < 0,
            "DeltaCAR must be negative");
    }

    // ── Test 2: NGFS Hot House — agri NPL spike ─────────────────────────────
    [Fact]
    public void MacroShockTransmitter_NGFSHotHouse_AgriNPLSpikes()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 2, InstitutionType: "MFB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 12.5m, NPL: 6.0m, LCR: 115.0m, NSFR: 108.0m, ROA: 1.2m,
            TotalAssets: 15_000m, TotalDeposits: 10_000m,
            OilSectorExposurePct: 2.0m,
            AgriExposurePct: 45.0m,
            FXLoansAssetPct: 1.0m, BondPortfolioAssetPct: 5.0m,
            TopDepositorConcentration: 20.0m);

        var parameters = new ResolvedShockParameters(
            ScenarioId: 3, InstitutionType: "MFB",
            GDPGrowthShock: -2.0m,
            OilPriceShockPct: 0m, FXDepreciationPct: 0m, InflationShockPp: 0m,
            InterestRateShockBps: 0,
            TradeVolumeShockPct: 0m,
            RemittanceShockPct: 0m,
            FDIShockPct: 0m,
            CarbonTaxUSDPerTon: 0m, StrandedAssetsPct: 5.0m,
            PhysicalRiskHazardCode: "FLOOD_DROUGHT",
            CARDeltaPerGDPPp: -0.30m, NPLDeltaPerGDPPp: 0.90m,
            LCRDeltaPerRateHike100: 0m, CARDeltaPerFXPct: 0m,
            NPLDeltaPerFXPct: 0m, CARDeltaPerOilPct: 0m, NPLDeltaPerOilPct: 0m,
            LCRDeltaPerCyber: 0m, DepositOutflowPctCyber: 0m);

        var result = _fx.ShockTransmitter.ApplyShock(snapshot, parameters);

        // Agri NPL spike: 45% agri × 35% hazard × 2 (FLOOD_DROUGHT) = 31.5pp NPL rise
        Assert.True(result.PostNPL > 20.0m,
            $"Hot House MFB NPL should exceed 20% with 45% agri exposure, got {result.PostNPL:F2}");
        Assert.True(result.BreachesCAR,
            "Hot House MFB should breach CAR minimum after provisioning impact");
    }

    // ── Test 3: Rate spike destroys LCR for bond-heavy bank ─────────────────
    [Fact]
    public void MacroShockTransmitter_RateSpike500bps_LCRBreaches()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 3, InstitutionType: "DMB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 17.5m, NPL: 3.2m, LCR: 118.0m, NSFR: 112.0m, ROA: 1.9m,
            TotalAssets: 800_000m, TotalDeposits: 600_000m,
            OilSectorExposurePct: 10m, AgriExposurePct: 5m,
            FXLoansAssetPct: 5m, BondPortfolioAssetPct: 30.0m,
            TopDepositorConcentration: 18m);

        var parameters = new ResolvedShockParameters(
            ScenarioId: 6, InstitutionType: "DMB",
            GDPGrowthShock: 0m, OilPriceShockPct: 0m,
            FXDepreciationPct: 0m, InflationShockPp: 0m,
            InterestRateShockBps: 500,
            TradeVolumeShockPct: 0m,
            RemittanceShockPct: 0m,
            FDIShockPct: 0m,
            CarbonTaxUSDPerTon: 0m, StrandedAssetsPct: 0m,
            PhysicalRiskHazardCode: null,
            CARDeltaPerGDPPp: 0m, NPLDeltaPerGDPPp: 0.60m,
            LCRDeltaPerRateHike100: -0.15m, CARDeltaPerFXPct: 0m,
            NPLDeltaPerFXPct: 0m, CARDeltaPerOilPct: 0m, NPLDeltaPerOilPct: 0m,
            LCRDeltaPerCyber: 0m, DepositOutflowPctCyber: 0m);

        var result = _fx.ShockTransmitter.ApplyShock(snapshot, parameters);

        Assert.True(result.PostLCR < result.PreLCR, "LCR must decrease on rate spike");
        Assert.True(result.BreachesLCR,
            $"LCR should breach 100% for heavy bond holder, got {result.PostLCR:F2}");
    }

    // ── Test 4: Cyber scenario triggers LCR collapse ─────────────────────────
    [Fact]
    public void MacroShockTransmitter_CyberScenario_LCRCollapse()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 4, InstitutionType: "DMB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 16.0m, NPL: 5.1m, LCR: 107.0m, NSFR: 104.0m, ROA: 1.1m,
            TotalAssets: 200_000m, TotalDeposits: 150_000m,
            OilSectorExposurePct: 8m, AgriExposurePct: 5m,
            FXLoansAssetPct: 5m, BondPortfolioAssetPct: 8m,
            TopDepositorConcentration: 35.0m);

        var parameters = new ResolvedShockParameters(
            ScenarioId: 8, InstitutionType: "DMB",
            GDPGrowthShock: 0m, OilPriceShockPct: 0m,
            FXDepreciationPct: 0m, InflationShockPp: 0m,
            InterestRateShockBps: 0,
            TradeVolumeShockPct: 0m,
            RemittanceShockPct: 0m,
            FDIShockPct: 0m,
            CarbonTaxUSDPerTon: 0m, StrandedAssetsPct: 0m,
            PhysicalRiskHazardCode: null,
            CARDeltaPerGDPPp: 0m, NPLDeltaPerGDPPp: 0m, LCRDeltaPerRateHike100: 0m,
            CARDeltaPerFXPct: 0m, NPLDeltaPerFXPct: 0m,
            CARDeltaPerOilPct: 0m, NPLDeltaPerOilPct: 0m,
            LCRDeltaPerCyber: -25.0m,
            DepositOutflowPctCyber: 15.0m);

        var result = _fx.ShockTransmitter.ApplyShock(snapshot, parameters);

        Assert.True(result.PostLCR < 100.0m,
            $"Cyber shock should breach LCR, got {result.PostLCR:F2}");
    }

    // ── Test 5: Full sector run aggregates correctly ──────────────────────────
    [Fact]
    public async Task Orchestrator_OilCollapseSector_CorrectBreachCounts()
    {
        await _fx.SeedSectorAsync("2026-Q1", new[]
        {
            (10, "DMB", 18.0m, 4.0m, 130.0m, 25.0m),   // oil-heavy → breach
            (11, "DMB", 22.0m, 2.5m, 145.0m,  5.0m),   // low oil  → survive
            (12, "MFB", 11.0m, 7.0m, 115.0m,  2.0m),   // marginal → breach
        });

        var summary = await _fx.Orchestrator.RunAsync(
            "CBN", scenarioId: 4, periodCode: "2026-Q1",
            timeHorizon: "1Y", initiatedByUserId: 1);

        Assert.Equal(3, summary.EntitiesShocked);
        Assert.True(summary.BySector.Sum(a => a.EntitiesBreachingCAR) >= 1,
            "At least 1 entity should breach CAR under oil collapse");
        Assert.True(summary.SystemicResilienceScore < 100,
            "Resilience score must be reduced from 100 after shocks");
        Assert.False(string.IsNullOrWhiteSpace(summary.ExecutiveSummary));
    }

    // ── Test 6: Contagion cascade propagates correctly ────────────────────────
    [Fact]
    public async Task ContagionEngine_InterbankExposure_PropagatesFailure()
    {
        await _fx.SeedSectorAsync("2026-Q1", new[]
        {
            (15, "DMB", 14.0m, 8.0m, 95.0m, 40.0m),
            (16, "DMB", 16.5m, 3.0m, 125.0m, 5.0m),
        });
        await _fx.SeedInterbankAsync("2026-Q1", (16, 15, 50_000m)); // 16 lent to 15

        var round0 = new List<EntityShockResult>
        {
            new(15, "DMB", 14m, 8m, 95m, 110m, 1.0m, 200_000m, 140_000m,
                8m, 15m, 60m, 95m, -1m, 5_000m, 2_000m,
                true, true, false, true, 0m, 0m)
        };

        var (addFailed, events, rounds) = await _fx.ContagionEngine.CascadeAsync(
            round0, "CBN", "2026-Q1", runId: 9999);

        Assert.True(events.Count > 0,
            "Contagion events should be generated from insolvent bank 15");
        Assert.True(rounds >= 1, "At least 1 contagion round should execute");
    }

    // ── Test 7: QuestPDF report generates without error ───────────────────────
    [Fact]
    public async Task ReportGenerator_OilCollapseRun_GeneratesPDF()
    {
        await _fx.SeedSectorAsync("2026-Q1", new[]
        {
            (1, "DMB", 18.0m, 4.0m, 130.0m, 25.0m),
            (2, "MFB", 11.5m, 7.0m, 112.0m,  2.0m),
        });

        var summary = await _fx.Orchestrator.RunAsync(
            "CBN", scenarioId: 4, periodCode: "2026-Q1",
            timeHorizon: "1Y", initiatedByUserId: 1);

        var pdfBytes = await _fx.ReportGenerator.GenerateAsync(
            summary.RunId, anonymiseEntities: true);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 5_000,
            $"PDF should be >5KB, got {pdfBytes.Length} bytes");

        // Validate PDF magic bytes: %PDF
        Assert.Equal(0x25, pdfBytes[0]);
        Assert.Equal(0x50, pdfBytes[1]);
        Assert.Equal(0x44, pdfBytes[2]);
        Assert.Equal(0x46, pdfBytes[3]);
    }

    [Fact]
    public async Task GetRunSummaryAsync_WithPersistedRun_ReturnsMaterialisedSummary()
    {
        await _fx.SeedSectorAsync("2026-Q1", new[]
        {
            (31, "DMB", 18.0m, 4.0m, 130.0m, 25.0m),
            (32, "MFB", 11.5m, 7.0m, 112.0m, 2.0m),
        });

        var run = await _fx.Orchestrator.RunAsync(
            "CBN", scenarioId: 4, periodCode: "2026-Q1",
            timeHorizon: "1Y", initiatedByUserId: 1);

        var reloaded = await _fx.Orchestrator.GetRunSummaryAsync(run.RunGuid, "CBN");

        Assert.NotNull(reloaded);
        Assert.Equal(run.RunGuid, reloaded!.RunGuid);
        Assert.Equal(run.RunId, reloaded.RunId);
        Assert.Equal(run.ScenarioCode, reloaded.ScenarioCode);
        Assert.Equal(run.PeriodCode, reloaded.PeriodCode);
        Assert.NotEmpty(reloaded.BySector);
        Assert.True(reloaded.Duration >= TimeSpan.Zero);
    }

    // ── Test 8: Resilience rating scale is correct ────────────────────────────
    [Fact]
    public void MacroShockTransmitter_HealthyBank_NoBreaches()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 99, InstitutionType: "DMB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 25.0m, NPL: 1.5m, LCR: 160.0m, NSFR: 130.0m, ROA: 2.5m,
            TotalAssets: 1_000_000m, TotalDeposits: 700_000m,
            OilSectorExposurePct: 2.0m, AgriExposurePct: 2.0m,
            FXLoansAssetPct: 2.0m, BondPortfolioAssetPct: 5.0m,
            TopDepositorConcentration: 10.0m);

        // Mild shock
        var parameters = new ResolvedShockParameters(
            ScenarioId: 1, InstitutionType: "DMB",
            GDPGrowthShock: -0.5m,
            OilPriceShockPct: 0m, FXDepreciationPct: 0m, InflationShockPp: 0m,
            InterestRateShockBps: 0,
            TradeVolumeShockPct: 0m,
            RemittanceShockPct: 0m,
            FDIShockPct: 0m,
            CarbonTaxUSDPerTon: 147m, StrandedAssetsPct: 8.0m,
            PhysicalRiskHazardCode: null,
            CARDeltaPerGDPPp: -0.10m, NPLDeltaPerGDPPp: 0.15m,
            LCRDeltaPerRateHike100: 0m, CARDeltaPerFXPct: 0m,
            NPLDeltaPerFXPct: 0m, CARDeltaPerOilPct: 0m, NPLDeltaPerOilPct: 0m,
            LCRDeltaPerCyber: 0m, DepositOutflowPctCyber: 0m);

        var result = _fx.ShockTransmitter.ApplyShock(snapshot, parameters);

        Assert.False(result.IsInsolvent, "Well-capitalised bank should not become insolvent under mild shock");
        Assert.Equal(0m, result.InsurableDeposits, precision: 0);
        Assert.Equal(0m, result.UninsurableDeposits, precision: 0);
    }
}

// ── Fixture ───────────────────────────────────────────────────────────────────
public sealed class StressTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Stress_T3st_P@ss!")
        .Build();

    public IMacroShockTransmitter ShockTransmitter { get; private set; } = null!;
    public IContagionCascadeEngine ContagionEngine { get; private set; } = null!;
    public IStressTestOrchestrator Orchestrator { get; private set; } = null!;
    public IStressTestReportGenerator ReportGenerator { get; private set; } = null!;
    public IDbConnectionFactory Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        var cs = _sqlContainer.GetConnectionString();

        // Apply schema using raw SQL (migration DDL)
        await using var setupConn = new SqlConnection(cs);
        await setupConn.OpenAsync();
        await ApplySchemaAsync(setupConn);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IDbConnectionFactory>(new TestDbConnectionFactory(cs));
        services.AddStressTestingFramework(new ConfigurationBuilder().Build());

        var sp = services.BuildServiceProvider();
        Db                = sp.GetRequiredService<IDbConnectionFactory>();
        ShockTransmitter  = sp.GetRequiredService<IMacroShockTransmitter>();
        ContagionEngine   = sp.GetRequiredService<IContagionCascadeEngine>();
        Orchestrator      = sp.GetRequiredService<IStressTestOrchestrator>();
        ReportGenerator   = sp.GetRequiredService<IStressTestReportGenerator>();

        await SeedBaseDataAsync();
    }

    public async Task DisposeAsync() => await _sqlContainer.DisposeAsync();

    private static async Task ApplySchemaAsync(SqlConnection conn)
    {
        // Create prerequisite tables that the stress test schema depends on
        await conn.ExecuteAsync("""
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='Regulators')
CREATE TABLE Regulators (Code VARCHAR(10) PRIMARY KEY, Name NVARCHAR(150) NOT NULL);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='Institutions')
CREATE TABLE Institutions (
    Id              INT PRIMARY KEY,
    RegulatorCode   VARCHAR(10)     NOT NULL,
    InstitutionType VARCHAR(20)     NOT NULL,
    LicenseNumber   VARCHAR(50)     NOT NULL,
    InstitutionName NVARCHAR(150)   NULL,
    ShortName       NVARCHAR(150)   NOT NULL,
    IsActive        BIT             NOT NULL DEFAULT 1
);

IF SCHEMA_ID('meta') IS NULL
    EXEC('CREATE SCHEMA meta');

IF OBJECT_ID('meta.prudential_metrics', 'U') IS NULL
CREATE TABLE meta.prudential_metrics (
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    InstitutionType     VARCHAR(20)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    AsOfDate            DATE            NOT NULL,
    CAR                 DECIMAL(8,4)    NULL,
    NPLRatio            DECIMAL(8,4)    NULL,
    LCR                 DECIMAL(8,4)    NULL,
    NSFR                DECIMAL(8,4)    NULL,
    ROA                 DECIMAL(8,4)    NULL,
    TotalAssets         DECIMAL(18,2)   NULL,
    TotalDeposits       DECIMAL(18,2)   NULL,
    OilSectorExposurePct  DECIMAL(8,4)  NULL,
    AgriExposurePct       DECIMAL(8,4)  NULL,
    FXLoansAssetPct       DECIMAL(8,4)  NULL,
    BondPortfolioAssetPct DECIMAL(8,4)  NULL,
    DepositConcentration  DECIMAL(8,4)  NULL,
    PRIMARY KEY (InstitutionId, PeriodCode)
);

IF OBJECT_ID('meta.interbank_exposures', 'U') IS NULL
CREATE TABLE meta.interbank_exposures (
    Id                      INT IDENTITY(1,1) PRIMARY KEY,
    LendingInstitutionId    INT             NOT NULL,
    BorrowingInstitutionId  INT             NOT NULL,
    RegulatorCode           VARCHAR(10)     NOT NULL,
    PeriodCode              VARCHAR(10)     NOT NULL,
    ExposureAmount          DECIMAL(18,2)   NOT NULL,
    ExposureType            VARCHAR(20)     NOT NULL,
    AsOfDate                DATE            NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='SystemConfiguration')
CREATE TABLE SystemConfiguration (
    ConfigKey   VARCHAR(100)    PRIMARY KEY,
    ConfigValue DECIMAL(18,2)   NOT NULL
);

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='StressScenarios')
BEGIN
    CREATE TABLE StressScenarios (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        ScenarioCode        VARCHAR(30)     NOT NULL,
        ScenarioName        NVARCHAR(150)   NOT NULL,
        Category            VARCHAR(20)     NOT NULL,
        NarrativeSummary    NVARCHAR(2000)  NOT NULL,
        TimeHorizon         VARCHAR(10)     NOT NULL DEFAULT '1Y',
        Severity            VARCHAR(10)     NOT NULL,
        IsActive            BIT             NOT NULL DEFAULT 1,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT UQ_StressScenarios_Code UNIQUE (ScenarioCode)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='StressScenarioParameters')
BEGIN
    CREATE TABLE StressScenarioParameters (
        Id                      INT IDENTITY(1,1) PRIMARY KEY,
        ScenarioId              INT             NOT NULL,
        InstitutionType         VARCHAR(20)     NOT NULL,
        GDPGrowthShock          DECIMAL(8,4)    NULL,
        OilPriceShockPct        DECIMAL(8,4)    NULL,
        FXDepreciationPct       DECIMAL(8,4)    NULL,
        InflationShockPp        DECIMAL(8,4)    NULL,
        InterestRateShockBps    INT             NULL,
        TradeVolumeShockPct     DECIMAL(8,4)    NULL,
        RemittanceShockPct      DECIMAL(8,4)    NULL,
        FDIShockPct             DECIMAL(8,4)    NULL,
        CarbonTaxUSDPerTon      DECIMAL(10,2)   NULL,
        PhysicalRiskHazardCode  VARCHAR(20)     NULL,
        StrandedAssetsPct       DECIMAL(8,4)    NULL,
        CARDeltaPerGDPPp        DECIMAL(8,6)    NULL,
        NPLDeltaPerGDPPp        DECIMAL(8,6)    NULL,
        LCRDeltaPerRateHike100  DECIMAL(8,6)    NULL,
        CARDeltaPerFXPct        DECIMAL(8,6)    NULL,
        NPLDeltaPerFXPct        DECIMAL(8,6)    NULL,
        CARDeltaPerOilPct       DECIMAL(8,6)    NULL,
        NPLDeltaPerOilPct       DECIMAL(8,6)    NULL,
        LCRDeltaPerCyber        DECIMAL(8,4)    NULL,
        DepositOutflowPctCyber  DECIMAL(8,4)    NULL,
        FOREIGN KEY (ScenarioId) REFERENCES StressScenarios(Id)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='StressTestRuns')
BEGIN
    CREATE TABLE StressTestRuns (
        Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,
        RunGuid                 UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        RegulatorCode           VARCHAR(10)     NOT NULL,
        ScenarioId              INT             NOT NULL,
        PeriodCode              VARCHAR(10)     NOT NULL,
        TimeHorizon             VARCHAR(10)     NOT NULL,
        Status                  VARCHAR(20)     NOT NULL DEFAULT 'RUNNING',
        EntitiesShocked         INT             NOT NULL DEFAULT 0,
        ContagionRounds         INT             NOT NULL DEFAULT 0,
        InitiatedByUserId       INT             NOT NULL,
        SystemicResilienceScore DECIMAL(5,2)    NULL,
        ExecutiveSummary        NVARCHAR(MAX)   NULL,
        ErrorMessage            NVARCHAR(2000)  NULL,
        StartedAt               DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CompletedAt             DATETIME2(3)    NULL,
        CONSTRAINT UQ_STR_Guid UNIQUE (RunGuid),
        FOREIGN KEY (ScenarioId) REFERENCES StressScenarios(Id)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='StressTestEntityResults')
BEGIN
    CREATE TABLE StressTestEntityResults (
        Id                   BIGINT IDENTITY(1,1) PRIMARY KEY,
        RunId                BIGINT          NOT NULL,
        InstitutionId        INT             NOT NULL,
        RegulatorCode        VARCHAR(10)     NOT NULL,
        InstitutionType      VARCHAR(20)     NOT NULL,
        PreCAR               DECIMAL(8,4)    NULL,
        PreNPL               DECIMAL(8,4)    NULL,
        PreLCR               DECIMAL(8,4)    NULL,
        PreNSFR              DECIMAL(8,4)    NULL,
        PreROA               DECIMAL(8,4)    NULL,
        PreTotalAssets       DECIMAL(18,2)   NULL,
        PreTotalDeposits     DECIMAL(18,2)   NULL,
        PostCAR              DECIMAL(8,4)    NULL,
        PostNPL              DECIMAL(8,4)    NULL,
        PostLCR              DECIMAL(8,4)    NULL,
        PostNSFR             DECIMAL(8,4)    NULL,
        PostROA              DECIMAL(8,4)    NULL,
        PostCapitalShortfall DECIMAL(18,2)   NULL,
        DeltaCAR             DECIMAL(8,4)    NULL,
        DeltaNPL             DECIMAL(8,4)    NULL,
        DeltaLCR             DECIMAL(8,4)    NULL,
        AdditionalProvisions DECIMAL(18,2)   NULL,
        BreachesCAR          BIT             NOT NULL DEFAULT 0,
        BreachesLCR          BIT             NOT NULL DEFAULT 0,
        BreachesNSFR         BIT             NOT NULL DEFAULT 0,
        IsInsolvent          BIT             NOT NULL DEFAULT 0,
        IsContagionVictim    BIT             NOT NULL DEFAULT 0,
        ContagionRound       INT             NULL,
        FailureCause         VARCHAR(30)     NULL,
        InsurableDeposits    DECIMAL(18,2)   NULL,
        UninsurableDeposits  DECIMAL(18,2)   NULL,
        FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='StressTestContagionEvents')
BEGIN
    CREATE TABLE StressTestContagionEvents (
        Id                    BIGINT IDENTITY(1,1) PRIMARY KEY,
        RunId                 BIGINT          NOT NULL,
        ContagionRound        INT             NOT NULL,
        FailingInstitutionId  INT             NOT NULL,
        AffectedInstitutionId INT             NOT NULL,
        ExposureAmount        DECIMAL(18,2)   NOT NULL,
        ExposureType          VARCHAR(20)     NOT NULL,
        TransmissionType      VARCHAR(20)     NOT NULL,
        CreatedAt             DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id)
    );
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name='StressTestSectorAggregates')
BEGIN
    CREATE TABLE StressTestSectorAggregates (
        Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,
        RunId                           BIGINT          NOT NULL,
        InstitutionType                 VARCHAR(20)     NOT NULL,
        EntityCount                     INT             NOT NULL,
        PreAvgCAR                       DECIMAL(8,4)    NULL,
        PreAvgNPL                       DECIMAL(8,4)    NULL,
        PreAvgLCR                       DECIMAL(8,4)    NULL,
        PostAvgCAR                      DECIMAL(8,4)    NULL,
        PostAvgNPL                      DECIMAL(8,4)    NULL,
        PostAvgLCR                      DECIMAL(8,4)    NULL,
        EntitiesBreachingCAR            INT             NOT NULL DEFAULT 0,
        EntitiesBreachingLCR            INT             NOT NULL DEFAULT 0,
        EntitiesInsolvent               INT             NOT NULL DEFAULT 0,
        EntitiesContagionVictims        INT             NOT NULL DEFAULT 0,
        TotalCapitalShortfall           DECIMAL(18,2)   NOT NULL DEFAULT 0,
        TotalAdditionalProvisions       DECIMAL(18,2)   NOT NULL DEFAULT 0,
        TotalInsurableDepositsAtRisk    DECIMAL(18,2)   NULL,
        TotalUninsurableDepositsAtRisk  DECIMAL(18,2)   NULL,
        FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id),
        UNIQUE (RunId, InstitutionType)
    );
END
""");

        // Seed scenarios + parameters
        await conn.ExecuteAsync("""
IF NOT EXISTS (SELECT 1 FROM StressScenarios WHERE ScenarioCode='OIL_PRICE_COLLAPSE')
BEGIN
    SET IDENTITY_INSERT StressScenarios ON;
    INSERT INTO StressScenarios (Id, ScenarioCode, ScenarioName, Category, Severity, TimeHorizon, NarrativeSummary)
    VALUES (4, 'OIL_PRICE_COLLAPSE', 'Oil Price Collapse (-50% from Baseline)',
            'MACRO', 'SEVERE', '1Y',
            'Global oil demand shock drives Brent crude from USD 80 to USD 40/barrel. Nigeria fiscal revenue falls sharply; naira depreciates 30%.');
    SET IDENTITY_INSERT StressScenarios OFF;
END

IF NOT EXISTS (SELECT 1 FROM StressScenarioParameters WHERE ScenarioId=4 AND InstitutionType='DMB')
    INSERT INTO StressScenarioParameters
        (ScenarioId, InstitutionType, OilPriceShockPct, GDPGrowthShock, FXDepreciationPct,
         CARDeltaPerGDPPp, NPLDeltaPerGDPPp, LCRDeltaPerRateHike100,
         CARDeltaPerFXPct, NPLDeltaPerFXPct, CARDeltaPerOilPct, NPLDeltaPerOilPct)
    VALUES (4,'DMB',-50.0,-3.0,30.0,-0.32,0.45,-0.08,-0.12,0.18,-0.10,0.25);

IF NOT EXISTS (SELECT 1 FROM StressScenarioParameters WHERE ScenarioId=4 AND InstitutionType='MFB')
    INSERT INTO StressScenarioParameters
        (ScenarioId, InstitutionType, OilPriceShockPct, GDPGrowthShock, FXDepreciationPct,
         CARDeltaPerGDPPp, NPLDeltaPerGDPPp, LCRDeltaPerRateHike100,
         CARDeltaPerFXPct, NPLDeltaPerFXPct, CARDeltaPerOilPct, NPLDeltaPerOilPct)
    VALUES (4,'MFB',-50.0,-3.0,30.0,-0.20,0.55,-0.05,-0.08,0.22,-0.05,0.30);

IF NOT EXISTS (SELECT 1 FROM SystemConfiguration WHERE ConfigKey='NDIC_FUND_CAPACITY_NGN_MILLIONS')
    INSERT INTO SystemConfiguration (ConfigKey, ConfigValue) VALUES ('NDIC_FUND_CAPACITY_NGN_MILLIONS', 1500000);
""");
    }

    private async Task SeedBaseDataAsync()
    {
        using var conn = await Db.OpenAsync();
        await conn.ExecuteAsync("""
IF NOT EXISTS (SELECT 1 FROM Regulators WHERE Code='CBN')
    INSERT INTO Regulators (Code, Name) VALUES ('CBN', 'Central Bank of Nigeria');

MERGE Institutions AS t
USING (VALUES
    (1,  'CBN','DMB','DMB-001','First Bank'),
    (2,  'CBN','MFB','MFB-001','LAPO MFB'),
    (3,  'CBN','DMB','DMB-003','Zenith Bank'),
    (4,  'CBN','DMB','DMB-004','GTBank'),
    (10, 'CBN','DMB','DMB-010','Test Bank Alpha'),
    (11, 'CBN','DMB','DMB-011','Test Bank Beta'),
    (12, 'CBN','MFB','MFB-012','Test MFB Gamma'),
    (15, 'CBN','DMB','DMB-015','Test Bank Delta'),
    (16, 'CBN','DMB','DMB-016','Test Bank Epsilon'),
    (99, 'CBN','DMB','DMB-099','Test Bank Healthy')
) AS s(Id, RegulatorCode, InstitutionType, LicenseNumber, ShortName)
ON t.Id = s.Id
WHEN NOT MATCHED THEN
    INSERT (Id, RegulatorCode, InstitutionType, LicenseNumber, InstitutionName, ShortName, IsActive)
    VALUES (s.Id, s.RegulatorCode, s.InstitutionType, s.LicenseNumber, s.ShortName, s.ShortName, 1);
""");
    }

    public async Task SeedSectorAsync(
        string periodCode,
        (int Id, string Type, decimal CAR, decimal NPL, decimal LCR, decimal OilPct)[] entities)
    {
        using var conn = await Db.OpenAsync();
        foreach (var e in entities)
        {
            await conn.ExecuteAsync("""
 MERGE meta.prudential_metrics AS t
USING (VALUES (@Id, @Period)) AS s(InstitutionId, PeriodCode)
ON t.InstitutionId = s.InstitutionId AND t.PeriodCode = s.PeriodCode
WHEN MATCHED THEN
    UPDATE SET CAR=@CAR, NPLRatio=@NPL, LCR=@LCR,
               OilSectorExposurePct=@Oil, NSFR=110, ROA=1.5,
               TotalAssets=200000, TotalDeposits=140000,
               InstitutionType=@Type, RegulatorCode='CBN',
               AsOfDate=CAST(SYSUTCDATETIME() AS DATE)
WHEN NOT MATCHED THEN
    INSERT (InstitutionId, RegulatorCode, InstitutionType, AsOfDate, PeriodCode,
            CAR, NPLRatio, LCR, NSFR, ROA, TotalAssets, TotalDeposits,
            OilSectorExposurePct)
    VALUES (@Id,'CBN',@Type, CAST(SYSUTCDATETIME() AS DATE), @Period,
            @CAR, @NPL, @LCR, 110, 1.5, 200000, 140000, @Oil);
""",
                new { Id = e.Id, Type = e.Type, Period = periodCode,
                      CAR = e.CAR, NPL = e.NPL, LCR = e.LCR, Oil = e.OilPct });
        }
    }

    public async Task SeedInterbankAsync(
        string periodCode, (int Lender, int Borrower, decimal Amount) edge)
    {
        using var conn = await Db.OpenAsync();
        await conn.ExecuteAsync("""
IF NOT EXISTS (
    SELECT 1 FROM meta.interbank_exposures
    WHERE LendingInstitutionId=@L AND BorrowingInstitutionId=@B
      AND ExposureType='PLACEMENT' AND PeriodCode=@P)
INSERT INTO meta.interbank_exposures
    (LendingInstitutionId, BorrowingInstitutionId, RegulatorCode,
     PeriodCode, ExposureAmount, ExposureType, AsOfDate)
VALUES (@L, @B, 'CBN', @P, @Amount, 'PLACEMENT',
        CAST(SYSUTCDATETIME() AS DATE))
""",
            new { L = edge.Lender, B = edge.Borrower, P = periodCode, Amount = edge.Amount });
    }
}

// ── Test DB connection factory ────────────────────────────────────────────────
internal sealed class TestDbConnectionFactory : IDbConnectionFactory
{
    private readonly string _cs;
    public TestDbConnectionFactory(string cs) => _cs = cs;

    public async Task<System.Data.IDbConnection> CreateConnectionAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        var conn = new SqlConnection(_cs);
        await conn.OpenAsync(ct);
        return conn;
    }
}
