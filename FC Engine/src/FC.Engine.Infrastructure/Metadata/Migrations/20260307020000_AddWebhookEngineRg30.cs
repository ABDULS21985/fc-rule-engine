#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260307020000_AddWebhookEngineRg30")]
public partial class AddWebhookEngineRg30 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.webhook_endpoints', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.webhook_endpoints (
                    Id                  INT                 IDENTITY(1,1)  PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL,
                    Url                 NVARCHAR(500)       NOT NULL,
                    Description         NVARCHAR(200)       NULL,
                    SecretKey           NVARCHAR(128)       NOT NULL,
                    EventTypes          NVARCHAR(MAX)       NOT NULL  DEFAULT '[]',
                    IsActive            BIT                 NOT NULL  DEFAULT 1,
                    CreatedBy           INT                 NOT NULL,
                    CreatedAt           DATETIME2           NOT NULL  DEFAULT SYSUTCDATETIME(),
                    LastDeliveryAt      DATETIME2           NULL,
                    FailureCount        INT                 NOT NULL  DEFAULT 0,
                    DisabledReason      NVARCHAR(200)       NULL
                );
            END;

            IF OBJECT_ID(N'dbo.webhook_deliveries', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.webhook_deliveries (
                    Id                  BIGINT              IDENTITY(1,1)  PRIMARY KEY,
                    EndpointId          INT                 NOT NULL  REFERENCES dbo.webhook_endpoints(Id) ON DELETE CASCADE,
                    EventType           NVARCHAR(50)        NOT NULL,
                    Payload             NVARCHAR(MAX)       NOT NULL,
                    HttpStatus          INT                 NULL,
                    ResponseBody        NVARCHAR(MAX)       NULL,
                    AttemptCount        INT                 NOT NULL  DEFAULT 0,
                    MaxAttempts         INT                 NOT NULL  DEFAULT 3,
                    NextRetryAt         DATETIME2           NULL,
                    DeliveredAt         DATETIME2           NULL,
                    DurationMs          INT                 NULL,
                    Status              NVARCHAR(20)        NOT NULL  DEFAULT 'Pending',
                    CreatedAt           DATETIME2           NOT NULL  DEFAULT SYSUTCDATETIME()
                );
            END;
            """);

        // Indexes
        migrationBuilder.Sql("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_webhook_endpoints_TenantId'
                  AND object_id = OBJECT_ID('dbo.webhook_endpoints'))
            BEGIN
                CREATE INDEX IX_webhook_endpoints_TenantId
                    ON dbo.webhook_endpoints(TenantId);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_webhook_endpoints_TenantId_IsActive'
                  AND object_id = OBJECT_ID('dbo.webhook_endpoints'))
            BEGIN
                CREATE INDEX IX_webhook_endpoints_TenantId_IsActive
                    ON dbo.webhook_endpoints(TenantId, IsActive);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_webhook_deliveries_Status_NextRetryAt'
                  AND object_id = OBJECT_ID('dbo.webhook_deliveries'))
            BEGIN
                CREATE INDEX IX_webhook_deliveries_Status_NextRetryAt
                    ON dbo.webhook_deliveries(Status, NextRetryAt);
            END;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_webhook_deliveries_EndpointId_CreatedAt'
                  AND object_id = OBJECT_ID('dbo.webhook_deliveries'))
            BEGIN
                CREATE INDEX IX_webhook_deliveries_EndpointId_CreatedAt
                    ON dbo.webhook_deliveries(EndpointId, CreatedAt);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.webhook_deliveries', N'U') IS NOT NULL
                DROP TABLE dbo.webhook_deliveries;
            IF OBJECT_ID(N'dbo.webhook_endpoints', N'U') IS NOT NULL
                DROP TABLE dbo.webhook_endpoints;
            """);
    }
}
