#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260309150000_AddDigitalExaminationToolkitRg39")]
public partial class AddDigitalExaminationToolkitRg39 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF COL_LENGTH('dbo.examination_projects', 'TeamAssignmentsJson') IS NULL
                ALTER TABLE dbo.examination_projects ADD TeamAssignmentsJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_examination_projects_TeamAssignmentsJson DEFAULT '[]';

            IF COL_LENGTH('dbo.examination_projects', 'TimelineJson') IS NULL
                ALTER TABLE dbo.examination_projects ADD TimelineJson NVARCHAR(MAX) NOT NULL CONSTRAINT DF_examination_projects_TimelineJson DEFAULT '[]';

            IF COL_LENGTH('dbo.examination_projects', 'IntelligencePackFilePath') IS NULL
                ALTER TABLE dbo.examination_projects ADD IntelligencePackFilePath NVARCHAR(500) NULL;

            IF COL_LENGTH('dbo.examination_projects', 'IntelligencePackGeneratedAt') IS NULL
                ALTER TABLE dbo.examination_projects ADD IntelligencePackGeneratedAt DATETIME2 NULL;

            IF OBJECT_ID(N'dbo.examination_findings', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.examination_findings (
                    Id                              INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId                        UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    ProjectId                       INT                 NOT NULL REFERENCES dbo.examination_projects(Id),
                    SubmissionId                    INT                 NULL REFERENCES dbo.return_submissions(Id),
                    InstitutionId                   INT                 NULL REFERENCES dbo.institutions(Id),
                    CarriedForwardFromFindingId     INT                 NULL REFERENCES dbo.examination_findings(Id),
                    Title                           NVARCHAR(250)       NOT NULL,
                    RiskArea                        NVARCHAR(120)       NOT NULL,
                    Observation                     NVARCHAR(MAX)       NOT NULL,
                    RiskRating                      NVARCHAR(20)        NOT NULL DEFAULT 'Medium',
                    Recommendation                  NVARCHAR(MAX)       NOT NULL,
                    Status                          NVARCHAR(40)        NOT NULL DEFAULT 'ToReview',
                    RemediationStatus               NVARCHAR(40)        NOT NULL DEFAULT 'Open',
                    ModuleCode                      NVARCHAR(60)        NULL,
                    PeriodLabel                     NVARCHAR(60)        NULL,
                    FieldCode                       NVARCHAR(120)       NULL,
                    FieldValue                      NVARCHAR(500)       NULL,
                    ValidationRuleId                NVARCHAR(120)       NULL,
                    ValidationMessage               NVARCHAR(1000)      NULL,
                    EvidenceReference               NVARCHAR(500)       NULL,
                    ManagementResponseDeadline      DATETIME2           NULL,
                    ManagementResponse              NVARCHAR(MAX)       NULL,
                    ManagementResponseSubmittedAt   DATETIME2           NULL,
                    ManagementActionPlan            NVARCHAR(MAX)       NULL,
                    IsCarriedForward                BIT                 NOT NULL DEFAULT 0,
                    EscalatedAt                     DATETIME2           NULL,
                    EscalationReason                NVARCHAR(500)       NULL,
                    VerifiedAt                      DATETIME2           NULL,
                    VerifiedBy                      INT                 NULL,
                    ClosedAt                        DATETIME2           NULL,
                    CreatedBy                       INT                 NOT NULL,
                    CreatedAt                       DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt                       DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_findings_TenantId' AND object_id = OBJECT_ID('dbo.examination_findings'))
                CREATE INDEX IX_examination_findings_TenantId ON dbo.examination_findings(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_findings_Tenant_Project_Status' AND object_id = OBJECT_ID('dbo.examination_findings'))
                CREATE INDEX IX_examination_findings_Tenant_Project_Status ON dbo.examination_findings(TenantId, ProjectId, Status);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_findings_Tenant_Project_Remediation' AND object_id = OBJECT_ID('dbo.examination_findings'))
                CREATE INDEX IX_examination_findings_Tenant_Project_Remediation ON dbo.examination_findings(TenantId, ProjectId, RemediationStatus);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_findings_Tenant_Institution_DueDate' AND object_id = OBJECT_ID('dbo.examination_findings'))
                CREATE INDEX IX_examination_findings_Tenant_Institution_DueDate ON dbo.examination_findings(TenantId, InstitutionId, ManagementResponseDeadline);

            IF OBJECT_ID(N'dbo.examination_evidence_requests', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.examination_evidence_requests (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    ProjectId           INT                 NOT NULL REFERENCES dbo.examination_projects(Id),
                    FindingId           INT                 NULL REFERENCES dbo.examination_findings(Id),
                    SubmissionId        INT                 NULL REFERENCES dbo.return_submissions(Id),
                    InstitutionId       INT                 NULL REFERENCES dbo.institutions(Id),
                    Title               NVARCHAR(250)       NOT NULL,
                    RequestText         NVARCHAR(MAX)       NOT NULL,
                    RequestedItemsJson  NVARCHAR(MAX)       NULL,
                    RequestedAt         DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    DueAt               DATETIME2           NULL,
                    Status              NVARCHAR(20)        NOT NULL DEFAULT 'Open',
                    RequestedBy         INT                 NOT NULL,
                    FulfilledAt         DATETIME2           NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_evidence_requests_TenantId' AND object_id = OBJECT_ID('dbo.examination_evidence_requests'))
                CREATE INDEX IX_examination_evidence_requests_TenantId ON dbo.examination_evidence_requests(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_evidence_requests_Tenant_Project_Status' AND object_id = OBJECT_ID('dbo.examination_evidence_requests'))
                CREATE INDEX IX_examination_evidence_requests_Tenant_Project_Status ON dbo.examination_evidence_requests(TenantId, ProjectId, Status);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_evidence_requests_Tenant_Finding_Status' AND object_id = OBJECT_ID('dbo.examination_evidence_requests'))
                CREATE INDEX IX_examination_evidence_requests_Tenant_Finding_Status ON dbo.examination_evidence_requests(TenantId, FindingId, Status);

            IF OBJECT_ID(N'dbo.examination_evidence_files', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.examination_evidence_files (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    ProjectId           INT                 NOT NULL REFERENCES dbo.examination_projects(Id),
                    FindingId           INT                 NULL REFERENCES dbo.examination_findings(Id),
                    EvidenceRequestId   INT                 NULL REFERENCES dbo.examination_evidence_requests(Id),
                    SubmissionId        INT                 NULL REFERENCES dbo.return_submissions(Id),
                    InstitutionId       INT                 NULL REFERENCES dbo.institutions(Id),
                    FileName            NVARCHAR(260)       NOT NULL,
                    ContentType         NVARCHAR(120)       NOT NULL,
                    FileSizeBytes       BIGINT              NOT NULL,
                    StoragePath         NVARCHAR(500)       NOT NULL,
                    FileHash            NVARCHAR(64)        NOT NULL,
                    Kind                NVARCHAR(40)        NOT NULL DEFAULT 'SupportingDocument',
                    UploadedByRole      NVARCHAR(20)        NOT NULL DEFAULT 'Examiner',
                    UploadedBy          INT                 NOT NULL,
                    UploadedAt          DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    Notes               NVARCHAR(500)       NULL,
                    IsVerified          BIT                 NOT NULL DEFAULT 0,
                    VerifiedBy          INT                 NULL,
                    VerifiedAt          DATETIME2           NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_evidence_files_TenantId' AND object_id = OBJECT_ID('dbo.examination_evidence_files'))
                CREATE INDEX IX_examination_evidence_files_TenantId ON dbo.examination_evidence_files(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_evidence_files_Tenant_Project_UploadedAt' AND object_id = OBJECT_ID('dbo.examination_evidence_files'))
                CREATE INDEX IX_examination_evidence_files_Tenant_Project_UploadedAt ON dbo.examination_evidence_files(TenantId, ProjectId, UploadedAt);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_evidence_files_Tenant_Finding' AND object_id = OBJECT_ID('dbo.examination_evidence_files'))
                CREATE INDEX IX_examination_evidence_files_Tenant_Finding ON dbo.examination_evidence_files(TenantId, FindingId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_evidence_files_FileHash' AND object_id = OBJECT_ID('dbo.examination_evidence_files'))
                CREATE INDEX IX_examination_evidence_files_FileHash ON dbo.examination_evidence_files(FileHash);

            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
            BEGIN
                BEGIN TRY
                    ALTER SECURITY POLICY dbo.TenantSecurityPolicy
                        ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.examination_findings,
                        ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.examination_findings,
                        ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.examination_evidence_requests,
                        ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.examination_evidence_requests,
                        ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.examination_evidence_files,
                        ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.examination_evidence_files;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (3728, 33280)
                        THROW;
                END CATCH
            END;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
            BEGIN
                BEGIN TRY
                    ALTER SECURITY POLICY dbo.TenantSecurityPolicy
                        DROP FILTER PREDICATE ON dbo.examination_evidence_files,
                        DROP BLOCK PREDICATE ON dbo.examination_evidence_files,
                        DROP FILTER PREDICATE ON dbo.examination_evidence_requests,
                        DROP BLOCK PREDICATE ON dbo.examination_evidence_requests,
                        DROP FILTER PREDICATE ON dbo.examination_findings,
                        DROP BLOCK PREDICATE ON dbo.examination_findings;
                END TRY
                BEGIN CATCH
                    IF ERROR_NUMBER() NOT IN (3727, 3730)
                        THROW;
                END CATCH
            END;

            IF OBJECT_ID(N'dbo.examination_evidence_files', N'U') IS NOT NULL
                DROP TABLE dbo.examination_evidence_files;

            IF OBJECT_ID(N'dbo.examination_evidence_requests', N'U') IS NOT NULL
                DROP TABLE dbo.examination_evidence_requests;

            IF OBJECT_ID(N'dbo.examination_findings', N'U') IS NOT NULL
                DROP TABLE dbo.examination_findings;

            IF COL_LENGTH('dbo.examination_projects', 'IntelligencePackGeneratedAt') IS NOT NULL
                ALTER TABLE dbo.examination_projects DROP COLUMN IntelligencePackGeneratedAt;

            IF COL_LENGTH('dbo.examination_projects', 'IntelligencePackFilePath') IS NOT NULL
                ALTER TABLE dbo.examination_projects DROP COLUMN IntelligencePackFilePath;

            IF COL_LENGTH('dbo.examination_projects', 'TimelineJson') IS NOT NULL
                ALTER TABLE dbo.examination_projects DROP COLUMN TimelineJson;

            IF COL_LENGTH('dbo.examination_projects', 'TeamAssignmentsJson') IS NOT NULL
                ALTER TABLE dbo.examination_projects DROP COLUMN TeamAssignmentsJson;
        ");
    }
}
