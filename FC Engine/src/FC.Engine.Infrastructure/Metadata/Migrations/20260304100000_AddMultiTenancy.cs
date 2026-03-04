using System;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace FC.Engine.Infrastructure.Metadata.Migrations
{
    /// <summary>
    /// RG-01: Multi-Tenancy Foundation Migration
    /// Creates Tenants table, adds TenantId to ALL existing tables,
    /// creates default tenant, sets up SQL Server Row-Level Security,
    /// and adds TenantId to all DdlEngine-managed dynamic tables.
    /// FULLY REVERSIBLE.
    /// </summary>
    [DbContext(typeof(MetadataDbContext))]
    [Migration("20260304100000_AddMultiTenancy")]
    public partial class AddMultiTenancy : Migration
    {
        // Default tenant ID — deterministic GUID for reproducibility
        private static readonly Guid DefaultTenantId = new("00000000-0000-0000-0000-000000000001");

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════
            // STEP 1: Create Tenants table
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TenantSlug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TenantStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "PendingActivation"),
                    ContactEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.TenantId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenants_TenantSlug",
                table: "tenants",
                column: "TenantSlug",
                unique: true);

            // ═══════════════════════════════════════════════════════════
            // STEP 2: Insert default tenant
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql($@"
                INSERT INTO dbo.tenants (TenantId, TenantName, TenantSlug, TenantStatus, CreatedAt, UpdatedAt)
                VALUES ('{DefaultTenantId}', 'FC Engine Legacy', 'fc-engine-legacy', 'Active', SYSUTCDATETIME(), SYSUTCDATETIME());
            ");

            // ═══════════════════════════════════════════════════════════
            // STEP 3: Add TenantId to all metadata/operational tables
            // ═══════════════════════════════════════════════════════════

            // -- Institutions (NOT NULL, FK to Tenants)
            AddTenantIdColumn(migrationBuilder, "institutions", nullable: false);
            migrationBuilder.AddForeignKey(
                name: "FK_institutions_tenants_TenantId",
                table: "institutions",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Cascade);

            // -- return_submissions
            AddTenantIdColumn(migrationBuilder, "return_submissions", nullable: false);

            // -- return_periods
            AddTenantIdColumn(migrationBuilder, "return_periods", nullable: false);

            // -- validation_reports
            AddTenantIdColumn(migrationBuilder, "validation_reports", nullable: false);

            // -- portal_notifications
            AddTenantIdColumn(migrationBuilder, "portal_notifications", nullable: false);

            // -- Meta schema tables
            AddTenantIdColumn(migrationBuilder, "institution_users", nullable: false, schema: "meta");
            AddTenantIdColumn(migrationBuilder, "submission_approvals", nullable: false, schema: "meta");
            AddTenantIdColumn(migrationBuilder, "portal_users", nullable: true, schema: "meta"); // Nullable: PlatformAdmin has no tenant
            AddTenantIdColumn(migrationBuilder, "login_attempts", nullable: true, schema: "meta"); // Nullable: login attempts may not have tenant
            AddTenantIdColumn(migrationBuilder, "audit_log", nullable: true, schema: "meta"); // Nullable: platform-level audits

            // -- Template/Rule metadata tables (nullable for global/system templates)
            AddTenantIdColumn(migrationBuilder, "return_templates", nullable: true, schema: "meta");
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_return_templates_ReturnCode'
                      AND object_id = OBJECT_ID(N'[meta].[return_templates]')
                )
                BEGIN
                    DROP INDEX [IX_return_templates_ReturnCode] ON [meta].[return_templates];
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_return_templates_ReturnCode_TenantId'
                      AND object_id = OBJECT_ID(N'[meta].[return_templates]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_return_templates_ReturnCode_TenantId]
                        ON [meta].[return_templates]([ReturnCode], [TenantId]);
                END;
            ");
            AddTenantIdColumn(migrationBuilder, "template_versions", nullable: true, schema: "meta");
            AddTenantIdColumn(migrationBuilder, "cross_sheet_rules", nullable: true, schema: "meta");
            AddTenantIdColumn(migrationBuilder, "business_rules", nullable: true, schema: "meta");

            // template_versions inherits tenant scope from its parent template.
            migrationBuilder.Sql(@"
                UPDATE tv
                SET tv.TenantId = rt.TenantId
                FROM [meta].[template_versions] tv
                INNER JOIN [meta].[return_templates] rt ON rt.Id = tv.TemplateId;
            ");

            // ═══════════════════════════════════════════════════════════
            // STEP 4: Add TenantId to ALL DdlEngine-managed dynamic tables
            // Uses dynamic SQL to find all tables with submission_id column
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql($@"
                DECLARE @defaultTenantId UNIQUEIDENTIFIER = '{DefaultTenantId}';
                DECLARE @sql NVARCHAR(MAX) = '';

                -- Find all tables with submission_id that don't yet have TenantId
                SELECT @sql = @sql +
                    'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name)
                    + ' ADD TenantId UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_' + t.name + '_TenantId] DEFAULT(''' + CAST(@defaultTenantId AS NVARCHAR(36)) + ''');' + CHAR(13)
                    + 'CREATE NONCLUSTERED INDEX [IX_' + t.name + '_TenantId] ON ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) + '(TenantId);' + CHAR(13)
                FROM sys.tables t
                INNER JOIN sys.columns c ON t.object_id = c.object_id AND c.name = 'submission_id'
                WHERE NOT EXISTS (
                    SELECT 1 FROM sys.columns c2
                    WHERE c2.object_id = t.object_id AND c2.name = 'TenantId'
                )
                AND t.name NOT IN ('return_submissions'); -- Already handled above

                IF LEN(@sql) > 0
                    EXEC sp_executesql @sql;
            ");

            // ═══════════════════════════════════════════════════════════
            // STEP 5: Create RLS security function
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql(@"
                CREATE FUNCTION dbo.fn_TenantFilter(@TenantId UNIQUEIDENTIFIER)
                RETURNS TABLE WITH SCHEMABINDING
                AS RETURN SELECT 1 AS result
                    WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER)
                       OR @TenantId IS NULL
                       OR SESSION_CONTEXT(N'TenantId') IS NULL;
            ");

            // ═══════════════════════════════════════════════════════════
            // STEP 6: Create RLS security policy dynamically
            // Applies FILTER + BLOCK predicates to ALL tables with TenantId
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql(@"
                DECLARE @policySql NVARCHAR(MAX) = 'CREATE SECURITY POLICY dbo.TenantSecurityPolicy' + CHAR(13);
                DECLARE @first BIT = 1;

                -- Build FILTER predicates for all tables with TenantId column
                SELECT
                    @policySql = @policySql +
                        CASE WHEN @first = 1 THEN '    ' ELSE '   ,' END +
                        'ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON ' +
                        QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) + CHAR(13),
                    @first = 0
                FROM sys.tables t
                INNER JOIN sys.columns c ON t.object_id = c.object_id
                WHERE c.name = 'TenantId'
                  AND t.name <> 'tenants'
                ORDER BY t.name;

                -- Add BLOCK predicates for all tables with TenantId column
                SELECT
                    @policySql = @policySql +
                        '   ,ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON ' +
                        QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) + CHAR(13)
                FROM sys.tables t
                INNER JOIN sys.columns c ON t.object_id = c.object_id
                WHERE c.name = 'TenantId'
                  AND t.name <> 'tenants'
                ORDER BY t.name;

                SET @policySql = @policySql + 'WITH (STATE = ON);';

                EXEC sp_executesql @policySql;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════
            // REVERSE STEP 6: Drop security policy
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')
                    DROP SECURITY POLICY dbo.TenantSecurityPolicy;
            ");

            // ═══════════════════════════════════════════════════════════
            // REVERSE STEP 5: Drop security function
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.objects WHERE name = 'fn_TenantFilter' AND type = 'IF')
                    DROP FUNCTION dbo.fn_TenantFilter;
            ");

            // ═══════════════════════════════════════════════════════════
            // REVERSE STEP 4: Remove TenantId from dynamic tables
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.Sql(@"
                DECLARE @sql NVARCHAR(MAX) = '';

                -- Drop indexes first
                SELECT @sql = @sql +
                    'DROP INDEX IF EXISTS [IX_' + t.name + '_TenantId] ON '
                    + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) + ';' + CHAR(13)
                FROM sys.tables t
                INNER JOIN sys.columns c ON t.object_id = c.object_id AND c.name = 'submission_id'
                INNER JOIN sys.columns ct ON t.object_id = ct.object_id AND ct.name = 'TenantId'
                WHERE t.name NOT IN ('return_submissions');

                -- Drop default constraints
                SELECT @sql = @sql +
                    'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name)
                    + ' DROP CONSTRAINT IF EXISTS [DF_' + t.name + '_TenantId];' + CHAR(13)
                FROM sys.tables t
                INNER JOIN sys.columns c ON t.object_id = c.object_id AND c.name = 'submission_id'
                INNER JOIN sys.columns ct ON t.object_id = ct.object_id AND ct.name = 'TenantId'
                WHERE t.name NOT IN ('return_submissions');

                -- Drop columns
                SELECT @sql = @sql +
                    'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name)
                    + ' DROP COLUMN TenantId;' + CHAR(13)
                FROM sys.tables t
                INNER JOIN sys.columns c ON t.object_id = c.object_id AND c.name = 'submission_id'
                INNER JOIN sys.columns ct ON t.object_id = ct.object_id AND ct.name = 'TenantId'
                WHERE t.name NOT IN ('return_submissions');

                IF LEN(@sql) > 0
                    EXEC sp_executesql @sql;
            ");

            // ═══════════════════════════════════════════════════════════
            // REVERSE STEP 3: Remove TenantId from metadata/operational tables
            // ═══════════════════════════════════════════════════════════

            // Drop FK first
            migrationBuilder.DropForeignKey(
                name: "FK_institutions_tenants_TenantId",
                table: "institutions");

            // Drop TenantId indexes and columns (reverse order of creation)
            RemoveTenantIdColumn(migrationBuilder, "business_rules", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "cross_sheet_rules", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "template_versions", schema: "meta");
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_return_templates_ReturnCode_TenantId'
                      AND object_id = OBJECT_ID(N'[meta].[return_templates]')
                )
                BEGIN
                    DROP INDEX [IX_return_templates_ReturnCode_TenantId] ON [meta].[return_templates];
                END;

                IF NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE name = 'IX_return_templates_ReturnCode'
                      AND object_id = OBJECT_ID(N'[meta].[return_templates]')
                )
                BEGIN
                    CREATE UNIQUE INDEX [IX_return_templates_ReturnCode]
                        ON [meta].[return_templates]([ReturnCode]);
                END;
            ");
            RemoveTenantIdColumn(migrationBuilder, "return_templates", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "audit_log", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "login_attempts", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "portal_users", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "submission_approvals", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "institution_users", schema: "meta");
            RemoveTenantIdColumn(migrationBuilder, "portal_notifications");
            RemoveTenantIdColumn(migrationBuilder, "validation_reports");
            RemoveTenantIdColumn(migrationBuilder, "return_periods");
            RemoveTenantIdColumn(migrationBuilder, "return_submissions");
            RemoveTenantIdColumn(migrationBuilder, "institutions");

            // ═══════════════════════════════════════════════════════════
            // REVERSE STEP 1: Drop Tenants table
            // ═══════════════════════════════════════════════════════════
            migrationBuilder.DropTable(name: "tenants");
        }

        // ═══════════════════════════════════════════════════════════
        // Helper: Add TenantId column with default value migration
        // ═══════════════════════════════════════════════════════════
        private void AddTenantIdColumn(MigrationBuilder migrationBuilder, string table, bool nullable, string? schema = null)
        {
            var fullTable = schema != null ? $"{schema}.{table}" : table;
            var defaultTenantStr = DefaultTenantId.ToString();

            // Add column (nullable initially to allow backfill)
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: table,
                schema: schema,
                type: "uniqueidentifier",
                nullable: true);

            // Backfill existing data with default tenant
            migrationBuilder.Sql($@"
                UPDATE [{schema ?? "dbo"}].[{table}]
                SET TenantId = '{defaultTenantStr}'
                WHERE TenantId IS NULL;
            ");

            // Make NOT NULL if required
            if (!nullable)
            {
                migrationBuilder.AlterColumn<Guid>(
                    name: "TenantId",
                    table: table,
                    schema: schema,
                    type: "uniqueidentifier",
                    nullable: false,
                    defaultValue: DefaultTenantId);
            }

            // Add index
            migrationBuilder.CreateIndex(
                name: $"IX_{table}_TenantId",
                table: table,
                schema: schema,
                column: "TenantId");
        }

        private static void RemoveTenantIdColumn(MigrationBuilder migrationBuilder, string table, string? schema = null)
        {
            migrationBuilder.DropIndex(
                name: $"IX_{table}_TenantId",
                table: table,
                schema: schema);

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: table,
                schema: schema);
        }
    }
}
