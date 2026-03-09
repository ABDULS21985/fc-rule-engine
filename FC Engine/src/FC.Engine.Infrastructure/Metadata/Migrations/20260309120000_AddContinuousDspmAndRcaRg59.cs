#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260309120000_AddContinuousDspmAndRcaRg59")]
public partial class AddContinuousDspmAndRcaRg59 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.data_source_registrations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.data_source_registrations (
                    Id                      UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId                UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    SourceName              NVARCHAR(200)       NOT NULL,
                    SourceType              NVARCHAR(50)        NOT NULL,
                    ConnectionIdentifier    NVARCHAR(200)       NULL,
                    EncryptionAtRestEnabled BIT                 NOT NULL DEFAULT 1,
                    TlsRequired             BIT                 NOT NULL DEFAULT 1,
                    FilesystemRootPath      NVARCHAR(500)       NULL,
                    SchemaJson              NVARCHAR(MAX)       NOT NULL,
                    PostureScore            DECIMAL(5,2)        NOT NULL DEFAULT 100,
                    MetadataJson            NVARCHAR(MAX)       NULL,
                    CreatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    LastScannedAt           DATETIME2           NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_data_source_registrations_TenantId' AND object_id = OBJECT_ID('dbo.data_source_registrations'))
                CREATE INDEX IX_data_source_registrations_TenantId ON dbo.data_source_registrations(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_data_source_registrations_Tenant_SourceName' AND object_id = OBJECT_ID('dbo.data_source_registrations'))
                CREATE UNIQUE INDEX IX_data_source_registrations_Tenant_SourceName
                    ON dbo.data_source_registrations(TenantId, SourceName);

            IF OBJECT_ID(N'dbo.cyber_assets', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.cyber_assets (
                    Id                      UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId                UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    AssetKey                NVARCHAR(100)       NOT NULL,
                    DisplayName             NVARCHAR(200)       NOT NULL,
                    AssetType               NVARCHAR(50)        NOT NULL,
                    Criticality             NVARCHAR(20)        NOT NULL,
                    LinkedDataSourceId      UNIQUEIDENTIFIER    NULL,
                    DataClassificationsJson NVARCHAR(MAX)       NOT NULL,
                    MetadataJson            NVARCHAR(MAX)       NULL,
                    CreatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cyber_assets_TenantId' AND object_id = OBJECT_ID('dbo.cyber_assets'))
                CREATE INDEX IX_cyber_assets_TenantId ON dbo.cyber_assets(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cyber_assets_Tenant_AssetKey' AND object_id = OBJECT_ID('dbo.cyber_assets'))
                CREATE UNIQUE INDEX IX_cyber_assets_Tenant_AssetKey ON dbo.cyber_assets(TenantId, AssetKey);

            IF OBJECT_ID(N'dbo.cyber_asset_dependencies', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.cyber_asset_dependencies (
                    Id               UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId         UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    AssetId          UNIQUEIDENTIFIER    NOT NULL,
                    DependsOnAssetId UNIQUEIDENTIFIER    NOT NULL,
                    CreatedAt        DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cyber_asset_dependencies_TenantId' AND object_id = OBJECT_ID('dbo.cyber_asset_dependencies'))
                CREATE INDEX IX_cyber_asset_dependencies_TenantId ON dbo.cyber_asset_dependencies(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_cyber_asset_dependencies_Tenant_Asset_Depends' AND object_id = OBJECT_ID('dbo.cyber_asset_dependencies'))
                CREATE UNIQUE INDEX IX_cyber_asset_dependencies_Tenant_Asset_Depends
                    ON dbo.cyber_asset_dependencies(TenantId, AssetId, DependsOnAssetId);

            IF OBJECT_ID(N'dbo.data_pipeline_definitions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.data_pipeline_definitions (
                    Id                    UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId              UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    PipelineName          NVARCHAR(200)       NOT NULL,
                    SourceDataSourceId    UNIQUEIDENTIFIER    NOT NULL,
                    TargetDataSourceId    UNIQUEIDENTIFIER    NOT NULL,
                    SourceTlsEnabled      BIT                 NOT NULL DEFAULT 1,
                    TargetTlsEnabled      BIT                 NOT NULL DEFAULT 1,
                    IsApproved            BIT                 NOT NULL DEFAULT 0,
                    MemoryLimitRows       BIGINT              NULL,
                    UpstreamPipelineIdsJson NVARCHAR(MAX)     NOT NULL,
                    MetadataJson          NVARCHAR(MAX)       NULL,
                    CreatedAt             DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt             DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_data_pipeline_definitions_TenantId' AND object_id = OBJECT_ID('dbo.data_pipeline_definitions'))
                CREATE INDEX IX_data_pipeline_definitions_TenantId ON dbo.data_pipeline_definitions(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_data_pipeline_definitions_Tenant_PipelineName' AND object_id = OBJECT_ID('dbo.data_pipeline_definitions'))
                CREATE UNIQUE INDEX IX_data_pipeline_definitions_Tenant_PipelineName
                    ON dbo.data_pipeline_definitions(TenantId, PipelineName);

            IF OBJECT_ID(N'dbo.data_pipeline_executions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.data_pipeline_executions (
                    Id                    UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId              UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    PipelineId            UNIQUEIDENTIFIER    NOT NULL,
                    SourceDataSourceId    UNIQUEIDENTIFIER    NOT NULL,
                    TargetDataSourceId    UNIQUEIDENTIFIER    NOT NULL,
                    SourceTlsEnabled      BIT                 NOT NULL,
                    TargetTlsEnabled      BIT                 NOT NULL,
                    IsApproved            BIT                 NOT NULL,
                    Status                NVARCHAR(40)        NOT NULL,
                    Phase                 NVARCHAR(100)       NULL,
                    SourceTablesJson      NVARCHAR(MAX)       NOT NULL,
                    TargetTablesJson      NVARCHAR(MAX)       NOT NULL,
                    ProcessedRows         BIGINT              NOT NULL DEFAULT 0,
                    ErrorMessage          NVARCHAR(MAX)       NULL,
                    MetadataJson          NVARCHAR(MAX)       NULL,
                    StartedAt             DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    CompletedAt           DATETIME2           NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_data_pipeline_executions_TenantId' AND object_id = OBJECT_ID('dbo.data_pipeline_executions'))
                CREATE INDEX IX_data_pipeline_executions_TenantId ON dbo.data_pipeline_executions(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_data_pipeline_executions_Pipeline_StartedAt' AND object_id = OBJECT_ID('dbo.data_pipeline_executions'))
                CREATE INDEX IX_data_pipeline_executions_Pipeline_StartedAt
                    ON dbo.data_pipeline_executions(PipelineId, StartedAt);

            IF OBJECT_ID(N'dbo.dspm_scan_records', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.dspm_scan_records (
                    Id                      UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId                UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    SourceDataSourceId      UNIQUEIDENTIFIER    NOT NULL,
                    PipelineId              UNIQUEIDENTIFIER    NULL,
                    PipelineExecutionId     UNIQUEIDENTIFIER    NULL,
                    [Trigger]               NVARCHAR(50)        NOT NULL,
                    Status                  NVARCHAR(30)        NOT NULL,
                    FindingsCount           INT                 NOT NULL DEFAULT 0,
                    NewPiiCount             INT                 NOT NULL DEFAULT 0,
                    DriftCount              INT                 NOT NULL DEFAULT 0,
                    PostureScore            DECIMAL(5,2)        NOT NULL DEFAULT 100,
                    EncryptionAtRestEnabled BIT                 NOT NULL DEFAULT 1,
                    ScopeTablesJson         NVARCHAR(MAX)       NOT NULL,
                    StartedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    CompletedAt             DATETIME2           NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_dspm_scan_records_TenantId' AND object_id = OBJECT_ID('dbo.dspm_scan_records'))
                CREATE INDEX IX_dspm_scan_records_TenantId ON dbo.dspm_scan_records(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_dspm_scan_records_Source_StartedAt' AND object_id = OBJECT_ID('dbo.dspm_scan_records'))
                CREATE INDEX IX_dspm_scan_records_Source_StartedAt
                    ON dbo.dspm_scan_records(SourceDataSourceId, StartedAt DESC);

            IF OBJECT_ID(N'dbo.dspm_column_findings', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.dspm_column_findings (
                    Id                   UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    ScanId               UNIQUEIDENTIFIER    NOT NULL,
                    TableName            NVARCHAR(200)       NOT NULL,
                    ColumnName           NVARCHAR(200)       NOT NULL,
                    DataType             NVARCHAR(100)       NOT NULL,
                    DetectedPiiTypesJson NVARCHAR(MAX)       NOT NULL,
                    PrimaryPiiType       NVARCHAR(50)        NULL,
                    Sensitivity          NVARCHAR(30)        NOT NULL,
                    ComplianceTagsJson   NVARCHAR(MAX)       NOT NULL,
                    IsNewPii             BIT                 NOT NULL DEFAULT 0,
                    IsDrift              BIT                 NOT NULL DEFAULT 0,
                    PreviousSensitivity  NVARCHAR(30)        NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_dspm_column_findings_ScanId' AND object_id = OBJECT_ID('dbo.dspm_column_findings'))
                CREATE INDEX IX_dspm_column_findings_ScanId ON dbo.dspm_column_findings(ScanId);

            IF OBJECT_ID(N'dbo.shadow_copy_records', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.shadow_copy_records (
                    Id                UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId          UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    SourceDataSourceId UNIQUEIDENTIFIER   NOT NULL,
                    TargetDataSourceId UNIQUEIDENTIFIER   NOT NULL,
                    SourceTable       NVARCHAR(200)       NOT NULL,
                    TargetTable       NVARCHAR(200)       NOT NULL,
                    DetectionType     NVARCHAR(50)        NOT NULL,
                    Fingerprint       NVARCHAR(128)       NOT NULL,
                    SimilarityScore   DECIMAL(5,2)        NOT NULL DEFAULT 0,
                    IsLegitimate      BIT                 NOT NULL DEFAULT 0,
                    RequiresReview    BIT                 NOT NULL DEFAULT 0,
                    EvidenceJson      NVARCHAR(MAX)       NULL,
                    DetectedAt        DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_shadow_copy_records_TenantId' AND object_id = OBJECT_ID('dbo.shadow_copy_records'))
                CREATE INDEX IX_shadow_copy_records_TenantId ON dbo.shadow_copy_records(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_shadow_copy_records_Core' AND object_id = OBJECT_ID('dbo.shadow_copy_records'))
                CREATE INDEX IX_shadow_copy_records_Core
                    ON dbo.shadow_copy_records(TenantId, SourceDataSourceId, TargetDataSourceId, SourceTable, TargetTable);

            IF OBJECT_ID(N'dbo.security_alerts', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.security_alerts (
                    Id                  UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    AlertType           NVARCHAR(50)        NOT NULL,
                    Severity            NVARCHAR(20)        NOT NULL,
                    Title               NVARCHAR(200)       NOT NULL,
                    Description         NVARCHAR(MAX)       NOT NULL,
                    AffectedAssetIdsJson NVARCHAR(MAX)      NOT NULL,
                    UserId              NVARCHAR(100)       NULL,
                    Username            NVARCHAR(200)       NULL,
                    SourceIp            NVARCHAR(64)        NULL,
                    MitreTechnique      NVARCHAR(32)        NULL,
                    Status              NVARCHAR(20)        NOT NULL DEFAULT 'open',
                    EvidenceJson        NVARCHAR(MAX)       NULL,
                    CreatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_security_alerts_TenantId' AND object_id = OBJECT_ID('dbo.security_alerts'))
                CREATE INDEX IX_security_alerts_TenantId ON dbo.security_alerts(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_security_alerts_Tenant_Type_CreatedAt' AND object_id = OBJECT_ID('dbo.security_alerts'))
                CREATE INDEX IX_security_alerts_Tenant_Type_CreatedAt
                    ON dbo.security_alerts(TenantId, AlertType, CreatedAt DESC);

            IF OBJECT_ID(N'dbo.security_events', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.security_events (
                    Id                UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId          UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    EventSource       NVARCHAR(50)        NOT NULL,
                    EventType         NVARCHAR(100)       NOT NULL,
                    AlertId           UNIQUEIDENTIFIER    NULL,
                    AssetId           UNIQUEIDENTIFIER    NULL,
                    UserId            NVARCHAR(100)       NULL,
                    Username          NVARCHAR(200)       NULL,
                    SourceIp          NVARCHAR(64)        NULL,
                    MitreTechnique    NVARCHAR(32)        NULL,
                    Description       NVARCHAR(MAX)       NOT NULL,
                    RelatedEntityType NVARCHAR(100)       NULL,
                    RelatedEntityId   NVARCHAR(100)       NULL,
                    EvidenceJson      NVARCHAR(MAX)       NULL,
                    OccurredAt        DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_security_events_TenantId' AND object_id = OBJECT_ID('dbo.security_events'))
                CREATE INDEX IX_security_events_TenantId ON dbo.security_events(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_security_events_Tenant_OccurredAt' AND object_id = OBJECT_ID('dbo.security_events'))
                CREATE INDEX IX_security_events_Tenant_OccurredAt ON dbo.security_events(TenantId, OccurredAt DESC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_security_events_Tenant_SourceIp' AND object_id = OBJECT_ID('dbo.security_events'))
                CREATE INDEX IX_security_events_Tenant_SourceIp ON dbo.security_events(TenantId, SourceIp);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_security_events_Tenant_UserId' AND object_id = OBJECT_ID('dbo.security_events'))
                CREATE INDEX IX_security_events_Tenant_UserId ON dbo.security_events(TenantId, UserId);

            IF OBJECT_ID(N'dbo.root_cause_analysis_records', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.root_cause_analysis_records (
                    Id                 UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY,
                    TenantId           UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    IncidentType       NVARCHAR(40)        NOT NULL,
                    IncidentId         UNIQUEIDENTIFIER    NOT NULL,
                    RootCauseType      NVARCHAR(100)       NOT NULL,
                    RootCauseSummary   NVARCHAR(MAX)       NOT NULL,
                    Confidence         DECIMAL(5,2)        NOT NULL DEFAULT 0,
                    TimelineJson       NVARCHAR(MAX)       NOT NULL,
                    CausalChainJson    NVARCHAR(MAX)       NOT NULL,
                    ImpactJson         NVARCHAR(MAX)       NOT NULL,
                    RecommendationsJson NVARCHAR(MAX)      NOT NULL,
                    ModelName          NVARCHAR(100)       NOT NULL,
                    ModelType          NVARCHAR(50)        NOT NULL,
                    ExplainabilityMode NVARCHAR(50)        NOT NULL,
                    GeneratedAt        DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_root_cause_analysis_records_TenantId' AND object_id = OBJECT_ID('dbo.root_cause_analysis_records'))
                CREATE INDEX IX_root_cause_analysis_records_TenantId ON dbo.root_cause_analysis_records(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_root_cause_analysis_records_Tenant_Incident' AND object_id = OBJECT_ID('dbo.root_cause_analysis_records'))
                CREATE UNIQUE INDEX IX_root_cause_analysis_records_Tenant_Incident
                    ON dbo.root_cause_analysis_records(TenantId, IncidentType, IncidentId);
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.root_cause_analysis_records', N'U') IS NOT NULL DROP TABLE dbo.root_cause_analysis_records;
            IF OBJECT_ID(N'dbo.security_events', N'U') IS NOT NULL DROP TABLE dbo.security_events;
            IF OBJECT_ID(N'dbo.security_alerts', N'U') IS NOT NULL DROP TABLE dbo.security_alerts;
            IF OBJECT_ID(N'dbo.shadow_copy_records', N'U') IS NOT NULL DROP TABLE dbo.shadow_copy_records;
            IF OBJECT_ID(N'dbo.dspm_column_findings', N'U') IS NOT NULL DROP TABLE dbo.dspm_column_findings;
            IF OBJECT_ID(N'dbo.dspm_scan_records', N'U') IS NOT NULL DROP TABLE dbo.dspm_scan_records;
            IF OBJECT_ID(N'dbo.data_pipeline_executions', N'U') IS NOT NULL DROP TABLE dbo.data_pipeline_executions;
            IF OBJECT_ID(N'dbo.data_pipeline_definitions', N'U') IS NOT NULL DROP TABLE dbo.data_pipeline_definitions;
            IF OBJECT_ID(N'dbo.cyber_asset_dependencies', N'U') IS NOT NULL DROP TABLE dbo.cyber_asset_dependencies;
            IF OBJECT_ID(N'dbo.cyber_assets', N'U') IS NOT NULL DROP TABLE dbo.cyber_assets;
            IF OBJECT_ID(N'dbo.data_source_registrations', N'U') IS NOT NULL DROP TABLE dbo.data_source_registrations;
        ");
    }
}
