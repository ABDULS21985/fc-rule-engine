#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260305220000_AddExportEngineRg13")]
public partial class AddExportEngineRg13 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.export_requests', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.export_requests (
                    Id              INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId        UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    SubmissionId    INT                 NOT NULL REFERENCES dbo.return_submissions(Id),
                    Format          NVARCHAR(10)        NOT NULL,
                    Status          NVARCHAR(20)        NOT NULL DEFAULT 'Queued',
                    RequestedBy     INT                 NOT NULL,
                    RequestedAt     DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    CompletedAt     DATETIME2           NULL,
                    FilePath        NVARCHAR(500)       NULL,
                    FileSize        BIGINT              NULL,
                    Sha256Hash      NVARCHAR(64)        NULL,
                    ErrorMessage    NVARCHAR(1000)      NULL,
                    ExpiresAt       DATETIME2           NULL
                );
            END;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_export_requests_TenantId' AND object_id = OBJECT_ID('dbo.export_requests'))
                CREATE INDEX IX_export_requests_TenantId ON dbo.export_requests(TenantId);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_export_requests_Tenant_Submission_Requested' AND object_id = OBJECT_ID('dbo.export_requests'))
                CREATE INDEX IX_export_requests_Tenant_Submission_Requested
                    ON dbo.export_requests(TenantId, SubmissionId, RequestedAt DESC);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_export_requests_Status_RequestedAt' AND object_id = OBJECT_ID('dbo.export_requests'))
                CREATE INDEX IX_export_requests_Status_RequestedAt ON dbo.export_requests(Status, RequestedAt);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_export_requests_ExpiresAt' AND object_id = OBJECT_ID('dbo.export_requests'))
                CREATE INDEX IX_export_requests_ExpiresAt ON dbo.export_requests(ExpiresAt);
        ");

        RebuildTenantSecurityPolicy(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.export_requests', N'U') IS NOT NULL
                DROP TABLE dbo.export_requests;
        ");

        RebuildTenantSecurityPolicy(migrationBuilder);
    }

    private static void RebuildTenantSecurityPolicy(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.fn_TenantFilter', N'IF') IS NULL
            BEGIN
                EXEC('
                    CREATE FUNCTION dbo.fn_TenantFilter(@TenantId UNIQUEIDENTIFIER)
                    RETURNS TABLE
                    WITH SCHEMABINDING
                    AS
                    RETURN
                    SELECT 1 AS fn_accessResult
                    WHERE @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                       OR @TenantId IS NULL
                       OR SESSION_CONTEXT(N''TenantId'') IS NULL;');
            END;

            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;

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
