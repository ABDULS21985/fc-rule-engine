using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations;

/// <summary>
/// RG-37: Sector-Wide Stress Testing Framework — idempotent schema migration.
/// Creates: StressScenarios, StressScenarioParameters, StressTestRuns,
///          StressTestEntityResults, StressTestContagionEvents, StressTestSectorAggregates.
/// Seeds: 8 calibrated scenarios (NGFS + Macro) with CBN/IMF transmission coefficients.
/// </summary>
public partial class AddStressTestingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StressScenarios')
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
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StressScenarioParameters')
BEGIN
    CREATE TABLE StressScenarioParameters (
        Id                      INT IDENTITY(1,1) PRIMARY KEY,
        ScenarioId              INT             NOT NULL,
        InstitutionType         VARCHAR(20)     NOT NULL,

        -- Macro shocks
        GDPGrowthShock          DECIMAL(8,4)    NULL,
        OilPriceShockPct        DECIMAL(8,4)    NULL,
        FXDepreciationPct       DECIMAL(8,4)    NULL,
        InflationShockPp        DECIMAL(8,4)    NULL,
        InterestRateShockBps    INT             NULL,
        TradeVolumeShockPct     DECIMAL(8,4)    NULL,
        RemittanceShockPct      DECIMAL(8,4)    NULL,
        FDIShockPct             DECIMAL(8,4)    NULL,

        -- NGFS climate parameters
        CarbonTaxUSDPerTon      DECIMAL(10,2)   NULL,
        PhysicalRiskHazardCode  VARCHAR(20)     NULL,
        StrandedAssetsPct       DECIMAL(8,4)    NULL,

        -- Transmission coefficients sourced from CBN/IMF literature (R-07)
        CARDeltaPerGDPPp        DECIMAL(8,6)    NULL,
        NPLDeltaPerGDPPp        DECIMAL(8,6)    NULL,
        LCRDeltaPerRateHike100  DECIMAL(8,6)    NULL,
        CARDeltaPerFXPct        DECIMAL(8,6)    NULL,
        NPLDeltaPerFXPct        DECIMAL(8,6)    NULL,
        CARDeltaPerOilPct       DECIMAL(8,6)    NULL,
        NPLDeltaPerOilPct       DECIMAL(8,6)    NULL,
        LCRDeltaPerCyber        DECIMAL(8,4)    NULL,
        DepositOutflowPctCyber  DECIMAL(8,4)    NULL,

        CONSTRAINT FK_StressScenarioParameters_Scenario
            FOREIGN KEY (ScenarioId) REFERENCES StressScenarios(Id)
    );
END
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StressTestRuns')
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

        CONSTRAINT UQ_StressTestRuns_Guid UNIQUE (RunGuid),
        CONSTRAINT FK_StressTestRuns_Scenario
            FOREIGN KEY (ScenarioId) REFERENCES StressScenarios(Id)
    );

    CREATE INDEX IX_StressTestRuns_Regulator
        ON StressTestRuns (RegulatorCode, StartedAt DESC);
END
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StressTestEntityResults')
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

        CONSTRAINT FK_StressTestEntityResults_Run
            FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id)
    );

    CREATE INDEX IX_StressTestEntityResults_Run
        ON StressTestEntityResults (RunId, InstitutionId);
    CREATE INDEX IX_StressTestEntityResults_Breaches
        ON StressTestEntityResults (RunId, BreachesCAR, IsInsolvent);
END
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StressTestContagionEvents')
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

        CONSTRAINT FK_StressTestContagionEvents_Run
            FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id)
    );

    CREATE INDEX IX_StressTestContagionEvents_Run
        ON StressTestContagionEvents (RunId, ContagionRound);
END
""");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StressTestSectorAggregates')
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

        CONSTRAINT FK_StressTestSectorAggregates_Run
            FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id),
        CONSTRAINT UQ_StressTestSectorAggregates_RunType
            UNIQUE (RunId, InstitutionType)
    );
END
""");

        // ── Seed: 8 calibrated scenarios ─────────────────────────────────────
        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM StressScenarios WHERE ScenarioCode = 'NGFS_ORDERLY')
BEGIN
    SET IDENTITY_INSERT StressScenarios ON;
    INSERT INTO StressScenarios (Id, ScenarioCode, ScenarioName, Category, Severity, TimeHorizon, NarrativeSummary) VALUES
    (1, 'NGFS_ORDERLY',
     'NGFS Orderly Transition — Net Zero 2050',
     'NGFS_CLIMATE', 'MODERATE', '5Y',
     'Gradual, well-managed transition to net-zero. Carbon price rises steadily from USD 30/ton in 2026 to USD 147/ton by 2035. Physical risks remain low. Stranded assets are limited to high-carbon sectors (upstream oil & gas, coal power). GDP impact is modest at −0.5pp over 5 years. Nigerian banks with significant oil-sector exposure face moderate impairment on transition risk assets.'),
    (2, 'NGFS_DISORDERLY',
     'NGFS Disorderly Transition — Delayed Policy Shift',
     'NGFS_CLIMATE', 'SEVERE', '3Y',
     'Policy inaction until 2030 followed by sudden, disorderly tightening. Carbon price spikes abruptly to USD 200/ton. High-carbon asset values collapse rapidly. Nigerian oil-dependent portfolios face significant stranded asset write-downs. CAR impact estimated at −3 to −5pp for heavily exposed DMBs. FX depreciation accelerates as oil revenues fall sharply.'),
    (3, 'NGFS_HOT_HOUSE',
     'NGFS Hot House World — Unmitigated Physical Risk',
     'NGFS_CLIMATE', 'EXTREME', '5Y',
     'No climate transition policy. Severe physical risks materialise: chronic flooding in Lagos, Niger Delta, and South-South; prolonged drought in North-West and North-East. Agricultural NPLs surge (MFBs and DFIs exposed). Infrastructure damage impairs collateral values. GDP growth depressed by −2pp per year by 2040. NDIC exposure elevated due to MFB stress.'),
    (4, 'OIL_PRICE_COLLAPSE',
     'Oil Price Collapse (−50% from Baseline)',
     'MACRO', 'SEVERE', '1Y',
     'Global oil demand shock drives Brent crude from USD 80 to USD 40/barrel. Nigeria fiscal revenue falls sharply; naira depreciates 30%; inflation rises 5pp; GDP growth contracts 3pp. FGN bond yields rise as fiscal pressures mount. DMBs with oil-sector loan concentrations face NPL surge; FX-open-position holders incur mark-to-market losses.'),
    (5, 'GLOBAL_RECESSION',
     'Global Recession — Trade & Capital Flow Reversal',
     'MACRO', 'SEVERE', '2Y',
     'Global GDP contracts 2.5%. Nigerian trade volumes fall 20%; remittances decline 30% as diaspora incomes fall; FDI dries up by 40%. Eurobond access closes. Pressure on current account and external reserves. IMTO operators face volume collapse. DFIs supporting export-oriented sectors see NPL spike. Tier 1 DMBs with offshore placements face counterparty risk.'),
    (6, 'INTEREST_RATE_SPIKE',
     'Interest Rate Spike (+500bps)',
     'MACRO', 'SEVERE', '1Y',
     'Inflationary pressures force CBN to raise MPR by 500bps (from current ~26.25% to ~31.25%). Bond portfolios mark-to-market losses reduce available-for-sale securities value. NIM initially widens but borrower stress rises with variable-rate loan repricing. Corporate NPLs surge 6–8pp. LCR deteriorates as HQLA values fall and funding costs rise. MFBs with fixed-rate loan books face margin squeeze.'),
    (7, 'PANDEMIC',
     'Pandemic Shock — COVID-19 Style',
     'MACRO', 'EXTREME', '2Y',
     'Novel pathogen triggers 6-month economic lockdown. CBN activates loan restructuring moratorium for 12 months. GDP contracts 5% year-on-year. Provisioning surges as stage-2 and stage-3 migrations rise sharply. Cash demand spikes; digital payment volumes surge but settlement risk rises for PSPs. NDIC payouts on MFB failures increase. Recovery in year 2 as vaccine rollout occurs.'),
    (8, 'CYBER_SYSTEMIC',
     'Systemic Cyber Incident — Payments Infrastructure Attack',
     'MACRO', 'EXTREME', '1Y',
     'Coordinated cyber attack on NIBSS and major DMB core banking systems. Payments infrastructure disrupted for 72 hours. Public confidence crisis triggers 15% deposit outflow from affected banks within 2 weeks. PSPs face settlement exposure. CBN activates Emergency Liquidity Assistance. Second-round contagion via interbank markets as liquidity hoarding begins. Recovery assumes CBN intervention after 4 weeks.');
    SET IDENTITY_INSERT StressScenarios OFF;
END
""");

        // ── Seed: transmission coefficient parameters ─────────────────────────
        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM StressScenarioParameters WHERE ScenarioId = 4 AND InstitutionType = 'DMB')
BEGIN
    -- Scenario 4: Oil Price Collapse
    INSERT INTO StressScenarioParameters
        (ScenarioId, InstitutionType,
         OilPriceShockPct, GDPGrowthShock, FXDepreciationPct, InflationShockPp,
         CARDeltaPerGDPPp, NPLDeltaPerGDPPp, LCRDeltaPerRateHike100,
         CARDeltaPerFXPct, NPLDeltaPerFXPct, CARDeltaPerOilPct, NPLDeltaPerOilPct)
    VALUES
    (4, 'DMB',    -50.0, -3.0, 30.0, 5.0,  -0.32, 0.45, -0.08, -0.12, 0.18, -0.10, 0.25),
    (4, 'MFB',    -50.0, -3.0, 30.0, 5.0,  -0.20, 0.55, -0.05, -0.08, 0.22, -0.05, 0.30),
    (4, 'BDC',    -50.0, -3.0, 30.0, 5.0,  -0.15, 0.20, -0.03, -0.35, 0.10, -0.08, 0.15),

    -- Scenario 5: Global Recession
    (5, 'DMB',     NULL, -4.0, 15.0, 3.0,  -0.40, 0.50,  0.00, -0.08, 0.15,  NULL,  NULL),

    -- Scenario 6: Interest Rate Spike
    (6, 'DMB',     NULL,  0.0,  0.0, 0.0,   0.00, 0.60, -0.15,  0.00, 0.00,  NULL,  NULL),
    (6, 'MFB',     NULL,  0.0,  0.0, 0.0,  -0.10, 0.70, -0.10,  0.00, 0.00,  NULL,  NULL),

    -- Scenario 7: Pandemic
    (7, 'DMB',     NULL, -5.0,  8.0, 4.0,  -0.50, 0.80, -0.12, -0.05, 0.12,  NULL,  NULL),
    (7, 'ALL',     NULL, -5.0,  8.0, 4.0,  -0.30, 0.90, -0.08,  NULL, NULL,  NULL,  NULL),

    -- Scenario 8: Cyber Systemic (cyber params set separately below)
    (8, 'PSP',     NULL,  0.0,  0.0, 0.0,   0.00, 0.00,  0.00,  NULL, NULL,  NULL,  NULL),
    (8, 'DMB',     NULL,  0.0,  0.0, 0.0,   0.00, 0.00,  0.00,  NULL, NULL,  NULL,  NULL);

    -- Cyber-specific shock parameters
    UPDATE StressScenarioParameters
    SET    LCRDeltaPerCyber = -25.0, DepositOutflowPctCyber = 15.0
    WHERE  ScenarioId = 8 AND InstitutionType IN ('DMB','PSP');

    -- NGFS Climate scenarios
    INSERT INTO StressScenarioParameters
        (ScenarioId, InstitutionType,
         CarbonTaxUSDPerTon, StrandedAssetsPct, PhysicalRiskHazardCode,
         GDPGrowthShock, CARDeltaPerGDPPp, NPLDeltaPerGDPPp)
    VALUES
    (1, 'DMB',  147.0,  8.0, 'NONE',          -0.5, -0.10, 0.15),
    (2, 'DMB',  200.0, 25.0, 'NONE',          -2.5, -0.55, 0.70),
    (3, 'DMB',    0.0,  5.0, 'FLOOD',         -2.0, -0.45, 0.60),
    (3, 'MFB',    0.0,  0.0, 'FLOOD_DROUGHT', -2.0, -0.30, 0.90);
END
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS StressTestSectorAggregates;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS StressTestContagionEvents;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS StressTestEntityResults;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS StressTestRuns;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS StressScenarioParameters;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS StressScenarios;");
    }
}
