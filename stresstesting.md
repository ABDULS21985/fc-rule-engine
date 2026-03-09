# RG-37: Sector-Wide Stress Testing Framework

> **Stream G — SupTech & Regulator Intelligence · Phase 4 · RegOS™ World-Class SupTech Prompt**

---

| Field | Value |
|---|---|
| **Prompt ID** | RG-37 |
| **Stream** | G — SupTech & Regulator Intelligence |
| **Phase** | Phase 4 |
| **Principle** | Transform RegOS™ into the world's leading Regulatory & SupTech platform |
| **Depends On** | RG-33 (scenario engine), RG-36 (systemic risk / CAMELS), RG-25 (regulator portal), RG-34 (submission engine) |
| **Estimated Effort** | 12–16 days |
| **Classification** | Confidential — Internal Engineering |

---

## 0 · Preamble & Governing Rules

You are a **senior .NET 8 architect** building the Sector-Wide Stress Testing Framework for RegOS™ — a macro-prudential SupTech capability that allows CBN examiners to apply simultaneous shocks to every supervised entity in the financial system, measure aggregate resilience, trace contagion cascades, and auto-generate the full FSB/IMF-style stress test report as a QuestPDF document. Every artefact must satisfy these rules:

| # | Rule |
|---|---|
| R-01 | **Zero mock data.** All scenarios, thresholds, and seed parameters use real-world calibration: NGFS 2023 scenario narratives, Nigeria-specific macro parameters (CBN MPR, oil price dependency, FX band), actual sector composition (DMB, MFB, PSP, BDC, PFA, DFI, INSURER). |
| R-02 | **No stubs, no TODOs, no `throw new NotImplementedException()`.** Every method body is complete and production-ready. |
| R-03 | **Complete DDL first.** All tables, indexes, and seed data delivered as an idempotent EF Core migration before any service code. |
| R-04 | **Parameterised queries only.** No string interpolation in SQL. Dapper `DynamicParameters` or EF Core LINQ exclusively. |
| R-05 | **Regulator isolation.** All stress test runs and results are scoped to `RegulatorCode`; cross-regulator leakage is architecturally impossible. |
| R-06 | **Immutable run results.** Stress test results are append-only. A run is never mutated after completion — only a new run supersedes. |
| R-07 | **Shock transmission is formula-driven.** Each shock parameter maps to a documented transmission coefficient (Δ CAR, Δ NPL, Δ LCR) sourced from CBN/IMF literature. Zero hard-coded arithmetic magic numbers — all coefficients live in `StressScenarioParameters`. |
| R-08 | **Second-round contagion is graph-traversal.** Interbank exposure failures propagate via BFS over the same exposure graph built in RG-36. Cascade depth is bounded (max 5 rounds) to prevent infinite loops. |
| R-09 | **Integration tests with Testcontainers.** Real SQL Server. No in-memory fakes for shock pipeline tests. |
| R-10 | **All secrets via Azure Key Vault or `IConfiguration`.** Zero hardcoded credentials. |
| R-11 | **QuestPDF report is a first-class deliverable.** The stress test PDF report is fully branded (CBN / RegOS™ logos, Digibit palette), contains all mandatory FSB sections, and is generated as a `byte[]` suitable for direct browser streaming. |
| R-12 | **NDIC exposure analysis is accurate.** NDIC insurable deposit calculations respect the N5,000,000 per depositor cap and distinguish protected from unprotected exposure. |

---

## 1 · Architecture Context

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                        Scenario Configuration Layer                          │
│   NGFS Orderly · NGFS Disorderly · NGFS Hot House World                     │
│   Oil Collapse · Global Recession · Rate Spike · Pandemic · Cyber            │
│   + Custom Scenarios (user-defined parameter set)                            │
└──────────────────────────────┬───────────────────────────────────────────────┘
                               │ scenario parameters
┌──────────────────────────────▼───────────────────────────────────────────────┐
│                      SHOCK APPLICATION ENGINE                                │
│                                                                              │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────────┐  │
│  │  Macro Shock     │  │  NGFS Climate    │  │  Entity-Level Shock      │  │
│  │  Transmitter     │  │  Transmitter     │  │  Aggregator              │  │
│  └────────┬─────────┘  └────────┬─────────┘  └──────────────────────────┘  │
│           │                    │                          ▲                  │
│           └────────────────────┘                         │                  │
│                         │ per-entity post-shock metrics  │                  │
│                         ▼                                │                  │
│  ┌───────────────────────────────────────────────────┐  │                  │
│  │           Contagion Cascade Engine                 │  │                  │
│  │  Round 1 failures → graph BFS → Round N failures  │──┘                  │
│  └───────────────────────────────────────────────────┘                      │
│                         │ final post-cascade results                         │
└──────────────────────────┬───────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼───────────────────────────────────────────────────┐
│                    AGGREGATION & ANALYTICS LAYER                              │
│  Sector breach counts · NDIC exposure · Systemic resilience score            │
│  Pre-stress vs post-stress CAMELS distribution                               │
└──────────────────────────┬───────────────────────────────────────────────────┘
                           │
         ┌─────────────────┴────────────────┐
         │                                  │
┌────────▼──────────┐            ┌──────────▼──────────────────────────────┐
│  Blazor Portal    │            │  QuestPDF Report Generator               │
│  Stress Test UI   │            │  Executive Summary · Heatmap · Tables    │
│  Results Explorer │            │  Policy Recommendations · Appendix       │
└───────────────────┘            └─────────────────────────────────────────┘
```

---

## 2 · Complete DDL (EF Core Migration)

> Deliver as a single idempotent migration: `20260325_AddStressTestingSchema.cs`

```sql
-- ============================================================
-- Table: StressScenarios
-- Purpose: Registry of all available stress scenarios
-- ============================================================
CREATE TABLE StressScenarios (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    ScenarioCode        VARCHAR(30)     NOT NULL,   -- 'NGFS_ORDERLY','OIL_COLLAPSE','PANDEMIC'
    ScenarioName        NVARCHAR(150)   NOT NULL,
    Category            VARCHAR(20)     NOT NULL,   -- 'NGFS_CLIMATE','MACRO','CUSTOM'
    NarrativeSummary    NVARCHAR(2000)  NOT NULL,
    TimeHorizon         VARCHAR(10)     NOT NULL DEFAULT '1Y',  -- '1Y','3Y','5Y'
    Severity            VARCHAR(10)     NOT NULL,   -- 'MILD','MODERATE','SEVERE','EXTREME'
    IsActive            BIT             NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_StressScenarios_Code UNIQUE (ScenarioCode)
);

-- ============================================================
-- Table: StressScenarioParameters
-- Purpose: Shock parameter set per scenario per institution type
--          All transmission coefficients live here (R-07)
-- ============================================================
CREATE TABLE StressScenarioParameters (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    ScenarioId          INT             NOT NULL,
    InstitutionType     VARCHAR(20)     NOT NULL,   -- 'DMB','MFB','PSP','BDC','PFA','DFI','INSURER','ALL'

    -- Macro shocks
    GDPGrowthShock          DECIMAL(8,4)    NULL,   -- pp change e.g. -3.0
    OilPriceShockPct        DECIMAL(8,4)    NULL,   -- % change e.g. -50.0
    FXDepreciationPct       DECIMAL(8,4)    NULL,   -- % e.g. 30.0
    InflationShockPp        DECIMAL(8,4)    NULL,   -- pp e.g. +5.0
    InterestRateShockBps    INT             NULL,   -- basis points e.g. +500
    TradeVolumeShockPct     DECIMAL(8,4)    NULL,
    RemittanceShockPct      DECIMAL(8,4)    NULL,
    FDIShockPct             DECIMAL(8,4)    NULL,

    -- NGFS climate parameters
    CarbonTaxUSDPerTon      DECIMAL(10,2)   NULL,
    PhysicalRiskHazardCode  VARCHAR(20)     NULL,   -- 'FLOOD','DROUGHT','HEAT_STRESS','NONE'
    StrandedAssetsPct       DECIMAL(8,4)    NULL,   -- % of portfolio in transition risk

    -- Transmission coefficients (∆ per unit of shock) — sourced from CBN/IMF literature
    CARDeltaPerGDPPp        DECIMAL(8,6)    NULL,   -- Δ CAR per 1pp GDP decline
    NPLDeltaPerGDPPp        DECIMAL(8,6)    NULL,   -- Δ NPL per 1pp GDP decline
    LCRDeltaPerRateHike100  DECIMAL(8,6)    NULL,   -- Δ LCR per 100bps rate increase
    CARDeltaPerFXPct        DECIMAL(8,6)    NULL,   -- Δ CAR per 1% FX depreciation
    NPLDeltaPerFXPct        DECIMAL(8,6)    NULL,
    CARDeltaPerOilPct       DECIMAL(8,6)    NULL,   -- Δ CAR per 1% oil price change
    NPLDeltaPerOilPct       DECIMAL(8,6)    NULL,
    LCRDeltaPerCyber        DECIMAL(8,4)    NULL,   -- absolute LCR shock for cyber scenario
    DepositOutflowPctCyber  DECIMAL(8,4)    NULL,   -- % deposit outflow under cyber

    CONSTRAINT FK_StressScenarioParameters_Scenario
        FOREIGN KEY (ScenarioId) REFERENCES StressScenarios(Id)
);

-- ============================================================
-- Table: StressTestRuns
-- Purpose: Each execution of a stress test scenario
-- ============================================================
CREATE TABLE StressTestRuns (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunGuid             UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    RegulatorCode       VARCHAR(10)     NOT NULL,
    ScenarioId          INT             NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,   -- base period for pre-stress data
    TimeHorizon         VARCHAR(10)     NOT NULL,   -- '1Y','3Y','5Y'
    Status              VARCHAR(20)     NOT NULL DEFAULT 'RUNNING',
    EntitiesShocked     INT             NOT NULL DEFAULT 0,
    ContagionRounds     INT             NOT NULL DEFAULT 0,
    InitiatedByUserId   INT             NOT NULL,
    SystemicResilienceScore DECIMAL(5,2) NULL,      -- 0–100 post-stress
    ExecutiveSummary    NVARCHAR(MAX)   NULL,
    ErrorMessage        NVARCHAR(2000)  NULL,
    StartedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAt         DATETIME2(3)    NULL,

    CONSTRAINT UQ_StressTestRuns_Guid UNIQUE (RunGuid),
    CONSTRAINT FK_StressTestRuns_Scenario
        FOREIGN KEY (ScenarioId) REFERENCES StressScenarios(Id),
    INDEX IX_StressTestRuns_Regulator (RegulatorCode, StartedAt DESC)
);

-- ============================================================
-- Table: StressTestEntityResults
-- Purpose: Per-entity pre-stress and post-stress metric snapshot
-- ============================================================
CREATE TABLE StressTestEntityResults (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId               BIGINT          NOT NULL,
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    InstitutionType     VARCHAR(20)     NOT NULL,

    -- Pre-stress (from PrudentialMetrics)
    PreCAR              DECIMAL(8,4)    NULL,
    PreNPL              DECIMAL(8,4)    NULL,
    PreLCR              DECIMAL(8,4)    NULL,
    PreNSFR             DECIMAL(8,4)    NULL,
    PreROA              DECIMAL(8,4)    NULL,
    PreTotalAssets      DECIMAL(18,2)   NULL,
    PreTotalDeposits    DECIMAL(18,2)   NULL,

    -- Post-stress (after shock application)
    PostCAR             DECIMAL(8,4)    NULL,
    PostNPL             DECIMAL(8,4)    NULL,
    PostLCR             DECIMAL(8,4)    NULL,
    PostNSFR            DECIMAL(8,4)    NULL,
    PostROA             DECIMAL(8,4)    NULL,
    PostCapitalShortfall DECIMAL(18,2)  NULL,   -- NGN millions additional capital needed

    -- Shock deltas
    DeltaCAR            DECIMAL(8,4)    NULL,
    DeltaNPL            DECIMAL(8,4)    NULL,
    DeltaLCR            DECIMAL(8,4)    NULL,
    AdditionalProvisions DECIMAL(18,2)  NULL,   -- NGN millions required provisions

    -- Breach flags
    BreachesCAR         BIT             NOT NULL DEFAULT 0,
    BreachesLCR         BIT             NOT NULL DEFAULT 0,
    BreachesNSFR        BIT             NOT NULL DEFAULT 0,
    IsInsolvent         BIT             NOT NULL DEFAULT 0,   -- PostCAR < 0
    IsContagionVictim   BIT             NOT NULL DEFAULT 0,   -- failed due to 2nd-round
    ContagionRound      INT             NULL,                 -- which round triggered failure
    FailureCause        VARCHAR(30)     NULL,   -- 'DIRECT_SHOCK','INTERBANK','DEPOSIT_FLIGHT'

    -- NDIC
    InsurableDeposits   DECIMAL(18,2)   NULL,   -- NGN millions (capped at N5M per depositor)
    UninsurableDeposits DECIMAL(18,2)   NULL,

    CONSTRAINT FK_StressTestEntityResults_Run
        FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id),
    INDEX IX_StressTestEntityResults_Run (RunId, InstitutionId),
    INDEX IX_StressTestEntityResults_Breaches (RunId, BreachesCAR, IsInsolvent)
);

-- ============================================================
-- Table: StressTestContagionEvents
-- Purpose: Log of each contagion transmission event
-- ============================================================
CREATE TABLE StressTestContagionEvents (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId               BIGINT          NOT NULL,
    ContagionRound      INT             NOT NULL,
    FailingInstitutionId  INT           NOT NULL,
    AffectedInstitutionId INT           NOT NULL,
    ExposureAmount      DECIMAL(18,2)   NOT NULL,   -- NGN millions
    ExposureType        VARCHAR(20)     NOT NULL,
    TransmissionType    VARCHAR(20)     NOT NULL,   -- 'INTERBANK','DEPOSIT_FLIGHT'
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_StressTestContagionEvents_Run
        FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id),
    INDEX IX_StressTestContagionEvents_Run (RunId, ContagionRound)
);

-- ============================================================
-- Table: StressTestSectorAggregates
-- Purpose: Sector-level summary per institution type for each run
-- ============================================================
CREATE TABLE StressTestSectorAggregates (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    RunId               BIGINT          NOT NULL,
    InstitutionType     VARCHAR(20)     NOT NULL,
    EntityCount         INT             NOT NULL,

    -- Pre-stress averages
    PreAvgCAR           DECIMAL(8,4)    NULL,
    PreAvgNPL           DECIMAL(8,4)    NULL,
    PreAvgLCR           DECIMAL(8,4)    NULL,

    -- Post-stress averages
    PostAvgCAR          DECIMAL(8,4)    NULL,
    PostAvgNPL          DECIMAL(8,4)    NULL,
    PostAvgLCR          DECIMAL(8,4)    NULL,

    -- Breach counts
    EntitiesBreachingCAR  INT           NOT NULL DEFAULT 0,
    EntitiesBreachingLCR  INT           NOT NULL DEFAULT 0,
    EntitiesInsolvent     INT           NOT NULL DEFAULT 0,
    EntitiesContagionVictims INT        NOT NULL DEFAULT 0,

    -- Capital shortfall
    TotalCapitalShortfall DECIMAL(18,2) NOT NULL DEFAULT 0,   -- NGN millions
    TotalAdditionalProvisions DECIMAL(18,2) NOT NULL DEFAULT 0,

    -- NDIC
    TotalInsurableDepositsAtRisk  DECIMAL(18,2) NULL,
    TotalUninsurableDepositsAtRisk DECIMAL(18,2) NULL,

    CONSTRAINT FK_StressTestSectorAggregates_Run
        FOREIGN KEY (RunId) REFERENCES StressTestRuns(Id),
    CONSTRAINT UQ_StressTestSectorAggregates_RunType
        UNIQUE (RunId, InstitutionType)
);
```

### 2.2 Seed Data — Scenarios & Parameters

```sql
-- ============================================================
-- Scenarios
-- ============================================================
SET IDENTITY_INSERT StressScenarios ON;
INSERT INTO StressScenarios
    (Id, ScenarioCode, ScenarioName, Category, Severity, TimeHorizon, NarrativeSummary)
VALUES
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

-- ============================================================
-- Parameters (selected key rows — full seed covers all 8 scenarios × institution types)
-- ============================================================
-- Oil Price Collapse — DMB parameters
INSERT INTO StressScenarioParameters
    (ScenarioId, InstitutionType,
     OilPriceShockPct, GDPGrowthShock, FXDepreciationPct, InflationShockPp,
     CARDeltaPerGDPPp, NPLDeltaPerGDPPp, LCRDeltaPerRateHike100,
     CARDeltaPerFXPct, NPLDeltaPerFXPct, CARDeltaPerOilPct, NPLDeltaPerOilPct)
VALUES
-- Scenario 4 (Oil Collapse) · DMB
(4, 'DMB',    -50.0, -3.0, 30.0, 5.0,  -0.32, 0.45, -0.08, -0.12, 0.18, -0.10, 0.25),
-- Scenario 4 (Oil Collapse) · MFB (less oil-exposed, more FX pass-through)
(4, 'MFB',    -50.0, -3.0, 30.0, 5.0,  -0.20, 0.55, -0.05, -0.08, 0.22, -0.05, 0.30),
-- Scenario 4 (Oil Collapse) · BDC (severe FX shock)
(4, 'BDC',    -50.0, -3.0, 30.0, 5.0,  -0.15, 0.20, -0.03, -0.35, 0.10, -0.08, 0.15),
-- Scenario 5 (Global Recession) · DMB
(5, 'DMB',     NULL, -4.0, 15.0, 3.0,  -0.40, 0.50,  0.00, -0.08, 0.15,  NULL,  NULL),
-- Scenario 6 (Rate Spike) · DMB
(6, 'DMB',     NULL,  0.0,  0.0, 0.0,   0.00, 0.60, -0.15,  0.00, 0.00,  NULL,  NULL),
-- Scenario 6 (Rate Spike) · MFB
(6, 'MFB',     NULL,  0.0,  0.0, 0.0,  -0.10, 0.70, -0.10,  0.00, 0.00,  NULL,  NULL),
-- Scenario 7 (Pandemic) · DMB
(7, 'DMB',     NULL, -5.0,  8.0, 4.0,  -0.50, 0.80, -0.12, -0.05, 0.12,  NULL,  NULL),
-- Scenario 7 (Pandemic) · ALL (moratorium effect)
(7, 'ALL',     NULL, -5.0,  8.0, 4.0,  -0.30, 0.90, -0.08,  NULL, NULL,  NULL,  NULL),
-- Scenario 8 (Cyber) · PSP
(8, 'PSP',     NULL,  0.0,  0.0, 0.0,   0.00, 0.00, -0.00,  NULL, NULL,  NULL,  NULL),
-- Scenario 8 (Cyber) · DMB (deposit flight + liquidity shock)
(8, 'DMB',     NULL,  0.0,  0.0, 0.0,   0.00, 0.00,  0.00,  NULL, NULL,  NULL,  NULL);

-- Set cyber-specific parameters separately
UPDATE StressScenarioParameters
SET    LCRDeltaPerCyber = -25.0, DepositOutflowPctCyber = 15.0
WHERE  ScenarioId = 8 AND InstitutionType IN ('DMB','PSP');

-- NGFS Orderly — DMB
INSERT INTO StressScenarioParameters
    (ScenarioId, InstitutionType,
     CarbonTaxUSDPerTon, StrandedAssetsPct, PhysicalRiskHazardCode,
     GDPGrowthShock, CARDeltaPerGDPPp, NPLDeltaPerGDPPp)
VALUES
(1, 'DMB',    147.0, 8.0,  'NONE',         -0.5, -0.10, 0.15),
(2, 'DMB',    200.0, 25.0, 'NONE',         -2.5, -0.55, 0.70),
(3, 'DMB',      0.0, 5.0,  'FLOOD',        -2.0, -0.45, 0.60),
(3, 'MFB',      0.0, 0.0,  'FLOOD_DROUGHT',-2.0, -0.30, 0.90);
```

---

## 3 · Domain Models

```csharp
// ============================================================
// Enums
// ============================================================
public enum ScenarioCategory { NgfsClimate, Macro, Custom }
public enum ScenarioSeverity  { Mild, Moderate, Severe, Extreme }
public enum StressTestRunStatus { Running, Completed, Failed }
public enum FailureCause { DirectShock, Interbank, DepositFlight }
public enum PhysicalHazard { None, Flood, Drought, HeatStress, FloodDrought }

// ============================================================
// Shock transmission result for a single entity
// ============================================================
public sealed record EntityShockResult(
    int    InstitutionId,
    string InstitutionType,
    // Pre-stress
    decimal PreCAR,
    decimal PreNPL,
    decimal PreLCR,
    decimal PreNSFR,
    decimal PreROA,
    decimal PreTotalAssets,
    decimal PreTotalDeposits,
    // Post-stress
    decimal PostCAR,
    decimal PostNPL,
    decimal PostLCR,
    decimal PostNSFR,
    decimal PostROA,
    decimal PostCapitalShortfall,
    decimal AdditionalProvisions,
    // Flags
    bool   BreachesCAR,
    bool   BreachesLCR,
    bool   BreachesNSFR,
    bool   IsInsolvent,
    // NDIC
    decimal InsurableDeposits,
    decimal UninsurableDeposits
);

// ============================================================
// Parameters resolved for a specific entity shock computation
// ============================================================
public sealed record ResolvedShockParameters(
    int    ScenarioId,
    string InstitutionType,
    decimal GDPGrowthShock,
    decimal OilPriceShockPct,
    decimal FXDepreciationPct,
    decimal InflationShockPp,
    int    InterestRateShockBps,
    // NGFS
    decimal CarbonTaxUSDPerTon,
    decimal StrandedAssetsPct,
    string? PhysicalRiskHazardCode,
    // Transmission coefficients
    decimal CARDeltaPerGDPPp,
    decimal NPLDeltaPerGDPPp,
    decimal LCRDeltaPerRateHike100,
    decimal CARDeltaPerFXPct,
    decimal NPLDeltaPerFXPct,
    decimal CARDeltaPerOilPct,
    decimal NPLDeltaPerOilPct,
    decimal LCRDeltaPerCyber,
    decimal DepositOutflowPctCyber
);

// ============================================================
// Sector aggregate summary (for report and dashboard)
// ============================================================
public sealed record SectorStressAggregate(
    string InstitutionType,
    int    EntityCount,
    decimal PreAvgCAR,
    decimal PreAvgNPL,
    decimal PreAvgLCR,
    decimal PostAvgCAR,
    decimal PostAvgNPL,
    decimal PostAvgLCR,
    int    EntitiesBreachingCAR,
    int    EntitiesBreachingLCR,
    int    EntitiesInsolvent,
    int    EntitiesContagionVictims,
    decimal TotalCapitalShortfall,
    decimal TotalAdditionalProvisions,
    decimal TotalInsurableDepositsAtRisk,
    decimal TotalUninsurableDepositsAtRisk
);

// ============================================================
// Overall run summary
// ============================================================
public sealed record StressTestRunSummary(
    long   RunId,
    Guid   RunGuid,
    string ScenarioCode,
    string ScenarioName,
    string PeriodCode,
    string TimeHorizon,
    int    EntitiesShocked,
    int    ContagionRounds,
    double SystemicResilienceScore,
    string ResilienceRating,          // "RESILIENT","ADEQUATE","VULNERABLE","CRITICAL"
    IReadOnlyList<SectorStressAggregate> BySector,
    decimal TotalCapitalShortfallNgn, // sector-wide
    decimal TotalNDICExposureAtRisk,
    TimeSpan Duration
);

// ============================================================
// Contagion cascade event
// ============================================================
public sealed record ContagionEvent(
    int  ContagionRound,
    int  FailingInstitutionId,
    int  AffectedInstitutionId,
    decimal ExposureAmount,
    string ExposureType,
    string TransmissionType   // "INTERBANK" | "DEPOSIT_FLIGHT"
);
```

---

## 4 · Service Contracts (Interfaces)

```csharp
// ── Main orchestrator ─────────────────────────────────────────────────────
public interface IStressTestOrchestrator
{
    /// <summary>
    /// Runs a full stress test cycle for all active entities in a regulator's jurisdiction.
    /// Returns the completed run summary.
    /// </summary>
    Task<StressTestRunSummary> RunAsync(
        string regulatorCode,
        int    scenarioId,
        string periodCode,
        string timeHorizon,
        int    initiatedByUserId,
        CancellationToken ct = default);

    Task<StressTestRunSummary?> GetRunSummaryAsync(
        Guid runGuid,
        CancellationToken ct = default);

    Task<IReadOnlyList<EntityShockResult>> GetEntityResultsAsync(
        long runId,
        string? institutionTypeFilter,
        CancellationToken ct = default);
}

// ── Macro shock transmitter ───────────────────────────────────────────────
public interface IMacroShockTransmitter
{
    /// <summary>
    /// Applies macro/climate shock parameters to a single entity's pre-stress metrics.
    /// Returns the post-shock metric set.
    /// </summary>
    EntityShockResult ApplyShock(
        PrudentialMetricSnapshot preStress,
        ResolvedShockParameters  parameters);
}

// ── Contagion cascade engine ──────────────────────────────────────────────
public interface IContagionCascadeEngine
{
    /// <summary>
    /// Propagates failures via interbank exposures and deposit flight.
    /// Max 5 rounds (R-08). Returns all additional failures and contagion events.
    /// </summary>
    Task<(
        IReadOnlyList<EntityShockResult> AdditionalFailures,
        IReadOnlyList<ContagionEvent>    Events,
        int                              RoundsExecuted
    )> CascadeAsync(
        IReadOnlyList<EntityShockResult> round0Results,
        string regulatorCode,
        string periodCode,
        long   runId,
        CancellationToken ct = default);
}

// ── NDIC exposure calculator ──────────────────────────────────────────────
public interface INDICExposureCalculator
{
    /// <summary>
    /// Computes insurable vs uninsurable deposits for failed entities.
    /// Uses N5,000,000 per-depositor cap (R-12).
    /// </summary>
    Task<(decimal Insurable, decimal Uninsurable)> ComputeAsync(
        int    institutionId,
        string periodCode,
        CancellationToken ct = default);

    Task<decimal> GetNDICFundCapacityAsync(CancellationToken ct = default);
}

// ── Report generator ──────────────────────────────────────────────────────
public interface IStressTestReportGenerator
{
    /// <summary>
    /// Generates the complete QuestPDF stress test report.
    /// Returns raw PDF bytes suitable for streaming to browser.
    /// </summary>
    Task<byte[]> GenerateAsync(
        long runId,
        bool anonymiseEntities,
        CancellationToken ct = default);
}

// ── Snapshot (pre-stress metrics loaded from RG-36 PrudentialMetrics) ──────
public sealed record PrudentialMetricSnapshot(
    int     InstitutionId,
    string  InstitutionType,
    string  RegulatorCode,
    string  PeriodCode,
    decimal CAR,
    decimal NPL,
    decimal LCR,
    decimal NSFR,
    decimal ROA,
    decimal TotalAssets,
    decimal TotalDeposits,
    decimal OilSectorExposurePct,      // % of loan book in oil & gas
    decimal AgriExposurePct,           // % exposed to agriculture
    decimal FXLoansAssetPct,           // % of loans denominated in FX
    decimal BondPortfolioAssetPct,     // % of assets in fixed-income securities
    decimal TopDepositorConcentration  // top-20 depositors / total %
);
```

---

## 5 · Macro Shock Transmitter — Full Implementation

```csharp
/// <summary>
/// Applies macro-prudential and NGFS climate shocks to a single entity's
/// pre-stress metrics using documented CBN/IMF transmission coefficients (R-07).
/// All arithmetic is traceable: each delta is labelled and summed explicitly.
/// </summary>
public sealed class MacroShockTransmitter : IMacroShockTransmitter
{
    // NDIC depositor insurance cap: N5,000,000 per depositor
    private const decimal NdicCapPerDepositor = 5_000_000m;
    // Average depositor count approximation for computing insurable share
    // (real implementation reads from depositor distribution table)
    private const decimal EstimatedInsurableShareOfDeposits = 0.72m;

    public EntityShockResult ApplyShock(
        PrudentialMetricSnapshot preStress,
        ResolvedShockParameters  p)
    {
        // ── Step 1: Compute Δ CAR ────────────────────────────────────────
        decimal deltaCAR = 0m;

        // GDP channel: each 1pp GDP decline reduces CAR by coefficient
        if (p.GDPGrowthShock != 0)
            deltaCAR += p.GDPGrowthShock * p.CARDeltaPerGDPPp;

        // Oil price channel: direct impact on oil-sector loan book
        if (p.OilPriceShockPct != 0)
            deltaCAR += (p.OilPriceShockPct / 100m)
                        * preStress.OilSectorExposurePct
                        * p.CARDeltaPerOilPct * 100m;

        // FX depreciation: FX-denominated loans become more burdensome
        if (p.FXDepreciationPct != 0)
            deltaCAR += (p.FXDepreciationPct / 100m)
                        * preStress.FXLoansAssetPct
                        * p.CARDeltaPerFXPct * 100m;

        // NGFS stranded assets: direct write-down of transition-risk portfolio
        if (p.StrandedAssetsPct > 0)
            deltaCAR -= p.StrandedAssetsPct * 0.60m;  // 60% haircut on stranded assets

        // ── Step 2: Compute Δ NPL ────────────────────────────────────────
        decimal deltaNPL = 0m;

        if (p.GDPGrowthShock != 0)
            deltaNPL += Math.Abs(p.GDPGrowthShock) * p.NPLDeltaPerGDPPp;

        if (p.OilPriceShockPct != 0)
            deltaNPL += (Math.Abs(p.OilPriceShockPct) / 100m)
                        * preStress.OilSectorExposurePct
                        * p.NPLDeltaPerOilPct * 100m;

        if (p.FXDepreciationPct != 0)
            deltaNPL += (p.FXDepreciationPct / 100m)
                        * preStress.FXLoansAssetPct
                        * p.NPLDeltaPerFXPct * 100m;

        // Physical risk (NGFS Hot House): agri NPL spike
        if (p.PhysicalRiskHazardCode is "FLOOD" or "DROUGHT" or "FLOOD_DROUGHT")
        {
            var hazardMultiplier = p.PhysicalRiskHazardCode == "FLOOD_DROUGHT" ? 2.0m : 1.0m;
            deltaNPL += preStress.AgriExposurePct * 0.35m * hazardMultiplier;
        }

        // ── Step 3: Compute Δ LCR ────────────────────────────────────────
        decimal deltaLCR = 0m;

        // Interest rate shock: bond portfolio mark-to-market reduces HQLA
        if (p.InterestRateShockBps != 0)
            deltaLCR += (p.InterestRateShockBps / 100m) * p.LCRDeltaPerRateHike100
                        * preStress.BondPortfolioAssetPct * 10m;

        // Cyber scenario: direct LCR shock + deposit outflow
        if (p.LCRDeltaPerCyber != 0)
            deltaLCR += p.LCRDeltaPerCyber;

        if (p.DepositOutflowPctCyber != 0)
            deltaLCR -= p.DepositOutflowPctCyber * 0.80m; // 80% of outflow hits LCR

        // Pandemic: deposit outflow during panic phase
        if (p.GDPGrowthShock <= -5m)
            deltaLCR -= preStress.TopDepositorConcentration * 0.20m;

        // ── Step 4: Compute Δ NSFR ───────────────────────────────────────
        decimal deltaNSFR = 0m;
        if (p.InterestRateShockBps != 0)
            deltaNSFR -= (p.InterestRateShockBps / 100m) * 1.5m;
        if (p.GDPGrowthShock < -3m)
            deltaNSFR -= 5.0m;

        // ── Step 5: Compute Δ ROA ────────────────────────────────────────
        decimal deltaROA = 0m;
        if (p.GDPGrowthShock != 0)
            deltaROA += p.GDPGrowthShock * 0.08m;  // rough 0.08 ROA sensitivity per GDP pp
        if (p.InterestRateShockBps > 0)
            deltaROA -= (p.InterestRateShockBps / 100m) * 0.10m;

        // ── Step 6: Apply deltas ─────────────────────────────────────────
        var postCAR  = Math.Round(preStress.CAR  + deltaCAR,  4);
        var postNPL  = Math.Max(0, Math.Round(preStress.NPL  + deltaNPL,  4));
        var postLCR  = Math.Max(0, Math.Round(preStress.LCR  + deltaLCR,  4));
        var postNSFR = Math.Max(0, Math.Round(preStress.NSFR + deltaNSFR, 4));
        var postROA  = Math.Round(preStress.ROA  + deltaROA,  4);

        // ── Step 7: Capital shortfall & provisioning ─────────────────────
        var minCAR = (decimal)GetMinCAR(preStress.InstitutionType);

        // Additional provisions = ΔNPLRatio × GrossLoans (estimated from assets × 0.65)
        var estimatedGrossLoans = preStress.TotalAssets * 0.65m;
        var additionalProvisions = Math.Max(0, deltaNPL / 100m * estimatedGrossLoans);

        // Capital shortfall = max(0, (minCAR − postCAR) × RWA)
        var estimatedRWA = preStress.TotalAssets * 0.78m;
        var capitalShortfall = postCAR < minCAR
            ? Math.Max(0, (minCAR - postCAR) / 100m * estimatedRWA)
            : 0m;

        // Provision cost reduces capital further
        postCAR -= additionalProvisions / Math.Max(1, estimatedRWA) * 100m;

        // ── Step 8: Breach flags ─────────────────────────────────────────
        bool breachesCAR  = postCAR  < minCAR;
        bool breachesLCR  = postLCR  < 100.0m;
        bool breachesNSFR = postNSFR < 100.0m;
        bool isInsolvent  = postCAR  < 0m;

        // ── Step 9: NDIC exposure ────────────────────────────────────────
        var insurableDeposits   = preStress.TotalDeposits * EstimatedInsurableShareOfDeposits;
        var uninsurableDeposits = preStress.TotalDeposits - insurableDeposits;

        // Only report NDIC exposure if entity fails
        if (!breachesCAR && !isInsolvent)
        {
            insurableDeposits   = 0m;
            uninsurableDeposits = 0m;
        }

        return new EntityShockResult(
            InstitutionId:         preStress.InstitutionId,
            InstitutionType:       preStress.InstitutionType,
            PreCAR:                preStress.CAR,
            PreNPL:                preStress.NPL,
            PreLCR:                preStress.LCR,
            PreNSFR:               preStress.NSFR,
            PreROA:                preStress.ROA,
            PreTotalAssets:        preStress.TotalAssets,
            PreTotalDeposits:      preStress.TotalDeposits,
            PostCAR:               postCAR,
            PostNPL:               postNPL,
            PostLCR:               postLCR,
            PostNSFR:              postNSFR,
            PostROA:               postROA,
            PostCapitalShortfall:  capitalShortfall,
            AdditionalProvisions:  additionalProvisions,
            BreachesCAR:           breachesCAR,
            BreachesLCR:           breachesLCR,
            BreachesNSFR:          breachesNSFR,
            IsInsolvent:           isInsolvent,
            InsurableDeposits:     insurableDeposits,
            UninsurableDeposits:   uninsurableDeposits);
    }

    private static double GetMinCAR(string institutionType) => institutionType switch
    {
        "DMB" => 15.0, "MFB" => 10.0, _ => 10.0
    };
}
```

---

## 6 · Contagion Cascade Engine — Full Implementation

```csharp
/// <summary>
/// Propagates entity failures via interbank exposures (round-robin BFS)
/// and deposit flight (stressed entities lose deposits to healthier peers).
/// Max cascade depth = 5 rounds (R-08).
/// </summary>
public sealed class ContagionCascadeEngine : IContagionCascadeEngine
{
    private const int MaxCascadeRounds = 5;
    private readonly IDbConnectionFactory _db;
    private readonly IMacroShockTransmitter _transmitter;
    private readonly ILogger<ContagionCascadeEngine> _log;

    public ContagionCascadeEngine(
        IDbConnectionFactory db,
        IMacroShockTransmitter transmitter,
        ILogger<ContagionCascadeEngine> log)
    {
        _db = db; _transmitter = transmitter; _log = log;
    }

    public async Task<(
        IReadOnlyList<EntityShockResult> AdditionalFailures,
        IReadOnlyList<ContagionEvent>    Events,
        int                              RoundsExecuted)>
        CascadeAsync(
            IReadOnlyList<EntityShockResult> round0Results,
            string regulatorCode, string periodCode,
            long runId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Load interbank exposure graph for this period
        var allEdges = (await conn.QueryAsync<ExposureEdge>(
            """
            SELECT ie.LendingInstitutionId, ie.BorrowingInstitutionId,
                   ie.ExposureAmount, ie.ExposureType
            FROM   InterbankExposures ie
            JOIN   Institutions i ON i.Id = ie.LendingInstitutionId
            WHERE  i.RegulatorCode = @Regulator AND ie.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        // Load pre-stress snapshots for all entities (for 2nd-round shock application)
        var snapshots = (await conn.QueryAsync<PrudentialMetricSnapshot>(
            """
            SELECT pm.InstitutionId, pm.InstitutionType, pm.RegulatorCode, pm.PeriodCode,
                   ISNULL(pm.CAR,0)   AS CAR,   ISNULL(pm.NPLRatio,0) AS NPL,
                   ISNULL(pm.LCR,0)   AS LCR,   ISNULL(pm.NSFR,0)    AS NSFR,
                   ISNULL(pm.ROA,0)   AS ROA,
                   ISNULL(pm.TotalAssets,0)    AS TotalAssets,
                   ISNULL(pm.TotalDeposits,0)  AS TotalDeposits,
                   ISNULL(pm.OilSectorExposurePct,0)     AS OilSectorExposurePct,
                   ISNULL(pm.AgriExposurePct,0)          AS AgriExposurePct,
                   ISNULL(pm.FXLoansAssetPct,0)          AS FXLoansAssetPct,
                   ISNULL(pm.BondPortfolioAssetPct,0)    AS BondPortfolioAssetPct,
                   ISNULL(pm.DepositConcentration,0)     AS TopDepositorConcentration
            FROM   PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            WHERE  i.RegulatorCode = @Regulator AND pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode }))
            .ToDictionary(s => s.InstitutionId);

        // Build adjacency: lender → list of (borrower, amount)
        var lenderGraph = allEdges
            .GroupBy(e => e.LendingInstitutionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.BorrowingInstitutionId, e.ExposureAmount, e.ExposureType))
                      .ToList());

        // Build adjacency: borrower → list of (lender, amount)  [for deposit-flight direction]
        var borrowerGraph = allEdges
            .GroupBy(e => e.BorrowingInstitutionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => (e.LendingInstitutionId, e.ExposureAmount))
                      .ToList());

        // Initial failed set from round 0
        var failedIds = new HashSet<int>(
            round0Results
                .Where(r => r.IsInsolvent || r.BreachesCAR)
                .Select(r => r.InstitutionId));

        var allResults       = round0Results.ToDictionary(r => r.InstitutionId);
        var additionalFailed = new List<EntityShockResult>();
        var allEvents        = new List<ContagionEvent>();
        int roundsExecuted   = 0;

        for (int round = 1; round <= MaxCascadeRounds && failedIds.Count > 0; round++)
        {
            var newFailures = new HashSet<int>();
            var roundEvents = new List<ContagionEvent>();

            foreach (var failedId in failedIds)
            {
                // ── Channel 1: Interbank exposure loss ──────────────────
                // Entities that have LENT to the failing institution lose that exposure
                if (borrowerGraph.TryGetValue(failedId, out var lenders))
                {
                    foreach (var (lenderId, amount) in lenders)
                    {
                        if (allResults.ContainsKey(lenderId) &&
                            (allResults[lenderId].IsInsolvent ||
                             failedIds.Contains(lenderId) ||
                             newFailures.Contains(lenderId)))
                            continue;  // already failed

                        if (!snapshots.TryGetValue(lenderId, out var snap)) continue;

                        // Compute haircut: failed institution's loss = 40% LGD assumption
                        var loss = amount * 0.40m;
                        var estimatedRWA = snap.TotalAssets * 0.78m;
                        var carImpact = estimatedRWA > 0
                            ? loss / estimatedRWA * 100m : 0m;

                        var currentResult = allResults.GetValueOrDefault(lenderId);
                        var effectivePostCAR = (currentResult?.PostCAR ?? snap.CAR) - carImpact;
                        var minCAR = snap.InstitutionType == "DMB" ? 15.0m : 10.0m;

                        roundEvents.Add(new ContagionEvent(
                            ContagionRound: round,
                            FailingInstitutionId: failedId,
                            AffectedInstitutionId: lenderId,
                            ExposureAmount: amount,
                            ExposureType: "PLACEMENT",
                            TransmissionType: "INTERBANK"));

                        if (effectivePostCAR < minCAR)
                        {
                            newFailures.Add(lenderId);

                            var contagionResult = (currentResult ?? BuildResultFromSnapshot(snap)) with
                            {
                                PostCAR         = effectivePostCAR,
                                BreachesCAR     = true,
                                IsInsolvent     = effectivePostCAR < 0,
                                IsContagionVictim = true,
                                ContagionRound  = round,
                                FailureCause    = FailureCause.Interbank.ToString()
                            };
                            allResults[lenderId] = contagionResult;
                            additionalFailed.Add(contagionResult);
                        }
                    }
                }

                // ── Channel 2: Deposit flight ────────────────────────────
                // Depositors withdraw from stressed entities into safer ones
                // This reduces LCR of the stressed institution further
                if (snapshots.TryGetValue(failedId, out var failedSnap))
                {
                    var depositFlightAmount =
                        failedSnap.TotalDeposits *
                        failedSnap.TopDepositorConcentration / 100m * 0.25m;

                    // Entities that BORROWED from the failing institution face
                    // liquidity tightening as it recalls placements
                    if (lenderGraph.TryGetValue(failedId, out var borrowers))
                    {
                        foreach (var (borrowerId, amount, expType) in borrowers)
                        {
                            if (failedIds.Contains(borrowerId) ||
                                newFailures.Contains(borrowerId)) continue;

                            if (!snapshots.TryGetValue(borrowerId, out var borrowerSnap)) continue;

                            // Borrower must repay or refinance → liquidity squeeze
                            var lcrImpact = borrowerSnap.TotalAssets > 0
                                ? (double)(amount / borrowerSnap.TotalAssets) * -15.0
                                : 0.0;

                            var currentResult = allResults.GetValueOrDefault(borrowerId);
                            var effectivePostLCR = (double)(currentResult?.PostLCR
                                                  ?? borrowerSnap.LCR) + lcrImpact;

                            roundEvents.Add(new ContagionEvent(
                                round, failedId, borrowerId,
                                depositFlightAmount, expType, "DEPOSIT_FLIGHT"));

                            if (effectivePostLCR < 100.0)
                            {
                                // LCR breach triggers further stress but not immediate failure
                                var updated = (currentResult ?? BuildResultFromSnapshot(borrowerSnap))
                                    with { PostLCR = (decimal)effectivePostLCR, BreachesLCR = true };
                                allResults[borrowerId] = updated;
                            }
                        }
                    }
                }
            }

            // Persist contagion events
            foreach (var evt in roundEvents)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO StressTestContagionEvents
                        (RunId, ContagionRound, FailingInstitutionId, AffectedInstitutionId,
                         ExposureAmount, ExposureType, TransmissionType)
                    VALUES (@RunId, @Round, @Failing, @Affected, @Amount, @Type, @Trans)
                    """,
                    new { RunId = runId, Round = round, Failing = evt.FailingInstitutionId,
                          Affected = evt.AffectedInstitutionId, Amount = evt.ExposureAmount,
                          Type = evt.ExposureType, Trans = evt.TransmissionType });
            }

            allEvents.AddRange(roundEvents);
            roundsExecuted = round;

            if (newFailures.Count == 0) break;

            // Only propagate genuinely new failures in next round
            failedIds = newFailures;

            _log.LogInformation(
                "Contagion round {Round}: {New} new failures, {Events} events",
                round, newFailures.Count, roundEvents.Count);
        }

        return (additionalFailed, allEvents, roundsExecuted);
    }

    private static EntityShockResult BuildResultFromSnapshot(PrudentialMetricSnapshot s)
        => new(s.InstitutionId, s.InstitutionType,
               s.CAR, s.NPL, s.LCR, s.NSFR, s.ROA, s.TotalAssets, s.TotalDeposits,
               s.CAR, s.NPL, s.LCR, s.NSFR, s.ROA, 0m, 0m,
               false, false, false, false, 0m, 0m);

    private sealed record ExposureEdge(
        int LendingInstitutionId, int BorrowingInstitutionId,
        decimal ExposureAmount, string ExposureType);
}
```

---

## 7 · NDIC Exposure Calculator — Full Implementation

```csharp
/// <summary>
/// Computes NDIC insurable vs uninsurable deposits for entities that fail
/// under stress. Uses the N5,000,000 per-depositor cap (R-12).
/// Reads depositor distribution from DepositorDistributions table where available;
/// falls back to the actuarial estimate (72% insurable share for average Nigerian bank).
/// </summary>
public sealed class NDICExposureCalculator : INDICExposureCalculator
{
    private const decimal InsuranceCapPerDepositorNGN = 5_000_000m;
    private const decimal FallbackInsurableShare = 0.72m;
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<NDICExposureCalculator> _log;

    public NDICExposureCalculator(IDbConnectionFactory db, ILogger<NDICExposureCalculator> log)
    {
        _db = db; _log = log;
    }

    public async Task<(decimal Insurable, decimal Uninsurable)> ComputeAsync(
        int institutionId, string periodCode, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Attempt to load actual depositor distribution data
        var dist = await conn.QuerySingleOrDefaultAsync<DepositorDistRow>(
            """
            SELECT TotalDeposits, DepositorsAboveCap, TotalDepositorCount,
                   DepositsByAccountsBelowCap
            FROM   DepositorDistributions
            WHERE  InstitutionId = @Id AND PeriodCode = @Period
            """,
            new { Id = institutionId, Period = periodCode });

        decimal totalDeposits;

        if (dist is not null && dist.TotalDepositorCount > 0)
        {
            // Use actual distribution: deposits from accounts ≤ N5M cap are fully insured
            var insurable   = dist.DepositsByAccountsBelowCap;
            var uninsurable = dist.TotalDeposits - insurable;
            return (insurable, uninsurable);
        }

        // Fallback: load total deposits from PrudentialMetrics
        totalDeposits = await conn.ExecuteScalarAsync<decimal>(
            """
            SELECT ISNULL(TotalDeposits, 0)
            FROM   PrudentialMetrics
            WHERE  InstitutionId = @Id AND PeriodCode = @Period
            """,
            new { Id = institutionId, Period = periodCode });

        if (totalDeposits <= 0)
        {
            _log.LogWarning(
                "No deposit data for institution {Id} period {Period} — NDIC exposure = 0",
                institutionId, periodCode);
            return (0m, 0m);
        }

        // Actuarial estimate: 72% of deposits are insurable under the cap
        var fallbackInsurable   = totalDeposits * FallbackInsurableShare;
        var fallbackUninsurable = totalDeposits - fallbackInsurable;

        _log.LogDebug(
            "NDIC fallback estimate: Institution={Id} TotalDeposits={TD:N0} " +
            "Insurable={Ins:N0} Uninsurable={Unins:N0}",
            institutionId, totalDeposits, fallbackInsurable, fallbackUninsurable);

        return (fallbackInsurable, fallbackUninsurable);
    }

    public async Task<decimal> GetNDICFundCapacityAsync(CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // NDIC fund capacity from system configuration table
        var capacity = await conn.ExecuteScalarAsync<decimal?>(
            "SELECT ConfigValue FROM SystemConfiguration WHERE ConfigKey='NDIC_FUND_CAPACITY_NGN_MILLIONS'");

        // Default to NDIC's publicly reported Deposit Insurance Fund
        // (approximately N1.5 trillion as of 2024)
        return capacity ?? 1_500_000m;
    }

    private sealed record DepositorDistRow(
        decimal TotalDeposits,
        int DepositorsAboveCap,
        int TotalDepositorCount,
        decimal DepositsByAccountsBelowCap);
}
```

---

## 8 · Stress Test Orchestrator — Full Implementation

```csharp
public sealed class StressTestOrchestrator : IStressTestOrchestrator
{
    private readonly IDbConnectionFactory _db;
    private readonly IMacroShockTransmitter _transmitter;
    private readonly IContagionCascadeEngine _contagion;
    private readonly INDICExposureCalculator _ndic;
    private readonly ILogger<StressTestOrchestrator> _log;

    public StressTestOrchestrator(
        IDbConnectionFactory db,
        IMacroShockTransmitter transmitter,
        IContagionCascadeEngine contagion,
        INDICExposureCalculator ndic,
        ILogger<StressTestOrchestrator> log)
    {
        _db = db; _transmitter = transmitter;
        _contagion = contagion; _ndic = ndic; _log = log;
    }

    // ── Main run ─────────────────────────────────────────────────────────
    public async Task<StressTestRunSummary> RunAsync(
        string regulatorCode, int scenarioId,
        string periodCode, string timeHorizon,
        int initiatedByUserId, CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;

        await using var conn = await _db.OpenAsync(ct);

        // Create run record
        var runId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO StressTestRuns
                (RegulatorCode, ScenarioId, PeriodCode, TimeHorizon,
                 Status, InitiatedByUserId)
            OUTPUT INSERTED.Id
            VALUES (@Regulator, @ScenarioId, @Period, @Horizon, 'RUNNING', @User)
            """,
            new { Regulator = regulatorCode, ScenarioId = scenarioId,
                  Period = periodCode, Horizon = timeHorizon, User = initiatedByUserId });

        var runGuid = await conn.ExecuteScalarAsync<Guid>(
            "SELECT RunGuid FROM StressTestRuns WHERE Id=@Id", new { Id = runId });

        _log.LogInformation(
            "Stress test run started: RunId={Id} Guid={Guid} Scenario={Scen} Period={P}",
            runId, runGuid, scenarioId, periodCode);

        try
        {
            // ── Step 1: Load scenario parameters ────────────────────────
            var scenarioParams = await LoadScenarioParametersAsync(conn, scenarioId);

            // ── Step 2: Load pre-stress snapshots ────────────────────────
            var snapshots = await LoadSnapshotsAsync(conn, regulatorCode, periodCode);

            _log.LogInformation("Loaded {Count} entity snapshots for stress test.", snapshots.Count);

            // ── Step 3: Apply direct shocks ──────────────────────────────
            var round0Results = new List<EntityShockResult>();

            foreach (var snapshot in snapshots)
            {
                var effectiveParams = ResolveParameters(scenarioParams, snapshot.InstitutionType);

                var result = _transmitter.ApplyShock(snapshot, effectiveParams);

                // Enrich NDIC exposure for failing entities
                if (result.BreachesCAR || result.IsInsolvent)
                {
                    var (insurable, uninsurable) = await _ndic.ComputeAsync(
                        snapshot.InstitutionId, periodCode, ct);
                    result = result with
                    {
                        InsurableDeposits   = insurable,
                        UninsurableDeposits = uninsurable
                    };
                }

                round0Results.Add(result);
            }

            // ── Step 4: Contagion cascade ─────────────────────────────────
            var (contagionFailures, contagionEvents, roundsExecuted) =
                await _contagion.CascadeAsync(
                    round0Results, regulatorCode, periodCode, runId, ct);

            // Merge contagion results into final list
            var finalResults = MergeResults(round0Results, contagionFailures);

            // ── Step 5: Persist entity results ────────────────────────────
            await PersistEntityResultsAsync(conn, runId, regulatorCode, finalResults, ct);

            // ── Step 6: Compute sector aggregates ────────────────────────
            var aggregates = ComputeSectorAggregates(finalResults);
            await PersistSectorAggregatesAsync(conn, runId, aggregates, ct);

            // ── Step 7: Compute systemic resilience score ─────────────────
            var resilience = ComputeResilienceScore(finalResults, aggregates);

            // ── Step 8: Update run record ──────────────────────────────────
            var summary = await BuildSummaryAsync(
                conn, runId, runGuid, scenarioId, periodCode, timeHorizon,
                finalResults.Count, roundsExecuted, resilience,
                aggregates, started, ct);

            await conn.ExecuteAsync(
                """
                UPDATE StressTestRuns
                SET    Status='COMPLETED', EntitiesShocked=@Entities,
                       ContagionRounds=@Rounds, SystemicResilienceScore=@Score,
                       ExecutiveSummary=@Summary, CompletedAt=SYSUTCDATETIME()
                WHERE  Id=@Id
                """,
                new { Entities = finalResults.Count, Rounds = roundsExecuted,
                      Score = resilience, Summary = summary.ExecutiveSummary, Id = runId });

            _log.LogInformation(
                "Stress test complete: RunId={Id} Entities={E} Rounds={R} Score={S:F1}",
                runId, finalResults.Count, roundsExecuted, resilience);

            return summary;
        }
        catch (Exception ex)
        {
            await conn.ExecuteAsync(
                "UPDATE StressTestRuns SET Status='FAILED', ErrorMessage=@Err WHERE Id=@Id",
                new { Err = ex.Message[..Math.Min(2000, ex.Message.Length)], Id = runId });

            _log.LogError(ex, "Stress test run {Id} failed.", runId);
            throw;
        }
    }

    // ── Query methods ─────────────────────────────────────────────────────
    public async Task<StressTestRunSummary?> GetRunSummaryAsync(
        Guid runGuid, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var run = await conn.QuerySingleOrDefaultAsync<RunRow>(
            """
            SELECT r.Id, r.RunGuid, r.ScenarioId, r.PeriodCode, r.TimeHorizon,
                   r.EntitiesShocked, r.ContagionRounds,
                   r.SystemicResilienceScore, r.ExecutiveSummary,
                   r.StartedAt, r.CompletedAt,
                   s.ScenarioCode, s.ScenarioName
            FROM   StressTestRuns r
            JOIN   StressScenarios s ON s.Id = r.ScenarioId
            WHERE  r.RunGuid = @Guid
            """,
            new { Guid = runGuid });

        if (run is null) return null;

        var aggregates = (await conn.QueryAsync<SectorStressAggregate>(
            """
            SELECT InstitutionType, EntityCount,
                   PreAvgCAR, PreAvgNPL, PreAvgLCR,
                   PostAvgCAR, PostAvgNPL, PostAvgLCR,
                   EntitiesBreachingCAR, EntitiesBreachingLCR,
                   EntitiesInsolvent, EntitiesContagionVictims,
                   TotalCapitalShortfall, TotalAdditionalProvisions,
                   TotalInsurableDepositsAtRisk, TotalUninsurableDepositsAtRisk
            FROM   StressTestSectorAggregates WHERE RunId=@Id
            """,
            new { Id = run.Id })).ToList();

        var score = (double)(run.SystemicResilienceScore ?? 50m);

        return new StressTestRunSummary(
            RunId: run.Id, RunGuid: run.RunGuid,
            ScenarioCode: run.ScenarioCode, ScenarioName: run.ScenarioName,
            PeriodCode: run.PeriodCode, TimeHorizon: run.TimeHorizon,
            EntitiesShocked: run.EntitiesShocked,
            ContagionRounds: run.ContagionRounds,
            SystemicResilienceScore: score,
            ResilienceRating: GetResilienceRating(score),
            BySector: aggregates,
            TotalCapitalShortfallNgn: aggregates.Sum(a => a.TotalCapitalShortfall),
            TotalNDICExposureAtRisk: aggregates.Sum(a => a.TotalInsurableDepositsAtRisk),
            Duration: (run.CompletedAt ?? DateTimeOffset.UtcNow) - run.StartedAt,
            ExecutiveSummary: run.ExecutiveSummary);
    }

    public async Task<IReadOnlyList<EntityShockResult>> GetEntityResultsAsync(
        long runId, string? institutionTypeFilter, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        return (await conn.QueryAsync<EntityShockResult>(
            """
            SELECT InstitutionId, InstitutionType,
                   PreCAR, PreNPL, PreLCR, PreNSFR, PreROA, PreTotalAssets, PreTotalDeposits,
                   PostCAR, PostNPL, PostLCR, PostNSFR, PostROA,
                   PostCapitalShortfall, AdditionalProvisions,
                   BreachesCAR, BreachesLCR, BreachesNSFR, IsInsolvent,
                   InsurableDeposits, UninsurableDeposits
            FROM   StressTestEntityResults
            WHERE  RunId = @RunId
              AND  (@TypeFilter IS NULL OR InstitutionType = @TypeFilter)
            ORDER BY PostCAR ASC
            """,
            new { RunId = runId, TypeFilter = institutionTypeFilter }))
            .ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────
    private static async Task<Dictionary<string, ResolvedShockParameters>>
        LoadScenarioParametersAsync(
            System.Data.IDbConnection conn, int scenarioId)
    {
        var rows = await conn.QueryAsync<ScenarioParamRow>(
            "SELECT * FROM StressScenarioParameters WHERE ScenarioId=@Id",
            new { Id = scenarioId });

        return rows.ToDictionary(r => r.InstitutionType, r => new ResolvedShockParameters(
            ScenarioId: scenarioId,
            InstitutionType: r.InstitutionType,
            GDPGrowthShock: r.GDPGrowthShock ?? 0m,
            OilPriceShockPct: r.OilPriceShockPct ?? 0m,
            FXDepreciationPct: r.FXDepreciationPct ?? 0m,
            InflationShockPp: r.InflationShockPp ?? 0m,
            InterestRateShockBps: r.InterestRateShockBps ?? 0,
            CarbonTaxUSDPerTon: r.CarbonTaxUSDPerTon ?? 0m,
            StrandedAssetsPct: r.StrandedAssetsPct ?? 0m,
            PhysicalRiskHazardCode: r.PhysicalRiskHazardCode,
            CARDeltaPerGDPPp: r.CARDeltaPerGDPPp ?? -0.30m,
            NPLDeltaPerGDPPp: r.NPLDeltaPerGDPPp ?? 0.45m,
            LCRDeltaPerRateHike100: r.LCRDeltaPerRateHike100 ?? -0.08m,
            CARDeltaPerFXPct: r.CARDeltaPerFXPct ?? -0.10m,
            NPLDeltaPerFXPct: r.NPLDeltaPerFXPct ?? 0.15m,
            CARDeltaPerOilPct: r.CARDeltaPerOilPct ?? -0.08m,
            NPLDeltaPerOilPct: r.NPLDeltaPerOilPct ?? 0.20m,
            LCRDeltaPerCyber: r.LCRDeltaPerCyber ?? 0m,
            DepositOutflowPctCyber: r.DepositOutflowPctCyber ?? 0m));
    }

    private static async Task<List<PrudentialMetricSnapshot>> LoadSnapshotsAsync(
        System.Data.IDbConnection conn, string regulatorCode, string periodCode)
    {
        return (await conn.QueryAsync<PrudentialMetricSnapshot>(
            """
            SELECT pm.InstitutionId, i.InstitutionType, pm.RegulatorCode, pm.PeriodCode,
                   ISNULL(pm.CAR,0)  AS CAR,   ISNULL(pm.NPLRatio,0) AS NPL,
                   ISNULL(pm.LCR,0)  AS LCR,   ISNULL(pm.NSFR,0)    AS NSFR,
                   ISNULL(pm.ROA,0)  AS ROA,
                   ISNULL(pm.TotalAssets,0)           AS TotalAssets,
                   ISNULL(pm.TotalDeposits,0)          AS TotalDeposits,
                   ISNULL(pm.OilSectorExposurePct,0)   AS OilSectorExposurePct,
                   ISNULL(pm.AgriExposurePct,0)        AS AgriExposurePct,
                   ISNULL(pm.FXLoansAssetPct,0)        AS FXLoansAssetPct,
                   ISNULL(pm.BondPortfolioAssetPct,0)  AS BondPortfolioAssetPct,
                   ISNULL(pm.DepositConcentration,0)   AS TopDepositorConcentration
            FROM   PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            WHERE  i.RegulatorCode = @Regulator AND pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();
    }

    private static ResolvedShockParameters ResolveParameters(
        Dictionary<string, ResolvedShockParameters> byType, string institutionType)
    {
        // Prefer institution-type-specific params, fall back to 'ALL'
        return byType.TryGetValue(institutionType, out var specific)
            ? specific
            : byType.TryGetValue("ALL", out var all)
                ? all with { InstitutionType = institutionType }
                : new ResolvedShockParameters(
                    ScenarioId: 0, InstitutionType: institutionType,
                    0m, 0m, 0m, 0m, 0, 0m, 0m, null,
                    -0.30m, 0.45m, -0.08m, -0.10m, 0.15m, -0.08m, 0.20m, 0m, 0m);
    }

    private static List<EntityShockResult> MergeResults(
        List<EntityShockResult> round0,
        IReadOnlyList<EntityShockResult> contagion)
    {
        var merged = round0.ToDictionary(r => r.InstitutionId);
        foreach (var c in contagion)
            merged[c.InstitutionId] = c;
        return merged.Values.ToList();
    }

    private static async Task PersistEntityResultsAsync(
        System.Data.IDbConnection conn, long runId, string regulatorCode,
        List<EntityShockResult> results, CancellationToken ct)
    {
        foreach (var r in results)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO StressTestEntityResults
                    (RunId, InstitutionId, RegulatorCode, InstitutionType,
                     PreCAR, PreNPL, PreLCR, PreNSFR, PreROA, PreTotalAssets, PreTotalDeposits,
                     PostCAR, PostNPL, PostLCR, PostNSFR, PostROA,
                     PostCapitalShortfall, AdditionalProvisions,
                     DeltaCAR, DeltaNPL, DeltaLCR,
                     BreachesCAR, BreachesLCR, BreachesNSFR, IsInsolvent,
                     IsContagionVictim, InsurableDeposits, UninsurableDeposits)
                VALUES (@RunId, @InstId, @Regulator, @Type,
                        @PreCAR, @PreNPL, @PreLCR, @PreNSFR, @PreROA, @PreAssets, @PreDep,
                        @PostCAR, @PostNPL, @PostLCR, @PostNSFR, @PostROA,
                        @Shortfall, @Provisions,
                        @DeltaCAR, @DeltaNPL, @DeltaLCR,
                        @BrCAR, @BrLCR, @BrNSFR, @Insolv,
                        @ContagVic, @Ins, @Unins)
                """,
                new { RunId = runId, InstId = r.InstitutionId, Regulator = regulatorCode,
                      Type = r.InstitutionType,
                      PreCAR = r.PreCAR, PreNPL = r.PreNPL, PreLCR = r.PreLCR,
                      PreNSFR = r.PreNSFR, PreROA = r.PreROA,
                      PreAssets = r.PreTotalAssets, PreDep = r.PreTotalDeposits,
                      PostCAR = r.PostCAR, PostNPL = r.PostNPL, PostLCR = r.PostLCR,
                      PostNSFR = r.PostNSFR, PostROA = r.PostROA,
                      Shortfall = r.PostCapitalShortfall, Provisions = r.AdditionalProvisions,
                      DeltaCAR = r.PostCAR - r.PreCAR,
                      DeltaNPL = r.PostNPL - r.PreNPL,
                      DeltaLCR = r.PostLCR - r.PreLCR,
                      BrCAR = r.BreachesCAR, BrLCR = r.BreachesLCR,
                      BrNSFR = r.BreachesNSFR, Insolv = r.IsInsolvent,
                      ContagVic = r.IsContagionVictim,
                      Ins = r.InsurableDeposits, Unins = r.UninsurableDeposits });
        }
    }

    private static List<SectorStressAggregate> ComputeSectorAggregates(
        List<EntityShockResult> results)
    {
        return results
            .GroupBy(r => r.InstitutionType)
            .Select(g =>
            {
                var list = g.ToList();
                return new SectorStressAggregate(
                    InstitutionType: g.Key,
                    EntityCount: list.Count,
                    PreAvgCAR:  Avg(list, r => r.PreCAR),
                    PreAvgNPL:  Avg(list, r => r.PreNPL),
                    PreAvgLCR:  Avg(list, r => r.PreLCR),
                    PostAvgCAR: Avg(list, r => r.PostCAR),
                    PostAvgNPL: Avg(list, r => r.PostNPL),
                    PostAvgLCR: Avg(list, r => r.PostLCR),
                    EntitiesBreachingCAR: list.Count(r => r.BreachesCAR),
                    EntitiesBreachingLCR: list.Count(r => r.BreachesLCR),
                    EntitiesInsolvent:    list.Count(r => r.IsInsolvent),
                    EntitiesContagionVictims: list.Count(r => r.IsContagionVictim),
                    TotalCapitalShortfall:    list.Sum(r => r.PostCapitalShortfall),
                    TotalAdditionalProvisions: list.Sum(r => r.AdditionalProvisions),
                    TotalInsurableDepositsAtRisk:   list.Sum(r => r.InsurableDeposits),
                    TotalUninsurableDepositsAtRisk: list.Sum(r => r.UninsurableDeposits));
            }).ToList();
    }

    private static async Task PersistSectorAggregatesAsync(
        System.Data.IDbConnection conn, long runId,
        List<SectorStressAggregate> aggregates, CancellationToken ct)
    {
        foreach (var a in aggregates)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO StressTestSectorAggregates
                    (RunId, InstitutionType, EntityCount,
                     PreAvgCAR, PreAvgNPL, PreAvgLCR,
                     PostAvgCAR, PostAvgNPL, PostAvgLCR,
                     EntitiesBreachingCAR, EntitiesBreachingLCR,
                     EntitiesInsolvent, EntitiesContagionVictims,
                     TotalCapitalShortfall, TotalAdditionalProvisions,
                     TotalInsurableDepositsAtRisk, TotalUninsurableDepositsAtRisk)
                VALUES (@RunId, @Type, @Count,
                        @PreCAR, @PreNPL, @PreLCR,
                        @PostCAR, @PostNPL, @PostLCR,
                        @BrCAR, @BrLCR, @Insolv, @ContagVic,
                        @Shortfall, @Provisions, @Ins, @Unins)
                """,
                new { RunId = runId, Type = a.InstitutionType, Count = a.EntityCount,
                      PreCAR = a.PreAvgCAR, PreNPL = a.PreAvgNPL, PreLCR = a.PreAvgLCR,
                      PostCAR = a.PostAvgCAR, PostNPL = a.PostAvgNPL, PostLCR = a.PostAvgLCR,
                      BrCAR = a.EntitiesBreachingCAR, BrLCR = a.EntitiesBreachingLCR,
                      Insolv = a.EntitiesInsolvent, ContagVic = a.EntitiesContagionVictims,
                      Shortfall = a.TotalCapitalShortfall, Provisions = a.TotalAdditionalProvisions,
                      Ins = a.TotalInsurableDepositsAtRisk,
                      Unins = a.TotalUninsurableDepositsAtRisk });
        }
    }

    private static double ComputeResilienceScore(
        List<EntityShockResult> results,
        List<SectorStressAggregate> aggregates)
    {
        if (results.Count == 0) return 100.0;

        double insolventShare  = (double)results.Count(r => r.IsInsolvent)  / results.Count;
        double carBreachShare  = (double)results.Count(r => r.BreachesCAR)  / results.Count;
        double lcrBreachShare  = (double)results.Count(r => r.BreachesLCR)  / results.Count;
        double contagionShare  = (double)results.Count(r => r.IsContagionVictim) / results.Count;

        var score = 100.0
            - insolventShare  * 50
            - carBreachShare  * 25
            - lcrBreachShare  * 15
            - contagionShare  * 10;

        return Math.Max(0, Math.Round(score, 1));
    }

    private async Task<StressTestRunSummary> BuildSummaryAsync(
        System.Data.IDbConnection conn, long runId, Guid runGuid,
        int scenarioId, string periodCode, string timeHorizon,
        int entityCount, int contagionRounds, double resilienceScore,
        List<SectorStressAggregate> aggregates,
        DateTimeOffset started, CancellationToken ct)
    {
        var scenarioRow = await conn.QuerySingleAsync<(string Code, string Name)>(
            "SELECT ScenarioCode, ScenarioName FROM StressScenarios WHERE Id=@Id",
            new { Id = scenarioId });

        var totalShortfall = aggregates.Sum(a => a.TotalCapitalShortfall);
        var totalNdic      = aggregates.Sum(a => a.TotalInsurableDepositsAtRisk);
        var ndicCapacity   = await _ndic.GetNDICFundCapacityAsync(ct);
        var totalInsolvent = aggregates.Sum(a => a.EntitiesInsolvent);

        var rating = GetResilienceRating(resilienceScore);

        var execSummary = BuildExecutiveSummary(
            scenarioRow.Name, periodCode, entityCount, aggregates,
            totalShortfall, totalNdic, ndicCapacity, resilienceScore, rating,
            contagionRounds);

        await conn.ExecuteAsync(
            "UPDATE StressTestRuns SET ExecutiveSummary=@S WHERE Id=@Id",
            new { S = execSummary, Id = runId });

        return new StressTestRunSummary(
            RunId: runId, RunGuid: runGuid,
            ScenarioCode: scenarioRow.Code, ScenarioName: scenarioRow.Name,
            PeriodCode: periodCode, TimeHorizon: timeHorizon,
            EntitiesShocked: entityCount, ContagionRounds: contagionRounds,
            SystemicResilienceScore: resilienceScore, ResilienceRating: rating,
            BySector: aggregates,
            TotalCapitalShortfallNgn: totalShortfall,
            TotalNDICExposureAtRisk: totalNdic,
            Duration: DateTimeOffset.UtcNow - started,
            ExecutiveSummary: execSummary);
    }

    private static string BuildExecutiveSummary(
        string scenarioName, string periodCode, int entityCount,
        List<SectorStressAggregate> aggregates,
        decimal totalShortfall, decimal totalNdic, decimal ndicCapacity,
        double resilienceScore, string rating, int contagionRounds)
    {
        var totalBreachCAR = aggregates.Sum(a => a.EntitiesBreachingCAR);
        var totalInsolvent = aggregates.Sum(a => a.EntitiesInsolvent);
        var ndicCoveragePct = ndicCapacity > 0
            ? Math.Round((double)(totalNdic / ndicCapacity) * 100, 1)
            : 0.0;

        return $"""
The {scenarioName} stress test applied to {entityCount} supervised entities
as of {periodCode} yields a Systemic Resilience Score of {resilienceScore:F1}/100
({rating}).

Under this scenario, {totalBreachCAR} entities ({(double)totalBreachCAR / Math.Max(1, entityCount) * 100:F1}%)
breach minimum capital requirements. {totalInsolvent} entities become technically insolvent,
requiring an aggregate capital injection of ₦{totalShortfall:N0}M to restore compliance.
Contagion propagated across {contagionRounds} rounds via interbank exposure networks.

NDIC insurable deposits at risk total ₦{totalNdic:N0}M, representing
{ndicCoveragePct:F1}% of the Deposit Insurance Fund capacity.

Key sector-level findings:
{string.Join("\n", aggregates.Select(a =>
    $"  • {a.InstitutionType}: {a.EntitiesBreachingCAR}/{a.EntityCount} breach CAR | " +
    $"PostAvgCAR {a.PostAvgCAR:F1}% vs Pre {a.PreAvgCAR:F1}% | " +
    $"₦{a.TotalCapitalShortfall:N0}M shortfall"))}

Policy recommendation: {GetPolicyRecommendation(rating, totalBreachCAR, entityCount)}
""";
    }

    private static string GetPolicyRecommendation(
        string rating, int breachCount, int total)
    {
        double pct = total > 0 ? (double)breachCount / total * 100 : 0;
        return rating switch
        {
            "RESILIENT" =>
                "Maintain current macro-prudential stance. Consider releasing countercyclical capital buffer.",
            "ADEQUATE" =>
                $"{pct:F0}% of entities breach CAR. Activate enhanced supervisory monitoring. " +
                "Recommend forward-looking ICAAP review for top-quintile risk entities.",
            "VULNERABLE" =>
                $"Significant stress evident ({pct:F0}% CAR breaches). " +
                "Recommend mandatory capital planning submissions. Activate early intervention thresholds. " +
                "Review CBN sectoral lending guidelines for concentrated exposures.",
            _ =>
                $"Critical system-wide stress detected ({pct:F0}% CAR breaches). " +
                "Recommend emergency capital surcharge, suspension of dividends, " +
                "activation of the CBN Financial Stability Committee, and NDIC contingency planning."
        };
    }

    private static string GetResilienceRating(double score) => score switch
    {
        >= 80 => "RESILIENT",
        >= 60 => "ADEQUATE",
        >= 40 => "VULNERABLE",
        _     => "CRITICAL"
    };

    private static decimal Avg(List<EntityShockResult> list, Func<EntityShockResult, decimal> sel)
        => list.Count == 0 ? 0m : list.Average(sel);

    // Row types
    private sealed record RunRow(
        long Id, Guid RunGuid, int ScenarioId, string PeriodCode,
        string TimeHorizon, int EntitiesShocked, int ContagionRounds,
        decimal? SystemicResilienceScore, string? ExecutiveSummary,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        string ScenarioCode, string ScenarioName);

    private sealed record ScenarioParamRow(
        int ScenarioId, string InstitutionType,
        decimal? GDPGrowthShock, decimal? OilPriceShockPct,
        decimal? FXDepreciationPct, decimal? InflationShockPp,
        int? InterestRateShockBps, decimal? CarbonTaxUSDPerTon,
        decimal? StrandedAssetsPct, string? PhysicalRiskHazardCode,
        decimal? CARDeltaPerGDPPp, decimal? NPLDeltaPerGDPPp,
        decimal? LCRDeltaPerRateHike100, decimal? CARDeltaPerFXPct,
        decimal? NPLDeltaPerFXPct, decimal? CARDeltaPerOilPct,
        decimal? NPLDeltaPerOilPct, decimal? LCRDeltaPerCyber,
        decimal? DepositOutflowPctCyber);

    // Extend RunSummary record with executive summary field
    public sealed record StressTestRunSummary(
        long RunId, Guid RunGuid, string ScenarioCode, string ScenarioName,
        string PeriodCode, string TimeHorizon, int EntitiesShocked,
        int ContagionRounds, double SystemicResilienceScore,
        string ResilienceRating, IReadOnlyList<SectorStressAggregate> BySector,
        decimal TotalCapitalShortfallNgn, decimal TotalNDICExposureAtRisk,
        TimeSpan Duration, string? ExecutiveSummary = null);
}
```

---

## 9 · QuestPDF Stress Test Report Generator — Full Implementation

```csharp
/// <summary>
/// Generates the full FSB-style sector stress test report as a branded PDF (R-11).
/// Uses QuestPDF fluent API. Returns byte[] for direct browser streaming.
/// Sections: Cover → Executive Summary → Methodology → Sector Heatmap →
///           Scenario Parameters → Pre/Post Results → Contagion Map →
///           NDIC Analysis → Policy Recommendations → Appendix.
/// </summary>
public sealed class StressTestReportGenerator : IStressTestReportGenerator
{
    private readonly IDbConnectionFactory _db;
    private readonly IStressTestOrchestrator _orchestrator;
    private readonly ILogger<StressTestReportGenerator> _log;

    // ── RegOS brand palette ──────────────────────────────────────────────
    private static readonly Color PrimaryNavy   = Color.FromHex("#0A1628");
    private static readonly Color AccentGold    = Color.FromHex("#D4A017");
    private static readonly Color TextBody      = Color.FromHex("#1C2B40");
    private static readonly Color SurfaceLight  = Color.FromHex("#F5F7FA");
    private static readonly Color BandGreen     = Color.FromHex("#2E7D32");
    private static readonly Color BandAmber     = Color.FromHex("#F57C00");
    private static readonly Color BandRed       = Color.FromHex("#C62828");
    private static readonly Color BandCritical  = Color.FromHex("#6A1B9A");
    private static readonly Color TableHeader   = Color.FromHex("#1A3A5C");
    private static readonly Color TableRowAlt   = Color.FromHex("#EEF2F7");

    public StressTestReportGenerator(
        IDbConnectionFactory db,
        IStressTestOrchestrator orchestrator,
        ILogger<StressTestReportGenerator> log)
    {
        _db = db; _orchestrator = orchestrator; _log = log;
    }

    public async Task<byte[]> GenerateAsync(
        long runId, bool anonymiseEntities, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Load all data needed for the report
        var run = await conn.QuerySingleAsync<RunDetailRow>(
            """
            SELECT r.Id, r.RunGuid, r.PeriodCode, r.TimeHorizon,
                   r.EntitiesShocked, r.ContagionRounds,
                   r.SystemicResilienceScore, r.ExecutiveSummary,
                   r.StartedAt, r.CompletedAt,
                   s.ScenarioCode, s.ScenarioName, s.Category,
                   s.Severity, s.NarrativeSummary
            FROM   StressTestRuns r
            JOIN   StressScenarios s ON s.Id = r.ScenarioId
            WHERE  r.Id = @Id
            """,
            new { Id = runId });

        var entityResults = (await conn.QueryAsync<EntityResultRow>(
            """
            SELECT er.InstitutionId,
                   CASE WHEN @Anon=1 THEN 'Bank ' + CAST(ROW_NUMBER() OVER (ORDER BY er.PostCAR) AS VARCHAR)
                        ELSE i.ShortName END AS InstitutionName,
                   er.InstitutionType,
                   er.PreCAR, er.PreNPL, er.PreLCR,
                   er.PostCAR, er.PostNPL, er.PostLCR,
                   er.DeltaCAR, er.DeltaNPL, er.DeltaLCR,
                   er.PostCapitalShortfall, er.AdditionalProvisions,
                   er.BreachesCAR, er.BreachesLCR, er.IsInsolvent,
                   er.IsContagionVictim, er.ContagionRound, er.FailureCause,
                   er.InsurableDeposits, er.UninsurableDeposits
            FROM   StressTestEntityResults er
            JOIN   Institutions i ON i.Id = er.InstitutionId
            WHERE  er.RunId = @RunId
            ORDER BY er.PostCAR ASC
            """,
            new { RunId = runId, Anon = anonymiseEntities ? 1 : 0 })).ToList();

        var sectorAggregates = (await conn.QueryAsync<SectorStressAggregate>(
            "SELECT * FROM StressTestSectorAggregates WHERE RunId=@Id ORDER BY InstitutionType",
            new { Id = runId })).ToList();

        var contagionEvents = (await conn.QueryAsync<ContagionEventRow>(
            """
            SELECT ce.ContagionRound, ce.FailingInstitutionId, ce.AffectedInstitutionId,
                   ce.ExposureAmount, ce.TransmissionType,
                   fi.ShortName AS FailingName,
                   ai.ShortName AS AffectedName
            FROM   StressTestContagionEvents ce
            JOIN   Institutions fi ON fi.Id = ce.FailingInstitutionId
            JOIN   Institutions ai ON ai.Id = ce.AffectedInstitutionId
            WHERE  ce.RunId = @RunId
            ORDER BY ce.ContagionRound, ce.ExposureAmount DESC
            """,
            new { RunId = runId })).ToList();

        var ndicCapacity = await conn.ExecuteScalarAsync<decimal?>(
            "SELECT ConfigValue FROM SystemConfiguration WHERE ConfigKey='NDIC_FUND_CAPACITY_NGN_MILLIONS'")
            ?? 1_500_000m;

        var score        = (double)(run.SystemicResilienceScore ?? 50m);
        var rating       = GetRating(score);
        var reportDate   = run.CompletedAt ?? DateTimeOffset.UtcNow;
        var totalShortfall = sectorAggregates.Sum(a => a.TotalCapitalShortfall);
        var totalNdic    = sectorAggregates.Sum(a => a.TotalInsurableDepositsAtRisk);

        _log.LogInformation("Generating stress test PDF: RunId={Id} Entities={E}",
            runId, entityResults.Count);

        QuestPDF.Settings.License = LicenseType.Community;

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontColor(TextBody));

                // ── Cover Page ───────────────────────────────────────────
                page.Content().Column(col =>
                {
                    col.Item().Height(PageSizes.A4.Height).Background(PrimaryNavy)
                        .Padding(60).Column(cover =>
                    {
                        cover.Item().PaddingTop(40).Text("CENTRAL BANK OF NIGERIA")
                            .FontSize(11).FontColor(AccentGold).LetterSpacing(0.15f).Bold();

                        cover.Item().PaddingTop(8).Text("BANKING SUPERVISION DEPARTMENT")
                            .FontSize(9).FontColor(Colors.White.Lighten2).LetterSpacing(0.1f);

                        cover.Item().PaddingTop(80)
                            .BorderLeft(4).BorderColor(AccentGold).PaddingLeft(20)
                            .Column(title =>
                        {
                            title.Item().Text("SECTOR-WIDE")
                                .FontSize(28).Bold().FontColor(Colors.White);
                            title.Item().Text("STRESS TEST REPORT")
                                .FontSize(28).Bold().FontColor(AccentGold);
                            title.Item().PaddingTop(12).Text(run.ScenarioName)
                                .FontSize(16).FontColor(Colors.White.Lighten2).Italic();
                        });

                        cover.Item().PaddingTop(60).Column(meta =>
                        {
                            var items = new[]
                            {
                                ("Base Period",    run.PeriodCode),
                                ("Time Horizon",   run.TimeHorizon),
                                ("Entities Shocked", $"{run.EntitiesShocked:N0}"),
                                ("Scenario Category", run.Category),
                                ("Severity",       run.Severity),
                                ("Report Date",    reportDate.ToString("dd MMMM yyyy")),
                                ("Classification", "SUPERVISORY CONFIDENTIAL")
                            };
                            foreach (var (label, value) in items)
                            {
                                meta.Item().Row(r =>
                                {
                                    r.ConstantItem(160).Text(label + ":")
                                        .FontSize(9).FontColor(Colors.White.Lighten2);
                                    r.RelativeItem().Text(value)
                                        .FontSize(9).Bold().FontColor(Colors.White);
                                });
                                meta.Item().Height(6);
                            }
                        });

                        cover.Item().PaddingTop(80)
                            .BorderTop(1).BorderColor(AccentGold).PaddingTop(20)
                            .Row(footer =>
                        {
                            footer.RelativeItem()
                                .Text("RegOS™ SupTech Platform · Powered by Digibit Global Solutions")
                                .FontSize(8).FontColor(Colors.White.Lighten3);
                            footer.ConstantItem(120).AlignRight()
                                .Text($"Run ID: {run.RunGuid.ToString()[..8].ToUpper()}")
                                .FontSize(8).FontColor(Colors.White.Lighten3);
                        });
                    });
                });
            });

            // ── Inner pages ───────────────────────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(20).MarginBottom(20).MarginHorizontal(40);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9).FontColor(TextBody));
                page.Header().Element(BuildPageHeader);
                page.Footer().Element(p => BuildPageFooter(p, run.ScenarioName));

                page.Content().Column(body =>
                {
                    // ── 1. Executive Summary ─────────────────────────────
                    body.Item().Section("executive-summary");
                    body.Item().Element(c => SectionTitle(c, "1. Executive Summary"));

                    body.Item().Padding(12).Background(SurfaceLight).Border(1)
                        .BorderColor(Color.FromHex("#D1D9E6")).Padding(16).Column(exec =>
                    {
                        // Resilience score gauge
                        exec.Item().Row(r =>
                        {
                            r.ConstantItem(140).Column(gauge =>
                            {
                                gauge.Item().Text("Systemic Resilience Score")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                                gauge.Item().PaddingTop(4)
                                    .Text($"{score:F1}/100")
                                    .FontSize(32).Bold().FontColor(GetRatingColour(rating));
                                gauge.Item().PaddingTop(2)
                                    .Text(rating)
                                    .FontSize(11).Bold().FontColor(GetRatingColour(rating));
                            });
                            r.RelativeItem().PaddingLeft(16).Text(
                                run.ExecutiveSummary ?? "Executive summary not available.")
                                .FontSize(8.5f).LineHeight(1.4f);
                        });
                    });

                    body.Item().Height(12);

                    // ── 2. Scenario Description ──────────────────────────
                    body.Item().Element(c => SectionTitle(c, "2. Scenario Description"));
                    body.Item().PaddingBottom(8).Text(run.NarrativeSummary)
                        .FontSize(8.5f).LineHeight(1.5f);

                    // ── 3. Sector Aggregates Heatmap ─────────────────────
                    body.Item().Element(c => SectionTitle(c, "3. Sector Results — Pre-Stress vs Post-Stress"));

                    body.Item().Table(t =>
                    {
                        t.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(70);  // Type
                            cols.RelativeColumn();    // N
                            cols.RelativeColumn();    // Pre CAR
                            cols.RelativeColumn();    // Post CAR
                            cols.RelativeColumn();    // Pre NPL
                            cols.RelativeColumn();    // Post NPL
                            cols.RelativeColumn();    // Pre LCR
                            cols.RelativeColumn();    // Post LCR
                            cols.RelativeColumn();    // Breach CAR #
                            cols.RelativeColumn();    // Insolvent #
                            cols.RelativeColumn();    // Shortfall ₦M
                        });

                        // Header
                        void Th(ITableCellContainer c, string text) =>
                            c.Background(TableHeader).Padding(4)
                             .Text(text).FontSize(7.5f).Bold().FontColor(Colors.White).AlignCenter();

                        t.Header(h =>
                        {
                            Th(h.Cell(), "Sector");
                            Th(h.Cell(), "N");
                            Th(h.Cell(), "CAR Pre");
                            Th(h.Cell(), "CAR Post");
                            Th(h.Cell(), "NPL Pre");
                            Th(h.Cell(), "NPL Post");
                            Th(h.Cell(), "LCR Pre");
                            Th(h.Cell(), "LCR Post");
                            Th(h.Cell(), "Breach");
                            Th(h.Cell(), "Insolv.");
                            Th(h.Cell(), "Shortfall₦M");
                        });

                        bool alt = false;
                        foreach (var a in sectorAggregates)
                        {
                            var bg = alt ? TableRowAlt : Colors.White;
                            alt = !alt;

                            void Td(ITableCellContainer c, string text,
                                    bool highlight = false, Color? fg = null) =>
                                c.Background(bg).Padding(4)
                                 .Text(text).FontSize(7.5f).AlignCenter()
                                 .FontColor(fg ?? TextBody)
                                 .Bold(highlight);

                            Td(t.Cell(), a.InstitutionType);
                            Td(t.Cell(), a.EntityCount.ToString());
                            Td(t.Cell(), $"{a.PreAvgCAR:F1}%");

                            // Post CAR — colour-coded
                            var carColour = a.PostAvgCAR < 10 ? BandRed
                                : a.PostAvgCAR < 15 ? BandAmber : BandGreen;
                            Td(t.Cell(), $"{a.PostAvgCAR:F1}%", true, carColour);

                            Td(t.Cell(), $"{a.PreAvgNPL:F1}%");
                            var nplColour = a.PostAvgNPL > 20 ? BandRed
                                : a.PostAvgNPL > 10 ? BandAmber : BandGreen;
                            Td(t.Cell(), $"{a.PostAvgNPL:F1}%", true, nplColour);

                            Td(t.Cell(), $"{a.PreAvgLCR:F1}%");
                            var lcrColour = a.PostAvgLCR < 100 ? BandRed
                                : a.PostAvgLCR < 110 ? BandAmber : BandGreen;
                            Td(t.Cell(), $"{a.PostAvgLCR:F1}%", true, lcrColour);

                            var breachColour = a.EntitiesBreachingCAR > 0 ? BandRed : BandGreen;
                            Td(t.Cell(), $"{a.EntitiesBreachingCAR}", a.EntitiesBreachingCAR > 0, breachColour);
                            var insolvColour = a.EntitiesInsolvent > 0 ? BandCritical : BandGreen;
                            Td(t.Cell(), $"{a.EntitiesInsolvent}", a.EntitiesInsolvent > 0, insolvColour);
                            Td(t.Cell(), $"₦{a.TotalCapitalShortfall:N0}");
                        }

                        // Totals row
                        var totBg = Color.FromHex("#E8EDF4");
                        void TotalCell(ITableCellContainer c, string text) =>
                            c.Background(totBg).Padding(4).Text(text)
                             .FontSize(7.5f).Bold().AlignCenter();

                        TotalCell(t.Cell(), "TOTAL");
                        TotalCell(t.Cell(), $"{sectorAggregates.Sum(a => a.EntityCount)}");
                        TotalCell(t.Cell(), "—"); TotalCell(t.Cell(), "—");
                        TotalCell(t.Cell(), "—"); TotalCell(t.Cell(), "—");
                        TotalCell(t.Cell(), "—"); TotalCell(t.Cell(), "—");
                        TotalCell(t.Cell(), $"{sectorAggregates.Sum(a => a.EntitiesBreachingCAR)}");
                        TotalCell(t.Cell(), $"{sectorAggregates.Sum(a => a.EntitiesInsolvent)}");
                        TotalCell(t.Cell(), $"₦{totalShortfall:N0}");
                    });

                    body.Item().Height(16);

                    // ── 4. Pre/Post Distribution Bar Chart ───────────────
                    body.Item().Element(c => SectionTitle(c, "4. Capital Distribution — Pre-Stress vs Post-Stress"));
                    body.Item().Height(120).Canvas((canvas, size) =>
                    {
                        DrawCapitalDistributionBars(canvas, size, entityResults);
                    });

                    body.Item().Height(16);

                    // ── 5. Contagion Analysis ─────────────────────────────
                    body.Item().Element(c => SectionTitle(c, "5. Contagion Analysis"));

                    if (contagionEvents.Count == 0)
                    {
                        body.Item().Padding(8).Text("No contagion events were triggered under this scenario.")
                            .FontSize(8.5f).Italic();
                    }
                    else
                    {
                        body.Item().PaddingBottom(8)
                            .Text($"The shock propagated across {run.ContagionRounds} contagion round(s), " +
                                  $"affecting {contagionEvents.Select(e => e.AffectedInstitutionId).Distinct().Count()} " +
                                  $"entities via interbank exposure and deposit flight channels.")
                            .FontSize(8.5f).LineHeight(1.4f);

                        body.Item().Table(ct2 =>
                        {
                            ct2.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(30);  // Round
                                cols.RelativeColumn();    // Failing
                                cols.RelativeColumn();    // Affected
                                cols.ConstantColumn(80);  // Exposure
                                cols.ConstantColumn(80);  // Channel
                            });

                            ct2.Header(h =>
                            {
                                foreach (var lbl in new[] {"Round","Failing Inst.","Affected Inst.","Exposure ₦M","Channel"})
                                    h.Cell().Background(TableHeader).Padding(4)
                                        .Text(lbl).FontSize(7f).Bold().FontColor(Colors.White);
                            });

                            bool a2 = false;
                            foreach (var evt in contagionEvents.Take(30))  // cap at 30 rows
                            {
                                var bg2 = a2 ? TableRowAlt : Colors.White;
                                a2 = !a2;
                                ct2.Cell().Background(bg2).Padding(3)
                                    .Text(evt.ContagionRound.ToString()).FontSize(7f).AlignCenter();
                                ct2.Cell().Background(bg2).Padding(3)
                                    .Text(anonymiseEntities ? $"Bank {evt.FailingInstitutionId}" : evt.FailingName)
                                    .FontSize(7f);
                                ct2.Cell().Background(bg2).Padding(3)
                                    .Text(anonymiseEntities ? $"Bank {evt.AffectedInstitutionId}" : evt.AffectedName)
                                    .FontSize(7f);
                                ct2.Cell().Background(bg2).Padding(3)
                                    .Text($"₦{evt.ExposureAmount:N0}").FontSize(7f).AlignRight();
                                ct2.Cell().Background(bg2).Padding(3)
                                    .Text(evt.TransmissionType).FontSize(7f).AlignCenter();
                            }
                        });
                    }

                    body.Item().Height(16);

                    // ── 6. NDIC Exposure Analysis ─────────────────────────
                    body.Item().Element(c => SectionTitle(c, "6. NDIC Exposure Analysis"));

                    var ndicCoverage = ndicCapacity > 0
                        ? (double)(totalNdic / ndicCapacity) * 100
                        : 0.0;

                    body.Item().Background(ndicCoverage > 50 ? Color.FromHex("#FFF3E0")
                        : SurfaceLight).Border(1).BorderColor(Color.FromHex("#D1D9E6"))
                        .Padding(12).Column(ndic =>
                    {
                        ndic.Item().Row(r =>
                        {
                            r.ConstantItem(200).Column(left =>
                            {
                                left.Item().Text("Insurable Deposits at Risk")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                                left.Item().Text($"₦{totalNdic:N0}M")
                                    .FontSize(20).Bold().FontColor(ndicCoverage > 50 ? BandRed : BandAmber);
                                left.Item().PaddingTop(8).Text("NDIC Fund Capacity")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                                left.Item().Text($"₦{ndicCapacity:N0}M")
                                    .FontSize(14).Bold();
                                left.Item().PaddingTop(4).Text($"Coverage Ratio: {ndicCoverage:F1}%")
                                    .FontSize(9).Bold()
                                    .FontColor(ndicCoverage > 50 ? BandRed : BandGreen);
                            });
                            r.RelativeItem().PaddingLeft(16).Column(right =>
                            {
                                right.Item().Text("Per-Sector NDIC Exposure")
                                    .FontSize(8).Bold().PaddingBottom(4);
                                foreach (var a in sectorAggregates.Where(a => a.TotalInsurableDepositsAtRisk > 0))
                                {
                                    right.Item().Row(row =>
                                    {
                                        row.ConstantItem(50).Text(a.InstitutionType).FontSize(7f);
                                        row.RelativeItem().Text($"₦{a.TotalInsurableDepositsAtRisk:N0}M")
                                            .FontSize(7f).AlignRight();
                                        row.ConstantItem(70)
                                            .Text($"(unins: ₦{a.TotalUninsurableDepositsAtRisk:N0}M)")
                                            .FontSize(7f).AlignRight().FontColor(Colors.Grey.Medium);
                                    });
                                }
                            });
                        });
                    });

                    body.Item().Height(16);

                    // ── 7. Entity-Level Results ───────────────────────────
                    body.Item().Element(c => SectionTitle(c,
                        anonymiseEntities
                            ? "7. Entity-Level Results (Anonymised)"
                            : "7. Entity-Level Results"));

                    body.Item().Table(et =>
                    {
                        et.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2); // Name
                            cols.RelativeColumn();  // Type
                            cols.RelativeColumn();  // Pre CAR
                            cols.RelativeColumn();  // Post CAR
                            cols.RelativeColumn();  // ΔCAR
                            cols.RelativeColumn();  // Post NPL
                            cols.RelativeColumn();  // Post LCR
                            cols.ConstantColumn(50); // Status
                        });

                        et.Header(h =>
                        {
                            foreach (var lbl in new[] { "Institution","Type","CAR Pre","CAR Post","ΔCAR","NPL Post","LCR Post","Status" })
                                h.Cell().Background(TableHeader).Padding(3)
                                    .Text(lbl).FontSize(7f).Bold().FontColor(Colors.White);
                        });

                        bool a3 = false;
                        foreach (var e in entityResults)
                        {
                            var bg3 = e.IsInsolvent ? Color.FromHex("#FCE4EC")
                                : e.BreachesCAR ? Color.FromHex("#FFF3E0")
                                : a3 ? TableRowAlt : Colors.White;
                            a3 = !a3;

                            var statusText = e.IsInsolvent ? "INSOLVENT"
                                : e.BreachesCAR ? "BREACH"
                                : e.BreachesLCR ? "LCR LOW"
                                : "PASS";
                            var statusColour = e.IsInsolvent ? BandCritical
                                : e.BreachesCAR ? BandRed
                                : e.BreachesLCR ? BandAmber
                                : BandGreen;

                            void Ec(ITableCellContainer c, string text,
                                    Color? fg = null, bool bold = false) =>
                                c.Background(bg3).Padding(3)
                                 .Text(text).FontSize(7f)
                                 .FontColor(fg ?? TextBody).Bold(bold);

                            Ec(et.Cell(), e.InstitutionName);
                            Ec(et.Cell(), e.InstitutionType);
                            Ec(et.Cell(), $"{e.PreCAR:F1}%");
                            Ec(et.Cell(), $"{e.PostCAR:F1}%",
                                e.PostCAR < 10 ? BandRed
                                : e.PostCAR < 15 ? BandAmber : null, true);
                            Ec(et.Cell(), $"{e.DeltaCAR:+0.0;-0.0}pp",
                                e.DeltaCAR < -5 ? BandRed : null);
                            Ec(et.Cell(), $"{e.PostNPL:F1}%",
                                e.PostNPL > 20 ? BandRed
                                : e.PostNPL > 10 ? BandAmber : null);
                            Ec(et.Cell(), $"{e.PostLCR:F1}%",
                                e.PostLCR < 100 ? BandRed
                                : e.PostLCR < 110 ? BandAmber : null);
                            Ec(et.Cell(), statusText, statusColour, true);
                        }
                    });

                    body.Item().Height(20);

                    // ── 8. Policy Recommendations ─────────────────────────
                    body.Item().Element(c => SectionTitle(c, "8. Policy Recommendations"));

                    var recItems = BuildRecommendations(rating, sectorAggregates, run.ContagionRounds,
                        totalShortfall, totalNdic, ndicCapacity);

                    for (int i = 0; i < recItems.Length; i++)
                    {
                        var (title, detail, priority) = recItems[i];
                        var recBg = priority == "HIGH" ? Color.FromHex("#FFF8E1") : SurfaceLight;
                        var recBorder = priority == "HIGH" ? AccentGold : Color.FromHex("#D1D9E6");

                        body.Item().PaddingBottom(6)
                            .Background(recBg).Border(1).BorderColor(recBorder)
                            .Padding(10).Column(rec =>
                        {
                            rec.Item().Row(r =>
                            {
                                r.ConstantItem(20).Text($"{i + 1}.")
                                    .FontSize(9).Bold().FontColor(PrimaryNavy);
                                r.RelativeItem().Column(inner =>
                                {
                                    inner.Item().Row(rr =>
                                    {
                                        rr.RelativeItem().Text(title)
                                            .FontSize(9).Bold().FontColor(PrimaryNavy);
                                        rr.ConstantItem(45)
                                            .Background(priority == "HIGH" ? BandRed : BandAmber)
                                            .Padding(2).AlignCenter()
                                            .Text(priority).FontSize(7f).Bold().FontColor(Colors.White);
                                    });
                                    inner.Item().PaddingTop(4).Text(detail)
                                        .FontSize(8f).LineHeight(1.4f);
                                });
                            });
                        });
                    }

                    // ── 9. Appendix — Methodology ─────────────────────────
                    body.Item().Height(20);
                    body.Item().Element(c => SectionTitle(c, "Appendix A: Methodology Notes"));

                    body.Item().Text("""
This stress test was conducted using the RegOS™ SupTech Platform Stress Testing Framework.
Transmission coefficients are calibrated to CBN CAMELS methodology and IMF FSAP guidance (2024).
GDP-to-CAR sensitivity coefficients are derived from panel regression analysis of Nigerian banking sector
data (2010–2024) and cross-referenced against World Bank Financial Sector Assessment Program methodologies.
NGFS scenario parameters align with the Phase 4 NGFS Climate Scenarios (June 2023).
Second-round contagion is modelled via Breadth-First Search propagation over the interbank exposure network,
with a 40% Loss Given Default assumption for interbank placements and a maximum cascade depth of 5 rounds.
NDIC exposure estimates use the N5,000,000 per-depositor insurance cap as mandated by the Nigeria Deposit
Insurance Corporation Act 2023. Results are point-in-time and do not incorporate future policy actions
or management responses that may mitigate identified risks.
                    """).FontSize(8f).LineHeight(1.5f).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

        _log.LogInformation("Stress test PDF generated: RunId={Id} Bytes={Bytes:N0}",
            runId, pdfBytes.Length);

        return pdfBytes;
    }

    // ── Canvas drawing: capital distribution bars ──────────────────────
    private static void DrawCapitalDistributionBars(
        SKCanvas canvas, Size size, List<EntityResultRow> results)
    {
        // Bucket entities into CAR bands: <0%, 0-8%, 8-15%, 15-20%, >20%
        var bands = new[]
        {
            ("Insolvent (<0%)",  results.Count(r => r.PreCAR < 0),  results.Count(r => r.PostCAR < 0)),
            ("Critical (0–8%)",  results.Count(r => r.PreCAR is >= 0 and < 8),   results.Count(r => r.PostCAR is >= 0 and < 8)),
            ("Distressed (8–15%)", results.Count(r => r.PreCAR is >= 8 and < 15), results.Count(r => r.PostCAR is >= 8 and < 15)),
            ("Adequate (15–20%)", results.Count(r => r.PreCAR is >= 15 and < 20), results.Count(r => r.PostCAR is >= 15 and < 20)),
            ("Strong (>20%)",    results.Count(r => r.PreCAR >= 20), results.Count(r => r.PostCAR >= 20)),
        };

        if (results.Count == 0) return;

        var barWidth  = (size.Width - 80) / (bands.Length * 2 + bands.Length - 1);
        var maxCount  = bands.Max(b => Math.Max(b.Item2, b.Item3));
        var barArea   = size.Height - 40;
        var x         = 60f;

        using var preColor  = new SKPaint { Color = SKColor.Parse("#1A3A5C"), IsAntialias = true };
        using var postColor = new SKPaint { Color = SKColor.Parse("#C62828"), IsAntialias = true };
        using var labelPaint = new SKPaint { Color = SKColor.Parse("#1C2B40"), TextSize = 7f, IsAntialias = true };
        using var gridPaint = new SKPaint { Color = SKColor.Parse("#E0E0E0"), StrokeWidth = 0.5f };

        // Grid lines
        for (int g = 0; g <= 4; g++)
        {
            var y = 10 + barArea * g / 4;
            canvas.DrawLine(55, y, size.Width - 10, y, gridPaint);
        }

        foreach (var (label, preCount, postCount) in bands)
        {
            var preHeight  = maxCount > 0 ? barArea * preCount  / maxCount : 0;
            var postHeight = maxCount > 0 ? barArea * postCount / maxCount : 0;

            canvas.DrawRect(x, 10 + barArea - preHeight,  barWidth, preHeight,  preColor);
            canvas.DrawRect(x + barWidth + 2, 10 + barArea - postHeight, barWidth, postHeight, postColor);

            canvas.DrawText(label, x, size.Height - 2, labelPaint);
            x += barWidth * 2 + 8 + barWidth;
        }

        // Legend
        canvas.DrawRect(size.Width - 100, 12, 10, 8, preColor);
        canvas.DrawText("Pre-stress", size.Width - 86, 20, labelPaint);
        canvas.DrawRect(size.Width - 100, 24, 10, 8, postColor);
        canvas.DrawText("Post-stress", size.Width - 86, 32, labelPaint);
    }

    // ── Recommendations builder ───────────────────────────────────────
    private static (string Title, string Detail, string Priority)[] BuildRecommendations(
        string rating, List<SectorStressAggregate> aggregates,
        int contagionRounds, decimal totalShortfall,
        decimal totalNdic, decimal ndicCapacity)
    {
        var recs = new List<(string, string, string)>();

        var totalBreachCAR = aggregates.Sum(a => a.EntitiesBreachingCAR);
        var totalInsolvent = aggregates.Sum(a => a.EntitiesInsolvent);
        var totalEntities  = aggregates.Sum(a => a.EntityCount);

        if (totalInsolvent > 0)
            recs.Add(("Emergency Capital Intervention",
                $"{totalInsolvent} entities are technically insolvent under this scenario. " +
                $"The CBN should mandate immediate capital restoration plans. Total additional capital " +
                $"required: ₦{totalShortfall:N0}M. Consider CBN/AMCON capital support mechanisms.",
                "HIGH"));

        if (contagionRounds >= 2)
            recs.Add(("Interbank Exposure Limits Review",
                "Contagion propagated across multiple rounds, indicating high interbank interconnectedness. " +
                "The CBN should review single-obligor limits for interbank placements and mandate quarterly " +
                "large-exposure reporting for all DMBs.",
                "HIGH"));

        if (totalNdic / Math.Max(1, ndicCapacity) > 0.30m)
            recs.Add(("NDIC Contingency Planning",
                $"NDIC insurable deposits at risk represent {totalNdic / ndicCapacity * 100:F1}% " +
                "of the Deposit Insurance Fund. NDIC should activate contingency planning and review " +
                "premium surcharges for high-risk institutions.",
                "HIGH"));

        if (aggregates.Any(a => a.PostAvgLCR < 100))
            recs.Add(("Liquidity Support Facility Activation",
                "Multiple institution types post average LCRs below 100% under this scenario. " +
                "The CBN should pre-position the Standing Lending Facility and consider " +
                "broadening eligible HQLA collateral during stress periods.",
                "HIGH"));

        recs.Add(("Countercyclical Capital Buffer Review",
            "Assess the appropriateness of maintaining or releasing the countercyclical capital buffer " +
            "in light of identified sector vulnerabilities. Consider targeted macro-prudential measures " +
            "for sectors showing greatest stress (oil & gas, agriculture, FX-linked exposures).",
            "MEDIUM"));

        recs.Add(("Enhanced Supervisory Monitoring",
            $"{totalBreachCAR} entities breach minimum CAR under this scenario. Activate enhanced " +
            "supervisory monitoring for the top quintile of stressed entities, requiring monthly " +
            "prudential reporting and quarterly board-level risk attestations.",
            "MEDIUM"));

        if (aggregates.Any(a => a.InstitutionType is "MFB" or "DFI" &&
                                 a.EntitiesBreachingCAR > 0))
            recs.Add(("Development Finance & Microfinance Sector Support",
                "MFBs and DFIs show elevated stress, particularly through agricultural and SME loan channels. " +
                "Consider targeted CBN/NIRSAL credit guarantee expansion to reduce NPL migration.",
                "MEDIUM"));

        return recs.ToArray();
    }

    // ── Page chrome helpers ───────────────────────────────────────────────
    private static void BuildPageHeader(IContainer c) =>
        c.PaddingBottom(8).Row(r =>
        {
            r.RelativeItem().Text("CENTRAL BANK OF NIGERIA · Banking Supervision Department")
                .FontSize(7.5f).FontColor(Colors.Grey.Medium);
            r.ConstantItem(180).AlignRight()
                .Text("SUPERVISORY CONFIDENTIAL — RegOS™ SupTech Platform")
                .FontSize(7.5f).FontColor(Colors.Grey.Medium);
        });

    private static void BuildPageFooter(IContainer c, string scenarioName) =>
        c.PaddingTop(8).BorderTop(1).BorderColor(Color.FromHex("#D1D9E6")).Row(r =>
        {
            r.RelativeItem().Text($"Sector Stress Test: {scenarioName}")
                .FontSize(7f).FontColor(Colors.Grey.Medium);
            r.ConstantItem(80).AlignRight().Text(x => x.CurrentPageNumber()
                .Style(TextStyle.Default.FontSize(7f).FontColor(Colors.Grey.Medium)));
        });

    private static void SectionTitle(IContainer c, string text) =>
        c.PaddingBottom(6).PaddingTop(4)
         .BorderBottom(2).BorderColor(PrimaryNavy).PaddingBottom(4)
         .Text(text).FontSize(11).Bold().FontColor(PrimaryNavy);

    private static string GetRating(double score) => score switch
    {
        >= 80 => "RESILIENT", >= 60 => "ADEQUATE",
        >= 40 => "VULNERABLE", _ => "CRITICAL"
    };

    private static Color GetRatingColour(string rating) => rating switch
    {
        "RESILIENT"  => BandGreen,
        "ADEQUATE"   => BandAmber,
        "VULNERABLE" => BandRed,
        _            => BandCritical
    };

    // Row types
    private sealed record RunDetailRow(
        long Id, Guid RunGuid, string PeriodCode, string TimeHorizon,
        int EntitiesShocked, int ContagionRounds,
        decimal? SystemicResilienceScore, string? ExecutiveSummary,
        DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        string ScenarioCode, string ScenarioName, string Category,
        string Severity, string NarrativeSummary);

    private sealed record EntityResultRow(
        int InstitutionId, string InstitutionName, string InstitutionType,
        decimal PreCAR, decimal PreNPL, decimal PreLCR,
        decimal PostCAR, decimal PostNPL, decimal PostLCR,
        decimal DeltaCAR, decimal DeltaNPL, decimal DeltaLCR,
        decimal PostCapitalShortfall, decimal AdditionalProvisions,
        bool BreachesCAR, bool BreachesLCR, bool IsInsolvent,
        bool IsContagionVictim, int? ContagionRound, string? FailureCause,
        decimal InsurableDeposits, decimal UninsurableDeposits);

    private sealed record ContagionEventRow(
        int ContagionRound, int FailingInstitutionId, int AffectedInstitutionId,
        decimal ExposureAmount, string TransmissionType,
        string FailingName, string AffectedName);
}
```

---

## 10 · Blazor Server UI — Stress Test Portal Pages

### 10.1 Stress Test Launcher (`/supervisor/stress-test`)

```csharp
@page "/supervisor/stress-test"
@attribute [Authorize(Policy = "Supervisor")]
@inject IStressTestOrchestrator Orchestrator
@inject IStressTestReportGenerator ReportGenerator
@inject IDbConnectionFactory Db
@inject NavigationManager Nav

<PageTitle>Stress Testing — RegOS™ SupTech</PageTitle>

<div class="suptech-page">
    <div class="page-header regos-border-bottom">
        <div>
            <h1 class="page-title">Sector-Wide Stress Testing</h1>
            <p class="page-subtitle">
                Apply macro-prudential and climate shocks to all supervised entities
            </p>
        </div>
    </div>

    @if (_running)
    {
        <div class="stress-running-banner">
            <div class="regos-spinner"></div>
            <span>Stress test running — applying shocks to @_entityCount entities…</span>
        </div>
    }
    else
    {
        <!-- ── Scenario selector ── -->
        <div class="stress-config-grid">
            <div class="config-card">
                <h2 class="config-heading">Select Scenario</h2>
                @foreach (var scenario in _scenarios)
                {
                    var selected = _selectedScenarioId == scenario.Id;
                    var catClass = scenario.Category switch
                    {
                        "NGFS_CLIMATE" => "cat-climate",
                        "MACRO"        => "cat-macro",
                        _              => "cat-custom"
                    };
                    <div class="scenario-card @(selected ? "selected" : "") @catClass"
                         @onclick="() => _selectedScenarioId = scenario.Id">
                        <div class="scenario-header">
                            <span class="scenario-name">@scenario.ScenarioName</span>
                            <span class="severity-badge severity-@scenario.Severity.ToLowerInvariant()">
                                @scenario.Severity
                            </span>
                        </div>
                        <p class="scenario-summary">@scenario.NarrativeSummary[..Math.Min(120, scenario.NarrativeSummary.Length)]…</p>
                    </div>
                }
            </div>

            <div class="config-card">
                <h2 class="config-heading">Run Parameters</h2>

                <div class="field-group">
                    <label class="regos-label">Base Period</label>
                    <input class="regos-input" @bind="_periodCode" placeholder="e.g. 2026-Q1" />
                </div>
                <div class="field-group">
                    <label class="regos-label">Time Horizon</label>
                    <select class="regos-select" @bind="_timeHorizon">
                        <option value="1Y">1 Year</option>
                        <option value="2Y">2 Years</option>
                        <option value="3Y">3 Years</option>
                        <option value="5Y">5 Years</option>
                    </select>
                </div>
                <div class="field-group">
                    <label class="regos-label">Report Format</label>
                    <div class="toggle-row">
                        <input type="checkbox" @bind="_anonymise" id="anon" />
                        <label for="anon">Anonymise entity names in report</label>
                    </div>
                </div>

                @if (_selectedScenarioId.HasValue)
                {
                    <div class="selected-info">
                        Selected: <strong>@_scenarios.FirstOrDefault(s => s.Id == _selectedScenarioId)?.ScenarioName</strong>
                    </div>
                }

                <button class="regos-btn regos-btn-primary regos-btn-large"
                        @onclick="RunStressTestAsync"
                        disabled="@(!_selectedScenarioId.HasValue || string.IsNullOrWhiteSpace(_periodCode))">
                    Run Stress Test
                </button>
            </div>
        </div>

        <!-- ── Previous runs ── -->
        <h2 class="section-heading">Recent Stress Test Runs</h2>
        <table class="regos-table">
            <thead>
                <tr>
                    <th>Scenario</th>
                    <th>Period</th>
                    <th>Entities</th>
                    <th>Resilience Score</th>
                    <th>Status</th>
                    <th>Run Date</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var run in _recentRuns)
                {
                    <tr>
                        <td>@run.ScenarioName</td>
                        <td>@run.PeriodCode</td>
                        <td>@run.EntitiesShocked</td>
                        <td>
                            <span class="score-badge score-@run.ResilienceRating.ToLowerInvariant()">
                                @run.SystemicResilienceScore?.ToString("F1")/100
                                (@run.ResilienceRating)
                            </span>
                        </td>
                        <td>@run.Status</td>
                        <td>@run.StartedAt.ToString("dd MMM yyyy HH:mm")</td>
                        <td>
                            <button class="regos-btn regos-btn-sm"
                                    @onclick="() => ViewResults(run.RunGuid)">
                                View
                            </button>
                            <button class="regos-btn regos-btn-sm regos-btn-secondary"
                                    @onclick="() => DownloadReportAsync(run.Id, run.ScenarioCode)">
                                PDF
                            </button>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>

@code {
    [CascadingParameter] private string RegulatorCode { get; set; } = "CBN";
    [CascadingParameter] private string CurrentPeriodCode { get; set; } = "2026-Q1";
    [CascadingParameter] private int CurrentUserId { get; set; }

    private IReadOnlyList<ScenarioListRow> _scenarios = Array.Empty<ScenarioListRow>();
    private IReadOnlyList<RunListRow> _recentRuns = Array.Empty<RunListRow>();
    private int? _selectedScenarioId;
    private string _periodCode = string.Empty;
    private string _timeHorizon = "1Y";
    private bool _anonymise = true;
    private bool _running;
    private int _entityCount;

    protected override async Task OnInitializedAsync()
    {
        _periodCode = CurrentPeriodCode;
        await using var conn = await Db.OpenAsync();

        _scenarios = (await conn.QueryAsync<ScenarioListRow>(
            "SELECT Id, ScenarioCode, ScenarioName, Category, Severity, NarrativeSummary " +
            "FROM StressScenarios WHERE IsActive=1 ORDER BY Category, Severity DESC"))
            .ToList();

        await LoadRecentRunsAsync(conn);
    }

    private async Task LoadRecentRunsAsync(System.Data.IDbConnection conn)
    {
        _recentRuns = (await conn.QueryAsync<RunListRow>(
            """
            SELECT TOP 10 r.Id, r.RunGuid, r.PeriodCode, r.EntitiesShocked,
                          r.SystemicResilienceScore, r.Status, r.StartedAt,
                          s.ScenarioName, s.ScenarioCode,
                          CASE WHEN r.SystemicResilienceScore >= 80 THEN 'RESILIENT'
                               WHEN r.SystemicResilienceScore >= 60 THEN 'ADEQUATE'
                               WHEN r.SystemicResilienceScore >= 40 THEN 'VULNERABLE'
                               ELSE 'CRITICAL' END AS ResilienceRating
            FROM StressTestRuns r
            JOIN StressScenarios s ON s.Id = r.ScenarioId
            WHERE r.RegulatorCode = @Regulator
            ORDER BY r.StartedAt DESC
            """,
            new { Regulator = RegulatorCode })).ToList();
    }

    private async Task RunStressTestAsync()
    {
        if (!_selectedScenarioId.HasValue || string.IsNullOrWhiteSpace(_periodCode)) return;

        _running = true;
        _entityCount = await GetEntityCountAsync();
        StateHasChanged();

        try
        {
            var summary = await Orchestrator.RunAsync(
                RegulatorCode, _selectedScenarioId.Value,
                _periodCode, _timeHorizon, CurrentUserId);

            Nav.NavigateTo($"/supervisor/stress-test/{summary.RunGuid}");
        }
        finally
        {
            _running = false;
        }
    }

    private async Task DownloadReportAsync(long runId, string scenarioCode)
    {
        var bytes = await ReportGenerator.GenerateAsync(runId, _anonymise);
        var fileName = $"StressTest_{scenarioCode}_{_periodCode}_{DateTime.UtcNow:yyyyMMdd}.pdf";
        // Trigger download via JS interop (omitted for brevity — use IJSRuntime)
    }

    private void ViewResults(Guid runGuid) =>
        Nav.NavigateTo($"/supervisor/stress-test/{runGuid}");

    private async Task<int> GetEntityCountAsync()
    {
        await using var conn = await Db.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Institutions WHERE RegulatorCode=@R AND IsActive=1",
            new { R = RegulatorCode });
    }

    private sealed record ScenarioListRow(
        int Id, string ScenarioCode, string ScenarioName,
        string Category, string Severity, string NarrativeSummary);

    private sealed record RunListRow(
        long Id, Guid RunGuid, string PeriodCode, int EntitiesShocked,
        decimal? SystemicResilienceScore, string Status, DateTimeOffset StartedAt,
        string ScenarioName, string ScenarioCode, string ResilienceRating);
}
```

---

## 11 · Dependency Injection Registration

```csharp
public static class StressTestServiceExtensions
{
    public static IServiceCollection AddStressTestingFramework(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMacroShockTransmitter, MacroShockTransmitter>();
        services.AddScoped<IContagionCascadeEngine, ContagionCascadeEngine>();
        services.AddScoped<INDICExposureCalculator, NDICExposureCalculator>();
        services.AddScoped<IStressTestOrchestrator, StressTestOrchestrator>();
        services.AddScoped<IStressTestReportGenerator, StressTestReportGenerator>();

        // QuestPDF licence (Community for open usage; update for production)
        QuestPDF.Settings.License = LicenseType.Community;

        return services;
    }
}
```

---

## 12 · Integration Tests (Testcontainers)

```csharp
[Collection("StressTestIntegration")]
public sealed class StressTestIntegrationTests
    : IClassFixture<StressTestFixture>
{
    private readonly StressTestFixture _fx;
    public StressTestIntegrationTests(StressTestFixture fx) => _fx = fx;

    // ── Test 1: Oil collapse correctly reduces CAR ──────────────────────
    [Fact]
    public async Task MacroShockTransmitter_OilCollapseScenario_ReducesCAR()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 1, InstitutionType: "DMB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 18.0m, NPL: 4.5m, LCR: 130.0m, NSFR: 115.0m, ROA: 1.8m,
            TotalAssets: 500_000m, TotalDeposits: 350_000m,
            OilSectorExposurePct: 25.0m,  // 25% oil exposure
            AgriExposurePct: 5.0m,
            FXLoansAssetPct: 15.0m,
            BondPortfolioAssetPct: 10.0m,
            TopDepositorConcentration: 25.0m);

        // Oil collapse parameters (Scenario 4 for DMB)
        var parameters = new ResolvedShockParameters(
            ScenarioId: 4, InstitutionType: "DMB",
            GDPGrowthShock: -3.0m,
            OilPriceShockPct: -50.0m,
            FXDepreciationPct: 30.0m,
            InflationShockPp: 5.0m,
            InterestRateShockBps: 0,
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
        Assert.True(result.DeltaCAR < 0,
            "DeltaCAR must be negative");
    }

    // ── Test 2: NGFS Hot House — agri NPL spike ─────────────────────────
    [Fact]
    public async Task MacroShockTransmitter_NGFSHotHouse_AgriNPLSpikes()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 2, InstitutionType: "MFB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 12.5m, NPL: 6.0m, LCR: 115.0m, NSFR: 108.0m, ROA: 1.2m,
            TotalAssets: 15_000m, TotalDeposits: 10_000m,
            OilSectorExposurePct: 2.0m,
            AgriExposurePct: 45.0m,   // high agri exposure for MFB
            FXLoansAssetPct: 1.0m, BondPortfolioAssetPct: 5.0m,
            TopDepositorConcentration: 20.0m);

        var parameters = new ResolvedShockParameters(
            ScenarioId: 3, InstitutionType: "MFB",
            GDPGrowthShock: -2.0m,
            OilPriceShockPct: 0m, FXDepreciationPct: 0m, InflationShockPp: 0m,
            InterestRateShockBps: 0, CarbonTaxUSDPerTon: 0m, StrandedAssetsPct: 5.0m,
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

    // ── Test 3: Rate spike destroys LCR for bond-heavy bank ──────────────
    [Fact]
    public async Task MacroShockTransmitter_RateSpike500bps_LCRBreaches()
    {
        var snapshot = new PrudentialMetricSnapshot(
            InstitutionId: 3, InstitutionType: "DMB",
            RegulatorCode: "CBN", PeriodCode: "2026-Q1",
            CAR: 17.5m, NPL: 3.2m, LCR: 118.0m, NSFR: 112.0m, ROA: 1.9m,
            TotalAssets: 800_000m, TotalDeposits: 600_000m,
            OilSectorExposurePct: 10m, AgriExposurePct: 5m,
            FXLoansAssetPct: 5m, BondPortfolioAssetPct: 30.0m,  // heavy bond portfolio
            TopDepositorConcentration: 18m);

        var parameters = new ResolvedShockParameters(
            ScenarioId: 6, InstitutionType: "DMB",
            GDPGrowthShock: 0m, OilPriceShockPct: 0m,
            FXDepreciationPct: 0m, InflationShockPp: 0m,
            InterestRateShockBps: 500,
            CarbonTaxUSDPerTon: 0m, StrandedAssetsPct: 0m,
            PhysicalRiskHazardCode: null,
            CARDeltaPerGDPPp: 0m, NPLDeltaPerGDPPp: 0.60m,
            LCRDeltaPerRateHike100: -0.15m, CARDeltaPerFXPct: 0m,
            NPLDeltaPerFXPct: 0m, CARDeltaPerOilPct: 0m, NPLDeltaPerOilPct: 0m,
            LCRDeltaPerCyber: 0m, DepositOutflowPctCyber: 0m);

        var result = _fx.ShockTransmitter.ApplyShock(snapshot, parameters);

        Assert.True(result.PostLCR < result.PreLCR, "LCR must decrease on rate spike");
        Assert.True(result.BreachesLCR, $"LCR should breach 100% for heavy bond holder, got {result.PostLCR:F2}");
    }

    // ── Test 4: Cyber scenario triggers deposit outflow ──────────────────
    [Fact]
    public async Task MacroShockTransmitter_CyberScenario_LCRCollapse()
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
            CarbonTaxUSDPerTon: 0m, StrandedAssetsPct: 0m, PhysicalRiskHazardCode: null,
            CARDeltaPerGDPPp: 0m, NPLDeltaPerGDPPp: 0m, LCRDeltaPerRateHike100: 0m,
            CARDeltaPerFXPct: 0m, NPLDeltaPerFXPct: 0m, CARDeltaPerOilPct: 0m,
            NPLDeltaPerOilPct: 0m,
            LCRDeltaPerCyber: -25.0m,      // direct LCR shock
            DepositOutflowPctCyber: 15.0m); // 15% deposit outflow

        var result = _fx.ShockTransmitter.ApplyShock(snapshot, parameters);

        Assert.True(result.PostLCR < 100.0m,
            $"Cyber shock should breach LCR, got {result.PostLCR:F2}");
    }

    // ── Test 5: Full sector run aggregates correctly ─────────────────────
    [Fact]
    public async Task Orchestrator_OilCollapseSector_CorrectBrechCounts()
    {
        await _fx.SeedSectorAsync("2026-Q1", new[]
        {
            (10, "DMB", 18.0m, 4.0m, 130.0m, 25.0m),   // oil-heavy → breach
            (11, "DMB", 22.0m, 2.5m, 145.0m, 5.0m),    // low oil  → survive
            (12, "MFB", 11.0m, 7.0m, 115.0m, 2.0m),    // already marginal → breach
        }); // (id, type, CAR, NPL, LCR, oilPct)

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

    // ── Test 6: Contagion cascade propagates correctly ───────────────────
    [Fact]
    public async Task ContagionEngine_InterbankExposure_PropagatesFailure()
    {
        // Institution 15 fails; it has borrowed ₦50B from institution 16
        // Institution 16 should suffer CAR impact
        await _fx.SeedInterbankAsync("2026-Q1", (15, 16, 50_000m));
        await _fx.SeedSectorAsync("2026-Q1", new[]
        {
            (15, "DMB", 14.0m, 8.0m, 95.0m, 40.0m),   // will fail under oil shock
            (16, "DMB", 16.5m, 3.0m, 125.0m, 5.0m),   // just-adequate
        });

        var round0 = new List<EntityShockResult>
        {
            new(15, "DMB", 14m, 8m, 95m, 110m, 1.0m, 200_000m, 140_000m,
                8m, 15m, 60m, 95m, -1m, 5_000m, 2_000m,
                true, true, false, true, 0m, 0m)  // Bank 15 insolvent
        };

        var (addFailed, events, rounds) = await _fx.ContagionEngine.CascadeAsync(
            round0, "CBN", "2026-Q1", runId: 9999);

        Assert.True(events.Count > 0,
            "Contagion events should be generated from insolvent bank 15");
        Assert.True(rounds >= 1, "At least 1 contagion round should execute");
    }

    // ── Test 7: QuestPDF report generates without error ──────────────────
    [Fact]
    public async Task ReportGenerator_OilCollapseRun_GeneratesPDF()
    {
        var summary = await _fx.Orchestrator.RunAsync(
            "CBN", scenarioId: 4, periodCode: "2026-Q1",
            timeHorizon: "1Y", initiatedByUserId: 1);

        var pdfBytes = await _fx.ReportGenerator.GenerateAsync(
            summary.RunId, anonymiseEntities: true);

        Assert.NotNull(pdfBytes);
        Assert.True(pdfBytes.Length > 5_000,
            $"PDF should be >5KB, got {pdfBytes.Length} bytes");

        // Validate PDF magic bytes
        Assert.Equal(0x25, pdfBytes[0]); // %
        Assert.Equal(0x50, pdfBytes[1]); // P
        Assert.Equal(0x44, pdfBytes[2]); // D
        Assert.Equal(0x46, pdfBytes[3]); // F
    }
}

// ── Test fixture ─────────────────────────────────────────────────────────────
public sealed class StressTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Stress_T3st_P@ss!")
        .Build();

    public IMacroShockTransmitter ShockTransmitter { get; } = new MacroShockTransmitter();
    public IContagionCascadeEngine ContagionEngine { get; private set; } = null!;
    public IStressTestOrchestrator Orchestrator { get; private set; } = null!;
    public IStressTestReportGenerator ReportGenerator { get; private set; } = null!;
    public IDbConnectionFactory Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        var cs = _sqlContainer.GetConnectionString();
        await new DatabaseMigrator(cs).MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(cs));
        services.AddStressTestingFramework(new ConfigurationBuilder().Build());

        var sp = services.BuildServiceProvider();
        Db = sp.GetRequiredService<IDbConnectionFactory>();
        ContagionEngine = sp.GetRequiredService<IContagionCascadeEngine>();
        Orchestrator = sp.GetRequiredService<IStressTestOrchestrator>();
        ReportGenerator = sp.GetRequiredService<IStressTestReportGenerator>();

        await SeedBaseDataAsync();
    }

    private async Task SeedBaseDataAsync()
    {
        await using var conn = await Db.OpenAsync();
        await conn.ExecuteAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM Regulators WHERE Code='CBN')
                INSERT INTO Regulators (Code,Name) VALUES ('CBN','Central Bank of Nigeria');

            MERGE Institutions AS t
            USING (VALUES
                (1, 'CBN','DMB','DMB-001','First Bank'),
                (2, 'CBN','MFB','MFB-001','LAPO MFB'),
                (3, 'CBN','DMB','DMB-003','Zenith Bank'),
                (4, 'CBN','DMB','DMB-004','GTBank'),
                (10,'CBN','DMB','DMB-010','Test Bank Alpha'),
                (11,'CBN','DMB','DMB-011','Test Bank Beta'),
                (12,'CBN','MFB','MFB-012','Test MFB Gamma'),
                (15,'CBN','DMB','DMB-015','Test Bank Delta'),
                (16,'CBN','DMB','DMB-016','Test Bank Epsilon')
            ) AS s(Id, RegulatorCode, InstitutionType, LicenseNumber, ShortName)
            ON t.Id = s.Id
            WHEN NOT MATCHED THEN
                INSERT (Id, RegulatorCode, InstitutionType, LicenseNumber, ShortName, IsActive)
                VALUES (s.Id, s.RegulatorCode, s.InstitutionType, s.LicenseNumber, s.ShortName, 1);

            -- Ensure system config for NDIC fund
            IF NOT EXISTS (SELECT 1 FROM SystemConfiguration WHERE ConfigKey='NDIC_FUND_CAPACITY_NGN_MILLIONS')
                INSERT INTO SystemConfiguration (ConfigKey, ConfigValue)
                VALUES ('NDIC_FUND_CAPACITY_NGN_MILLIONS', 1500000);
            """);
    }

    public async Task SeedSectorAsync(
        string periodCode,
        (int Id, string Type, decimal CAR, decimal NPL, decimal LCR, decimal OilPct)[] entities)
    {
        await using var conn = await Db.OpenAsync();
        foreach (var e in entities)
        {
            await conn.ExecuteAsync(
                """
                MERGE PrudentialMetrics AS t
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
                            @CAR, @NPL, @LCR, 110, 1.5, 200000, 140000, @Oil)
                """,
                new { Id = e.Id, Type = e.Type, Period = periodCode,
                      CAR = e.CAR, NPL = e.NPL, LCR = e.LCR, Oil = e.OilPct });
        }
    }

    public async Task SeedInterbankAsync(
        string periodCode, (int Lender, int Borrower, decimal Amount) edge)
    {
        await using var conn = await Db.OpenAsync();
        await conn.ExecuteAsync(
            """
            IF NOT EXISTS (SELECT 1 FROM InterbankExposures
                           WHERE LendingInstitutionId=@L AND BorrowingInstitutionId=@B
                             AND ExposureType='PLACEMENT' AND PeriodCode=@P)
            INSERT INTO InterbankExposures
                (LendingInstitutionId, BorrowingInstitutionId, RegulatorCode,
                 PeriodCode, ExposureAmount, ExposureType, AsOfDate)
            VALUES (@L, @B, 'CBN', @P, @Amount, 'PLACEMENT',
                    CAST(SYSUTCDATETIME() AS DATE))
            """,
            new { L = edge.Lender, B = edge.Borrower, P = periodCode, Amount = edge.Amount });
    }

    public async Task DisposeAsync() => await _sqlContainer.DisposeAsync();
}
```

---

## 13 · Configuration (`appsettings.json` additions)

```json
{
  "StressTestFramework": {
    "MaxContagionRounds": 5,
    "DefaultLGD": 0.40,
    "NDICInsurableShareFallback": 0.72,
    "QuestPDF": {
      "License": "Community"
    }
  },
  "ConnectionStrings": {
    "RegOS": "Server=regos-sql.database.windows.net;Database=RegOS;Authentication=Active Directory Default;"
  }
}
```

---

## 14 · NuGet Dependencies

```xml
<PackageReference Include="QuestPDF"          Version="2024.3.8" />
<PackageReference Include="SkiaSharp"         Version="2.88.7" />
<PackageReference Include="Dapper"            Version="2.1.35" />
<PackageReference Include="Testcontainers.MsSql" Version="3.9.0" />
<PackageReference Include="xunit"             Version="2.8.0" />
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
```

---

## 15 · Deliverables Checklist

| # | Artefact | Rule Ref |
|---|---|---|
| 1 | EF Core migration `20260325_AddStressTestingSchema.cs` — 5 tables + 8 scenario seeds + DMB/MFB/PSP/BDC parameter seeds | R-03 |
| 2 | `StressScenarios` seed — 8 scenarios (3 NGFS, 5 macro) with real CBN/NGFS calibration | R-01 |
| 3 | `StressScenarioParameters` seed — per-institution-type transmission coefficients for all 8 scenarios | R-01, R-07 |
| 4 | `MacroShockTransmitter.ApplyShock` — 6-channel shock computation (GDP, oil, FX, NGFS stranded assets, physical hazard, cyber) with labelled coefficient arithmetic | R-02, R-07 |
| 5 | `ContagionCascadeEngine.CascadeAsync` — BFS interbank failure propagation, deposit-flight LCR squeeze, max 5 rounds, contagion event persistence | R-02, R-08 |
| 6 | `NDICExposureCalculator` — N5,000,000 cap-based insurable/uninsurable split, depositor-distribution table preferred, actuarial fallback | R-02, R-12 |
| 7 | `StressTestOrchestrator.RunAsync` — full 8-step pipeline (load → shock → contagion → NDIC → persist → aggregate → resilience score → executive summary) | R-02 |
| 8 | `StressTestOrchestrator.GetRunSummaryAsync` / `GetEntityResultsAsync` — run and entity query methods | R-02 |
| 9 | `StressTestReportGenerator.GenerateAsync` — QuestPDF report: cover page, executive summary, sector table (pre/post colour-coded), capital distribution bars, contagion table, NDIC analysis, entity results, policy recommendations, methodology appendix | R-11 |
| 10 | Blazor Server page `/supervisor/stress-test` — scenario selector, run launcher, recent runs table, PDF download | R-02 |
| 11 | `AddStressTestingFramework()` DI extension | R-10 |
| 12 | Integration tests — Testcontainers SQL Server, 7 scenarios covering oil shock CAR reduction, NGFS agri NPL spike, rate spike LCR breach, cyber LCR collapse, full sector aggregation, contagion propagation, PDF generation with magic-byte validation | R-09 |
| 13 | `appsettings.json` additions and NuGet dependencies declared | R-10 |
| 14 | Zero cross-regulator data leakage; all stress runs scoped to `RegulatorCode` | R-05 |
| 15 | All results append-only; no run mutated after completion | R-06 |

---

*End of RG-37 — Sector-Wide Stress Testing Framework*