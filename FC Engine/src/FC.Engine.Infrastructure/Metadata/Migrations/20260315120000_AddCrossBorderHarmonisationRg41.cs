#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260315120000_AddCrossBorderHarmonisationRg41")]
public class AddCrossBorderHarmonisationRg41 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
-- ============================================================
-- RG-41: Cross-Border Regulatory Harmonisation Engine
-- ============================================================

-- ── Table: regulatory_jurisdictions ─────────────────────────
IF OBJECT_ID(N'dbo.regulatory_jurisdictions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.regulatory_jurisdictions (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        JurisdictionCode    VARCHAR(3)      NOT NULL,
        CountryName         NVARCHAR(120)   NOT NULL,
        RegulatorCode       VARCHAR(10)     NOT NULL,
        RegulatorName       NVARCHAR(200)   NOT NULL,
        CurrencyCode        VARCHAR(3)      NOT NULL,
        CurrencySymbol      NVARCHAR(10)    NOT NULL,
        TimeZoneId          VARCHAR(50)     NOT NULL,
        RegulatoryFramework VARCHAR(40)     NOT NULL,
        EcowasRegion        BIT             NOT NULL DEFAULT 0,
        AfcftaMember        BIT             NOT NULL DEFAULT 1,
        IsActive            BIT             NOT NULL DEFAULT 1,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_RegJurisdictions_Code UNIQUE (JurisdictionCode),
        CONSTRAINT UQ_RegJurisdictions_Regulator UNIQUE (RegulatorCode)
    );
END

-- ── Table: financial_groups ─────────────────────────────────
IF OBJECT_ID(N'dbo.financial_groups', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.financial_groups (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        GroupCode           VARCHAR(20)     NOT NULL,
        GroupName           NVARCHAR(200)   NOT NULL,
        HeadquarterJurisdiction VARCHAR(3) NOT NULL,
        BaseCurrency        VARCHAR(3)      NOT NULL,
        IsActive            BIT             NOT NULL DEFAULT 1,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_FinancialGroups_Code UNIQUE (GroupCode),
        CONSTRAINT FK_FinancialGroups_HQ
            FOREIGN KEY (HeadquarterJurisdiction) REFERENCES dbo.regulatory_jurisdictions(JurisdictionCode)
    );
END

-- ── Table: group_subsidiaries ───────────────────────────────
IF OBJECT_ID(N'dbo.group_subsidiaries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.group_subsidiaries (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        GroupId             INT             NOT NULL,
        InstitutionId       INT             NOT NULL,
        JurisdictionCode    VARCHAR(3)      NOT NULL,
        SubsidiaryCode      VARCHAR(20)     NOT NULL,
        SubsidiaryName      NVARCHAR(200)   NOT NULL,
        EntityType          VARCHAR(20)     NOT NULL,
        LocalCurrency       VARCHAR(3)      NOT NULL,
        OwnershipPercentage DECIMAL(5,2)    NOT NULL,
        ConsolidationMethod VARCHAR(20)     NOT NULL DEFAULT 'Full',
        IsActive            BIT             NOT NULL DEFAULT 1,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_GroupSubsidiaries_Group
            FOREIGN KEY (GroupId) REFERENCES dbo.financial_groups(Id),
        CONSTRAINT FK_GroupSubsidiaries_Jurisdiction
            FOREIGN KEY (JurisdictionCode) REFERENCES dbo.regulatory_jurisdictions(JurisdictionCode),
        CONSTRAINT UQ_GroupSubsidiaries_Code UNIQUE (GroupId, SubsidiaryCode)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GroupSubsidiaries_GroupJurisdiction')
    CREATE INDEX IX_GroupSubsidiaries_GroupJurisdiction ON dbo.group_subsidiaries (GroupId, JurisdictionCode);

-- ── Table: regulatory_equivalence_mappings ──────────────────
IF OBJECT_ID(N'dbo.regulatory_equivalence_mappings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.regulatory_equivalence_mappings (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        MappingCode         VARCHAR(40)     NOT NULL,
        MappingName         NVARCHAR(200)   NOT NULL,
        ConceptDomain       VARCHAR(40)     NOT NULL,
        Description         NVARCHAR(MAX)   NULL,
        Version             INT             NOT NULL DEFAULT 1,
        IsActive            BIT             NOT NULL DEFAULT 1,
        CreatedByUserId     INT             NOT NULL,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_EquivMappings_CodeVersion UNIQUE (MappingCode, Version)
    );
END

-- ── Table: equivalence_mapping_entries ──────────────────────
IF OBJECT_ID(N'dbo.equivalence_mapping_entries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.equivalence_mapping_entries (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        MappingId           BIGINT          NOT NULL,
        JurisdictionCode    VARCHAR(3)      NOT NULL,
        RegulatorCode       VARCHAR(10)     NOT NULL,
        LocalParameterCode  VARCHAR(40)     NOT NULL,
        LocalParameterName  NVARCHAR(200)   NOT NULL,
        LocalThreshold      DECIMAL(18,6)   NOT NULL,
        ThresholdUnit       VARCHAR(20)     NOT NULL,
        CalculationBasis    NVARCHAR(500)   NOT NULL,
        ReturnFormCode      VARCHAR(30)     NULL,
        ReturnLineReference VARCHAR(60)     NULL,
        RegulatoryFramework VARCHAR(40)     NOT NULL,
        Notes               NVARCHAR(MAX)   NULL,
        DisplayOrder        INT             NOT NULL DEFAULT 0,

        CONSTRAINT FK_EquivEntries_Mapping
            FOREIGN KEY (MappingId) REFERENCES dbo.regulatory_equivalence_mappings(Id) ON DELETE CASCADE,
        CONSTRAINT FK_EquivEntries_Jurisdiction
            FOREIGN KEY (JurisdictionCode) REFERENCES dbo.regulatory_jurisdictions(JurisdictionCode),
        CONSTRAINT UQ_EquivEntries_MappingJurisdiction UNIQUE (MappingId, JurisdictionCode)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EquivEntries_Mapping')
    CREATE INDEX IX_EquivEntries_Mapping ON dbo.equivalence_mapping_entries (MappingId);

-- ── Table: cross_border_fx_rates ────────────────────────────
IF OBJECT_ID(N'dbo.cross_border_fx_rates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.cross_border_fx_rates (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        BaseCurrency        VARCHAR(3)      NOT NULL,
        QuoteCurrency       VARCHAR(3)      NOT NULL,
        RateDate            DATE            NOT NULL,
        Rate                DECIMAL(18,8)   NOT NULL,
        InverseRate         DECIMAL(18,8)   NOT NULL,
        RateSource          VARCHAR(40)     NOT NULL,
        RateType            VARCHAR(20)     NOT NULL DEFAULT 'PeriodEnd',
        IsActive            BIT             NOT NULL DEFAULT 1,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_CBFxRates_PairDateType UNIQUE (BaseCurrency, QuoteCurrency, RateDate, RateType)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CBFxRates_Date')
    CREATE INDEX IX_CBFxRates_Date ON dbo.cross_border_fx_rates (RateDate DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CBFxRates_Base')
    CREATE INDEX IX_CBFxRates_Base ON dbo.cross_border_fx_rates (BaseCurrency, RateDate DESC);

-- ── Table: consolidation_runs ───────────────────────────────
IF OBJECT_ID(N'dbo.consolidation_runs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.consolidation_runs (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        GroupId             INT             NOT NULL,
        RunNumber           INT             NOT NULL,
        ReportingPeriod     VARCHAR(10)     NOT NULL,
        SnapshotDate        DATE            NOT NULL,
        BaseCurrency        VARCHAR(3)      NOT NULL,
        Status              VARCHAR(20)     NOT NULL DEFAULT 'Pending',
        TotalSubsidiaries   INT             NOT NULL DEFAULT 0,
        SubsidiariesCollected INT           NOT NULL DEFAULT 0,
        TotalAdjustments    INT             NOT NULL DEFAULT 0,
        ConsolidatedTotalAssets  DECIMAL(18,2) NULL,
        ConsolidatedTotalCapital DECIMAL(18,2) NULL,
        ConsolidatedCAR     DECIMAL(8,4)    NULL,
        CorrelationId       UNIQUEIDENTIFIER NOT NULL,
        ExecutionTimeMs     BIGINT          NULL,
        ErrorMessage        NVARCHAR(2000)  NULL,
        CreatedByUserId     INT             NOT NULL,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        CompletedAt         DATETIME2(3)    NULL,

        CONSTRAINT FK_ConsolidationRuns_Group
            FOREIGN KEY (GroupId) REFERENCES dbo.financial_groups(Id),
        CONSTRAINT UQ_ConsolidationRuns_Number UNIQUE (GroupId, RunNumber)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsolidationRuns_GroupPeriod')
    CREATE INDEX IX_ConsolidationRuns_GroupPeriod ON dbo.consolidation_runs (GroupId, ReportingPeriod);

-- ── Table: consolidation_subsidiary_snapshots ───────────────
IF OBJECT_ID(N'dbo.consolidation_subsidiary_snapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.consolidation_subsidiary_snapshots (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        RunId               BIGINT          NOT NULL,
        SubsidiaryId        INT             NOT NULL,
        GroupId             INT             NOT NULL,
        JurisdictionCode    VARCHAR(3)      NOT NULL,
        LocalCurrency       VARCHAR(3)      NOT NULL,
        LocalTotalAssets    DECIMAL(18,2)   NOT NULL,
        LocalTotalLiabilities DECIMAL(18,2) NOT NULL,
        LocalTotalCapital   DECIMAL(18,2)   NOT NULL,
        LocalRWA            DECIMAL(18,2)   NOT NULL,
        LocalCAR            DECIMAL(8,4)    NOT NULL,
        LocalLCR            DECIMAL(8,4)    NULL,
        LocalNSFR           DECIMAL(8,4)    NULL,
        FxRateUsed          DECIMAL(18,8)   NOT NULL,
        FxRateDate          DATE            NOT NULL,
        FxRateSource        VARCHAR(40)     NOT NULL,
        ConvertedTotalAssets    DECIMAL(18,2) NOT NULL,
        ConvertedTotalLiabilities DECIMAL(18,2) NOT NULL,
        ConvertedTotalCapital   DECIMAL(18,2) NOT NULL,
        ConvertedRWA            DECIMAL(18,2) NOT NULL,
        OwnershipPercentage DECIMAL(5,2)    NOT NULL,
        ConsolidationMethodUsed VARCHAR(20) NOT NULL,
        AdjustedTotalAssets DECIMAL(18,2)   NOT NULL,
        AdjustedTotalCapital DECIMAL(18,2)  NOT NULL,
        AdjustedRWA         DECIMAL(18,2)   NOT NULL,
        SourceReturnInstanceId BIGINT       NULL,
        DataCollectedAt     DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_ConsolSnapshots_Run
            FOREIGN KEY (RunId) REFERENCES dbo.consolidation_runs(Id),
        CONSTRAINT FK_ConsolSnapshots_Subsidiary
            FOREIGN KEY (SubsidiaryId) REFERENCES dbo.group_subsidiaries(Id)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ConsolSnapshots_Run')
    CREATE INDEX IX_ConsolSnapshots_Run ON dbo.consolidation_subsidiary_snapshots (RunId);

-- ── Table: group_consolidation_adjustments ──────────────────
IF OBJECT_ID(N'dbo.group_consolidation_adjustments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.group_consolidation_adjustments (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        RunId               BIGINT          NOT NULL,
        GroupId             INT             NOT NULL,
        AdjustmentType      VARCHAR(40)     NOT NULL,
        Description         NVARCHAR(500)   NOT NULL,
        AffectedSubsidiaryId INT           NULL,
        DebitAccount        VARCHAR(40)     NOT NULL,
        CreditAccount       VARCHAR(40)     NOT NULL,
        Amount              DECIMAL(18,2)   NOT NULL,
        Currency            VARCHAR(3)      NOT NULL,
        IsAutomatic         BIT             NOT NULL DEFAULT 1,
        AppliedByUserId     INT             NULL,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_GroupConsolAdj_Run
            FOREIGN KEY (RunId) REFERENCES dbo.consolidation_runs(Id)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_GroupConsolAdj_Run')
    CREATE INDEX IX_GroupConsolAdj_Run ON dbo.group_consolidation_adjustments (RunId);

-- ── Table: cross_border_data_flows ──────────────────────────
IF OBJECT_ID(N'dbo.cross_border_data_flows', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.cross_border_data_flows (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        GroupId             INT             NOT NULL,
        FlowCode            VARCHAR(40)     NOT NULL,
        FlowName            NVARCHAR(200)   NOT NULL,
        SourceJurisdiction  VARCHAR(3)      NOT NULL,
        SourceReturnCode    VARCHAR(30)     NOT NULL,
        SourceLineCode      VARCHAR(60)     NOT NULL,
        TargetJurisdiction  VARCHAR(3)      NOT NULL,
        TargetReturnCode    VARCHAR(30)     NOT NULL,
        TargetLineCode      VARCHAR(60)     NOT NULL,
        TransformationType  VARCHAR(30)     NOT NULL DEFAULT 'Direct',
        TransformationFormula NVARCHAR(500) NULL,
        RequiresCurrencyConversion BIT      NOT NULL DEFAULT 0,
        IsActive            BIT             NOT NULL DEFAULT 1,
        CreatedByUserId     INT             NOT NULL,
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_DataFlows_Group
            FOREIGN KEY (GroupId) REFERENCES dbo.financial_groups(Id),
        CONSTRAINT UQ_DataFlows_Code UNIQUE (GroupId, FlowCode)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DataFlows_Source')
    CREATE INDEX IX_DataFlows_Source ON dbo.cross_border_data_flows (SourceJurisdiction, SourceReturnCode);

-- ── Table: data_flow_executions ─────────────────────────────
IF OBJECT_ID(N'dbo.data_flow_executions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.data_flow_executions (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        FlowId              BIGINT          NOT NULL,
        GroupId             INT             NOT NULL,
        ReportingPeriod     VARCHAR(10)     NOT NULL,
        SourceValue         DECIMAL(18,6)   NOT NULL,
        SourceCurrency      VARCHAR(3)      NOT NULL,
        FxRateApplied       DECIMAL(18,8)   NULL,
        ConvertedValue      DECIMAL(18,6)   NULL,
        TargetValue         DECIMAL(18,6)   NOT NULL,
        TargetCurrency      VARCHAR(3)      NOT NULL,
        Status              VARCHAR(20)     NOT NULL,
        ErrorMessage        NVARCHAR(500)   NULL,
        CorrelationId       UNIQUEIDENTIFIER NOT NULL,
        ExecutedAt          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_DataFlowExec_Flow
            FOREIGN KEY (FlowId) REFERENCES dbo.cross_border_data_flows(Id)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DataFlowExec_FlowPeriod')
    CREATE INDEX IX_DataFlowExec_FlowPeriod ON dbo.data_flow_executions (FlowId, ReportingPeriod);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DataFlowExec_Correlation')
    CREATE INDEX IX_DataFlowExec_Correlation ON dbo.data_flow_executions (CorrelationId);

-- ── Table: regulatory_divergences ───────────────────────────
IF OBJECT_ID(N'dbo.regulatory_divergences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.regulatory_divergences (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        MappingId           BIGINT          NOT NULL,
        ConceptDomain       VARCHAR(40)     NOT NULL,
        DivergenceType      VARCHAR(30)     NOT NULL,
        SourceJurisdiction  VARCHAR(3)      NOT NULL,
        AffectedJurisdictions NVARCHAR(200) NOT NULL,
        PreviousValue       NVARCHAR(200)   NULL,
        NewValue            NVARCHAR(200)   NULL,
        Description         NVARCHAR(MAX)   NOT NULL,
        Severity            VARCHAR(10)     NOT NULL,
        Status              VARCHAR(20)     NOT NULL DEFAULT 'Open',
        DetectedAt          DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        ResolvedAt          DATETIME2(3)    NULL,
        DetectedBySystem    BIT             NOT NULL DEFAULT 1,
        AcknowledgedByUserId INT           NULL,

        CONSTRAINT FK_Divergences_Mapping
            FOREIGN KEY (MappingId) REFERENCES dbo.regulatory_equivalence_mappings(Id)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergences_StatusSeverity')
    CREATE INDEX IX_Divergences_StatusSeverity ON dbo.regulatory_divergences (Status, Severity);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Divergences_DomainStatus')
    CREATE INDEX IX_Divergences_DomainStatus ON dbo.regulatory_divergences (ConceptDomain, Status);

-- ── Table: divergence_notifications ─────────────────────────
IF OBJECT_ID(N'dbo.divergence_notifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.divergence_notifications (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        DivergenceId        BIGINT          NOT NULL,
        GroupId             INT             NOT NULL,
        NotifiedUserId      INT             NOT NULL,
        NotificationChannel VARCHAR(20)     NOT NULL DEFAULT 'IN_APP',
        Status              VARCHAR(20)     NOT NULL DEFAULT 'SENT',
        SentAt              DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),
        ReadAt              DATETIME2(3)    NULL,

        CONSTRAINT FK_DivNotif_Divergence
            FOREIGN KEY (DivergenceId) REFERENCES dbo.regulatory_divergences(Id)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DivNotif_GroupStatus')
    CREATE INDEX IX_DivNotif_GroupStatus ON dbo.divergence_notifications (GroupId, Status);

-- ── Table: afcfta_protocol_tracking ─────────────────────────
IF OBJECT_ID(N'dbo.afcfta_protocol_tracking', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.afcfta_protocol_tracking (
        Id                  INT IDENTITY(1,1) PRIMARY KEY,
        ProtocolCode        VARCHAR(40)     NOT NULL,
        ProtocolName        NVARCHAR(200)   NOT NULL,
        Category            VARCHAR(40)     NOT NULL,
        Status              VARCHAR(20)     NOT NULL DEFAULT 'Proposed',
        ParticipatingJurisdictions NVARCHAR(500) NOT NULL,
        TargetEffectiveDate DATE            NULL,
        ActualEffectiveDate DATE            NULL,
        Description         NVARCHAR(MAX)   NULL,
        ImpactOnRegOS       NVARCHAR(MAX)   NULL,
        LastUpdated         DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT UQ_AfcftaProtocol_Code UNIQUE (ProtocolCode)
    );
END

-- ── Table: regulatory_deadlines ─────────────────────────────
IF OBJECT_ID(N'dbo.regulatory_deadlines', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.regulatory_deadlines (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        JurisdictionCode    VARCHAR(3)      NOT NULL,
        RegulatorCode       VARCHAR(10)     NOT NULL,
        ReturnCode          VARCHAR(30)     NOT NULL,
        ReturnName          NVARCHAR(200)   NOT NULL,
        ReportingPeriod     VARCHAR(10)     NOT NULL,
        DeadlineUtc         DATETIMEOFFSET(3) NOT NULL,
        LocalTimeZone       VARCHAR(50)     NOT NULL,
        Frequency           VARCHAR(20)     NOT NULL,
        GroupId             INT             NULL,
        Status              VARCHAR(20)     NOT NULL DEFAULT 'Upcoming',
        CreatedAt           DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME(),

        CONSTRAINT FK_Deadlines_Jurisdiction
            FOREIGN KEY (JurisdictionCode) REFERENCES dbo.regulatory_jurisdictions(JurisdictionCode)
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Deadlines_GroupDeadline')
    CREATE INDEX IX_Deadlines_GroupDeadline ON dbo.regulatory_deadlines (GroupId, DeadlineUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Deadlines_JurisdictionDeadline')
    CREATE INDEX IX_Deadlines_JurisdictionDeadline ON dbo.regulatory_deadlines (JurisdictionCode, DeadlineUtc);

-- ── Table: harmonisation_audit_log ──────────────────────────
IF OBJECT_ID(N'dbo.harmonisation_audit_log', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.harmonisation_audit_log (
        Id                  BIGINT IDENTITY(1,1) PRIMARY KEY,
        GroupId             INT             NULL,
        JurisdictionCode    VARCHAR(3)      NULL,
        CorrelationId       UNIQUEIDENTIFIER NOT NULL,
        Action              VARCHAR(50)     NOT NULL,
        Detail              NVARCHAR(MAX)   NULL,
        PerformedByUserId   INT             NULL,
        PerformedAt         DATETIME2(3)    NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HarmonisationAudit_Group')
    CREATE INDEX IX_HarmonisationAudit_Group ON dbo.harmonisation_audit_log (GroupId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HarmonisationAudit_Correlation')
    CREATE INDEX IX_HarmonisationAudit_Correlation ON dbo.harmonisation_audit_log (CorrelationId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HarmonisationAudit_Time')
    CREATE INDEX IX_HarmonisationAudit_Time ON dbo.harmonisation_audit_log (PerformedAt DESC);

-- ═══════════════════════════════════════════════════════════════
-- SEED DATA
-- ═══════════════════════════════════════════════════════════════

-- ── Jurisdictions ───────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.regulatory_jurisdictions WHERE JurisdictionCode = 'NG')
BEGIN
    INSERT INTO dbo.regulatory_jurisdictions
        (JurisdictionCode, CountryName, RegulatorCode, RegulatorName, CurrencyCode, CurrencySymbol, TimeZoneId, RegulatoryFramework, EcowasRegion, AfcftaMember)
    VALUES
        ('NG', 'Nigeria',        'CBN',  'Central Bank of Nigeria',             'NGN', N'₦',     'Africa/Lagos',          'BASEL_III',               1, 1),
        ('GH', 'Ghana',          'BOG',  'Bank of Ghana',                       'GHS', N'GH₵',   'Africa/Accra',          'BASEL_III_TRANSITIONAL',  1, 1),
        ('KE', 'Kenya',          'CBK',  'Central Bank of Kenya',               'KES', 'KSh',    'Africa/Nairobi',        'BASEL_III',               0, 1),
        ('ZA', 'South Africa',   'SARB', 'South African Reserve Bank',          'ZAR', 'R',      'Africa/Johannesburg',   'BASEL_III',               0, 1),
        ('EG', 'Egypt',          'CBE',  'Central Bank of Egypt',               'EGP', N'E£',    'Africa/Cairo',          'BASEL_III_TRANSITIONAL',  0, 1),
        ('CI', N'Côte d''Ivoire','BCEAO','Central Bank of West African States', 'XOF', 'CFA',    'Africa/Abidjan',        'BASEL_II',                1, 1),
        ('TZ', 'Tanzania',       'BOT',  'Bank of Tanzania',                    'TZS', 'TSh',    'Africa/Dar_es_Salaam',  'BASEL_II',                0, 1),
        ('RW', 'Rwanda',         'BNR',  'National Bank of Rwanda',             'RWF', 'FRw',    'Africa/Kigali',         'BASEL_III_TRANSITIONAL',  0, 1);
END

-- ── Financial Groups ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.financial_groups WHERE GroupCode = 'ACCESSGRP')
BEGIN
    INSERT INTO dbo.financial_groups (GroupCode, GroupName, HeadquarterJurisdiction, BaseCurrency)
    VALUES
        ('ACCESSGRP', 'Access Holdings Plc',           'NG', 'NGN'),
        ('UBAGRP',    'United Bank for Africa Plc',    'NG', 'NGN'),
        ('ECOGRP',    'Ecobank Transnational Inc.',    'CI', 'XOF');
END

-- ── Subsidiaries (Access Holdings) ──────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.group_subsidiaries WHERE SubsidiaryCode = 'ACCESS-NG')
BEGIN
    DECLARE @accessGroupId INT = (SELECT Id FROM dbo.financial_groups WHERE GroupCode = 'ACCESSGRP');
    IF @accessGroupId IS NOT NULL
    BEGIN
        INSERT INTO dbo.group_subsidiaries
            (GroupId, InstitutionId, JurisdictionCode, SubsidiaryCode, SubsidiaryName, EntityType, LocalCurrency, OwnershipPercentage, ConsolidationMethod)
        VALUES
            (@accessGroupId, 1,  'NG', 'ACCESS-NG', 'Access Bank Plc (Nigeria)',    'DMB',             'NGN', 100.00, 'Full'),
            (@accessGroupId, 10, 'GH', 'ACCESS-GH', 'Access Bank Ghana Plc',        'COMMERCIAL_BANK', 'GHS',  97.50, 'Full'),
            (@accessGroupId, 20, 'KE', 'ACCESS-KE', 'Access Bank Kenya Plc',        'COMMERCIAL_BANK', 'KES', 100.00, 'Full'),
            (@accessGroupId, 30, 'ZA', 'ACCESS-ZA', 'Access Bank South Africa Ltd', 'COMMERCIAL_BANK', 'ZAR',  95.00, 'Full'),
            (@accessGroupId, 40, 'RW', 'ACCESS-RW', 'Access Bank Rwanda Plc',       'COMMERCIAL_BANK', 'RWF', 100.00, 'Full');
    END
END

-- ── Equivalence Mapping: CAR ────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.regulatory_equivalence_mappings WHERE MappingCode = 'CAR_MAPPING')
BEGIN
    INSERT INTO dbo.regulatory_equivalence_mappings (MappingCode, MappingName, ConceptDomain, Description, CreatedByUserId)
    VALUES ('CAR_MAPPING', 'Capital Adequacy Ratio Cross-Border Equivalence', 'CAPITAL_ADEQUACY',
            'Maps the minimum CAR requirements across African jurisdictions.', 1);

    DECLARE @carMappingId BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.equivalence_mapping_entries
        (MappingId, JurisdictionCode, RegulatorCode, LocalParameterCode, LocalParameterName, LocalThreshold, ThresholdUnit, CalculationBasis, ReturnFormCode, ReturnLineReference, RegulatoryFramework)
    VALUES
        (@carMappingId, 'NG', 'CBN',  'MIN_CAR',      'CBN Minimum CAR',            10.000000, 'PERCENTAGE', 'Total Qualifying Capital / Total Risk-Weighted Assets',       'SRF-001',    'SRF-001.L45',    'BASEL_III'),
        (@carMappingId, 'GH', 'BOG',  'CAR_FLOOR',    'BOG Capital Adequacy Floor',  10.000000, 'PERCENTAGE', 'Total Regulatory Capital / Risk-Weighted Assets',             'BSD-CAR-01', 'BSD-CAR-01.R12', 'BASEL_III_TRANSITIONAL'),
        (@carMappingId, 'KE', 'CBK',  'MIN_CORE_CAR', 'CBK Core Capital to TRWA',    10.500000, 'PERCENTAGE', 'Core Capital / Total Risk-Weighted Assets',                   'CBK-PR-01',  'CBK-PR-01.C15',  'BASEL_III'),
        (@carMappingId, 'ZA', 'SARB', 'MIN_CAR_SA',   'SARB Minimum CAR',            10.000000, 'PERCENTAGE', 'Qualifying Capital and Reserve Funds / Risk-Weighted Assets', 'BA-700',     'BA-700.L22',     'BASEL_III'),
        (@carMappingId, 'EG', 'CBE',  'MIN_CAR_EG',   'CBE Minimum CAR',             12.500000, 'PERCENTAGE', 'Total Capital / Risk-Weighted Exposures',                     'CBE-CAR-01', 'CBE-CAR-01.F08', 'BASEL_III_TRANSITIONAL');
END

-- ── FX Rates ────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.cross_border_fx_rates WHERE BaseCurrency = 'NGN' AND QuoteCurrency = 'GHS')
BEGIN
    INSERT INTO dbo.cross_border_fx_rates (BaseCurrency, QuoteCurrency, RateDate, Rate, InverseRate, RateSource, RateType)
    VALUES
        ('NGN', 'GHS', '2026-03-31', 0.00800000, 125.00000000, 'CBN_OFFICIAL',  'PeriodEnd'),
        ('NGN', 'KES', '2026-03-31', 0.08600000,  11.62790698, 'CBN_OFFICIAL',  'PeriodEnd'),
        ('NGN', 'ZAR', '2026-03-31', 0.01200000,  83.33333333, 'CBN_OFFICIAL',  'PeriodEnd'),
        ('NGN', 'EGP', '2026-03-31', 0.03250000,  30.76923077, 'CBN_OFFICIAL',  'PeriodEnd'),
        ('NGN', 'RWF', '2026-03-31', 0.87000000,   1.14942529, 'CBN_OFFICIAL',  'PeriodEnd'),
        ('NGN', 'USD', '2026-03-31', 0.00064000,1562.50000000, 'CBN_OFFICIAL',  'PeriodEnd'),
        ('GHS', 'NGN', '2026-03-31', 125.000000,   0.00800000, 'BOG_REFERENCE', 'PeriodEnd'),
        ('KES', 'NGN', '2026-03-31',  11.627907,   0.08600000, 'CBK_REFERENCE', 'PeriodEnd');
END

-- ── AfCFTA Protocols ────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM dbo.afcfta_protocol_tracking WHERE ProtocolCode = 'AFCFTA_FS_PASSPORT')
BEGIN
    INSERT INTO dbo.afcfta_protocol_tracking (ProtocolCode, ProtocolName, Category, Status, ParticipatingJurisdictions, TargetEffectiveDate, Description, ImpactOnRegOS)
    VALUES
        ('AFCFTA_FS_PASSPORT',   'AfCFTA Financial Services Passporting Protocol',  'PASSPORTING',               'Negotiating', 'NG,GH,KE,ZA,EG,CI,TZ,RW', '2028-01-01', 'Single licence for cross-border financial services within AfCFTA member states.', 'Enable licence verification cross-border, harmonise reporting templates.'),
        ('AFCFTA_FS_PRUDENTIAL', 'AfCFTA Prudential Standards Convergence',         'PRUDENTIAL_HARMONISATION',  'Proposed',    'NG,GH,KE,ZA,EG',           '2029-06-01', 'Convergence of minimum prudential standards (CAR, LCR) across member states.',    'Support unified threshold management, dual-reporting during transition.'),
        ('AFCFTA_FS_MARKET',     'AfCFTA Financial Market Access Protocol',          'MARKET_ACCESS',             'Proposed',    'NG,GH,KE,ZA,EG,CI',        NULL,         'Mutual recognition of banking licences and reduced branch establishment barriers.','Support multi-jurisdiction licensing status tracking.');
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.harmonisation_audit_log', N'U') IS NOT NULL DROP TABLE dbo.harmonisation_audit_log;
IF OBJECT_ID(N'dbo.regulatory_deadlines', N'U') IS NOT NULL DROP TABLE dbo.regulatory_deadlines;
IF OBJECT_ID(N'dbo.afcfta_protocol_tracking', N'U') IS NOT NULL DROP TABLE dbo.afcfta_protocol_tracking;
IF OBJECT_ID(N'dbo.divergence_notifications', N'U') IS NOT NULL DROP TABLE dbo.divergence_notifications;
IF OBJECT_ID(N'dbo.regulatory_divergences', N'U') IS NOT NULL DROP TABLE dbo.regulatory_divergences;
IF OBJECT_ID(N'dbo.data_flow_executions', N'U') IS NOT NULL DROP TABLE dbo.data_flow_executions;
IF OBJECT_ID(N'dbo.cross_border_data_flows', N'U') IS NOT NULL DROP TABLE dbo.cross_border_data_flows;
IF OBJECT_ID(N'dbo.group_consolidation_adjustments', N'U') IS NOT NULL DROP TABLE dbo.group_consolidation_adjustments;
IF OBJECT_ID(N'dbo.consolidation_subsidiary_snapshots', N'U') IS NOT NULL DROP TABLE dbo.consolidation_subsidiary_snapshots;
IF OBJECT_ID(N'dbo.consolidation_runs', N'U') IS NOT NULL DROP TABLE dbo.consolidation_runs;
IF OBJECT_ID(N'dbo.cross_border_fx_rates', N'U') IS NOT NULL DROP TABLE dbo.cross_border_fx_rates;
IF OBJECT_ID(N'dbo.equivalence_mapping_entries', N'U') IS NOT NULL DROP TABLE dbo.equivalence_mapping_entries;
IF OBJECT_ID(N'dbo.regulatory_equivalence_mappings', N'U') IS NOT NULL DROP TABLE dbo.regulatory_equivalence_mappings;
IF OBJECT_ID(N'dbo.group_subsidiaries', N'U') IS NOT NULL DROP TABLE dbo.group_subsidiaries;
IF OBJECT_ID(N'dbo.financial_groups', N'U') IS NOT NULL DROP TABLE dbo.financial_groups;
IF OBJECT_ID(N'dbo.regulatory_jurisdictions', N'U') IS NOT NULL DROP TABLE dbo.regulatory_jurisdictions;
");
    }
}
