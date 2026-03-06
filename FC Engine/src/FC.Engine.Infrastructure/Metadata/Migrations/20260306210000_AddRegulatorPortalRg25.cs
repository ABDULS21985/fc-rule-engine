#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260306210000_AddRegulatorPortalRg25")]
public partial class AddRegulatorPortalRg25 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.regulator_receipts', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.regulator_receipts (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    RegulatorTenantId   UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    SubmissionId        INT                 NOT NULL REFERENCES dbo.return_submissions(Id),
                    Status              NVARCHAR(30)        NOT NULL DEFAULT 'Received',
                    ReceivedAt          DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    ReviewedBy          INT                 NULL,
                    ReviewedAt          DATETIME2           NULL,
                    AcceptedAt          DATETIME2           NULL,
                    FinalAcceptedAt     DATETIME2           NULL,
                    Notes               NVARCHAR(MAX)       NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_regulator_receipts_TenantId' AND object_id = OBJECT_ID('dbo.regulator_receipts'))
                CREATE INDEX IX_regulator_receipts_TenantId ON dbo.regulator_receipts(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_regulator_receipts_RegulatorTenantId' AND object_id = OBJECT_ID('dbo.regulator_receipts'))
                CREATE INDEX IX_regulator_receipts_RegulatorTenantId ON dbo.regulator_receipts(RegulatorTenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_regulator_receipts_RegulatorTenant_Submission' AND object_id = OBJECT_ID('dbo.regulator_receipts'))
                CREATE UNIQUE INDEX UQ_regulator_receipts_RegulatorTenant_Submission ON dbo.regulator_receipts(RegulatorTenantId, SubmissionId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_regulator_receipts_RegulatorTenant_Status_ReceivedAt' AND object_id = OBJECT_ID('dbo.regulator_receipts'))
                CREATE INDEX IX_regulator_receipts_RegulatorTenant_Status_ReceivedAt ON dbo.regulator_receipts(RegulatorTenantId, Status, ReceivedAt);

            IF OBJECT_ID(N'dbo.examiner_queries', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.examiner_queries (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    RegulatorTenantId   UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    SubmissionId        INT                 NOT NULL REFERENCES dbo.return_submissions(Id),
                    FieldCode           NVARCHAR(50)        NULL,
                    QueryText           NVARCHAR(MAX)       NOT NULL,
                    RaisedBy            INT                 NOT NULL,
                    RaisedAt            DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    ResponseText        NVARCHAR(MAX)       NULL,
                    RespondedBy         INT                 NULL,
                    RespondedAt         DATETIME2           NULL,
                    Status              NVARCHAR(20)        NOT NULL DEFAULT 'Open',
                    Priority            NVARCHAR(10)        NOT NULL DEFAULT 'Normal'
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examiner_queries_TenantId' AND object_id = OBJECT_ID('dbo.examiner_queries'))
                CREATE INDEX IX_examiner_queries_TenantId ON dbo.examiner_queries(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examiner_queries_RegulatorTenantId' AND object_id = OBJECT_ID('dbo.examiner_queries'))
                CREATE INDEX IX_examiner_queries_RegulatorTenantId ON dbo.examiner_queries(RegulatorTenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examiner_queries_RegulatorTenant_Submission_Status' AND object_id = OBJECT_ID('dbo.examiner_queries'))
                CREATE INDEX IX_examiner_queries_RegulatorTenant_Submission_Status ON dbo.examiner_queries(RegulatorTenantId, SubmissionId, Status);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examiner_queries_RegulatorTenant_RaisedAt' AND object_id = OBJECT_ID('dbo.examiner_queries'))
                CREATE INDEX IX_examiner_queries_RegulatorTenant_RaisedAt ON dbo.examiner_queries(RegulatorTenantId, RaisedAt);

            IF OBJECT_ID(N'dbo.examination_projects', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.examination_projects (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    Name                NVARCHAR(200)       NOT NULL,
                    Scope               NVARCHAR(1000)      NOT NULL,
                    EntityIdsJson       NVARCHAR(MAX)       NOT NULL,
                    ModuleCodesJson     NVARCHAR(MAX)       NOT NULL,
                    PeriodFrom          DATETIME2           NULL,
                    PeriodTo            DATETIME2           NULL,
                    Status              NVARCHAR(20)        NOT NULL DEFAULT 'Draft',
                    CreatedBy           INT                 NOT NULL,
                    CreatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    ReportFilePath      NVARCHAR(500)       NULL,
                    LastReportGeneratedAt DATETIME2         NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_projects_TenantId' AND object_id = OBJECT_ID('dbo.examination_projects'))
                CREATE INDEX IX_examination_projects_TenantId ON dbo.examination_projects(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_projects_Tenant_Status_CreatedAt' AND object_id = OBJECT_ID('dbo.examination_projects'))
                CREATE INDEX IX_examination_projects_Tenant_Status_CreatedAt ON dbo.examination_projects(TenantId, Status, CreatedAt);

            IF OBJECT_ID(N'dbo.examination_annotations', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.examination_annotations (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    ProjectId           INT                 NOT NULL REFERENCES dbo.examination_projects(Id),
                    SubmissionId        INT                 NOT NULL REFERENCES dbo.return_submissions(Id),
                    InstitutionId       INT                 NULL,
                    FieldCode           NVARCHAR(50)        NULL,
                    Note                NVARCHAR(MAX)       NOT NULL,
                    CreatedBy           INT                 NOT NULL,
                    CreatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_annotations_TenantId' AND object_id = OBJECT_ID('dbo.examination_annotations'))
                CREATE INDEX IX_examination_annotations_TenantId ON dbo.examination_annotations(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_annotations_Tenant_Project_Submission' AND object_id = OBJECT_ID('dbo.examination_annotations'))
                CREATE INDEX IX_examination_annotations_Tenant_Project_Submission ON dbo.examination_annotations(TenantId, ProjectId, SubmissionId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_examination_annotations_Tenant_CreatedAt' AND object_id = OBJECT_ID('dbo.examination_annotations'))
                CREATE INDEX IX_examination_annotations_Tenant_CreatedAt ON dbo.examination_annotations(TenantId, CreatedAt);
        ");

        RebuildTenantSecurityPolicyForRegulator(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.examination_annotations', N'U') IS NOT NULL
                DROP TABLE dbo.examination_annotations;

            IF OBJECT_ID(N'dbo.examination_projects', N'U') IS NOT NULL
                DROP TABLE dbo.examination_projects;

            IF OBJECT_ID(N'dbo.examiner_queries', N'U') IS NOT NULL
                DROP TABLE dbo.examiner_queries;

            IF OBJECT_ID(N'dbo.regulator_receipts', N'U') IS NOT NULL
                DROP TABLE dbo.regulator_receipts;
        ");

        RebuildTenantSecurityPolicyLegacy(migrationBuilder);
    }

    private static void RebuildTenantSecurityPolicyForRegulator(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;

            IF OBJECT_ID(N'dbo.fn_TenantFilter', N'IF') IS NOT NULL
                DROP FUNCTION dbo.fn_TenantFilter;

            EXEC('
                CREATE FUNCTION dbo.fn_TenantFilter(@TenantId UNIQUEIDENTIFIER)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                SELECT 1 AS fn_accessResult
                WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                   OR @TenantId IS NULL
                   OR SESSION_CONTEXT(N''TenantId'') IS NULL
                   OR EXISTS (
                        SELECT 1
                        FROM dbo.tenants p
                        WHERE p.TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                          AND p.TenantType = N''WhiteLabelPartner''
                          AND EXISTS (
                                SELECT 1
                                FROM dbo.tenants c
                                WHERE c.TenantId = @TenantId
                                  AND c.ParentTenantId = p.TenantId
                          )
                   )
                   OR (
                        CAST(SESSION_CONTEXT(N''TenantType'') AS NVARCHAR(30)) = N''Regulator''
                        AND CAST(SESSION_CONTEXT(N''RegulatorCode'') AS NVARCHAR(30)) IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM dbo.tenant_licence_types tlt
                            INNER JOIN dbo.licence_module_matrix lmm ON lmm.LicenceTypeId = tlt.LicenceTypeId
                            INNER JOIN dbo.modules m ON m.Id = lmm.ModuleId
                            WHERE tlt.TenantId = @TenantId
                              AND tlt.IsActive = 1
                              AND (lmm.IsRequired = 1 OR lmm.IsOptional = 1)
                              AND m.RegulatorCode = CAST(SESSION_CONTEXT(N''RegulatorCode'') AS NVARCHAR(30))
                        )
                   );');

            DECLARE @sql NVARCHAR(MAX) = N'CREATE SECURITY POLICY dbo.TenantSecurityPolicy' + CHAR(13);
            DECLARE @first BIT = 1;

            DECLARE tenant_cursor CURSOR FAST_FORWARD FOR
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE c.name = 'TenantId'
              AND t.is_ms_shipped = 0;

            DECLARE @schemaName SYSNAME, @tableName SYSNAME;
            OPEN tenant_cursor;
            FETCH NEXT FROM tenant_cursor INTO @schemaName, @tableName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @first = 0 SET @sql += N',' + CHAR(13);
                SET @sql += N'    ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                SET @first = 0;
                FETCH NEXT FROM tenant_cursor INTO @schemaName, @tableName;
            END;
            CLOSE tenant_cursor;
            DEALLOCATE tenant_cursor;

            DECLARE tenant_cursor_block CURSOR FAST_FORWARD FOR
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE c.name = 'TenantId'
              AND t.is_ms_shipped = 0;

            OPEN tenant_cursor_block;
            FETCH NEXT FROM tenant_cursor_block INTO @schemaName, @tableName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @sql += N',' + CHAR(13)
                         + N'    ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                FETCH NEXT FROM tenant_cursor_block INTO @schemaName, @tableName;
            END;
            CLOSE tenant_cursor_block;
            DEALLOCATE tenant_cursor_block;

            SET @sql += CHAR(13) + N'WITH (STATE = ON);';
            EXEC sp_executesql @sql;
        ");
    }

    private static void RebuildTenantSecurityPolicyLegacy(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;

            IF OBJECT_ID(N'dbo.fn_TenantFilter', N'IF') IS NOT NULL
                DROP FUNCTION dbo.fn_TenantFilter;

            EXEC('
                CREATE FUNCTION dbo.fn_TenantFilter(@TenantId UNIQUEIDENTIFIER)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS
                RETURN
                SELECT 1 AS fn_accessResult
                WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                   OR @TenantId IS NULL
                   OR SESSION_CONTEXT(N''TenantId'') IS NULL
                   OR EXISTS (
                        SELECT 1
                        FROM dbo.tenants p
                        WHERE p.TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                          AND p.TenantType = N''WhiteLabelPartner''
                          AND EXISTS (
                                SELECT 1
                                FROM dbo.tenants c
                                WHERE c.TenantId = @TenantId
                                  AND c.ParentTenantId = p.TenantId
                          )
                   );');

            DECLARE @sql NVARCHAR(MAX) = N'CREATE SECURITY POLICY dbo.TenantSecurityPolicy' + CHAR(13);
            DECLARE @first BIT = 1;

            DECLARE tenant_cursor CURSOR FAST_FORWARD FOR
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE c.name = 'TenantId'
              AND t.is_ms_shipped = 0;

            DECLARE @schemaName SYSNAME, @tableName SYSNAME;
            OPEN tenant_cursor;
            FETCH NEXT FROM tenant_cursor INTO @schemaName, @tableName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                IF @first = 0 SET @sql += N',' + CHAR(13);
                SET @sql += N'    ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                SET @first = 0;
                FETCH NEXT FROM tenant_cursor INTO @schemaName, @tableName;
            END;
            CLOSE tenant_cursor;
            DEALLOCATE tenant_cursor;

            DECLARE tenant_cursor_block CURSOR FAST_FORWARD FOR
            SELECT s.name AS SchemaName, t.name AS TableName
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            INNER JOIN sys.columns c ON c.object_id = t.object_id
            WHERE c.name = 'TenantId'
              AND t.is_ms_shipped = 0;

            OPEN tenant_cursor_block;
            FETCH NEXT FROM tenant_cursor_block INTO @schemaName, @tableName;
            WHILE @@FETCH_STATUS = 0
            BEGIN
                SET @sql += N',' + CHAR(13)
                         + N'    ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON [' + @schemaName + N'].[' + @tableName + N']';
                FETCH NEXT FROM tenant_cursor_block INTO @schemaName, @tableName;
            END;
            CLOSE tenant_cursor_block;
            DEALLOCATE tenant_cursor_block;

            SET @sql += CHAR(13) + N'WITH (STATE = ON);';
            EXEC sp_executesql @sql;
        ");
    }
}
