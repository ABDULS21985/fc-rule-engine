#nullable enable
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FC.Engine.Infrastructure.Metadata.Migrations;

[DbContext(typeof(MetadataDbContext))]
[Migration("20260305120000_AddAuthEvolutionRg05")]
public partial class AddAuthEvolutionRg05 : Migration
{
    private static readonly Guid LegacyTenantId = new("00000000-0000-0000-0000-000000000001");

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($@"
            IF OBJECT_ID(N'dbo.refresh_tokens', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.refresh_tokens (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    UserId              INT                 NOT NULL,
                    UserType            NVARCHAR(20)        NOT NULL,
                    Token               NVARCHAR(256)       NOT NULL,
                    TokenHash           NVARCHAR(128)       NOT NULL,
                    ExpiresAt           DATETIME2           NOT NULL,
                    CreatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    CreatedByIp         NVARCHAR(45)        NULL,
                    RevokedAt           DATETIME2           NULL,
                    RevokedByIp         NVARCHAR(45)        NULL,
                    ReplacedByTokenHash NVARCHAR(128)       NULL,
                    IsRevoked           BIT                 NOT NULL DEFAULT 0,
                    IsUsed              BIT                 NOT NULL DEFAULT 0
                );

                CREATE UNIQUE INDEX UX_refresh_tokens_Token ON dbo.refresh_tokens(Token);
                CREATE UNIQUE INDEX UX_refresh_tokens_TokenHash ON dbo.refresh_tokens(TokenHash);
                CREATE INDEX IX_refresh_tokens_TenantId ON dbo.refresh_tokens(TenantId);
                CREATE INDEX IX_refresh_tokens_User ON dbo.refresh_tokens(UserId, UserType);
            END;

            IF OBJECT_ID(N'dbo.user_mfa_configs', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.user_mfa_configs (
                    Id              INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId        UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    UserId          INT                 NOT NULL,
                    UserType        NVARCHAR(20)        NOT NULL,
                    SecretKey       NVARCHAR(128)       NOT NULL,
                    BackupCodes     NVARCHAR(MAX)       NOT NULL,
                    IsEnabled       BIT                 NOT NULL DEFAULT 0,
                    EnabledAt       DATETIME2           NULL,
                    LastUsedAt      DATETIME2           NULL,
                    CONSTRAINT UQ_user_mfa_configs_User UNIQUE (UserId, UserType)
                );

                CREATE INDEX IX_user_mfa_configs_TenantId ON dbo.user_mfa_configs(TenantId);
            END;

            IF OBJECT_ID(N'dbo.permissions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.permissions (
                    Id              INT             IDENTITY(1,1) PRIMARY KEY,
                    PermissionCode  NVARCHAR(50)    NOT NULL,
                    Description     NVARCHAR(200)   NOT NULL,
                    Category        NVARCHAR(50)    NOT NULL,
                    IsActive        BIT             NOT NULL DEFAULT 1,
                    CONSTRAINT UQ_permissions_Code UNIQUE (PermissionCode)
                );
            END;

            IF OBJECT_ID(N'dbo.roles', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.roles (
                    Id              INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId        UNIQUEIDENTIFIER    NULL REFERENCES dbo.tenants(TenantId),
                    RoleName        NVARCHAR(50)        NOT NULL,
                    Description     NVARCHAR(200)       NULL,
                    IsSystemRole    BIT                 NOT NULL DEFAULT 0,
                    IsActive        BIT                 NOT NULL DEFAULT 1,
                    CONSTRAINT UQ_roles_Tenant_Role UNIQUE (TenantId, RoleName)
                );

                CREATE INDEX IX_roles_TenantId ON dbo.roles(TenantId);
            END;

            IF OBJECT_ID(N'dbo.role_permissions', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.role_permissions (
                    RoleId          INT             NOT NULL REFERENCES dbo.roles(Id) ON DELETE CASCADE,
                    PermissionId    INT             NOT NULL REFERENCES dbo.permissions(Id) ON DELETE CASCADE,
                    CONSTRAINT PK_role_permissions PRIMARY KEY (RoleId, PermissionId)
                );
            END;

            IF OBJECT_ID(N'dbo.tenant_sso_configs', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.tenant_sso_configs (
                    Id                      INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId                UNIQUEIDENTIFIER    NOT NULL UNIQUE REFERENCES dbo.tenants(TenantId),
                    SsoEnabled              BIT                 NOT NULL DEFAULT 0,
                    IdpEntityId             NVARCHAR(500)       NOT NULL,
                    IdpSsoUrl               NVARCHAR(500)       NOT NULL,
                    IdpSloUrl               NVARCHAR(500)       NULL,
                    IdpCertificate          NVARCHAR(MAX)       NOT NULL,
                    SpEntityId              NVARCHAR(500)       NOT NULL,
                    AttributeMapping        NVARCHAR(MAX)       NOT NULL,
                    DefaultRole             NVARCHAR(50)        NOT NULL DEFAULT 'Viewer',
                    JitProvisioningEnabled  BIT                 NOT NULL DEFAULT 1,
                    CreatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt               DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID(N'dbo.api_keys', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.api_keys (
                    Id                  INT                 IDENTITY(1,1) PRIMARY KEY,
                    TenantId            UNIQUEIDENTIFIER    NOT NULL REFERENCES dbo.tenants(TenantId),
                    KeyHash             NVARCHAR(500)       NOT NULL,
                    KeyPrefix           NVARCHAR(20)        NOT NULL DEFAULT '',
                    Description         NVARCHAR(500)       NULL,
                    Permissions         NVARCHAR(MAX)       NULL,
                    RateLimitPerMinute  INT                 NOT NULL DEFAULT 100,
                    ExpiresAt           DATETIME2           NULL,
                    LastUsedAt          DATETIME2           NULL,
                    LastUsedIp          NVARCHAR(45)        NULL,
                    CreatedBy           INT                 NOT NULL DEFAULT 0,
                    IsActive            BIT                 NOT NULL DEFAULT 1,
                    CreatedAt           DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX IX_api_keys_TenantId ON dbo.api_keys(TenantId);
                CREATE INDEX IX_api_keys_KeyPrefix ON dbo.api_keys(KeyPrefix);
                CREATE INDEX IX_api_keys_ExpiresAt ON dbo.api_keys(ExpiresAt);
            END
            ELSE
            BEGIN
                IF COL_LENGTH('dbo.api_keys', 'TenantId') IS NULL
                BEGIN
                    ALTER TABLE dbo.api_keys ADD TenantId UNIQUEIDENTIFIER NULL;
                    UPDATE dbo.api_keys SET TenantId = '{LegacyTenantId}' WHERE TenantId IS NULL;
                    ALTER TABLE dbo.api_keys ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL;
                    ALTER TABLE dbo.api_keys ADD CONSTRAINT FK_api_keys_tenants_TenantId
                        FOREIGN KEY (TenantId) REFERENCES dbo.tenants(TenantId);
                END;

                IF COL_LENGTH('dbo.api_keys', 'KeyPrefix') IS NULL
                    ALTER TABLE dbo.api_keys ADD KeyPrefix NVARCHAR(20) NOT NULL CONSTRAINT DF_api_keys_KeyPrefix DEFAULT('');

                IF COL_LENGTH('dbo.api_keys', 'Permissions') IS NULL
                    ALTER TABLE dbo.api_keys ADD Permissions NVARCHAR(MAX) NULL;

                IF COL_LENGTH('dbo.api_keys', 'RateLimitPerMinute') IS NULL
                    ALTER TABLE dbo.api_keys ADD RateLimitPerMinute INT NOT NULL CONSTRAINT DF_api_keys_RateLimit DEFAULT(100);

                IF COL_LENGTH('dbo.api_keys', 'ExpiresAt') IS NULL
                    ALTER TABLE dbo.api_keys ADD ExpiresAt DATETIME2 NULL;

                IF COL_LENGTH('dbo.api_keys', 'LastUsedAt') IS NULL
                    ALTER TABLE dbo.api_keys ADD LastUsedAt DATETIME2 NULL;

                IF COL_LENGTH('dbo.api_keys', 'LastUsedIp') IS NULL
                    ALTER TABLE dbo.api_keys ADD LastUsedIp NVARCHAR(45) NULL;

                IF COL_LENGTH('dbo.api_keys', 'CreatedAt') IS NULL
                    ALTER TABLE dbo.api_keys ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_api_keys_CreatedAt DEFAULT SYSUTCDATETIME();

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_api_keys_TenantId' AND object_id = OBJECT_ID('dbo.api_keys'))
                    CREATE INDEX IX_api_keys_TenantId ON dbo.api_keys(TenantId);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_api_keys_KeyPrefix' AND object_id = OBJECT_ID('dbo.api_keys'))
                    CREATE INDEX IX_api_keys_KeyPrefix ON dbo.api_keys(KeyPrefix);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_api_keys_ExpiresAt' AND object_id = OBJECT_ID('dbo.api_keys'))
                    CREATE INDEX IX_api_keys_ExpiresAt ON dbo.api_keys(ExpiresAt);
            END;
        ");

        migrationBuilder.Sql(@"
            DECLARE @permissions TABLE
            (
                PermissionCode NVARCHAR(50) NOT NULL,
                Description NVARCHAR(200) NOT NULL,
                Category NVARCHAR(50) NOT NULL
            );

            INSERT INTO @permissions (PermissionCode, Description, Category) VALUES
                ('template.read', 'View return templates', 'template'),
                ('template.edit', 'Edit template definitions', 'template'),
                ('template.publish', 'Publish template versions', 'template'),
                ('submission.read', 'View return submissions', 'submission'),
                ('submission.create', 'Create new returns', 'submission'),
                ('submission.edit', 'Edit return data', 'submission'),
                ('submission.validate', 'Run validation', 'submission'),
                ('submission.submit', 'Submit for review', 'submission'),
                ('submission.review', 'Review submitted returns', 'submission'),
                ('submission.approve', 'Approve returns', 'submission'),
                ('submission.reject', 'Reject returns', 'submission'),
                ('submission.export', 'Export return data', 'submission'),
                ('submission.delete', 'Delete draft returns', 'submission'),
                ('user.read', 'View user list', 'user'),
                ('user.create', 'Create new users', 'user'),
                ('user.edit', 'Edit user profiles', 'user'),
                ('user.deactivate', 'Deactivate users', 'user'),
                ('user.role.assign', 'Assign roles to users', 'user'),
                ('billing.read', 'View invoices and subscription', 'billing'),
                ('billing.manage', 'Change plan and modules', 'billing'),
                ('report.read', 'View dashboards and reports', 'report'),
                ('report.create', 'Create ad-hoc reports', 'report'),
                ('report.schedule', 'Schedule report delivery', 'report'),
                ('settings.read', 'View tenant settings', 'settings'),
                ('settings.edit', 'Edit tenant settings', 'settings'),
                ('settings.branding', 'Edit branding configuration', 'settings'),
                ('audit.read', 'View audit logs', 'audit'),
                ('calendar.read', 'View filing calendar', 'calendar'),
                ('calendar.manage', 'Manage filing deadlines', 'calendar'),
                ('notification.manage', 'Manage notification preferences', 'notification'),
                ('admin.platform', 'Platform administration', 'admin');

            MERGE dbo.permissions AS target
            USING @permissions AS source
                ON target.PermissionCode = source.PermissionCode
            WHEN MATCHED THEN
                UPDATE SET
                    Description = source.Description,
                    Category = source.Category,
                    IsActive = 1
            WHEN NOT MATCHED THEN
                INSERT (PermissionCode, Description, Category, IsActive)
                VALUES (source.PermissionCode, source.Description, source.Category, 1);
        ");

        migrationBuilder.Sql(@"
            DECLARE @roles TABLE
            (
                RoleName NVARCHAR(50) NOT NULL,
                Description NVARCHAR(200) NULL,
                IsSystemRole BIT NOT NULL
            );

            INSERT INTO @roles (RoleName, Description, IsSystemRole) VALUES
                ('Admin', 'Default admin role', 1),
                ('Maker', 'Default maker role', 1),
                ('Checker', 'Default checker role', 1),
                ('Approver', 'Default approver role', 1),
                ('Viewer', 'Default viewer role', 1),
                ('PlatformAdmin', 'Platform administrator role', 1);

            MERGE dbo.roles AS target
            USING @roles AS source
                ON target.TenantId IS NULL AND target.RoleName = source.RoleName
            WHEN MATCHED THEN
                UPDATE SET
                    Description = source.Description,
                    IsSystemRole = source.IsSystemRole,
                    IsActive = 1
            WHEN NOT MATCHED THEN
                INSERT (TenantId, RoleName, Description, IsSystemRole, IsActive)
                VALUES (NULL, source.RoleName, source.Description, source.IsSystemRole, 1);
        ");

        migrationBuilder.Sql(@"
            DECLARE @rolePermissionMap TABLE
            (
                RoleName NVARCHAR(50) NOT NULL,
                PermissionCode NVARCHAR(50) NOT NULL
            );

            INSERT INTO @rolePermissionMap (RoleName, PermissionCode)
            SELECT 'Admin', PermissionCode
            FROM dbo.permissions
            WHERE PermissionCode <> 'admin.platform';

            INSERT INTO @rolePermissionMap (RoleName, PermissionCode) VALUES
                ('Maker', 'template.read'),
                ('Maker', 'submission.read'),
                ('Maker', 'submission.create'),
                ('Maker', 'submission.edit'),
                ('Maker', 'submission.validate'),
                ('Maker', 'submission.submit'),
                ('Maker', 'report.read'),
                ('Maker', 'calendar.read'),

                ('Checker', 'template.read'),
                ('Checker', 'submission.read'),
                ('Checker', 'submission.review'),
                ('Checker', 'submission.reject'),
                ('Checker', 'report.read'),

                ('Approver', 'template.read'),
                ('Approver', 'submission.read'),
                ('Approver', 'submission.approve'),
                ('Approver', 'submission.reject'),
                ('Approver', 'report.read'),

                ('Viewer', 'template.read'),
                ('Viewer', 'submission.read'),
                ('Viewer', 'report.read'),
                ('Viewer', 'calendar.read');

            INSERT INTO @rolePermissionMap (RoleName, PermissionCode)
            SELECT 'PlatformAdmin', PermissionCode
            FROM dbo.permissions;

            DELETE rp
            FROM dbo.role_permissions rp
            INNER JOIN dbo.roles r
                ON r.Id = rp.RoleId
            WHERE r.TenantId IS NULL
              AND r.RoleName IN ('Admin', 'Maker', 'Checker', 'Approver', 'Viewer', 'PlatformAdmin');

            INSERT INTO dbo.role_permissions (RoleId, PermissionId)
            SELECT DISTINCT r.Id, p.Id
            FROM @rolePermissionMap map
            INNER JOIN dbo.roles r
                ON r.TenantId IS NULL
               AND r.RoleName = map.RoleName
            INNER JOIN dbo.permissions p
                ON p.PermissionCode = map.PermissionCode
            WHERE NOT EXISTS (
                SELECT 1
                FROM dbo.role_permissions rp
                WHERE rp.RoleId = r.Id
                  AND rp.PermissionId = p.Id
            );
        ");

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

            SET @first = 1;
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

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF OBJECT_ID(N'dbo.role_permissions', N'U') IS NOT NULL DROP TABLE dbo.role_permissions;
            IF OBJECT_ID(N'dbo.roles', N'U') IS NOT NULL DROP TABLE dbo.roles;
            IF OBJECT_ID(N'dbo.permissions', N'U') IS NOT NULL DROP TABLE dbo.permissions;
            IF OBJECT_ID(N'dbo.tenant_sso_configs', N'U') IS NOT NULL DROP TABLE dbo.tenant_sso_configs;
            IF OBJECT_ID(N'dbo.user_mfa_configs', N'U') IS NOT NULL DROP TABLE dbo.user_mfa_configs;
            IF OBJECT_ID(N'dbo.refresh_tokens', N'U') IS NOT NULL DROP TABLE dbo.refresh_tokens;
            -- api_keys additive columns are intentionally kept for backward compatibility.
        ");
    }
}
