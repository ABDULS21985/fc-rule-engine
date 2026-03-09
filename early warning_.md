# RG-36: Supervisory Early Warning & Systemic Risk Engine

> **Stream G — SupTech & Regulator Intelligence · Phase 4 · RegOS™ World-Class SupTech Prompt**

---

| Field | Value |
|---|---|
| **Prompt ID** | RG-36 |
| **Stream** | G — SupTech & Regulator Intelligence |
| **Phase** | Phase 4 |
| **Principle** | Transform RegOS™ into the world's leading Regulatory & SupTech platform |
| **Depends On** | RG-25 (regulator portal), RG-32 (compliance scoring), RG-11 (validation pipeline), RG-34 (submission engine), AI-01 (anomaly detection) |
| **Estimated Effort** | 10–14 days |
| **Classification** | Confidential — Internal Engineering |

---

## 0 · Preamble & Governing Rules

You are a **senior .NET 8 architect** building the Supervisory Early Warning & Systemic Risk Engine for RegOS™ — a real-time SupTech capability that monitors Nigeria's entire supervised financial sector, surfaces emerging risks before they become crises, and triggers automated supervisory actions up the CBN/NDIC/SEC chain of command. Every artefact must satisfy these non-negotiable rules:

| # | Rule |
|---|---|
| R-01 | **Zero mock data.** All seed and fixture data uses real Nigerian regulatory context: actual institution types (DMB, MFB, PSP, BDC, PFA, Insurer), real prudential ratios (CAR minimum 15% for DMBs, 10% for MFBs; NPL threshold 5%; LCR minimum 100%), real regulator codes. |
| R-02 | **No stubs, no TODOs, no `throw new NotImplementedException()`.** Every method body is complete and production-ready. |
| R-03 | **Complete DDL first.** All tables, indexes, constraints, and seed data delivered as idempotent EF Core migrations before any service code. |
| R-04 | **Parameterised queries only.** Zero string interpolation in SQL. Dapper `DynamicParameters` or EF Core LINQ exclusively. |
| R-05 | **Tenant isolation per regulator.** All data-bearing tables carry `RegulatorCode`; supervisors can only see entities within their jurisdiction. No cross-regulator data leakage. |
| R-06 | **Immutable EWI audit trail.** Every Early Warning Indicator trigger is written to an append-only table. No trigger record is ever updated or deleted — only new records added. |
| R-07 | **Time-series aware.** All prudential metrics carry `AsOfDate`; all trend analysis operates on ordered time series. Point-in-time queries must never cross-contaminate periods. |
| R-08 | **Structured logging with correlation.** Every risk computation cycle carries a `ComputationRunId` (GUID) propagated through all log entries, alert rows, and audit records. |
| R-09 | **Integration tests with Testcontainers.** Real SQL Server + Redis. No in-memory fakes for EWI pipeline tests. |
| R-10 | **All secrets via Azure Key Vault or `IConfiguration`.** Zero hardcoded credentials. |
| R-11 | **Contagion analysis is graph-aware.** The interbank exposure network is modelled as a directed weighted graph. Network centrality (eigenvector, betweenness) is computed for systemic importance ranking. |
| R-12 | **Supervisory actions are fully audited.** Every letter generated, every escalation, every remediation update writes to `SupervisoryActionAuditLog`. |

---

## 1 · Architecture Context

```
┌────────────────────────────────────────────────────────────────────────────┐
│                        Data Ingestion Layer                                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐ │
│  │ Filed Returns │  │  Market Data │  │ Interbank    │  │  External     │ │
│  │ (RG-34)       │  │  Feed        │  │  Exposure    │  │  Credit Data  │ │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └──────┬────────┘ │
└─────────┼─────────────────┼─────────────────┼─────────────────┼──────────┘
          │                 │                 │                 │
┌─────────▼─────────────────▼─────────────────▼─────────────────▼──────────┐
│                   METRIC WAREHOUSE (Prudential Time Series)                │
│  PrudentialMetrics · InterbankExposures · MarketRiskMetrics               │
└────────────────────────────────┬───────────────────────────────────────────┘
                                 │
┌────────────────────────────────▼───────────────────────────────────────────┐
│                     EARLY WARNING ENGINE (RG-36)                           │
│                                                                            │
│  ┌─────────────┐  ┌────────────────┐  ┌──────────────┐  ┌─────────────┐  │
│  │  EWI         │  │  CAMELS        │  │  Systemic    │  │  Contagion  │  │
│  │  Evaluator   │  │  Scorer        │  │  Aggregator  │  │  Analyzer   │  │
│  └──────┬──────┘  └────────┬───────┘  └──────┬───────┘  └──────┬──────┘  │
│         │                 │                  │                 │          │
│         └─────────────────┴──────────────────┴─────────────────┘          │
│                                      │                                     │
│                         ┌────────────▼──────────────┐                    │
│                         │  Alert & Escalation Engine │                    │
│                         └────────────┬──────────────┘                    │
└──────────────────────────────────────┼─────────────────────────────────────┘
                                       │
┌──────────────────────────────────────▼─────────────────────────────────────┐
│                    REGULATOR PORTAL (Blazor Server)                        │
│  Sector Heatmap · EWI Dashboard · CAMELS Matrix · Contagion Graph          │
│  Supervisory Letters · Escalation Tracker · Remediation Workspace          │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## 2 · Complete DDL (EF Core Migration)

> Deliver as a single idempotent migration: `20260320_AddEarlyWarningSchema.cs`

```sql
-- ============================================================
-- Table: PrudentialMetrics
-- Purpose: Time-series snapshot of key prudential ratios per institution
-- ============================================================
CREATE TABLE PrudentialMetrics (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    InstitutionType     VARCHAR(20)     NOT NULL,   -- 'DMB','MFB','PSP','BDC','PFA','INSURER'
    AsOfDate            DATE            NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,   -- '2026-Q1', '2026-03'

    -- Capital
    CAR                 DECIMAL(8,4)    NULL,        -- Capital Adequacy Ratio (%)
    Tier1Ratio          DECIMAL(8,4)    NULL,
    Tier2Capital        DECIMAL(18,2)   NULL,        -- NGN millions
    RWA                 DECIMAL(18,2)   NULL,

    -- Asset Quality
    NPLRatio            DECIMAL(8,4)    NULL,        -- Non-Performing Loans (%)
    GrossNPL            DECIMAL(18,2)   NULL,
    GrossLoans          DECIMAL(18,2)   NULL,
    ProvisioningCoverage DECIMAL(8,4)   NULL,        -- (%)

    -- Earnings
    ROA                 DECIMAL(8,4)    NULL,        -- Return on Assets (%)
    ROE                 DECIMAL(8,4)    NULL,
    NIM                 DECIMAL(8,4)    NULL,        -- Net Interest Margin (%)
    CIR                 DECIMAL(8,4)    NULL,        -- Cost-to-Income Ratio (%)

    -- Liquidity
    LCR                 DECIMAL(8,4)    NULL,        -- Liquidity Coverage Ratio (%)
    NSFR                DECIMAL(8,4)    NULL,        -- Net Stable Funding Ratio (%)
    LiquidAssetsRatio   DECIMAL(8,4)    NULL,
    DepositConcentration DECIMAL(8,4)   NULL,        -- Top-20 depositors as % of total

    -- Market / Sensitivity
    FXExposureRatio     DECIMAL(8,4)    NULL,        -- Net open FX position / capital (%)
    InterestRateSensitivity DECIMAL(8,4) NULL,

    -- Management / Compliance (sourced from RG-32)
    ComplianceScore     DECIMAL(5,2)    NULL,
    LateFilingCount     INT             NULL,
    AuditOpinionCode    VARCHAR(10)     NULL,        -- 'CLEAN','QUALIFIED','ADVERSE','DISCLAIMER'

    -- Related party
    RelatedPartyLendingRatio DECIMAL(8,4) NULL,

    SourceReturnInstanceId BIGINT        NULL,       -- FK to ReturnInstances
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_PrudentialMetrics_Entity_Period
        UNIQUE (InstitutionId, PeriodCode),
    INDEX IX_PrudentialMetrics_Regulator (RegulatorCode, AsOfDate DESC),
    INDEX IX_PrudentialMetrics_Type (InstitutionType, AsOfDate DESC)
);

-- ============================================================
-- Table: EWIDefinitions
-- Purpose: Registry of all Early Warning Indicator rules
-- ============================================================
CREATE TABLE EWIDefinitions (
    Id                  INT IDENTITY(1,1) PRIMARY KEY,
    EWICode             VARCHAR(30)     NOT NULL,   -- 'CAR_DECLINING_3Q','NPL_THRESHOLD_BREACH'
    EWIName             NVARCHAR(150)   NOT NULL,
    Category            VARCHAR(20)     NOT NULL,   -- 'INSTITUTIONAL','SYSTEMIC'
    CAMELSComponent     VARCHAR(1)      NOT NULL,   -- 'C','A','M','E','L','S'
    DefaultSeverity     VARCHAR(10)     NOT NULL,   -- 'LOW','MEDIUM','HIGH','CRITICAL'
    Description         NVARCHAR(500)   NOT NULL,
    RemediationGuidance NVARCHAR(1000)  NULL,
    IsActive            BIT             NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_EWIDefinitions_Code UNIQUE (EWICode)
);

-- ============================================================
-- Table: EWITriggers
-- Purpose: Immutable append-only log of every EWI trigger event
-- ============================================================
CREATE TABLE EWITriggers (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    EWICode             VARCHAR(30)     NOT NULL,
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    Severity            VARCHAR(10)     NOT NULL,
    TriggerValue        DECIMAL(18,6)   NULL,        -- the metric value that breached
    ThresholdValue      DECIMAL(18,6)   NULL,        -- the threshold that was breached
    TrendData           NVARCHAR(MAX)   NULL,        -- JSON: last N periods values
    IsActive            BIT             NOT NULL DEFAULT 1,  -- cleared when remediated
    IsSystemic          BIT             NOT NULL DEFAULT 0,
    ComputationRunId    UNIQUEIDENTIFIER NOT NULL,
    TriggeredAt         DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    ClearedAt           DATETIME2(3)    NULL,        -- when threshold no longer breached
    ClearedByRunId      UNIQUEIDENTIFIER NULL,

    INDEX IX_EWITriggers_Institution (InstitutionId, IsActive, TriggeredAt DESC),
    INDEX IX_EWITriggers_Regulator (RegulatorCode, IsActive, TriggeredAt DESC),
    INDEX IX_EWITriggers_EWICode (EWICode, TriggeredAt DESC),
    INDEX IX_EWITriggers_RunId (ComputationRunId)
);

-- ============================================================
-- Table: CAMELSRatings
-- Purpose: Composite CAMELS risk rating per institution per period
-- ============================================================
CREATE TABLE CAMELSRatings (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    AsOfDate            DATE            NOT NULL,

    -- Component scores (1=Strong, 2=Satisfactory, 3=Fair, 4=Marginal, 5=Unsatisfactory)
    CapitalScore        TINYINT         NOT NULL,
    AssetQualityScore   TINYINT         NOT NULL,
    ManagementScore     TINYINT         NOT NULL,
    EarningsScore       TINYINT         NOT NULL,
    LiquidityScore      TINYINT         NOT NULL,
    SensitivityScore    TINYINT         NOT NULL,

    CompositeScore      DECIMAL(4,2)    NOT NULL,   -- weighted average
    RiskBand            VARCHAR(10)     NOT NULL,   -- 'GREEN','AMBER','RED','CRITICAL'
    TotalAssets         DECIMAL(18,2)   NULL,        -- NGN millions (for heatmap sizing)
    ComputationRunId    UNIQUEIDENTIFIER NOT NULL,
    ComputedAt          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_CAMELSRatings_Entity_Period
        UNIQUE (InstitutionId, PeriodCode),
    INDEX IX_CAMELSRatings_Regulator (RegulatorCode, AsOfDate DESC),
    INDEX IX_CAMELSRatings_Band (RiskBand, ComputedAt DESC)
);

-- ============================================================
-- Table: SystemicRiskIndicators
-- Purpose: Sector-wide aggregated risk indicators per period
-- ============================================================
CREATE TABLE SystemicRiskIndicators (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    InstitutionType     VARCHAR(20)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    AsOfDate            DATE            NOT NULL,

    EntityCount         INT             NOT NULL,
    SectorAvgCAR        DECIMAL(8,4)    NULL,
    SectorAvgNPL        DECIMAL(8,4)    NULL,
    SectorAvgLCR        DECIMAL(8,4)    NULL,
    SectorAvgROA        DECIMAL(8,4)    NULL,
    EntitiesBreachingCAR  INT           NOT NULL DEFAULT 0,
    EntitiesBreachingNPL  INT           NOT NULL DEFAULT 0,
    EntitiesBreachingLCR  INT           NOT NULL DEFAULT 0,
    HighRiskEntityCount   INT           NOT NULL DEFAULT 0,   -- RiskBand = RED or CRITICAL
    SystemicRiskScore     DECIMAL(5,2)  NOT NULL,             -- 0–100
    SystemicRiskBand      VARCHAR(10)   NOT NULL,             -- 'LOW','MODERATE','HIGH','SEVERE'
    AggregateInterbankExposure DECIMAL(18,2) NULL,
    ComputationRunId    UNIQUEIDENTIFIER NOT NULL,
    ComputedAt          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_SystemicRiskIndicators_Type_Period
        UNIQUE (RegulatorCode, InstitutionType, PeriodCode),
    INDEX IX_SystemicRiskIndicators_Regulator (RegulatorCode, AsOfDate DESC)
);

-- ============================================================
-- Table: InterbankExposures
-- Purpose: Directed interbank lending/borrowing network for contagion analysis
-- ============================================================
CREATE TABLE InterbankExposures (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    LendingInstitutionId  INT           NOT NULL,
    BorrowingInstitutionId INT          NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    ExposureAmount      DECIMAL(18,2)   NOT NULL,   -- NGN millions
    ExposureType        VARCHAR(20)     NOT NULL,   -- 'PLACEMENT','LENDING','BOND','EQUITY'
    AsOfDate            DATE            NOT NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_InterbankExposures_Pair_Period
        UNIQUE (LendingInstitutionId, BorrowingInstitutionId, ExposureType, PeriodCode),
    INDEX IX_InterbankExposures_Lender (LendingInstitutionId, PeriodCode),
    INDEX IX_InterbankExposures_Borrower (BorrowingInstitutionId, PeriodCode)
);

-- ============================================================
-- Table: ContagionAnalysisResults
-- Purpose: Network centrality and contagion risk results per period
-- ============================================================
CREATE TABLE ContagionAnalysisResults (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    EigenvectorCentrality DECIMAL(12,8) NOT NULL,   -- systemic importance score
    BetweennessCentrality DECIMAL(12,8) NOT NULL,
    TotalOutboundExposure DECIMAL(18,2) NOT NULL,
    TotalInboundExposure  DECIMAL(18,2) NOT NULL,
    DirectCounterparties  INT           NOT NULL,
    ContagionRiskScore    DECIMAL(5,2)  NOT NULL,   -- 0–100
    IsSystemicallyImportant BIT         NOT NULL DEFAULT 0,  -- D-SIB flag
    ComputationRunId    UNIQUEIDENTIFIER NOT NULL,
    ComputedAt          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT UQ_ContagionAnalysis_Entity_Period
        UNIQUE (InstitutionId, PeriodCode),
    INDEX IX_ContagionAnalysis_DSIB (IsSystemicallyImportant, PeriodCode)
);

-- ============================================================
-- Table: SupervisoryActions
-- Purpose: Regulatory actions triggered by EWI breaches
-- ============================================================
CREATE TABLE SupervisoryActions (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    EWITriggerId        BIGINT          NOT NULL,
    ActionType          VARCHAR(30)     NOT NULL,
        -- 'ADVISORY_LETTER','WARNING_LETTER','SHOW_CAUSE',
        -- 'REMEDIATION_PLAN','SANCTIONS','ESCALATION'
    Severity            VARCHAR(10)     NOT NULL,
    Title               NVARCHAR(200)   NOT NULL,
    LetterContent       NVARCHAR(MAX)   NULL,        -- generated letter text
    Status              VARCHAR(20)     NOT NULL DEFAULT 'DRAFT',
        -- 'DRAFT','ISSUED','ACKNOWLEDGED','IN_REMEDIATION','CLOSED','ESCALATED'
    IssuedAt            DATETIME2(3)    NULL,
    IssuedByUserId      INT             NULL,
    AcknowledgedAt      DATETIME2(3)    NULL,
    DueDate             DATE            NULL,
    EscalationLevel     TINYINT         NOT NULL DEFAULT 1,
        -- 1=Analyst, 2=Senior Examiner, 3=Director, 4=Governor
    CurrentAssigneeUserId INT           NULL,
    RemediationPlanJson NVARCHAR(MAX)   NULL,
    CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    CONSTRAINT FK_SupervisoryActions_EWI
        FOREIGN KEY (EWITriggerId) REFERENCES EWITriggers(Id),
    INDEX IX_SupervisoryActions_Institution (InstitutionId, Status),
    INDEX IX_SupervisoryActions_Regulator (RegulatorCode, Status, Severity)
);

-- ============================================================
-- Table: SupervisoryActionAuditLog
-- Purpose: Immutable audit trail of every supervisory action event
-- ============================================================
CREATE TABLE SupervisoryActionAuditLog (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    SupervisoryActionId BIGINT          NOT NULL,
    InstitutionId       INT             NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    EventType           VARCHAR(40)     NOT NULL,
        -- 'ACTION_CREATED','LETTER_GENERATED','LETTER_ISSUED','ACKNOWLEDGED',
        -- 'REMEDIATION_UPDATED','ESCALATED','CLOSED'
    Detail              NVARCHAR(MAX)   NULL,        -- JSON
    PerformedByUserId   INT             NULL,
    PerformedAt         DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

    INDEX IX_SupervisoryActionAuditLog_Action (SupervisoryActionId),
    INDEX IX_SupervisoryActionAuditLog_Time (PerformedAt DESC)
);

-- ============================================================
-- Table: EWIComputationRuns
-- Purpose: Log of each full EWI computation cycle
-- ============================================================
CREATE TABLE EWIComputationRuns (
    Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
    ComputationRunId    UNIQUEIDENTIFIER NOT NULL,
    RegulatorCode       VARCHAR(10)     NOT NULL,
    PeriodCode          VARCHAR(10)     NOT NULL,
    Status              VARCHAR(20)     NOT NULL DEFAULT 'RUNNING',
        -- 'RUNNING','COMPLETED','FAILED'
    EntitiesEvaluated   INT             NOT NULL DEFAULT 0,
    EWIsTriggered       INT             NOT NULL DEFAULT 0,
    EWIsCleared         INT             NOT NULL DEFAULT 0,
    ActionsGenerated    INT             NOT NULL DEFAULT 0,
    ErrorMessage        NVARCHAR(2000)  NULL,
    StartedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
    CompletedAt         DATETIME2(3)    NULL,

    CONSTRAINT UQ_EWIComputationRuns_RunId UNIQUE (ComputationRunId),
    INDEX IX_EWIComputationRuns_Regulator (RegulatorCode, StartedAt DESC)
);
```

### 2.2 Seed Data — EWI Definitions

```sql
SET IDENTITY_INSERT EWIDefinitions ON;
INSERT INTO EWIDefinitions
    (Id, EWICode, EWIName, Category, CAMELSComponent, DefaultSeverity, Description, RemediationGuidance)
VALUES
-- Capital
(1, 'CAR_DECLINING_3Q',      'CAR Declining 3+ Consecutive Quarters',    'INSTITUTIONAL','C','HIGH',
 'Capital Adequacy Ratio has declined for three or more consecutive quarters, indicating sustained capital erosion.',
 'Submit capital restoration plan within 30 days. Consider rights issue or retained earnings strategy.'),
(2, 'CAR_BREACH_MINIMUM',    'CAR Below Regulatory Minimum',             'INSTITUTIONAL','C','CRITICAL',
 'Capital Adequacy Ratio has fallen below the regulatory minimum threshold.',
 'Immediate capital injection required. Suspend dividend payments. Submit emergency capital plan within 7 days.'),
(3, 'TIER1_RATIO_LOW',       'Tier 1 Ratio Below Warning Threshold',     'INSTITUTIONAL','C','MEDIUM',
 'Tier 1 capital ratio has fallen to within 2 percentage points of the minimum requirement.',
 'Review capital planning. Restrict discretionary spending. Engage shareholders on capital support.'),

-- Asset Quality
(4, 'NPL_THRESHOLD_BREACH',  'NPL Ratio Exceeds 5%',                     'INSTITUTIONAL','A','HIGH',
 'Non-Performing Loan ratio has breached the 5% regulatory warning threshold.',
 'Submit NPL resolution plan. Increase provisioning. Suspend new lending in affected segments.'),
(5, 'NPL_RAPID_RISE',        'NPL Ratio Risen >2pp in Single Quarter',   'INSTITUTIONAL','A','HIGH',
 'NPL ratio increased by more than 2 percentage points within a single reporting quarter.',
 'Immediate loan book review. Identify concentration of new NPLs. Engage board risk committee.'),
(6, 'PROVISIONING_LOW',      'Provisioning Coverage Below 50%',          'INSTITUTIONAL','A','MEDIUM',
 'Provision coverage ratio has fallen below 50%, indicating inadequate loss absorption buffer.',
 'Increase provisions to minimum 60% within two quarters. Review classification methodology.'),

-- Liquidity
(7, 'LCR_WARNING_ZONE',      'LCR Below 110% (Approaching Minimum)',     'INSTITUTIONAL','L','MEDIUM',
 'Liquidity Coverage Ratio has entered the warning zone below 110%, approaching the 100% regulatory minimum.',
 'Reduce short-term liabilities. Build HQLA buffer. Review funding concentration.'),
(8, 'LCR_BREACH',            'LCR Below 100% (Regulatory Minimum)',      'INSTITUTIONAL','L','CRITICAL',
 'LCR has fallen below the 100% regulatory minimum, indicating a liquidity stress event.',
 'Immediate access to CBN Standing Lending Facility. Submit liquidity recovery plan within 24 hours.'),
(9, 'DEPOSIT_CONCENTRATION', 'Top-20 Depositors Exceed 30% of Total',    'INSTITUTIONAL','L','MEDIUM',
 'Deposit concentration risk: the top 20 depositors account for more than 30% of total deposits.',
 'Diversify funding base. Implement depositor concentration limits. Review wholesale funding reliance.'),
(10,'NSFR_BREACH',           'NSFR Below 100%',                          'INSTITUTIONAL','L','HIGH',
 'Net Stable Funding Ratio has breached 100%, indicating structural liquidity vulnerability.',
 'Extend liability maturity profile. Reduce reliance on short-term wholesale funding.'),

-- Management
(11,'LATE_FILINGS_2PLUS',    '2+ Consecutive Late Filings',              'INSTITUTIONAL','M','MEDIUM',
 'Institution has filed regulatory returns late for two or more consecutive periods.',
 'Review internal reporting processes. Designate dedicated regulatory reporting officer.'),
(12,'RELATED_PARTY_EXCESS',  'Related-Party Lending Exceeds Limit',      'INSTITUTIONAL','M','HIGH',
 'Related-party lending has exceeded the regulatory limit as a percentage of capital.',
 'Immediate cessation of new related-party facilities. Develop wind-down schedule for excess.'),
(13,'AUDIT_ADVERSE',         'Adverse or Disclaimer Audit Opinion',      'INSTITUTIONAL','M','CRITICAL',
 'External auditors have issued an adverse or disclaimer of opinion on financial statements.',
 'Immediate disclosure to CBN. Engage new auditors. Submit remediation plan within 14 days.'),

-- Earnings
(14,'ROA_NEGATIVE',          'Return on Assets Negative',                'INSTITUTIONAL','E','HIGH',
 'Institution is operating at a loss, with negative Return on Assets.',
 'Submit earnings recovery plan. Review cost structure. Identify non-core asset disposals.'),
(15,'CIR_CRITICAL',          'Cost-to-Income Ratio Exceeds 80%',         'INSTITUTIONAL','E','MEDIUM',
 'Operating efficiency has deteriorated with CIR above 80%, threatening long-term viability.',
 'Implement cost reduction plan. Review branch rationalisation. Automate manual processes.'),

-- Sensitivity / FX
(16,'FX_EXPOSURE_EXCESS',    'Net FX Position Exceeds 20% of Capital',   'INSTITUTIONAL','S','HIGH',
 'Net open foreign exchange position has exceeded 20% of shareholders funds.',
 'Immediately reduce FX exposure. Submit FX risk management plan. Review hedging strategy.'),
(17,'SUDDEN_ASSET_GROWTH',   'Assets Grew >30% Quarter-on-Quarter',      'INSTITUTIONAL','C','MEDIUM',
 'Total assets have grown by more than 30% in a single quarter, raising capital adequacy concerns.',
 'Ensure CAR adequacy for new asset base. Review asset quality of new growth. Submit growth plan.'),

-- Systemic
(18,'SYSTEMIC_NPL_RISING',   'Sector NPL Rising Across Multiple Types',  'SYSTEMIC',     'A','HIGH',
 'Aggregate NPL is rising simultaneously across multiple supervised institution types.',
 'Sector-wide stress test. Macroprudential policy review. Consider countercyclical buffer.'),
(19,'SYSTEMIC_LCR_STRESS',   'Multiple Entities Breaching LCR',          'SYSTEMIC',     'L','CRITICAL',
 'Three or more institutions have simultaneously breached the LCR minimum.',
 'Activate systemic liquidity support framework. Consider Emergency Liquidity Assistance.'),
(20,'CONTAGION_DSIB_RISK',   'D-SIB Contagion Risk Elevated',            'SYSTEMIC',     'S','CRITICAL',
 'A Domestic Systemically Important Bank shows elevated contagion risk via interbank network.',
 'Immediate supervisory engagement with D-SIB board. Consider enhanced supervision mandate.');
SET IDENTITY_INSERT EWIDefinitions OFF;
```

---

## 3 · Domain Models

```csharp
// ============================================================
// Enums
// ============================================================
public enum CAMELSComponentScore { Strong = 1, Satisfactory = 2, Fair = 3, Marginal = 4, Unsatisfactory = 5 }
public enum RiskBand { Green, Amber, Red, Critical }
public enum SystemicRiskBand { Low, Moderate, High, Severe }
public enum EWISeverity { Low, Medium, High, Critical }
public enum SupervisoryActionType { AdvisoryLetter, WarningLetter, ShowCause, RemediationPlan, Sanctions, Escalation }
public enum SupervisoryActionStatus { Draft, Issued, Acknowledged, InRemediation, Closed, Escalated }

// ============================================================
// CAMELS scoring weights (CBN CAMELS methodology)
// ============================================================
public static class CamelsWeights
{
    public const double Capital       = 0.20;
    public const double AssetQuality  = 0.20;
    public const double Management    = 0.15;
    public const double Earnings      = 0.20;
    public const double Liquidity     = 0.15;
    public const double Sensitivity   = 0.10;
}

// ============================================================
// Regulatory prudential thresholds per institution type
// ============================================================
public static class PrudentialThresholds
{
    public static double GetMinCAR(string institutionType) => institutionType switch
    {
        "DMB"     => 15.0,   // CBN: 15% for deposit money banks
        "MFB"     => 10.0,   // CBN: 10% for microfinance banks
        "PFA"     => 0.0,    // PenCom: capital base not ratio-based
        "INSURER" => 0.0,    // NAICOM: solvency margin basis
        _         => 10.0
    };

    public const double NPLWarningThreshold     = 5.0;    // %
    public const double NPLRapidRiseThreshold   = 2.0;    // pp in single quarter
    public const double LCRMinimum              = 100.0;  // %
    public const double LCRWarningZone          = 110.0;  // %
    public const double NSFRMinimum             = 100.0;  // %
    public const double DepositConcentrationCap = 30.0;   // top-20 / total %
    public const double RelatedPartyLendingCap  = 5.0;    // % of capital
    public const double FXExposureCap           = 20.0;   // % of shareholders' funds
    public const double SuddenGrowthThreshold   = 30.0;   // QoQ % growth
    public const double ProvisioningWarning     = 50.0;   // coverage %
    public const double ROANegativeThreshold    = 0.0;    // %
    public const double CIRCriticalThreshold    = 80.0;   // %
}

// ============================================================
// Value objects
// ============================================================
public sealed record EWITriggerContext(
    string EWICode,
    string EWISeverity,
    decimal? TriggerValue,
    decimal? ThresholdValue,
    string? TrendDataJson,      // JSON array of last N period values
    bool IsSystemic
);

public sealed record CAMELSResult(
    int InstitutionId,
    string PeriodCode,
    int CapitalScore,
    int AssetQualityScore,
    int ManagementScore,
    int EarningsScore,
    int LiquidityScore,
    int SensitivityScore,
    double CompositeScore,
    RiskBand RiskBand,
    decimal? TotalAssets
);

public sealed record EWIComputationSummary(
    Guid ComputationRunId,
    string RegulatorCode,
    string PeriodCode,
    int EntitiesEvaluated,
    int EWIsTriggered,
    int EWIsCleared,
    int ActionsGenerated,
    TimeSpan Duration
);

public sealed record ContagionNode(
    int InstitutionId,
    string InstitutionName,
    string InstitutionType,
    decimal TotalOutbound,
    decimal TotalInbound,
    double EigenvectorCentrality,
    double BetweennessCentrality,
    double ContagionRiskScore,
    bool IsSystemicallyImportant
);

public sealed record ContagionEdge(
    int LendingInstitutionId,
    int BorrowingInstitutionId,
    decimal ExposureAmount,
    string ExposureType
);

public sealed record HeatmapCell(
    int InstitutionId,
    string InstitutionName,
    string InstitutionType,
    double CompositeScore,
    RiskBand Band,
    decimal TotalAssets,        // for bubble sizing
    int ActiveEWICount,
    bool HasCriticalEWI
);
```

---

## 4 · Service Contracts (Interfaces)

### 4.1 EWI Engine

```csharp
public interface IEWIEngine
{
    /// <summary>
    /// Evaluates all active EWI rules for a single institution for the given period.
    /// Returns triggered indicators (new or persisting) and clears resolved ones.
    /// </summary>
    Task<IReadOnlyList<EWITriggerContext>> EvaluateInstitutionAsync(
        int institutionId,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a full EWI evaluation cycle across all institutions in a regulator's jurisdiction.
    /// </summary>
    Task<EWIComputationSummary> RunFullCycleAsync(
        string regulatorCode,
        string periodCode,
        CancellationToken ct = default);
}
```

### 4.2 CAMELS Scorer

```csharp
public interface ICAMELSScorer
{
    Task<CAMELSResult> ScoreInstitutionAsync(
        int institutionId,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CAMELSResult>> ScoreSectorAsync(
        string regulatorCode,
        string institutionType,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);
}
```

### 4.3 Systemic Risk Aggregator

```csharp
public interface ISystemicRiskAggregator
{
    Task<SystemicRiskIndicators> AggregateAsync(
        string regulatorCode,
        string institutionType,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);
}
```

### 4.4 Contagion Analyzer

```csharp
public interface IContagionAnalyzer
{
    /// <summary>
    /// Builds the interbank exposure graph and computes network centrality metrics.
    /// Identifies D-SIBs and contagion risk scores.
    /// </summary>
    Task<IReadOnlyList<ContagionNode>> AnalyzeAsync(
        string regulatorCode,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns directed edges for rendering the contagion network graph in the portal.
    /// </summary>
    Task<(IReadOnlyList<ContagionNode> Nodes, IReadOnlyList<ContagionEdge> Edges)>
        GetNetworkGraphAsync(
            string regulatorCode,
            string periodCode,
            CancellationToken ct = default);
}
```

### 4.5 Supervisory Action Engine

```csharp
public interface ISupervisoryActionEngine
{
    /// <summary>
    /// Auto-generates supervisory actions for all critical/high EWI triggers
    /// that don't already have an open action.
    /// </summary>
    Task<IReadOnlyList<long>> GenerateActionsForRunAsync(
        Guid computationRunId,
        string regulatorCode,
        CancellationToken ct = default);

    Task<string> GenerateLetterContentAsync(
        long supervisoryActionId,
        CancellationToken ct = default);

    Task IssueActionAsync(
        long supervisoryActionId,
        int issuedByUserId,
        CancellationToken ct = default);

    Task EscalateActionAsync(
        long supervisoryActionId,
        int escalatedByUserId,
        string reason,
        CancellationToken ct = default);

    Task RecordRemediationUpdateAsync(
        long supervisoryActionId,
        string updateJson,
        int updatedByUserId,
        CancellationToken ct = default);

    Task CloseActionAsync(
        long supervisoryActionId,
        int closedByUserId,
        string closureReason,
        CancellationToken ct = default);
}
```

### 4.6 Heatmap Query Service

```csharp
public interface IHeatmapQueryService
{
    Task<IReadOnlyList<HeatmapCell>> GetSectorHeatmapAsync(
        string regulatorCode,
        string periodCode,
        string? institutionTypeFilter,
        CancellationToken ct = default);

    Task<IReadOnlyList<EWITriggerRow>> GetInstitutionEWIHistoryAsync(
        int institutionId,
        string regulatorCode,
        int periods,
        CancellationToken ct = default);

    Task<double[][]> GetCorrelationMatrixAsync(
        string regulatorCode,
        string institutionType,
        string periodCode,
        CancellationToken ct = default);
}

public sealed record EWITriggerRow(
    long TriggerId,
    string EWICode,
    string EWIName,
    string CAMELSComponent,
    string Severity,
    decimal? TriggerValue,
    decimal? ThresholdValue,
    string? TrendDataJson,
    bool IsActive,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? ClearedAt);
```

---

## 5 · EWI Engine — Full Implementation

```csharp
public sealed class EWIEngine : IEWIEngine
{
    private readonly IDbConnectionFactory _db;
    private readonly ICAMELSScorer _camels;
    private readonly ISupervisoryActionEngine _actionEngine;
    private readonly ILogger<EWIEngine> _log;

    public EWIEngine(
        IDbConnectionFactory db,
        ICAMELSScorer camels,
        ISupervisoryActionEngine actionEngine,
        ILogger<EWIEngine> log)
    {
        _db = db; _camels = camels; _actionEngine = actionEngine; _log = log;
    }

    // ── Full cycle run ──────────────────────────────────────────────────
    public async Task<EWIComputationSummary> RunFullCycleAsync(
        string regulatorCode, string periodCode, CancellationToken ct = default)
    {
        var runId   = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow;

        _log.LogInformation(
            "EWI cycle starting: RunId={RunId} Regulator={Regulator} Period={Period}",
            runId, regulatorCode, periodCode);

        await using var conn = await _db.OpenAsync(ct);

        // Record the run
        var dbRunId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO EWIComputationRuns
                (ComputationRunId, RegulatorCode, PeriodCode, Status)
            OUTPUT INSERTED.Id
            VALUES (@RunId, @Regulator, @Period, 'RUNNING')
            """,
            new { RunId = runId, Regulator = regulatorCode, Period = periodCode });

        var institutionIds = (await conn.QueryAsync<int>(
            """
            SELECT DISTINCT Id FROM Institutions
            WHERE  RegulatorCode = @Regulator AND IsActive = 1
            """,
            new { Regulator = regulatorCode })).AsList();

        int totalTriggered = 0, totalCleared = 0, actionsGenerated = 0;

        foreach (var institutionId in institutionIds)
        {
            try
            {
                var triggers = await EvaluateInstitutionAsync(
                    institutionId, periodCode, runId, ct);
                totalTriggered += triggers.Count(t => t.TriggerValue.HasValue);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "EWI evaluation failed for institution {Id} run {RunId}",
                    institutionId, runId);
            }
        }

        // Evaluate systemic indicators
        await EvaluateSystemicEWIsAsync(conn, regulatorCode, periodCode, runId, ct);

        // Generate supervisory actions for new critical/high triggers
        var actionIds = await _actionEngine.GenerateActionsForRunAsync(
            runId, regulatorCode, ct);
        actionsGenerated = actionIds.Count;

        // Mark cleared EWIs (triggers that no longer breach)
        totalCleared = await ClearResolvedTriggersAsync(
            conn, regulatorCode, periodCode, runId, ct);

        // Update run record
        await conn.ExecuteAsync(
            """
            UPDATE EWIComputationRuns
            SET    Status = 'COMPLETED',
                   EntitiesEvaluated = @Entities,
                   EWIsTriggered = @Triggered,
                   EWIsCleared = @Cleared,
                   ActionsGenerated = @Actions,
                   CompletedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """,
            new { Entities = institutionIds.Count, Triggered = totalTriggered,
                  Cleared = totalCleared, Actions = actionsGenerated, Id = dbRunId });

        var duration = DateTimeOffset.UtcNow - started;
        _log.LogInformation(
            "EWI cycle complete: RunId={RunId} Entities={E} Triggered={T} Cleared={C} " +
            "Actions={A} Duration={D}ms",
            runId, institutionIds.Count, totalTriggered, totalCleared,
            actionsGenerated, duration.TotalMilliseconds);

        return new EWIComputationSummary(
            runId, regulatorCode, periodCode, institutionIds.Count,
            totalTriggered, totalCleared, actionsGenerated, duration);
    }

    // ── Single institution evaluation ───────────────────────────────────
    public async Task<IReadOnlyList<EWITriggerContext>> EvaluateInstitutionAsync(
        int institutionId, string periodCode,
        Guid computationRunId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Load current metrics
        var current = await conn.QuerySingleOrDefaultAsync<PrudentialMetricRow>(
            """
            SELECT * FROM PrudentialMetrics
            WHERE  InstitutionId = @Id AND PeriodCode = @Period
            """,
            new { Id = institutionId, Period = periodCode });

        if (current is null)
        {
            _log.LogDebug(
                "No metrics found for institution {Id} period {Period} — skipping EWI.",
                institutionId, periodCode);
            return Array.Empty<EWITriggerContext>();
        }

        // Load last 4 periods for trend analysis
        var history = (await conn.QueryAsync<PrudentialMetricRow>(
            """
            SELECT TOP 4 * FROM PrudentialMetrics
            WHERE  InstitutionId = @Id AND PeriodCode < @Period
            ORDER BY AsOfDate DESC
            """,
            new { Id = institutionId, Period = periodCode })).ToList();

        var triggers = new List<EWITriggerContext>();

        // ── Capital checks ────────────────────────────────────────────
        var minCAR = (decimal)PrudentialThresholds.GetMinCAR(current.InstitutionType);

        if (current.CAR.HasValue && current.CAR < minCAR)
            triggers.Add(NewTrigger("CAR_BREACH_MINIMUM", "CRITICAL",
                current.CAR, minCAR, null));

        if (IsDeclining3Quarters(history.Select(h => h.CAR).ToList(), current.CAR))
            triggers.Add(NewTrigger("CAR_DECLINING_3Q", "HIGH",
                current.CAR, null,
                BuildTrendJson(history.Select(h => h.CAR).Prepend(current.CAR).ToArray())));

        if (current.CAR.HasValue &&
            current.CAR >= minCAR &&
            current.CAR < minCAR + 2)
            triggers.Add(NewTrigger("TIER1_RATIO_LOW", "MEDIUM",
                current.CAR, minCAR + 2, null));

        if (current.Tier1Ratio.HasValue && current.Tier1Ratio < minCAR * 0.75m)
            triggers.Add(NewTrigger("TIER1_RATIO_LOW", "MEDIUM",
                current.Tier1Ratio, minCAR * 0.75m, null));

        // Sudden asset growth
        if (history.Count > 0 && current.TotalAssets.HasValue && history[0].TotalAssets.HasValue)
        {
            var qoqGrowth = (current.TotalAssets!.Value - history[0].TotalAssets!.Value)
                            / history[0].TotalAssets!.Value * 100m;
            if (qoqGrowth > (decimal)PrudentialThresholds.SuddenGrowthThreshold)
                triggers.Add(NewTrigger("SUDDEN_ASSET_GROWTH", "MEDIUM",
                    qoqGrowth, (decimal)PrudentialThresholds.SuddenGrowthThreshold, null));
        }

        // ── Asset quality checks ──────────────────────────────────────
        if (current.NPLRatio.HasValue)
        {
            if (current.NPLRatio > (decimal)PrudentialThresholds.NPLWarningThreshold)
                triggers.Add(NewTrigger("NPL_THRESHOLD_BREACH", "HIGH",
                    current.NPLRatio,
                    (decimal)PrudentialThresholds.NPLWarningThreshold,
                    BuildTrendJson(history.Select(h => h.NPLRatio).Prepend(current.NPLRatio).ToArray())));

            if (history.Count > 0 && history[0].NPLRatio.HasValue)
            {
                var nplRise = current.NPLRatio.Value - history[0].NPLRatio!.Value;
                if (nplRise > (decimal)PrudentialThresholds.NPLRapidRiseThreshold)
                    triggers.Add(NewTrigger("NPL_RAPID_RISE", "HIGH",
                        nplRise, (decimal)PrudentialThresholds.NPLRapidRiseThreshold, null));
            }
        }

        if (current.ProvisioningCoverage.HasValue &&
            current.ProvisioningCoverage < (decimal)PrudentialThresholds.ProvisioningWarning)
            triggers.Add(NewTrigger("PROVISIONING_LOW", "MEDIUM",
                current.ProvisioningCoverage,
                (decimal)PrudentialThresholds.ProvisioningWarning, null));

        // ── Liquidity checks ──────────────────────────────────────────
        if (current.LCR.HasValue)
        {
            if (current.LCR < (decimal)PrudentialThresholds.LCRMinimum)
                triggers.Add(NewTrigger("LCR_BREACH", "CRITICAL",
                    current.LCR, (decimal)PrudentialThresholds.LCRMinimum,
                    BuildTrendJson(history.Select(h => h.LCR).Prepend(current.LCR).ToArray())));
            else if (current.LCR < (decimal)PrudentialThresholds.LCRWarningZone)
                triggers.Add(NewTrigger("LCR_WARNING_ZONE", "MEDIUM",
                    current.LCR, (decimal)PrudentialThresholds.LCRWarningZone, null));
        }

        if (current.NSFR.HasValue &&
            current.NSFR < (decimal)PrudentialThresholds.NSFRMinimum)
            triggers.Add(NewTrigger("NSFR_BREACH", "HIGH",
                current.NSFR, (decimal)PrudentialThresholds.NSFRMinimum, null));

        if (current.DepositConcentration.HasValue &&
            current.DepositConcentration > (decimal)PrudentialThresholds.DepositConcentrationCap)
            triggers.Add(NewTrigger("DEPOSIT_CONCENTRATION", "MEDIUM",
                current.DepositConcentration,
                (decimal)PrudentialThresholds.DepositConcentrationCap, null));

        // ── Management checks ─────────────────────────────────────────
        if (current.LateFilingCount >= 2)
            triggers.Add(NewTrigger("LATE_FILINGS_2PLUS", "MEDIUM",
                current.LateFilingCount, 2, null));

        if (current.RelatedPartyLendingRatio.HasValue &&
            current.RelatedPartyLendingRatio > (decimal)PrudentialThresholds.RelatedPartyLendingCap)
            triggers.Add(NewTrigger("RELATED_PARTY_EXCESS", "HIGH",
                current.RelatedPartyLendingRatio,
                (decimal)PrudentialThresholds.RelatedPartyLendingCap, null));

        if (current.AuditOpinionCode is "ADVERSE" or "DISCLAIMER")
            triggers.Add(NewTrigger("AUDIT_ADVERSE", "CRITICAL",
                null, null,
                System.Text.Json.JsonSerializer.Serialize(
                    new { opinion = current.AuditOpinionCode })));

        // ── Earnings checks ───────────────────────────────────────────
        if (current.ROA.HasValue && current.ROA < 0)
            triggers.Add(NewTrigger("ROA_NEGATIVE", "HIGH", current.ROA, 0m, null));

        if (current.CIR.HasValue &&
            current.CIR > (decimal)PrudentialThresholds.CIRCriticalThreshold)
            triggers.Add(NewTrigger("CIR_CRITICAL", "MEDIUM",
                current.CIR, (decimal)PrudentialThresholds.CIRCriticalThreshold, null));

        // ── Sensitivity / FX checks ───────────────────────────────────
        if (current.FXExposureRatio.HasValue &&
            Math.Abs(current.FXExposureRatio.Value) >
                (decimal)PrudentialThresholds.FXExposureCap)
            triggers.Add(NewTrigger("FX_EXPOSURE_EXCESS", "HIGH",
                current.FXExposureRatio,
                (decimal)PrudentialThresholds.FXExposureCap, null));

        // ── Persist triggers ──────────────────────────────────────────
        foreach (var trigger in triggers)
        {
            await conn.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM EWITriggers
                    WHERE  InstitutionId = @InstId
                      AND  EWICode = @Code
                      AND  PeriodCode = @Period
                      AND  IsActive = 1
                )
                INSERT INTO EWITriggers
                    (EWICode, InstitutionId, RegulatorCode, PeriodCode,
                     Severity, TriggerValue, ThresholdValue,
                     TrendData, IsActive, IsSystemic, ComputationRunId)
                SELECT @Code, @InstId,
                       (SELECT RegulatorCode FROM Institutions WHERE Id=@InstId),
                       @Period, @Severity, @TriggerValue, @ThresholdValue,
                       @TrendData, 1, 0, @RunId
                """,
                new { InstId = institutionId, Code = trigger.EWICode,
                      Period = periodCode, Severity = trigger.EWISeverity,
                      TriggerValue = trigger.TriggerValue,
                      ThresholdValue = trigger.ThresholdValue,
                      TrendData = trigger.TrendDataJson, RunId = computationRunId });
        }

        return triggers;
    }

    // ── Systemic EWI evaluation ─────────────────────────────────────────
    private async Task EvaluateSystemicEWIsAsync(
        System.Data.IDbConnection conn,
        string regulatorCode, string periodCode,
        Guid runId, CancellationToken ct)
    {
        // Count entities breaching LCR
        var lcrBreachCount = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            WHERE  i.RegulatorCode = @Regulator
              AND  pm.PeriodCode = @Period
              AND  pm.LCR < 100
            """,
            new { Regulator = regulatorCode, Period = periodCode });

        if (lcrBreachCount >= 3)
            await InsertSystemicTriggerAsync(conn, "SYSTEMIC_LCR_STRESS", "CRITICAL",
                lcrBreachCount, 3, regulatorCode, periodCode, runId, ct);

        // Check sector-wide NPL trend
        var sectorNPLRow = await conn.QuerySingleOrDefaultAsync<dynamic>(
            """
            SELECT AVG(pm.NPLRatio) AS CurrentAvgNPL,
                   COUNT(DISTINCT pm.InstitutionType) AS InstitutionTypesRising
            FROM   PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            WHERE  i.RegulatorCode = @Regulator AND pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode });

        if (sectorNPLRow?.CurrentAvgNPL > (decimal)PrudentialThresholds.NPLWarningThreshold &&
            sectorNPLRow?.InstitutionTypesRising >= 2)
            await InsertSystemicTriggerAsync(conn, "SYSTEMIC_NPL_RISING", "HIGH",
                (decimal?)sectorNPLRow.CurrentAvgNPL,
                (decimal)PrudentialThresholds.NPLWarningThreshold,
                regulatorCode, periodCode, runId, ct);
    }

    private static async Task InsertSystemicTriggerAsync(
        System.Data.IDbConnection conn,
        string ewiCode, string severity,
        decimal? triggerValue, decimal? thresholdValue,
        string regulatorCode, string periodCode,
        Guid runId, CancellationToken ct)
    {
        await conn.ExecuteAsync(
            """
            IF NOT EXISTS (
                SELECT 1 FROM EWITriggers
                WHERE  EWICode = @Code AND RegulatorCode = @Regulator
                  AND  PeriodCode = @Period AND IsSystemic = 1 AND IsActive = 1
            )
            INSERT INTO EWITriggers
                (EWICode, InstitutionId, RegulatorCode, PeriodCode,
                 Severity, TriggerValue, ThresholdValue, IsActive, IsSystemic, ComputationRunId)
            VALUES (@Code, 0, @Regulator, @Period,
                    @Severity, @TriggerValue, @ThresholdValue, 1, 1, @RunId)
            """,
            new { Code = ewiCode, Regulator = regulatorCode, Period = periodCode,
                  Severity = severity, TriggerValue = triggerValue,
                  ThresholdValue = thresholdValue, RunId = runId });
    }

    private static async Task<int> ClearResolvedTriggersAsync(
        System.Data.IDbConnection conn,
        string regulatorCode, string periodCode,
        Guid runId, CancellationToken ct)
    {
        // Clear triggers that existed in prior periods but were NOT re-triggered this run
        return await conn.ExecuteAsync(
            """
            UPDATE EWITriggers
            SET    IsActive = 0, ClearedAt = SYSUTCDATETIME(), ClearedByRunId = @RunId
            WHERE  RegulatorCode = @Regulator
              AND  IsActive = 1
              AND  PeriodCode < @Period
              AND  ClearedByRunId IS NULL
              AND  EWICode NOT IN (
                  SELECT DISTINCT EWICode FROM EWITriggers
                  WHERE  RegulatorCode = @Regulator
                    AND  PeriodCode = @Period
                    AND  ComputationRunId = @RunId
              )
            """,
            new { RunId = runId, Regulator = regulatorCode, Period = periodCode });
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static bool IsDeclining3Quarters(
        IList<decimal?> history, decimal? current)
    {
        // history[0] = most recent prior period, history[1] = two periods ago, etc.
        if (current is null || history.Count < 3) return false;
        if (history[0] is null || history[1] is null || history[2] is null) return false;

        return current < history[0] &&
               history[0] < history[1] &&
               history[1] < history[2];
    }

    private static string? BuildTrendJson(decimal?[] values)
    {
        if (values.Length == 0) return null;
        return System.Text.Json.JsonSerializer.Serialize(values);
    }

    private static EWITriggerContext NewTrigger(
        string code, string severity,
        decimal? triggerValue, decimal? thresholdValue,
        string? trendJson)
        => new(code, severity, triggerValue, thresholdValue, trendJson, false);

    // Dapper row type
    private sealed record PrudentialMetricRow(
        int InstitutionId, string InstitutionType, string PeriodCode,
        decimal? CAR, decimal? Tier1Ratio, decimal? NPLRatio, decimal? GrossNPL,
        decimal? GrossLoans, decimal? ProvisioningCoverage,
        decimal? ROA, decimal? ROE, decimal? NIM, decimal? CIR,
        decimal? LCR, decimal? NSFR, decimal? DepositConcentration,
        decimal? FXExposureRatio, decimal? RelatedPartyLendingRatio,
        decimal? ComplianceScore, int? LateFilingCount,
        string? AuditOpinionCode, decimal? TotalAssets);
}
```

---

## 6 · CAMELS Scorer — Full Implementation

```csharp
public sealed class CAMELSScorer : ICAMELSScorer
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<CAMELSScorer> _log;

    public CAMELSScorer(IDbConnectionFactory db, ILogger<CAMELSScorer> log)
    {
        _db = db; _log = log;
    }

    public async Task<CAMELSResult> ScoreInstitutionAsync(
        int institutionId, string periodCode,
        Guid computationRunId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var m = await conn.QuerySingleOrDefaultAsync<PrudentialMetricRow>(
            "SELECT * FROM PrudentialMetrics WHERE InstitutionId=@Id AND PeriodCode=@Period",
            new { Id = institutionId, Period = periodCode });

        if (m is null)
            throw new InvalidOperationException(
                $"No metrics for institution {institutionId} period {periodCode}");

        var minCAR = PrudentialThresholds.GetMinCAR(m.InstitutionType);

        // ── C — Capital Adequacy ──────────────────────────────────────
        int cScore = m.CAR switch
        {
            null => 3,
            var v when v >= (decimal)(minCAR + 5) => 1,    // Strong: ≥ min+5pp
            var v when v >= (decimal)(minCAR + 2) => 2,    // Satisfactory
            var v when v >= (decimal)minCAR        => 3,   // Fair: at minimum
            var v when v >= (decimal)(minCAR - 3)  => 4,   // Marginal: approaching breach
            _                                      => 5    // Unsatisfactory: below minimum
        };

        // ── A — Asset Quality ─────────────────────────────────────────
        int aScore = m.NPLRatio switch
        {
            null => 3,
            <= 2     => 1,   // Strong: NPL ≤ 2%
            <= 5     => 2,   // Satisfactory: ≤ 5%
            <= 10    => 3,   // Fair: ≤ 10%
            <= 20    => 4,   // Marginal: ≤ 20%
            _        => 5    // Unsatisfactory: > 20%
        };

        // Provisioning coverage weighs on asset quality
        if (m.ProvisioningCoverage < 50 && aScore < 4) aScore++;

        // ── M — Management ────────────────────────────────────────────
        int mScore = 2; // default Satisfactory
        if (m.LateFilingCount >= 2) mScore++;
        if (m.AuditOpinionCode is "QUALIFIED") mScore++;
        if (m.AuditOpinionCode is "ADVERSE" or "DISCLAIMER") mScore = 5;
        if (m.RelatedPartyLendingRatio > 5) mScore++;
        if (m.ComplianceScore < 60) mScore++;
        mScore = Math.Min(5, mScore);

        // Compliance score bonus
        if (m.ComplianceScore >= 90 && mScore > 1) mScore--;

        // ── E — Earnings ──────────────────────────────────────────────
        int eScore = m.ROA switch
        {
            null => 3,
            > 2      => 1,   // Strong: ROA > 2%
            > 1      => 2,   // Satisfactory
            > 0      => 3,   // Fair
            > -1     => 4,   // Marginal
            _        => 5    // Unsatisfactory: significant loss
        };

        if (m.CIR > 80 && eScore < 4) eScore++;
        if (m.NIM < 3 && eScore < 4) eScore++;

        // ── L — Liquidity ─────────────────────────────────────────────
        int lScore = m.LCR switch
        {
            null    => 3,
            >= 150  => 1,
            >= 120  => 2,
            >= 100  => 3,
            >= 90   => 4,
            _       => 5
        };

        if (m.NSFR < 100 && lScore < 4) lScore++;
        if (m.DepositConcentration > 30 && lScore < 4) lScore++;

        // ── S — Sensitivity to Market Risk ────────────────────────────
        int sScore = m.FXExposureRatio switch
        {
            null => 2,
            var v when Math.Abs(v) < 5   => 1,
            var v when Math.Abs(v) < 10  => 2,
            var v when Math.Abs(v) < 15  => 3,
            var v when Math.Abs(v) < 20  => 4,
            _                            => 5
        };

        if (m.InterestRateSensitivity > 10 && sScore < 4) sScore++;

        // ── Composite (weighted average) ──────────────────────────────
        var composite = Math.Round(
            cScore * CamelsWeights.Capital +
            aScore * CamelsWeights.AssetQuality +
            mScore * CamelsWeights.Management +
            eScore * CamelsWeights.Earnings +
            lScore * CamelsWeights.Liquidity +
            sScore * CamelsWeights.Sensitivity, 2);

        var band = composite switch
        {
            <= 1.5 => RiskBand.Green,
            <= 2.5 => RiskBand.Amber,
            <= 3.5 => RiskBand.Red,
            _      => RiskBand.Critical
        };

        // Persist rating
        await conn.ExecuteAsync(
            """
            MERGE CAMELSRatings AS target
            USING (VALUES (@InstId, @Period)) AS src (InstitutionId, PeriodCode)
            ON target.InstitutionId = src.InstitutionId
               AND target.PeriodCode = src.PeriodCode
            WHEN MATCHED THEN
                UPDATE SET CapitalScore=@C, AssetQualityScore=@A, ManagementScore=@M,
                           EarningsScore=@E, LiquidityScore=@L, SensitivityScore=@S,
                           CompositeScore=@Composite, RiskBand=@Band,
                           TotalAssets=@Assets, ComputationRunId=@RunId,
                           ComputedAt=SYSUTCDATETIME(),
                           RegulatorCode=(SELECT RegulatorCode FROM Institutions WHERE Id=@InstId),
                           AsOfDate=(SELECT AsOfDate FROM PrudentialMetrics
                                     WHERE InstitutionId=@InstId AND PeriodCode=@Period)
            WHEN NOT MATCHED THEN
                INSERT (InstitutionId, RegulatorCode, PeriodCode, AsOfDate,
                        CapitalScore, AssetQualityScore, ManagementScore,
                        EarningsScore, LiquidityScore, SensitivityScore,
                        CompositeScore, RiskBand, TotalAssets, ComputationRunId)
                VALUES (@InstId,
                        (SELECT RegulatorCode FROM Institutions WHERE Id=@InstId),
                        @Period,
                        (SELECT AsOfDate FROM PrudentialMetrics
                         WHERE InstitutionId=@InstId AND PeriodCode=@Period),
                        @C, @A, @M, @E, @L, @S, @Composite, @Band, @Assets, @RunId);
            """,
            new { InstId = institutionId, Period = periodCode,
                  C = cScore, A = aScore, M = mScore,
                  E = eScore, L = lScore, S = sScore,
                  Composite = composite, Band = band.ToString().ToUpperInvariant(),
                  Assets = m.TotalAssets, RunId = computationRunId });

        _log.LogDebug(
            "CAMELS scored: Institution={Id} Period={Period} Composite={Score} Band={Band}",
            institutionId, periodCode, composite, band);

        return new CAMELSResult(institutionId, periodCode,
            cScore, aScore, mScore, eScore, lScore, sScore,
            composite, band, m.TotalAssets);
    }

    public async Task<IReadOnlyList<CAMELSResult>> ScoreSectorAsync(
        string regulatorCode, string institutionType,
        string periodCode, Guid computationRunId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var institutionIds = (await conn.QueryAsync<int>(
            """
            SELECT Id FROM Institutions
            WHERE  RegulatorCode = @Regulator
              AND  InstitutionType = @Type
              AND  IsActive = 1
            """,
            new { Regulator = regulatorCode, Type = institutionType })).ToList();

        var results = new List<CAMELSResult>();
        foreach (var id in institutionIds)
        {
            try
            {
                var result = await ScoreInstitutionAsync(id, periodCode, computationRunId, ct);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "CAMELS scoring skipped for institution {Id}: {Message}", id, ex.Message);
            }
        }
        return results;
    }

    private sealed record PrudentialMetricRow(
        int InstitutionId, string InstitutionType, string PeriodCode, DATE AsOfDate,
        decimal? CAR, decimal? Tier1Ratio, decimal? NPLRatio,
        decimal? ProvisioningCoverage, decimal? ROA, decimal? NIM, decimal? CIR,
        decimal? LCR, decimal? NSFR, decimal? DepositConcentration,
        decimal? FXExposureRatio, decimal? InterestRateSensitivity,
        decimal? RelatedPartyLendingRatio, decimal? ComplianceScore,
        int? LateFilingCount, string? AuditOpinionCode, decimal? TotalAssets);
}
```

---

## 7 · Systemic Risk Aggregator — Full Implementation

```csharp
public sealed class SystemicRiskAggregator : ISystemicRiskAggregator
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SystemicRiskAggregator> _log;

    public SystemicRiskAggregator(IDbConnectionFactory db, ILogger<SystemicRiskAggregator> log)
    {
        _db = db; _log = log;
    }

    public async Task<SystemicRiskIndicators> AggregateAsync(
        string regulatorCode, string institutionType,
        string periodCode, Guid computationRunId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var metrics = (await conn.QueryAsync<SectorMetricRow>(
            """
            SELECT pm.CAR, pm.NPLRatio, pm.LCR, pm.ROA, pm.TotalAssets,
                   cr.RiskBand
            FROM   PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            LEFT JOIN CAMELSRatings cr
                   ON cr.InstitutionId = pm.InstitutionId
                  AND cr.PeriodCode = pm.PeriodCode
            WHERE  i.RegulatorCode = @Regulator
              AND  i.InstitutionType = @Type
              AND  pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode }))
            .ToList();

        if (metrics.Count == 0)
        {
            _log.LogWarning(
                "No metrics found for systemic aggregation: {Regulator}/{Type}/{Period}",
                regulatorCode, institutionType, periodCode);
            return BuildEmptyIndicators(regulatorCode, institutionType, periodCode, computationRunId);
        }

        var minCAR = (decimal)PrudentialThresholds.GetMinCAR(institutionType);

        var avgCAR = metrics.Where(m => m.CAR.HasValue).Select(m => m.CAR!.Value).AverageOrNull();
        var avgNPL = metrics.Where(m => m.NPLRatio.HasValue).Select(m => m.NPLRatio!.Value).AverageOrNull();
        var avgLCR = metrics.Where(m => m.LCR.HasValue).Select(m => m.LCR!.Value).AverageOrNull();
        var avgROA = metrics.Where(m => m.ROA.HasValue).Select(m => m.ROA!.Value).AverageOrNull();

        int breachCAR = metrics.Count(m => m.CAR.HasValue && m.CAR < minCAR);
        int breachNPL = metrics.Count(m => m.NPLRatio.HasValue &&
                                           m.NPLRatio > (decimal)PrudentialThresholds.NPLWarningThreshold);
        int breachLCR = metrics.Count(m => m.LCR.HasValue &&
                                           m.LCR < (decimal)PrudentialThresholds.LCRMinimum);
        int highRisk  = metrics.Count(m => m.RiskBand is "RED" or "CRITICAL");

        // Aggregate interbank exposure
        var aggInterbank = await conn.ExecuteScalarAsync<decimal?>(
            """
            SELECT SUM(ie.ExposureAmount)
            FROM   InterbankExposures ie
            JOIN   Institutions i ON i.Id = ie.LendingInstitutionId
            WHERE  i.RegulatorCode = @Regulator AND ie.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode });

        // Systemic risk score (0–100): higher = more dangerous
        double systemicScore = 0;
        if (metrics.Count > 0)
        {
            // Weighted contributions
            systemicScore += (double)breachCAR / metrics.Count * 35;   // CAR breaches: 35%
            systemicScore += (double)breachNPL / metrics.Count * 25;   // NPL breaches: 25%
            systemicScore += (double)breachLCR / metrics.Count * 25;   // LCR breaches: 25%
            systemicScore += (double)highRisk  / metrics.Count * 15;   // Red/Critical: 15%
            systemicScore  = Math.Min(100, Math.Round(systemicScore * 100, 1));
        }

        var band = systemicScore switch
        {
            < 20  => "LOW",
            < 40  => "MODERATE",
            < 70  => "HIGH",
            _     => "SEVERE"
        };

        var asOfDate = await conn.ExecuteScalarAsync<DateTime>(
            """
            SELECT MAX(AsOfDate) FROM PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            WHERE  i.RegulatorCode = @Regulator
              AND  i.InstitutionType = @Type
              AND  pm.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode });

        // Upsert
        await conn.ExecuteAsync(
            """
            MERGE SystemicRiskIndicators AS t
            USING (VALUES (@Regulator, @Type, @Period)) AS s (RegulatorCode, InstitutionType, PeriodCode)
            ON t.RegulatorCode = s.RegulatorCode
               AND t.InstitutionType = s.InstitutionType
               AND t.PeriodCode = s.PeriodCode
            WHEN MATCHED THEN
                UPDATE SET EntityCount=@Count, SectorAvgCAR=@CAR, SectorAvgNPL=@NPL,
                           SectorAvgLCR=@LCR, SectorAvgROA=@ROA,
                           EntitiesBreachingCAR=@BrCAR, EntitiesBreachingNPL=@BrNPL,
                           EntitiesBreachingLCR=@BrLCR, HighRiskEntityCount=@HighRisk,
                           SystemicRiskScore=@Score, SystemicRiskBand=@Band,
                           AggregateInterbankExposure=@Interbank,
                           ComputationRunId=@RunId, ComputedAt=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (RegulatorCode, InstitutionType, PeriodCode, AsOfDate,
                        EntityCount, SectorAvgCAR, SectorAvgNPL, SectorAvgLCR, SectorAvgROA,
                        EntitiesBreachingCAR, EntitiesBreachingNPL, EntitiesBreachingLCR,
                        HighRiskEntityCount, SystemicRiskScore, SystemicRiskBand,
                        AggregateInterbankExposure, ComputationRunId)
                VALUES (@Regulator, @Type, @Period, @AsOf, @Count,
                        @CAR, @NPL, @LCR, @ROA, @BrCAR, @BrNPL, @BrLCR,
                        @HighRisk, @Score, @Band, @Interbank, @RunId);
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode,
                  AsOf = asOfDate, Count = metrics.Count,
                  CAR = avgCAR, NPL = avgNPL, LCR = avgLCR, ROA = avgROA,
                  BrCAR = breachCAR, BrNPL = breachNPL, BrLCR = breachLCR,
                  HighRisk = highRisk, Score = systemicScore, Band = band,
                  Interbank = aggInterbank, RunId = computationRunId });

        return new SystemicRiskIndicators
        {
            RegulatorCode = regulatorCode, InstitutionType = institutionType,
            PeriodCode = periodCode, EntityCount = metrics.Count,
            SectorAvgCAR = avgCAR, SectorAvgNPL = avgNPL,
            SectorAvgLCR = avgLCR, SectorAvgROA = avgROA,
            EntitiesBreachingCAR = breachCAR, EntitiesBreachingNPL = breachNPL,
            EntitiesBreachingLCR = breachLCR, HighRiskEntityCount = highRisk,
            SystemicRiskScore = (decimal)systemicScore, SystemicRiskBand = band,
            AggregateInterbankExposure = aggInterbank,
            ComputationRunId = computationRunId, ComputedAt = DateTimeOffset.UtcNow
        };
    }

    private static SystemicRiskIndicators BuildEmptyIndicators(
        string regulatorCode, string institutionType,
        string periodCode, Guid runId) => new()
    {
        RegulatorCode = regulatorCode, InstitutionType = institutionType,
        PeriodCode = periodCode, EntityCount = 0,
        SystemicRiskScore = 0, SystemicRiskBand = "LOW",
        ComputationRunId = runId, ComputedAt = DateTimeOffset.UtcNow
    };

    private sealed record SectorMetricRow(
        decimal? CAR, decimal? NPLRatio, decimal? LCR,
        decimal? ROA, decimal? TotalAssets, string? RiskBand);
}

internal static class DecimalExtensions
{
    public static decimal? AverageOrNull(this IEnumerable<decimal> source)
    {
        var list = source.ToList();
        return list.Count == 0 ? null : list.Average();
    }
}
```

---

## 8 · Contagion Analyzer — Full Implementation

```csharp
/// <summary>
/// Builds directed interbank exposure graph.
/// Computes eigenvector centrality (power iteration) and betweenness centrality
/// to identify Domestic Systemically Important Banks (D-SIBs).
/// </summary>
public sealed class ContagionAnalyzer : IContagionAnalyzer
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<ContagionAnalyzer> _log;

    public ContagionAnalyzer(IDbConnectionFactory db, ILogger<ContagionAnalyzer> log)
    {
        _db = db; _log = log;
    }

    public async Task<IReadOnlyList<ContagionNode>> AnalyzeAsync(
        string regulatorCode, string periodCode,
        Guid computationRunId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var edges = (await conn.QueryAsync<InterbankEdgeRow>(
            """
            SELECT ie.LendingInstitutionId, ie.BorrowingInstitutionId,
                   ie.ExposureAmount, ie.ExposureType
            FROM   InterbankExposures ie
            JOIN   Institutions i ON i.Id = ie.LendingInstitutionId
            WHERE  i.RegulatorCode = @Regulator AND ie.PeriodCode = @Period
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        if (edges.Count == 0)
        {
            _log.LogInformation(
                "No interbank exposures for {Regulator}/{Period} — skipping contagion analysis.",
                regulatorCode, periodCode);
            return Array.Empty<ContagionNode>();
        }

        // Build adjacency structures
        var nodeIds = edges
            .SelectMany(e => new[] { e.LendingInstitutionId, e.BorrowingInstitutionId })
            .Distinct().OrderBy(x => x).ToList();

        var n = nodeIds.Count;
        var idxMap = nodeIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        // Weighted adjacency matrix (row = lender, col = borrower, value = NGN millions)
        var matrix = new double[n, n];
        var outbound = new decimal[n];
        var inbound  = new decimal[n];
        var counterpartyCount = new int[n];

        foreach (var edge in edges)
        {
            var li = idxMap[edge.LendingInstitutionId];
            var bi = idxMap[edge.BorrowingInstitutionId];
            matrix[li, bi] += (double)edge.ExposureAmount;
            outbound[li] += edge.ExposureAmount;
            inbound[bi]  += edge.ExposureAmount;
            counterpartyCount[li]++;
        }

        // Eigenvector centrality via power iteration (50 iterations)
        var eigenvector = ComputeEigenvectorCentrality(matrix, n, iterations: 50);

        // Betweenness centrality via Brandes algorithm (unweighted approximation)
        var betweenness = ComputeBetweennessCentrality(matrix, n);

        // Load institution names for display
        var instNames = (await conn.QueryAsync<(int Id, string Name, string Type)>(
            """
            SELECT Id, ShortName, InstitutionType FROM Institutions
            WHERE  Id IN @Ids
            """,
            new { Ids = nodeIds }))
            .ToDictionary(x => x.Id, x => (x.Name, x.Type));

        // D-SIB threshold: top 20% by eigenvector centrality
        var eigSorted = eigenvector.OrderByDescending(v => v).ToArray();
        var dsibThreshold = eigSorted.Length > 0
            ? eigSorted[(int)(eigSorted.Length * 0.2)]
            : 0.0;

        var nodes = new List<ContagionNode>();

        for (int i = 0; i < n; i++)
        {
            var institutionId = nodeIds[i];
            var (name, type)  = instNames.TryGetValue(institutionId, out var t)
                ? t : ($"Institution {institutionId}", "UNKNOWN");

            // Contagion risk score: composite of eigenvector, betweenness, and absolute exposure
            var maxOutbound = outbound.Length > 0 ? (double)outbound.Max() : 1.0;
            var contagionScore = Math.Min(100.0, Math.Round(
                eigenvector[i] * 40 +
                betweenness[i] * 40 +
                ((double)outbound[i] / Math.Max(1, maxOutbound)) * 20, 1));

            var isDSIB = eigenvector[i] >= dsibThreshold && contagionScore >= 50;

            nodes.Add(new ContagionNode(
                InstitutionId: institutionId,
                InstitutionName: name,
                InstitutionType: type,
                TotalOutbound: outbound[i],
                TotalInbound:  inbound[i],
                EigenvectorCentrality: Math.Round(eigenvector[i], 8),
                BetweennessCentrality: Math.Round(betweenness[i], 8),
                ContagionRiskScore: contagionScore,
                IsSystemicallyImportant: isDSIB));
        }

        // Persist results
        foreach (var node in nodes)
        {
            await conn.ExecuteAsync(
                """
                MERGE ContagionAnalysisResults AS t
                USING (VALUES (@InstId, @Period)) AS s (InstitutionId, PeriodCode)
                ON t.InstitutionId = s.InstitutionId AND t.PeriodCode = s.PeriodCode
                WHEN MATCHED THEN
                    UPDATE SET EigenvectorCentrality=@Eig, BetweennessCentrality=@Bet,
                               TotalOutboundExposure=@Out, TotalInboundExposure=@In,
                               DirectCounterparties=@CC, ContagionRiskScore=@Score,
                               IsSystemicallyImportant=@DSIB,
                               RegulatorCode=@Regulator, ComputationRunId=@RunId,
                               ComputedAt=SYSUTCDATETIME()
                WHEN NOT MATCHED THEN
                    INSERT (InstitutionId, RegulatorCode, PeriodCode,
                            EigenvectorCentrality, BetweennessCentrality,
                            TotalOutboundExposure, TotalInboundExposure,
                            DirectCounterparties, ContagionRiskScore,
                            IsSystemicallyImportant, ComputationRunId)
                    VALUES (@InstId, @Regulator, @Period, @Eig, @Bet,
                            @Out, @In, @CC, @Score, @DSIB, @RunId);
                """,
                new { InstId = node.InstitutionId, Regulator = regulatorCode,
                      Period = periodCode, Eig = node.EigenvectorCentrality,
                      Bet = node.BetweennessCentrality, Out = node.TotalOutbound,
                      In  = node.TotalInbound,
                      CC  = edges.Count(e =>
                          e.LendingInstitutionId == node.InstitutionId ||
                          e.BorrowingInstitutionId == node.InstitutionId),
                      Score = node.ContagionRiskScore,
                      DSIB = node.IsSystemicallyImportant, RunId = computationRunId });
        }

        var dsibCount = nodes.Count(n => n.IsSystemicallyImportant);
        _log.LogInformation(
            "Contagion analysis complete: {Regulator}/{Period} Nodes={N} D-SIBs={D}",
            regulatorCode, periodCode, nodes.Count, dsibCount);

        // Raise systemic EWI if any D-SIB has critical contagion score
        var criticalDSIBs = nodes.Where(n => n.IsSystemicallyImportant &&
                                              n.ContagionRiskScore >= 70).ToList();
        if (criticalDSIBs.Count > 0)
        {
            await conn.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM EWITriggers
                    WHERE  EWICode='CONTAGION_DSIB_RISK' AND RegulatorCode=@Regulator
                      AND  PeriodCode=@Period AND IsActive=1
                )
                INSERT INTO EWITriggers
                    (EWICode, InstitutionId, RegulatorCode, PeriodCode,
                     Severity, TriggerValue, ThresholdValue, IsActive, IsSystemic, ComputationRunId)
                VALUES ('CONTAGION_DSIB_RISK', 0, @Regulator, @Period,
                        'CRITICAL', @Score, 70, 1, 1, @RunId)
                """,
                new { Regulator = regulatorCode, Period = periodCode,
                      Score = criticalDSIBs.Max(n => n.ContagionRiskScore),
                      RunId = computationRunId });
        }

        return nodes;
    }

    public async Task<(IReadOnlyList<ContagionNode> Nodes, IReadOnlyList<ContagionEdge> Edges)>
        GetNetworkGraphAsync(string regulatorCode, string periodCode, CancellationToken ct)
    {
        await using var conn = await _db.OpenAsync(ct);

        var nodes = (await conn.QueryAsync<ContagionNode>(
            """
            SELECT car.InstitutionId,
                   i.ShortName     AS InstitutionName,
                   i.InstitutionType,
                   car.TotalOutboundExposure   AS TotalOutbound,
                   car.TotalInboundExposure    AS TotalInbound,
                   car.EigenvectorCentrality,
                   car.BetweennessCentrality,
                   car.ContagionRiskScore,
                   car.IsSystemicallyImportant
            FROM   ContagionAnalysisResults car
            JOIN   Institutions i ON i.Id = car.InstitutionId
            WHERE  car.RegulatorCode = @Regulator AND car.PeriodCode = @Period
            ORDER BY car.ContagionRiskScore DESC
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        var edges = (await conn.QueryAsync<ContagionEdge>(
            """
            SELECT ie.LendingInstitutionId, ie.BorrowingInstitutionId,
                   ie.ExposureAmount, ie.ExposureType
            FROM   InterbankExposures ie
            JOIN   Institutions i ON i.Id = ie.LendingInstitutionId
            WHERE  i.RegulatorCode = @Regulator AND ie.PeriodCode = @Period
            ORDER BY ie.ExposureAmount DESC
            """,
            new { Regulator = regulatorCode, Period = periodCode })).ToList();

        return (nodes, edges);
    }

    // ── Graph algorithms ─────────────────────────────────────────────────

    private static double[] ComputeEigenvectorCentrality(double[,] matrix, int n, int iterations)
    {
        var centrality = Enumerable.Repeat(1.0 / n, n).ToArray();

        for (int iter = 0; iter < iterations; iter++)
        {
            var next = new double[n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    next[i] += matrix[j, i] * centrality[j];  // in-neighbour influence

            // Normalise
            var norm = Math.Sqrt(next.Sum(v => v * v));
            if (norm < 1e-12) break;
            for (int i = 0; i < n; i++)
                centrality[i] = next[i] / norm;
        }

        // Normalise to [0, 1]
        var max = centrality.Max();
        if (max > 0)
            for (int i = 0; i < n; i++)
                centrality[i] /= max;

        return centrality;
    }

    private static double[] ComputeBetweennessCentrality(double[,] matrix, int n)
    {
        // Brandes algorithm (unweighted BFS approximation)
        var betweenness = new double[n];

        for (int s = 0; s < n; s++)
        {
            var stack  = new Stack<int>();
            var pred   = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
            var sigma  = new double[n];
            var dist   = Enumerable.Repeat(-1, n).ToArray();
            sigma[s]   = 1;
            dist[s]    = 0;

            var queue = new Queue<int>();
            queue.Enqueue(s);

            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                stack.Push(v);
                for (int w = 0; w < n; w++)
                {
                    if (matrix[v, w] <= 0) continue;
                    if (dist[w] < 0)
                    {
                        queue.Enqueue(w);
                        dist[w] = dist[v] + 1;
                    }
                    if (dist[w] == dist[v] + 1)
                    {
                        sigma[w] += sigma[v];
                        pred[w].Add(v);
                    }
                }
            }

            var delta = new double[n];
            while (stack.Count > 0)
            {
                var w = stack.Pop();
                foreach (var v in pred[w])
                {
                    delta[v] += sigma[v] / Math.Max(sigma[w], 1) * (1 + delta[w]);
                    if (w != s) betweenness[w] += delta[w];
                }
            }
        }

        // Normalise to [0, 1]
        var maxB = betweenness.Max();
        if (maxB > 0)
            for (int i = 0; i < n; i++)
                betweenness[i] /= maxB;

        return betweenness;
    }

    private sealed record InterbankEdgeRow(
        int LendingInstitutionId, int BorrowingInstitutionId,
        decimal ExposureAmount, string ExposureType);
}
```

---

## 9 · Supervisory Action Engine — Full Implementation

```csharp
public sealed class SupervisoryActionEngine : ISupervisoryActionEngine
{
    private readonly IDbConnectionFactory _db;
    private readonly ILogger<SupervisoryActionEngine> _log;

    // Escalation map: severity + trigger count → level
    private static readonly Dictionary<string, int> _defaultEscalationLevel = new()
    {
        ["LOW"]      = 1,   // Analyst
        ["MEDIUM"]   = 1,   // Analyst
        ["HIGH"]     = 2,   // Senior Examiner
        ["CRITICAL"] = 3    // Director
    };

    private static readonly string[] _escalationTitles =
    {
        "", // 0 unused
        "Analyst",
        "Senior Examiner",
        "Director, Supervision",
        "Governor, CBN"
    };

    public SupervisoryActionEngine(IDbConnectionFactory db, ILogger<SupervisoryActionEngine> log)
    {
        _db = db; _log = log;
    }

    // ── Auto-generate actions for a computation run ─────────────────────
    public async Task<IReadOnlyList<long>> GenerateActionsForRunAsync(
        Guid computationRunId, string regulatorCode, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Fetch HIGH and CRITICAL triggers from this run that have no open action
        var triggers = (await conn.QueryAsync<NewTriggerRow>(
            """
            SELECT t.Id         AS TriggerId,
                   t.InstitutionId,
                   t.EWICode,
                   t.Severity,
                   t.PeriodCode,
                   t.RegulatorCode,
                   t.IsSystemic,
                   d.EWIName,
                   d.RemediationGuidance,
                   i.ShortName  AS InstitutionName
            FROM   EWITriggers t
            JOIN   EWIDefinitions d ON d.EWICode = t.EWICode
            LEFT JOIN Institutions i ON i.Id = t.InstitutionId
            WHERE  t.ComputationRunId = @RunId
              AND  t.IsActive = 1
              AND  t.Severity IN ('HIGH','CRITICAL')
              AND  NOT EXISTS (
                  SELECT 1 FROM SupervisoryActions sa
                  WHERE  sa.EWITriggerId = t.Id
                    AND  sa.Status NOT IN ('CLOSED')
              )
            """,
            new { RunId = computationRunId })).ToList();

        var actionIds = new List<long>();

        foreach (var trigger in triggers)
        {
            var actionType = trigger.Severity == "CRITICAL"
                ? "WARNING_LETTER"
                : "ADVISORY_LETTER";

            var escalationLevel = _defaultEscalationLevel.GetValueOrDefault(
                trigger.Severity, 1);

            var title = $"{trigger.Severity} Alert: {trigger.EWIName} — " +
                        $"{trigger.InstitutionName ?? "Sector-Wide"} ({trigger.PeriodCode})";

            var actionId = await conn.ExecuteScalarAsync<long>(
                """
                INSERT INTO SupervisoryActions
                    (InstitutionId, RegulatorCode, EWITriggerId, ActionType,
                     Severity, Title, Status, EscalationLevel, DueDate)
                OUTPUT INSERTED.Id
                VALUES (@InstId, @Regulator, @TriggerId, @ActionType,
                        @Severity, @Title, 'DRAFT', @EscLevel,
                        DATEADD(DAY, @DueDays, CAST(SYSUTCDATETIME() AS DATE)))
                """,
                new { InstId = trigger.InstitutionId, Regulator = trigger.RegulatorCode,
                      TriggerId = trigger.TriggerId, ActionType = actionType,
                      Severity = trigger.Severity, Title = title,
                      EscLevel = escalationLevel,
                      DueDays = trigger.Severity == "CRITICAL" ? 7 : 30 });

            // Generate letter content immediately
            var letter = BuildLetterContent(trigger, actionType);

            await conn.ExecuteAsync(
                "UPDATE SupervisoryActions SET LetterContent=@Letter WHERE Id=@Id",
                new { Letter = letter, Id = actionId });

            // Audit log
            await WriteAuditAsync(conn, actionId, trigger.InstitutionId,
                trigger.RegulatorCode, "ACTION_CREATED",
                new { trigger.EWICode, trigger.Severity, actionType },
                userId: null);

            actionIds.Add(actionId);

            _log.LogInformation(
                "Supervisory action created: ActionId={Id} EWI={EWI} Institution={Inst} " +
                "Severity={Sev} EscLevel={Esc}",
                actionId, trigger.EWICode, trigger.InstitutionName,
                trigger.Severity, escalationLevel);
        }

        return actionIds;
    }

    // ── Generate letter content ─────────────────────────────────────────
    public async Task<string> GenerateLetterContentAsync(
        long supervisoryActionId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var row = await conn.QuerySingleOrDefaultAsync<ActionDetailRow>(
            """
            SELECT sa.Id, sa.InstitutionId, sa.RegulatorCode, sa.EWITriggerId,
                   sa.ActionType, sa.Severity, sa.Title,
                   t.EWICode, t.TriggerValue, t.ThresholdValue, t.PeriodCode,
                   d.EWIName, d.RemediationGuidance,
                   i.ShortName AS InstitutionName,
                   i.InstitutionType
            FROM   SupervisoryActions sa
            JOIN   EWITriggers t ON t.Id = sa.EWITriggerId
            JOIN   EWIDefinitions d ON d.EWICode = t.EWICode
            LEFT JOIN Institutions i ON i.Id = sa.InstitutionId
            WHERE  sa.Id = @Id
            """,
            new { Id = supervisoryActionId });

        if (row is null)
            throw new KeyNotFoundException($"Supervisory action {supervisoryActionId} not found.");

        var trigger = new NewTriggerRow(
            TriggerId: row.EWITriggerId,
            InstitutionId: row.InstitutionId,
            EWICode: row.EWICode,
            Severity: row.Severity,
            PeriodCode: row.PeriodCode,
            RegulatorCode: row.RegulatorCode,
            IsSystemic: false,
            EWIName: row.EWIName,
            RemediationGuidance: row.RemediationGuidance,
            InstitutionName: row.InstitutionName);

        var letter = BuildLetterContent(trigger, row.ActionType);

        await conn.ExecuteAsync(
            "UPDATE SupervisoryActions SET LetterContent=@Letter, UpdatedAt=SYSUTCDATETIME() WHERE Id=@Id",
            new { Letter = letter, Id = supervisoryActionId });

        return letter;
    }

    // ── Issue action ────────────────────────────────────────────────────
    public async Task IssueActionAsync(
        long supervisoryActionId, int issuedByUserId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var affected = await conn.ExecuteAsync(
            """
            UPDATE SupervisoryActions
            SET    Status = 'ISSUED', IssuedAt = SYSUTCDATETIME(),
                   IssuedByUserId = @UserId, UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id AND Status = 'DRAFT'
            """,
            new { Id = supervisoryActionId, UserId = issuedByUserId });

        if (affected == 0)
            throw new InvalidOperationException(
                $"Action {supervisoryActionId} cannot be issued (not in DRAFT status).");

        var (instId, regCode) = await GetActionContextAsync(conn, supervisoryActionId);

        await WriteAuditAsync(conn, supervisoryActionId, instId, regCode,
            "LETTER_ISSUED", new { issuedByUserId }, issuedByUserId);

        _log.LogInformation(
            "Supervisory action issued: ActionId={Id} IssuedBy={User}",
            supervisoryActionId, issuedByUserId);
    }

    // ── Escalate action ─────────────────────────────────────────────────
    public async Task EscalateActionAsync(
        long supervisoryActionId, int escalatedByUserId,
        string reason, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var current = await conn.QuerySingleOrDefaultAsync<EscalationRow>(
            "SELECT EscalationLevel, InstitutionId, RegulatorCode FROM SupervisoryActions WHERE Id=@Id",
            new { Id = supervisoryActionId });

        if (current is null)
            throw new KeyNotFoundException($"Action {supervisoryActionId} not found.");

        if (current.EscalationLevel >= 4)
            throw new InvalidOperationException(
                "Action is already escalated to Governor level (maximum).");

        var newLevel = current.EscalationLevel + 1;

        await conn.ExecuteAsync(
            """
            UPDATE SupervisoryActions
            SET    EscalationLevel = @Level,
                   Status = 'ESCALATED',
                   UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """,
            new { Level = newLevel, Id = supervisoryActionId });

        await WriteAuditAsync(conn, supervisoryActionId,
            current.InstitutionId, current.RegulatorCode, "ESCALATED",
            new { from = current.EscalationLevel, to = newLevel,
                  escalatedTo = _escalationTitles[newLevel], reason },
            escalatedByUserId);

        _log.LogWarning(
            "Action escalated: ActionId={Id} Level={Level} ({Title})",
            supervisoryActionId, newLevel, _escalationTitles[newLevel]);
    }

    // ── Record remediation update ────────────────────────────────────────
    public async Task RecordRemediationUpdateAsync(
        long supervisoryActionId, string updateJson,
        int updatedByUserId, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            """
            UPDATE SupervisoryActions
            SET    Status = 'IN_REMEDIATION',
                   RemediationPlanJson = @Plan,
                   UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """,
            new { Plan = updateJson, Id = supervisoryActionId });

        var (instId, regCode) = await GetActionContextAsync(conn, supervisoryActionId);

        await WriteAuditAsync(conn, supervisoryActionId, instId, regCode,
            "REMEDIATION_UPDATED", updateJson, updatedByUserId);
    }

    // ── Close action ─────────────────────────────────────────────────────
    public async Task CloseActionAsync(
        long supervisoryActionId, int closedByUserId,
        string closureReason, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        await conn.ExecuteAsync(
            """
            UPDATE SupervisoryActions
            SET    Status = 'CLOSED', UpdatedAt = SYSUTCDATETIME()
            WHERE  Id = @Id
            """,
            new { Id = supervisoryActionId });

        var (instId, regCode) = await GetActionContextAsync(conn, supervisoryActionId);

        await WriteAuditAsync(conn, supervisoryActionId, instId, regCode,
            "CLOSED", new { closedByUserId, closureReason }, closedByUserId);

        _log.LogInformation(
            "Supervisory action closed: ActionId={Id} ClosedBy={User}",
            supervisoryActionId, closedByUserId);
    }

    // ── Letter builder ───────────────────────────────────────────────────
    private static string BuildLetterContent(NewTriggerRow trigger, string actionType)
    {
        var today         = DateOnly.FromDateTime(DateTime.UtcNow);
        var ref_          = $"CBN/BSD/{today.Year}/{trigger.EWICode}/{trigger.TriggerId}";
        var entityLine    = trigger.IsSystemic
            ? "ALL DEPOSIT MONEY BANKS AND FINANCIAL INSTITUTIONS"
            : trigger.InstitutionName?.ToUpperInvariant() ?? "THE BOARD AND MANAGEMENT";
        var letterHeading = actionType == "WARNING_LETTER"
            ? "NOTICE OF REGULATORY CONCERN"
            : "ADVISORY NOTICE";

        var metricLine = trigger.TriggerValue.HasValue && trigger.ThresholdValue.HasValue
            ? $"The reported value of {trigger.TriggerValue:F2}% has breached the regulatory " +
              $"threshold of {trigger.ThresholdValue:F2}%."
            : $"The Early Warning Indicator '{trigger.EWIName}' has been triggered.";

        return $"""
CENTRAL BANK OF NIGERIA
BANKING SUPERVISION DEPARTMENT

{ref_}                                               {today:dd MMMM yyyy}

{entityLine}
RE: {letterHeading} — {trigger.EWIName.ToUpperInvariant()} ({trigger.PeriodCode})

Dear Sir/Madam,

1. BACKGROUND

The Central Bank of Nigeria (CBN) has, pursuant to its mandate under the Banks and Other Financial Institutions Act (BOFIA) 2020, conducted a review of prudential returns submitted for the period {trigger.PeriodCode}. This review has identified the following supervisory concern warranting your immediate attention.

2. INDICATOR TRIGGERED

Early Warning Indicator: {trigger.EWIName}
Severity Classification: {trigger.Severity}
CAMELS Component: Capital / Asset Quality / Liquidity (as applicable)
Reporting Period: {trigger.PeriodCode}

{metricLine}

This indicator signals a material deviation from expected prudential standards and requires prompt remedial action to protect the stability of your institution and the broader financial system.

3. REGULATORY EXPECTATIONS

The CBN expects your institution to:

(a) Acknowledge receipt of this notice within five (5) working days of the date hereof;
(b) Submit a detailed Root Cause Analysis and Management Response within fourteen (14) calendar days;
(c) Implement the remediation actions outlined below and provide a structured Remediation Action Plan (RAP) with quarterly milestones;
(d) Assign a designated Board-level sponsor for oversight of the remediation process.

4. REQUIRED REMEDIATION ACTIONS

{trigger.RemediationGuidance ?? "Submit a comprehensive remediation plan addressing the identified risk area within the timelines stated above."}

5. ESCALATION NOTICE

Please be advised that failure to comply with the requirements of this notice within the stipulated timeframes shall result in escalation of this matter to the Director of Banking Supervision and may attract the imposition of regulatory sanctions as provided for under BOFIA 2020 and CBN's Regulation on the Scope of Banking Activities and Ancillary Matters (Regulations).

6. CONTACT

All correspondence in response to this notice should be addressed to:

Director, Banking Supervision Department
Central Bank of Nigeria
33 Tafawa Balewa Way, Central Business District
Abuja, Federal Capital Territory

and copied electronically to your designated supervisory examiner at the CBN.

Yours faithfully,

_______________________________
DIRECTOR, BANKING SUPERVISION
For: GOVERNOR, CENTRAL BANK OF NIGERIA

cc: Board Chairman
    External Auditors (for information)
    File
""";
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static async Task WriteAuditAsync(
        System.Data.IDbConnection conn,
        long actionId, int institutionId, string regulatorCode,
        string eventType, object? detail, int? userId)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO SupervisoryActionAuditLog
                (SupervisoryActionId, InstitutionId, RegulatorCode, EventType, Detail, PerformedByUserId)
            VALUES (@ActionId, @InstId, @Regulator, @EventType, @Detail, @UserId)
            """,
            new { ActionId = actionId, InstId = institutionId, Regulator = regulatorCode,
                  EventType = eventType,
                  Detail = detail is string s ? s
                      : System.Text.Json.JsonSerializer.Serialize(detail),
                  UserId = userId });
    }

    private static async Task<(int InstitutionId, string RegulatorCode)>
        GetActionContextAsync(System.Data.IDbConnection conn, long actionId)
    {
        var row = await conn.QuerySingleAsync<(int, string)>(
            "SELECT InstitutionId, RegulatorCode FROM SupervisoryActions WHERE Id=@Id",
            new { Id = actionId });
        return row;
    }

    private sealed record NewTriggerRow(
        long TriggerId, int InstitutionId, string EWICode, string Severity,
        string PeriodCode, string RegulatorCode, bool IsSystemic,
        string EWIName, string? RemediationGuidance, string? InstitutionName,
        decimal? TriggerValue = null, decimal? ThresholdValue = null);

    private sealed record ActionDetailRow(
        long Id, int InstitutionId, string RegulatorCode, long EWITriggerId,
        string ActionType, string Severity, string Title,
        string EWICode, decimal? TriggerValue, decimal? ThresholdValue,
        string PeriodCode, string EWIName, string? RemediationGuidance,
        string? InstitutionName, string? InstitutionType);

    private sealed record EscalationRow(
        int EscalationLevel, int InstitutionId, string RegulatorCode);
}
```

---

## 10 · Heatmap Query Service — Full Implementation

```csharp
public sealed class HeatmapQueryService : IHeatmapQueryService
{
    private readonly IDbConnectionFactory _db;

    public HeatmapQueryService(IDbConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<HeatmapCell>> GetSectorHeatmapAsync(
        string regulatorCode, string periodCode,
        string? institutionTypeFilter, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync<HeatmapRow>(
            """
            SELECT cr.InstitutionId,
                   i.ShortName             AS InstitutionName,
                   i.InstitutionType,
                   cr.CompositeScore,
                   cr.RiskBand,
                   ISNULL(cr.TotalAssets, 0) AS TotalAssets,
                   (SELECT COUNT(*) FROM EWITriggers t
                    WHERE  t.InstitutionId = cr.InstitutionId
                      AND  t.IsActive = 1) AS ActiveEWICount,
                   CAST(CASE WHEN EXISTS (
                       SELECT 1 FROM EWITriggers t
                       WHERE  t.InstitutionId = cr.InstitutionId
                         AND  t.IsActive = 1
                         AND  t.Severity = 'CRITICAL')
                   THEN 1 ELSE 0 END AS BIT) AS HasCriticalEWI
            FROM   CAMELSRatings cr
            JOIN   Institutions i ON i.Id = cr.InstitutionId
            WHERE  cr.RegulatorCode = @Regulator
              AND  cr.PeriodCode = @Period
              AND  (@TypeFilter IS NULL OR i.InstitutionType = @TypeFilter)
            ORDER BY cr.CompositeScore DESC
            """,
            new { Regulator = regulatorCode, Period = periodCode,
                  TypeFilter = institutionTypeFilter });

        return rows.Select(r => new HeatmapCell(
            r.InstitutionId, r.InstitutionName, r.InstitutionType,
            (double)r.CompositeScore,
            Enum.Parse<RiskBand>(r.RiskBand, ignoreCase: true),
            r.TotalAssets, r.ActiveEWICount, r.HasCriticalEWI)).ToList();
    }

    public async Task<IReadOnlyList<EWITriggerRow>> GetInstitutionEWIHistoryAsync(
        int institutionId, string regulatorCode, int periods, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        var rows = await conn.QueryAsync<EWITriggerRow>(
            """
            SELECT TOP (@Periods)
                   t.Id         AS TriggerId,
                   t.EWICode,
                   d.EWIName,
                   d.CAMELSComponent,
                   t.Severity,
                   t.TriggerValue,
                   t.ThresholdValue,
                   t.TrendData  AS TrendDataJson,
                   t.IsActive,
                   t.TriggeredAt,
                   t.ClearedAt
            FROM   EWITriggers t
            JOIN   EWIDefinitions d ON d.EWICode = t.EWICode
            WHERE  t.InstitutionId = @InstId
              AND  t.RegulatorCode = @Regulator
            ORDER BY t.TriggeredAt DESC
            """,
            new { InstId = institutionId, Regulator = regulatorCode, Periods = periods });

        return rows.ToList();
    }

    public async Task<double[][]> GetCorrelationMatrixAsync(
        string regulatorCode, string institutionType,
        string periodCode, CancellationToken ct = default)
    {
        await using var conn = await _db.OpenAsync(ct);

        // Fetch CAR time series for last 8 periods for all institutions of this type
        var rows = await conn.QueryAsync<CorrelationRow>(
            """
            SELECT pm.InstitutionId, pm.PeriodCode,
                   ISNULL(pm.CAR, 0) AS CAR
            FROM   PrudentialMetrics pm
            JOIN   Institutions i ON i.Id = pm.InstitutionId
            WHERE  i.RegulatorCode = @Regulator
              AND  i.InstitutionType = @Type
              AND  pm.AsOfDate >= DATEADD(MONTH, -24,
                       (SELECT MAX(AsOfDate) FROM PrudentialMetrics p2
                        JOIN Institutions i2 ON i2.Id = p2.InstitutionId
                        WHERE i2.RegulatorCode = @Regulator
                          AND i2.InstitutionType = @Type
                          AND p2.PeriodCode = @Period))
            ORDER BY pm.InstitutionId, pm.AsOfDate
            """,
            new { Regulator = regulatorCode, Type = institutionType, Period = periodCode });

        // Build time series per institution
        var grouped = rows
            .GroupBy(r => r.InstitutionId)
            .ToDictionary(g => g.Key,
                g => g.OrderBy(r => r.PeriodCode).Select(r => (double)r.CAR).ToArray());

        var ids = grouped.Keys.OrderBy(x => x).ToList();
        int n   = ids.Count;

        if (n == 0) return Array.Empty<double[]>();

        var matrix = new double[n][];
        for (int i = 0; i < n; i++)
        {
            matrix[i] = new double[n];
            for (int j = 0; j < n; j++)
            {
                if (i == j) { matrix[i][j] = 1.0; continue; }
                var seriesI = grouped[ids[i]];
                var seriesJ = grouped[ids[j]];
                matrix[i][j] = Math.Round(PearsonCorrelation(seriesI, seriesJ), 4);
            }
        }

        return matrix;
    }

    private static double PearsonCorrelation(double[] x, double[] y)
    {
        var n = Math.Min(x.Length, y.Length);
        if (n < 2) return 0.0;

        var meanX = x.Take(n).Average();
        var meanY = y.Take(n).Average();

        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < n; i++)
        {
            var dx = x[i] - meanX;
            var dy = y[i] - meanY;
            cov  += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        var denom = Math.Sqrt(varX * varY);
        return denom < 1e-12 ? 0.0 : cov / denom;
    }

    private sealed record HeatmapRow(
        int InstitutionId, string InstitutionName, string InstitutionType,
        decimal CompositeScore, string RiskBand, decimal TotalAssets,
        int ActiveEWICount, bool HasCriticalEWI);

    private sealed record CorrelationRow(
        int InstitutionId, string PeriodCode, decimal CAR);
}
```

---

## 11 · Blazor Server UI — Regulator Portal Pages

### 11.1 Sector Heatmap (`/supervisor/heatmap`)

```csharp
@page "/supervisor/heatmap"
@attribute [Authorize(Policy = "Supervisor")]
@inject IHeatmapQueryService HeatmapService
@inject IContagionAnalyzer ContagionAnalyzer
@inject NavigationManager Nav

<PageTitle>Sector Risk Heatmap — RegOS™ SupTech</PageTitle>

<div class="suptech-page">
    <!-- ── Header bar ── -->
    <div class="page-header regos-border-bottom">
        <div>
            <h1 class="page-title">Sector Risk Heatmap</h1>
            <p class="page-subtitle">
                @_cells.Count supervised entities · Period: @_periodCode
            </p>
        </div>
        <div class="header-controls">
            <select @bind="_institutionTypeFilter" class="regos-select">
                <option value="">All Institution Types</option>
                <option value="DMB">Deposit Money Banks</option>
                <option value="MFB">Microfinance Banks</option>
                <option value="PSP">Payment Service Providers</option>
                <option value="BDC">Bureau de Change</option>
                <option value="PFA">Pension Fund Administrators</option>
                <option value="INSURER">Insurers</option>
            </select>
            <button class="regos-btn regos-btn-primary" @onclick="LoadDataAsync">
                Refresh
            </button>
        </div>
    </div>

    @if (_loading)
    {
        <div class="regos-spinner-center"><div class="regos-spinner"></div></div>
    }
    else
    {
        <!-- ── Risk band summary cards ── -->
        <div class="risk-summary-row">
            @foreach (var band in new[] { "Critical","Red","Amber","Green" })
            {
                var count = _cells.Count(c =>
                    string.Equals(c.Band.ToString(), band, StringComparison.OrdinalIgnoreCase));
                <div class="risk-card risk-card-@band.ToLowerInvariant()">
                    <span class="risk-card-count">@count</span>
                    <span class="risk-card-label">@band</span>
                </div>
            }
        </div>

        <!-- ── Heatmap grid ── -->
        <div class="heatmap-container">
            @foreach (var cell in _cells.OrderByDescending(c => c.CompositeScore))
            {
                var bandClass = cell.Band switch
                {
                    RiskBand.Green    => "band-green",
                    RiskBand.Amber    => "band-amber",
                    RiskBand.Red      => "band-red",
                    RiskBand.Critical => "band-critical",
                    _                 => "band-green"
                };

                // Bubble size proportional to total assets (log scale)
                var sizeRem = cell.TotalAssets > 0
                    ? Math.Max(4, Math.Min(14, 2 + Math.Log10((double)cell.TotalAssets + 1) * 1.5))
                    : 4.0;

                <div class="heatmap-cell @bandClass @(cell.HasCriticalEWI ? "has-critical" : "")"
                     style="width:@(sizeRem)rem; height:@(sizeRem)rem;"
                     title="@cell.InstitutionName — Score: @cell.CompositeScore:F2 | EWIs: @cell.ActiveEWICount"
                     @onclick="() => DrillDown(cell.InstitutionId)">
                    <span class="cell-abbr">
                        @(cell.InstitutionName.Length > 8
                            ? cell.InstitutionName[..8]
                            : cell.InstitutionName)
                    </span>
                    <span class="cell-score">@cell.CompositeScore.ToString("F1")</span>
                    @if (cell.ActiveEWICount > 0)
                    {
                        <span class="cell-ewi-badge">@cell.ActiveEWICount</span>
                    }
                </div>
            }
        </div>

        <!-- ── Legend ── -->
        <div class="heatmap-legend">
            <span class="legend-item"><span class="legend-dot band-green"></span> Green (1.0–1.5): Stable</span>
            <span class="legend-item"><span class="legend-dot band-amber"></span> Amber (1.5–2.5): Watch</span>
            <span class="legend-item"><span class="legend-dot band-red"></span> Red (2.5–3.5): Intervene</span>
            <span class="legend-item"><span class="legend-dot band-critical"></span> Critical (&gt;3.5): Emergency</span>
            <span class="legend-item">Bubble size = total assets (log scale)</span>
        </div>
    }
</div>

@code {
    [CascadingParameter] private string RegulatorCode { get; set; } = "CBN";
    [CascadingParameter] private string CurrentPeriodCode { get; set; } = "2026-Q1";

    private IReadOnlyList<HeatmapCell> _cells = Array.Empty<HeatmapCell>();
    private bool _loading = true;
    private string? _institutionTypeFilter;
    private string _periodCode = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        _periodCode = CurrentPeriodCode;
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _loading = true;
        _cells = await HeatmapService.GetSectorHeatmapAsync(
            RegulatorCode, _periodCode, _institutionTypeFilter);
        _loading = false;
    }

    private void DrillDown(int institutionId)
        => Nav.NavigateTo($"/supervisor/institution/{institutionId}?period={_periodCode}");
}
```

### 11.2 Institution Drill-Down (`/supervisor/institution/{id}`)

```csharp
@page "/supervisor/institution/{InstitutionId:int}"
@attribute [Authorize(Policy = "Supervisor")]
@inject IHeatmapQueryService HeatmapService
@inject ISupervisoryActionEngine ActionEngine
@inject IDbConnectionFactory Db

<PageTitle>Institution Risk Detail — RegOS™ SupTech</PageTitle>

<div class="suptech-page">
    @if (_loading)
    {
        <div class="regos-spinner-center"><div class="regos-spinner"></div></div>
    }
    else
    {
        <!-- ── Institution header ── -->
        <div class="institution-header regos-border-bottom">
            <div>
                <h1 class="page-title">@_institutionName</h1>
                <span class="badge badge-@_riskBand.ToLowerInvariant()">
                    @_riskBand
                </span>
                <span class="institution-meta">
                    @_institutionType · CAMELS Score: @_compositeScore.ToString("F2")
                </span>
            </div>
            <button class="regos-btn regos-btn-danger"
                    @onclick="GenerateActionAsync"
                    disabled="@_generatingAction">
                @(_generatingAction ? "Generating…" : "Generate Supervisory Action")
            </button>
        </div>

        <!-- ── CAMELS radar ── -->
        <div class="camels-grid">
            @foreach (var (component, score, label) in _camelsComponents)
            {
                var scoreClass = score switch
                {
                    1 => "score-strong",
                    2 => "score-satisfactory",
                    3 => "score-fair",
                    4 => "score-marginal",
                    _ => "score-unsatisfactory"
                };
                <div class="camels-cell @scoreClass">
                    <span class="camels-letter">@component</span>
                    <span class="camels-score">@score</span>
                    <span class="camels-label">@label</span>
                </div>
            }
        </div>

        <!-- ── Active EWIs ── -->
        <h2 class="section-heading">Active Early Warning Indicators</h2>
        @if (!_ewis.Any())
        {
            <p class="regos-muted">No active EWIs for this period.</p>
        }
        else
        {
            <table class="regos-table">
                <thead>
                    <tr>
                        <th>EWI</th>
                        <th>CAMELS</th>
                        <th>Severity</th>
                        <th>Triggered Value</th>
                        <th>Threshold</th>
                        <th>Triggered</th>
                        <th>Trend</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var ewi in _ewis)
                    {
                        <tr class="@(ewi.Severity == "CRITICAL" ? "row-critical" : "")">
                            <td>
                                <strong>@ewi.EWICode</strong><br/>
                                <small class="regos-muted">@ewi.EWIName</small>
                            </td>
                            <td><span class="camels-badge">@ewi.CAMELSComponent</span></td>
                            <td>
                                <span class="severity-badge severity-@ewi.Severity.ToLowerInvariant()">
                                    @ewi.Severity
                                </span>
                            </td>
                            <td class="text-right">@ewi.TriggerValue?.ToString("F2")</td>
                            <td class="text-right regos-muted">@ewi.ThresholdValue?.ToString("F2")</td>
                            <td>@ewi.TriggeredAt.ToString("dd MMM yyyy")</td>
                            <td>
                                @if (ewi.TrendDataJson is not null)
                                {
                                    <MiniSparkline DataJson="@ewi.TrendDataJson" />
                                }
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }

        <!-- ── Open supervisory actions ── -->
        <h2 class="section-heading">Open Supervisory Actions</h2>
        @if (!_actions.Any())
        {
            <p class="regos-muted">No open supervisory actions.</p>
        }
        else
        {
            <table class="regos-table">
                <thead>
                    <tr>
                        <th>Action</th>
                        <th>Severity</th>
                        <th>Status</th>
                        <th>Escalation Level</th>
                        <th>Due Date</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var action in _actions)
                    {
                        <tr>
                            <td>@action.Title</td>
                            <td>
                                <span class="severity-badge severity-@action.Severity.ToLowerInvariant()">
                                    @action.Severity
                                </span>
                            </td>
                            <td>@action.Status</td>
                            <td>@action.EscalationLabel</td>
                            <td class="@(action.IsOverdue ? "text-danger" : "")">
                                @action.DueDate?.ToString("dd MMM yyyy")
                                @(action.IsOverdue ? " ⚠" : "")
                            </td>
                            <td>
                                <button class="regos-btn regos-btn-sm"
                                        @onclick="() => IssueAction(action.ActionId)">
                                    Issue
                                </button>
                                <button class="regos-btn regos-btn-sm regos-btn-warning"
                                        @onclick="() => EscalateAction(action.ActionId)">
                                    Escalate
                                </button>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    }
</div>

@code {
    [Parameter] public int InstitutionId { get; set; }
    [SupplyParameterFromQuery] public string Period { get; set; } = string.Empty;
    [CascadingParameter] private string RegulatorCode { get; set; } = "CBN";

    private bool _loading = true;
    private bool _generatingAction;
    private string _institutionName = string.Empty;
    private string _riskBand = "GREEN";
    private string _institutionType = string.Empty;
    private double _compositeScore;
    private IReadOnlyList<EWITriggerRow> _ewis = Array.Empty<EWITriggerRow>();
    private IReadOnlyList<ActionSummaryRow> _actions = Array.Empty<ActionSummaryRow>();

    private List<(string Letter, int Score, string Label)> _camelsComponents = new();

    protected override async Task OnInitializedAsync()
    {
        _loading = true;
        _ewis = await HeatmapService.GetInstitutionEWIHistoryAsync(
            InstitutionId, RegulatorCode, 50);

        await using var conn = await Db.OpenAsync();

        var rating = await conn.QuerySingleOrDefaultAsync<CamelsRatingRow>(
            """
            SELECT cr.CompositeScore, cr.RiskBand,
                   cr.CapitalScore, cr.AssetQualityScore, cr.ManagementScore,
                   cr.EarningsScore, cr.LiquidityScore, cr.SensitivityScore,
                   i.ShortName, i.InstitutionType
            FROM   CAMELSRatings cr
            JOIN   Institutions i ON i.Id = cr.InstitutionId
            WHERE  cr.InstitutionId = @Id AND cr.PeriodCode = @Period
            """,
            new { Id = InstitutionId, Period });

        if (rating is not null)
        {
            _institutionName  = rating.ShortName;
            _riskBand         = rating.RiskBand;
            _institutionType  = rating.InstitutionType;
            _compositeScore   = (double)rating.CompositeScore;

            _camelsComponents = new List<(string, int, string)>
            {
                ("C", rating.CapitalScore,       "Capital"),
                ("A", rating.AssetQualityScore,  "Assets"),
                ("M", rating.ManagementScore,    "Management"),
                ("E", rating.EarningsScore,      "Earnings"),
                ("L", rating.LiquidityScore,     "Liquidity"),
                ("S", rating.SensitivityScore,   "Sensitivity")
            };
        }

        _actions = (await conn.QueryAsync<ActionSummaryRow>(
            """
            SELECT sa.Id AS ActionId, sa.Title, sa.Severity, sa.Status,
                   sa.EscalationLevel, sa.DueDate
            FROM   SupervisoryActions sa
            WHERE  sa.InstitutionId = @Id AND sa.Status NOT IN ('CLOSED')
            ORDER BY sa.EscalationLevel DESC, sa.CreatedAt DESC
            """,
            new { Id = InstitutionId }))
            .Select(r => r with
            {
                EscalationLabel = r.EscalationLevel switch
                {
                    1 => "Analyst",
                    2 => "Senior Examiner",
                    3 => "Director",
                    4 => "Governor",
                    _ => "Unknown"
                },
                IsOverdue = r.DueDate.HasValue && r.DueDate.Value < DateOnly.FromDateTime(DateTime.UtcNow)
            }).ToList();

        _loading = false;
    }

    private async Task GenerateActionAsync()
    {
        _generatingAction = true;
        // Trigger manual generation — finds unactioned critical/high EWIs
        var runId = (await Db.OpenAsync().Result.QuerySingleOrDefaultAsync<Guid>(
            "SELECT TOP 1 ComputationRunId FROM EWITriggers WHERE InstitutionId=@Id AND IsActive=1 ORDER BY TriggeredAt DESC",
            new { Id = InstitutionId }));
        await ActionEngine.GenerateActionsForRunAsync(runId, RegulatorCode);
        await OnInitializedAsync();
        _generatingAction = false;
    }

    private async Task IssueAction(long actionId)
    {
        await ActionEngine.IssueActionAsync(actionId, issuedByUserId: 0); // 0 = system/current user
        await OnInitializedAsync();
    }

    private async Task EscalateAction(long actionId)
    {
        await ActionEngine.EscalateActionAsync(actionId, 0, "Manual escalation from portal");
        await OnInitializedAsync();
    }

    private sealed record CamelsRatingRow(
        decimal CompositeScore, string RiskBand,
        int CapitalScore, int AssetQualityScore, int ManagementScore,
        int EarningsScore, int LiquidityScore, int SensitivityScore,
        string ShortName, string InstitutionType);

    private sealed record ActionSummaryRow(
        long ActionId, string Title, string Severity, string Status,
        int EscalationLevel, DateOnly? DueDate,
        string EscalationLabel = "", bool IsOverdue = false);
}
```

### 11.3 Contagion Network Dashboard (`/supervisor/contagion`)

```csharp
@page "/supervisor/contagion"
@attribute [Authorize(Policy = "Supervisor")]
@inject IContagionAnalyzer ContagionAnalyzer

<PageTitle>Contagion Network Analysis — RegOS™ SupTech</PageTitle>

<div class="suptech-page">
    <div class="page-header regos-border-bottom">
        <div>
            <h1 class="page-title">Interbank Contagion Network</h1>
            <p class="page-subtitle">
                Systemic importance ranking · D-SIB identification · Exposure network
            </p>
        </div>
    </div>

    @if (_loading)
    {
        <div class="regos-spinner-center"><div class="regos-spinner"></div></div>
    }
    else
    {
        <!-- ── D-SIB alert banner ── -->
        @if (_dsibs.Count > 0)
        {
            <div class="alert alert-critical">
                <strong>⚠ @_dsibs.Count Domestically Systemically Important Bank(s) detected</strong>
                with elevated contagion risk. Immediate supervisory attention required.
            </div>
        }

        <!-- ── Node table ── -->
        <table class="regos-table">
            <thead>
                <tr>
                    <th>Institution</th>
                    <th>Type</th>
                    <th class="text-right">Outbound (₦M)</th>
                    <th class="text-right">Inbound (₦M)</th>
                    <th class="text-right">Eigenvector</th>
                    <th class="text-right">Betweenness</th>
                    <th class="text-right">Contagion Score</th>
                    <th>D-SIB</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var node in _nodes.OrderByDescending(n => n.ContagionRiskScore))
                {
                    <tr class="@(node.IsSystemicallyImportant ? "row-dsib" : "")">
                        <td>@node.InstitutionName</td>
                        <td>@node.InstitutionType</td>
                        <td class="text-right">@node.TotalOutbound.ToString("N0")</td>
                        <td class="text-right">@node.TotalInbound.ToString("N0")</td>
                        <td class="text-right">@node.EigenvectorCentrality.ToString("F4")</td>
                        <td class="text-right">@node.BetweennessCentrality.ToString("F4")</td>
                        <td class="text-right">
                            <ContagionScoreBadge Score="@node.ContagionRiskScore" />
                        </td>
                        <td>
                            @if (node.IsSystemicallyImportant)
                            {
                                <span class="badge badge-critical">D-SIB</span>
                            }
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

    private IReadOnlyList<ContagionNode> _nodes = Array.Empty<ContagionNode>();
    private IReadOnlyList<ContagionNode> _dsibs = Array.Empty<ContagionNode>();
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        _nodes = await ContagionAnalyzer.AnalyzeAsync(
            RegulatorCode, CurrentPeriodCode, Guid.NewGuid());
        _dsibs = _nodes.Where(n => n.IsSystemicallyImportant).ToList();
        _loading = false;
    }
}
```

---

## 12 · Dependency Injection Registration

```csharp
public static class EarlyWarningServiceExtensions
{
    public static IServiceCollection AddEarlyWarningEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Core engine services ─────────────────────────────────────────
        services.AddScoped<IEWIEngine, EWIEngine>();
        services.AddScoped<ICAMELSScorer, CAMELSScorer>();
        services.AddScoped<ISystemicRiskAggregator, SystemicRiskAggregator>();
        services.AddScoped<IContagionAnalyzer, ContagionAnalyzer>();
        services.AddScoped<ISupervisoryActionEngine, SupervisoryActionEngine>();
        services.AddScoped<IHeatmapQueryService, HeatmapQueryService>();

        // ── Background computation ───────────────────────────────────────
        services.AddHostedService<EWICycleBackgroundService>();

        // ── Options ──────────────────────────────────────────────────────
        services.Configure<EWIEngineOptions>(
            configuration.GetSection("EWIEngine"));

        return services;
    }
}

public sealed class EWIEngineOptions
{
    public int CycleIntervalMinutes { get; set; } = 60;
    public string DefaultRegulatorCode { get; set; } = "CBN";
    public bool AutoGenerateActions { get; set; } = true;
    public bool RunContagionAnalysis { get; set; } = true;
}
```

---

## 13 · EWI Cycle Background Service

```csharp
/// <summary>
/// Runs the full EWI computation cycle on a configurable interval.
/// On each cycle: scores all CAMELS ratings → evaluates all EWIs →
/// aggregates systemic indicators → runs contagion analysis →
/// generates supervisory actions for new critical/high triggers.
/// </summary>
public sealed class EWICycleBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptions<EWIEngineOptions> _options;
    private readonly ILogger<EWICycleBackgroundService> _log;

    public EWICycleBackgroundService(
        IServiceProvider services,
        IOptions<EWIEngineOptions> options,
        ILogger<EWICycleBackgroundService> log)
    {
        _services = services;
        _options  = options;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("EWI background service started. Interval={Min}m",
            _options.Value.CycleIntervalMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogError(ex, "EWI cycle threw unhandled exception — will retry next interval.");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(_options.Value.CycleIntervalMinutes), ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var opts       = _options.Value;
        var db         = sp.GetRequiredService<IDbConnectionFactory>();
        var camels     = sp.GetRequiredService<ICAMELSScorer>();
        var ewi        = sp.GetRequiredService<IEWIEngine>();
        var systemic   = sp.GetRequiredService<ISystemicRiskAggregator>();
        var contagion  = sp.GetRequiredService<IContagionAnalyzer>();

        var periodCode = DeriveCurrentPeriod();
        var runId      = Guid.NewGuid();

        _log.LogInformation("EWI cycle: RunId={RunId} Period={Period}", runId, periodCode);

        await using var conn = await db.OpenAsync(ct);

        // Get all active institution types for this regulator
        var institutionTypes = (await conn.QueryAsync<string>(
            """
            SELECT DISTINCT InstitutionType FROM Institutions
            WHERE  RegulatorCode = @Regulator AND IsActive = 1
            """,
            new { Regulator = opts.DefaultRegulatorCode })).ToList();

        // ── Step 1: Score all CAMELS ratings ──────────────────────────
        foreach (var type in institutionTypes)
        {
            await camels.ScoreSectorAsync(
                opts.DefaultRegulatorCode, type, periodCode, runId, ct);
        }

        // ── Step 2: Run full EWI cycle ────────────────────────────────
        var summary = await ewi.RunFullCycleAsync(
            opts.DefaultRegulatorCode, periodCode, ct);

        _log.LogInformation(
            "EWI cycle summary: Triggered={T} Cleared={C} Actions={A}",
            summary.EWIsTriggered, summary.EWIsCleared, summary.ActionsGenerated);

        // ── Step 3: Aggregate systemic indicators per type ────────────
        foreach (var type in institutionTypes)
        {
            await systemic.AggregateAsync(
                opts.DefaultRegulatorCode, type, periodCode, runId, ct);
        }

        // ── Step 4: Contagion analysis ────────────────────────────────
        if (opts.RunContagionAnalysis)
        {
            await contagion.AnalyzeAsync(
                opts.DefaultRegulatorCode, periodCode, runId, ct);
        }
    }

    private static string DeriveCurrentPeriod()
    {
        var now = DateTime.UtcNow.AddMonths(-1); // previous complete month
        return $"{now.Year}-{now.Month:D2}";
    }
}
```

---

## 14 · Integration Tests (Testcontainers)

```csharp
[Collection("EWIIntegration")]
public sealed class EWIEngineIntegrationTests
    : IClassFixture<EWITestFixture>
{
    private readonly EWITestFixture _fx;
    public EWIEngineIntegrationTests(EWITestFixture fx) => _fx = fx;

    // ── Test 1: CAR declining 3 quarters triggers EWI ──────────────────
    [Fact]
    public async Task EWIEngine_CARDeclining3Quarters_TriggersCAR_DECLINING_3Q()
    {
        // Seed 4 periods of declining CAR for DMB-001
        await _fx.SeedPrudentialMetricsAsync(institutionId: 1, metrics: new[]
        {
            ("2025-Q2", 22.5m, 4.2m, 135.0m),  // Q2 2025: healthy
            ("2025-Q3", 20.1m, 4.5m, 128.0m),  // Q3 2025: declining
            ("2025-Q4", 18.3m, 4.8m, 115.0m),  // Q4 2025: declining
            ("2026-Q1", 17.0m, 5.1m, 108.0m),  // Q1 2026: 3rd consecutive decline
        }); // (periodCode, CAR, NPLRatio, LCR)

        var triggers = await _fx.EWIEngine.EvaluateInstitutionAsync(
            institutionId: 1, periodCode: "2026-Q1",
            computationRunId: Guid.NewGuid());

        Assert.Contains(triggers, t => t.EWICode == "CAR_DECLINING_3Q");
        Assert.Contains(triggers, t => t.EWICode == "NPL_THRESHOLD_BREACH");

        var carTrigger = triggers.First(t => t.EWICode == "CAR_DECLINING_3Q");
        Assert.NotNull(carTrigger.TrendDataJson);
        Assert.Equal("HIGH", carTrigger.EWISeverity);
    }

    // ── Test 2: LCR below 100% triggers CRITICAL ────────────────────────
    [Fact]
    public async Task EWIEngine_LCRBelow100_TriggersCriticalLCR_BREACH()
    {
        await _fx.SeedPrudentialMetricsAsync(institutionId: 2, metrics: new[]
        {
            ("2026-Q1", 18.0m, 3.0m, 94.5m),  // LCR below 100%
        });

        var triggers = await _fx.EWIEngine.EvaluateInstitutionAsync(
            2, "2026-Q1", Guid.NewGuid());

        var lcrTrigger = triggers.FirstOrDefault(t => t.EWICode == "LCR_BREACH");
        Assert.NotNull(lcrTrigger);
        Assert.Equal("CRITICAL", lcrTrigger!.EWISeverity);
        Assert.Equal(94.5m, lcrTrigger.TriggerValue);
        Assert.Equal(100.0m, lcrTrigger.ThresholdValue);
    }

    // ── Test 3: CAMELS scoring gives correct composite ──────────────────
    [Fact]
    public async Task CAMELSScorer_HealthyDMB_ScoresGreenBand()
    {
        await _fx.SeedPrudentialMetricsAsync(institutionId: 3, metrics: new[]
        {
            // CAR=21%, NPL=2.5%, LCR=145%, ROA=2.1%, CIR=55%, FX=8%
            ("2026-Q1", 21.0m, 2.5m, 145.0m),
        }, extended: new PrudentialExtended
        {
            ROA = 2.1m, CIR = 55.0m, FXExposureRatio = 8.0m,
            ProvisioningCoverage = 75.0m, NSFR = 120.0m,
            ComplianceScore = 88.0m, LateFilingCount = 0,
            AuditOpinionCode = "CLEAN"
        });

        var result = await _fx.CAMELSScorer.ScoreInstitutionAsync(
            3, "2026-Q1", Guid.NewGuid());

        Assert.Equal(RiskBand.Green, result.RiskBand);
        Assert.True(result.CompositeScore <= 1.5,
            $"Expected score ≤ 1.5 for healthy DMB, got {result.CompositeScore}");
        Assert.Equal(1, result.CapitalScore);
        Assert.True(result.LiquidityScore <= 2);
    }

    // ── Test 4: CAMELS scoring gives CRITICAL for distressed bank ────────
    [Fact]
    public async Task CAMELSScorer_DistressedBank_ScoresCriticalBand()
    {
        await _fx.SeedPrudentialMetricsAsync(institutionId: 4, metrics: new[]
        {
            ("2026-Q1", 11.5m, 18.0m, 85.0m),  // CAR below min, NPL critical, LCR breach
        }, extended: new PrudentialExtended
        {
            ROA = -2.5m, CIR = 92.0m, FXExposureRatio = 25.0m,
            ProvisioningCoverage = 35.0m, NSFR = 88.0m,
            ComplianceScore = 42.0m, LateFilingCount = 4,
            AuditOpinionCode = "ADVERSE"
        });

        var result = await _fx.CAMELSScorer.ScoreInstitutionAsync(
            4, "2026-Q1", Guid.NewGuid());

        Assert.Equal(RiskBand.Critical, result.RiskBand);
        Assert.True(result.CompositeScore > 3.5,
            $"Expected CRITICAL score > 3.5, got {result.CompositeScore}");
        Assert.Equal(5, result.ManagementScore);  // ADVERSE opinion → 5
    }

    // ── Test 5: Systemic aggregation counts breach entities ─────────────
    [Fact]
    public async Task SystemicAggregator_MultipleBreaches_ReturnsCorrectCounts()
    {
        // 3 DMBs: 2 breach NPL, 1 breaches LCR
        await _fx.SeedSectorMetricsAsync("DMB", "2026-Q1", new[]
        {
            (5, 17.0m, 8.2m, 120.0m),   // NPL breach
            (6, 16.0m, 6.5m, 95.0m),    // NPL breach + LCR breach
            (7, 19.0m, 3.1m, 145.0m),   // Healthy
        });

        var result = await _fx.SystemicAggregator.AggregateAsync(
            "CBN", "DMB", "2026-Q1", Guid.NewGuid());

        Assert.Equal(3, result.EntityCount);
        Assert.Equal(2, result.EntitiesBreachingNPL);
        Assert.Equal(1, result.EntitiesBreachingLCR);
        Assert.True(result.SystemicRiskScore > 0);
    }

    // ── Test 6: Contagion identifies D-SIB ──────────────────────────────
    [Fact]
    public async Task ContagionAnalyzer_HubBank_MarkedAsDSIB()
    {
        // Seed interbank exposures: Bank 8 lends to 5 others (hub)
        await _fx.SeedInterbankExposuresAsync("2026-Q1", new[]
        {
            (8, 9,  50_000m),   // Bank 8 → Bank 9
            (8, 10, 80_000m),   // Bank 8 → Bank 10
            (8, 11, 60_000m),   // Bank 8 → Bank 11
            (8, 12, 45_000m),   // Bank 8 → Bank 12
            (8, 13, 70_000m),   // Bank 8 → Bank 13
            (9, 10, 20_000m),   // Bank 9 → Bank 10
        });

        var nodes = await _fx.ContagionAnalyzer.AnalyzeAsync(
            "CBN", "2026-Q1", Guid.NewGuid());

        var hubNode = nodes.FirstOrDefault(n => n.InstitutionId == 8);
        Assert.NotNull(hubNode);
        Assert.True(hubNode!.IsSystemicallyImportant,
            "Hub bank with 5 counterparties should be D-SIB.");
        Assert.True(hubNode.EigenvectorCentrality >=
            nodes.Average(n => n.EigenvectorCentrality),
            "D-SIB should have above-average eigenvector centrality.");
    }

    // ── Test 7: Supervisory action generated for CRITICAL EWI ───────────
    [Fact]
    public async Task SupervisoryActionEngine_CriticalEWI_GeneratesWarningLetter()
    {
        var runId = Guid.NewGuid();

        // Insert a CRITICAL trigger manually
        await using var conn = await _fx.Db.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO EWITriggers
                (EWICode, InstitutionId, RegulatorCode, PeriodCode,
                 Severity, TriggerValue, ThresholdValue, IsActive, IsSystemic, ComputationRunId)
            VALUES ('LCR_BREACH', 14, 'CBN', '2026-Q1',
                    'CRITICAL', 87.5, 100.0, 1, 0, @RunId)
            """,
            new { RunId = runId });

        var actionIds = await _fx.ActionEngine.GenerateActionsForRunAsync(runId, "CBN");

        Assert.NotEmpty(actionIds);

        var action = await conn.QuerySingleAsync<ActionCheckRow>(
            "SELECT ActionType, Severity, Status, EscalationLevel, LetterContent FROM SupervisoryActions WHERE Id=@Id",
            new { Id = actionIds[0] });

        Assert.Equal("WARNING_LETTER", action.ActionType);
        Assert.Equal("CRITICAL", action.Severity);
        Assert.Equal("DRAFT", action.Status);
        Assert.Equal(3, action.EscalationLevel);  // Director level for CRITICAL
        Assert.Contains("CENTRAL BANK OF NIGERIA", action.LetterContent);
        Assert.Contains("LCR_BREACH", action.LetterContent);
    }

    // ── Test 8: Full cycle run completes without errors ──────────────────
    [Fact]
    public async Task EWIEngine_FullCycleRun_CompletesSuccessfully()
    {
        // Seed a mix of healthy and distressed institutions
        await _fx.SeedFullSectorAsync("2026-Q1");

        var summary = await _fx.EWIEngine.RunFullCycleAsync("CBN", "2026-Q1");

        Assert.True(summary.EntitiesEvaluated >= 4);
        Assert.True(summary.EWIsTriggered >= 0);
        Assert.True(summary.Duration.TotalSeconds < 30);  // must complete within 30s

        // Verify run is recorded
        await using var conn = await _fx.Db.OpenAsync();
        var run = await conn.QuerySingleOrDefaultAsync<(string Status, int Entities)>(
            "SELECT Status, EntitiesEvaluated FROM EWIComputationRuns WHERE ComputationRunId=@Id",
            new { Id = summary.ComputationRunId });

        Assert.NotNull(run);
        Assert.Equal("COMPLETED", run.Status);
    }
}

// ── Test fixture ────────────────────────────────────────────────────────────
public sealed class EWITestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("EWI_Test_P@ss1!")
        .Build();

    public IEWIEngine EWIEngine { get; private set; } = null!;
    public ICAMELSScorer CAMELSScorer { get; private set; } = null!;
    public ISystemicRiskAggregator SystemicAggregator { get; private set; } = null!;
    public IContagionAnalyzer ContagionAnalyzer { get; private set; } = null!;
    public ISupervisoryActionEngine ActionEngine { get; private set; } = null!;
    public IDbConnectionFactory Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _sqlContainer.StartAsync();
        var cs = _sqlContainer.GetConnectionString();
        await new DatabaseMigrator(cs).MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(cs));
        services.AddEarlyWarningEngine(new ConfigurationBuilder().Build());

        var sp = services.BuildServiceProvider();
        Db = sp.GetRequiredService<IDbConnectionFactory>();
        EWIEngine = sp.GetRequiredService<IEWIEngine>();
        CAMELSScorer = sp.GetRequiredService<ICAMELSScorer>();
        SystemicAggregator = sp.GetRequiredService<ISystemicRiskAggregator>();
        ContagionAnalyzer = sp.GetRequiredService<IContagionAnalyzer>();
        ActionEngine = sp.GetRequiredService<ISupervisoryActionEngine>();

        await SeedBaseDataAsync();
    }

    private async Task SeedBaseDataAsync()
    {
        await using var conn = await Db.OpenAsync();
        await conn.ExecuteAsync(
            """
            -- Seed CBN as regulator
            IF NOT EXISTS (SELECT 1 FROM Regulators WHERE Code='CBN')
                INSERT INTO Regulators (Code, Name) VALUES ('CBN','Central Bank of Nigeria');

            -- Seed test institutions
            MERGE Institutions AS t
            USING (VALUES
                (1, 'CBN', 'DMB', 'FBN-001', 'First Bank of Nigeria'),
                (2, 'CBN', 'DMB', 'DMB-002', 'Access Bank'),
                (3, 'CBN', 'DMB', 'DMB-003', 'Zenith Bank'),
                (4, 'CBN', 'DMB', 'DMB-004', 'Heritage Bank'),
                (5, 'CBN', 'DMB', 'DMB-005', 'Fidelity Bank'),
                (6, 'CBN', 'DMB', 'DMB-006', 'Sterling Bank'),
                (7, 'CBN', 'DMB', 'DMB-007', 'Wema Bank'),
                (8, 'CBN', 'DMB', 'DMB-008', 'GTBank'),
                (9, 'CBN', 'DMB', 'DMB-009', 'UBA'),
                (10,'CBN', 'DMB', 'DMB-010', 'Ecobank'),
                (11,'CBN', 'DMB', 'DMB-011', 'Keystone Bank'),
                (12,'CBN', 'DMB', 'DMB-012', 'Polaris Bank'),
                (13,'CBN', 'DMB', 'DMB-013', 'FCMB'),
                (14,'CBN', 'DMB', 'DMB-014', 'Unity Bank')
            ) AS s (Id, RegulatorCode, InstitutionType, LicenseNumber, ShortName)
            ON t.Id = s.Id
            WHEN NOT MATCHED THEN
                INSERT (Id, RegulatorCode, InstitutionType, LicenseNumber, ShortName, IsActive)
                VALUES (s.Id, s.RegulatorCode, s.InstitutionType, s.LicenseNumber, s.ShortName, 1);
            """);
    }

    public async Task SeedPrudentialMetricsAsync(
        int institutionId,
        (string PeriodCode, decimal CAR, decimal NPL, decimal LCR)[] metrics,
        PrudentialExtended? extended = null)
    {
        await using var conn = await Db.OpenAsync();
        foreach (var m in metrics)
        {
            await conn.ExecuteAsync(
                """
                MERGE PrudentialMetrics AS t
                USING (VALUES (@Id, @Period)) AS s(InstitutionId, PeriodCode)
                ON t.InstitutionId=s.InstitutionId AND t.PeriodCode=s.PeriodCode
                WHEN MATCHED THEN
                    UPDATE SET CAR=@CAR, NPLRatio=@NPL, LCR=@LCR,
                               ROA=@ROA, CIR=@CIR, FXExposureRatio=@FX,
                               ProvisioningCoverage=@PC, NSFR=@NSFR,
                               ComplianceScore=@CS, LateFilingCount=@LF,
                               AuditOpinionCode=@AOC,
                               InstitutionType='DMB', RegulatorCode='CBN',
                               AsOfDate=CAST(SYSUTCDATETIME() AS DATE)
                WHEN NOT MATCHED THEN
                    INSERT (InstitutionId, RegulatorCode, InstitutionType, AsOfDate, PeriodCode,
                            CAR, NPLRatio, LCR, ROA, CIR, FXExposureRatio,
                            ProvisioningCoverage, NSFR, ComplianceScore,
                            LateFilingCount, AuditOpinionCode)
                    VALUES (@Id, 'CBN', 'DMB', CAST(SYSUTCDATETIME() AS DATE), @Period,
                            @CAR, @NPL, @LCR, @ROA, @CIR, @FX,
                            @PC, @NSFR, @CS, @LF, @AOC);
                """,
                new { Id = institutionId, Period = m.PeriodCode,
                      CAR = m.CAR, NPL = m.NPL, LCR = m.LCR,
                      ROA = extended?.ROA ?? 1.5m,
                      CIR = extended?.CIR ?? 60.0m,
                      FX  = extended?.FXExposureRatio ?? 5.0m,
                      PC  = extended?.ProvisioningCoverage ?? 65.0m,
                      NSFR = extended?.NSFR ?? 110.0m,
                      CS  = extended?.ComplianceScore ?? 80.0m,
                      LF  = extended?.LateFilingCount ?? 0,
                      AOC = extended?.AuditOpinionCode ?? "CLEAN" });
        }
    }

    public async Task SeedSectorMetricsAsync(
        string institutionType, string periodCode,
        (int Id, decimal CAR, decimal NPL, decimal LCR)[] metrics)
    {
        foreach (var m in metrics)
        {
            await SeedPrudentialMetricsAsync(m.Id,
                new[] { (periodCode, m.CAR, m.NPL, m.LCR) });
        }
    }

    public async Task SeedInterbankExposuresAsync(
        string periodCode,
        (int Lender, int Borrower, decimal Amount)[] exposures)
    {
        await using var conn = await Db.OpenAsync();
        foreach (var e in exposures)
        {
            await conn.ExecuteAsync(
                """
                IF NOT EXISTS (
                    SELECT 1 FROM InterbankExposures
                    WHERE LendingInstitutionId=@L AND BorrowingInstitutionId=@B
                      AND ExposureType='PLACEMENT' AND PeriodCode=@P)
                INSERT INTO InterbankExposures
                    (LendingInstitutionId, BorrowingInstitutionId, RegulatorCode,
                     PeriodCode, ExposureAmount, ExposureType, AsOfDate)
                VALUES (@L, @B, 'CBN', @P, @Amount, 'PLACEMENT',
                        CAST(SYSUTCDATETIME() AS DATE))
                """,
                new { L = e.Lender, B = e.Borrower, P = periodCode, Amount = e.Amount });
        }
    }

    public async Task SeedFullSectorAsync(string periodCode)
    {
        await SeedSectorMetricsAsync("DMB", periodCode, new[]
        {
            (1,  21.0m, 2.5m, 145.0m),
            (2,  17.0m, 7.8m,  98.0m),   // NPL breach + LCR breach
            (3,  19.5m, 3.2m, 130.0m),
            (4,  13.5m, 15.0m, 85.0m),   // CAR breach + NPL + LCR
        });
    }

    private sealed record ActionCheckRow(
        string ActionType, string Severity, string Status,
        int EscalationLevel, string LetterContent);

    public sealed record PrudentialExtended(
        decimal ROA = 1.5m, decimal CIR = 60.0m, decimal FXExposureRatio = 5.0m,
        decimal ProvisioningCoverage = 65.0m, decimal NSFR = 110.0m,
        decimal ComplianceScore = 80.0m, int LateFilingCount = 0,
        string AuditOpinionCode = "CLEAN");

    public async Task DisposeAsync() => await _sqlContainer.DisposeAsync();
}
```

---

## 15 · Configuration (`appsettings.json`)

```json
{
  "KeyVault": {
    "Uri": "https://regos-kv-prod.vault.azure.net/"
  },
  "EWIEngine": {
    "CycleIntervalMinutes": 60,
    "DefaultRegulatorCode": "CBN",
    "AutoGenerateActions": true,
    "RunContagionAnalysis": true
  },
  "PrudentialThresholds": {
    "CommentOnly": "Thresholds are defined as compile-time constants in PrudentialThresholds.cs — see R-01",
    "DMB_MinCAR": 15.0,
    "MFB_MinCAR": 10.0,
    "NPL_Warning": 5.0,
    "LCR_Minimum": 100.0,
    "LCR_WarningZone": 110.0,
    "DepositConcentration_Cap": 30.0,
    "FXExposure_Cap": 20.0
  },
  "SupervisoryActions": {
    "CriticalDueDays": 7,
    "HighDueDays": 30,
    "MediumDueDays": 60
  },
  "ConnectionStrings": {
    "RegOS": "Server=regos-sql.database.windows.net;Database=RegOS;Authentication=Active Directory Default;"
  }
}
```

---

## 16 · Deliverables Checklist

Confirm every item below is complete before marking RG-36 as **Done**.

| # | Artefact | Rule Ref |
|---|---|---|
| 1 | EF Core migration `20260320_AddEarlyWarningSchema.cs` — 8 tables, all indexes and constraints, 20 EWI seed definitions | R-03 |
| 2 | `PrudentialThresholds` static class — real CBN/NDIC minimums (DMB 15%, MFB 10%, NPL 5%, LCR 100%) | R-01 |
| 3 | `EWIEngine.EvaluateInstitutionAsync` — 17 institutional EWI rules fully implemented across C/A/M/E/L/S | R-02 |
| 4 | `EWIEngine.RunFullCycleAsync` — full sector sweep, systemic EWI aggregation, trigger clearing, run log | R-02, R-06 |
| 5 | `CAMELSScorer.ScoreInstitutionAsync` — complete 6-component CAMELS algorithm with CBN weighting (C20/A20/M15/E20/L15/S10) | R-02 |
| 6 | `SystemicRiskAggregator.AggregateAsync` — sector-wide breach counts, systemic score 0–100, band classification | R-02 |
| 7 | `ContagionAnalyzer.AnalyzeAsync` — directed graph construction, power-iteration eigenvector centrality, Brandes betweenness, D-SIB identification | R-11 |
| 8 | `SupervisoryActionEngine` — auto-generate letters, CBN-formatted letter templates, 4-level escalation (Analyst→Senior Examiner→Director→Governor), remediation tracking, close | R-12 |
| 9 | `HeatmapQueryService` — sector heatmap cells, institution EWI history, Pearson correlation matrix | R-02 |
| 10 | Blazor Server pages — `/supervisor/heatmap` (traffic light bubbles), `/supervisor/institution/{id}` (CAMELS grid + EWI table + actions), `/supervisor/contagion` (D-SIB table) | R-02 |
| 11 | `EWICycleBackgroundService` — hourly cycle: CAMELS → EWI → systemic aggregation → contagion → action generation | R-02 |
| 12 | DI registration `AddEarlyWarningEngine()` — all services, hosted service, `EWIEngineOptions` binding | R-10 |
| 13 | Integration tests — Testcontainers SQL Server; 8 test scenarios covering CAR declining 3Q, LCR breach, CAMELS healthy/distressed, systemic counts, D-SIB identification, action generation, full cycle | R-09 |
| 14 | `appsettings.json` snippet with all EWI configuration keys | R-10 |
| 15 | Zero hardcoded thresholds in SQL, zero cross-regulator data leakage, immutable EWI audit trail (append-only) | R-04, R-05, R-06, R-07 |

---

*End of RG-36 — Supervisory Early Warning & Systemic Risk Engine*