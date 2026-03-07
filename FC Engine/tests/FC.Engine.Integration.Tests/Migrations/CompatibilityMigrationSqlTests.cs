using Dapper;
using FC.Engine.Infrastructure.Metadata;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace FC.Engine.Integration.Tests.Migrations;

public class CompatibilityMigrationSqlTests
{
    private const string LegacyPreModularMigration = "20260304100000_AddMultiTenancy";
    private static readonly Guid LegacyTenantId = new("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task LegacyInstall_Migrates_To_ModularSchema_And_Backfills_Current_Compatibility_Data()
    {
        var baselineConnectionString = await TestSqlConnectionResolver.ResolveAsync();
        var databaseName = $"FcEngineCompat_{Guid.NewGuid():N}";
        var databaseConnectionString = BuildDatabaseConnectionString(baselineConnectionString, databaseName);
        var masterConnectionString = BuildDatabaseConnectionString(baselineConnectionString, "master");

        await CreateDatabaseAsync(masterConnectionString, databaseName);

        try
        {
            await using (var legacyDb = CreateDbContext(databaseConnectionString))
            {
                var migrator = legacyDb.GetService<IMigrator>();
                await migrator.MigrateAsync(LegacyPreModularMigration);
            }

            await SeedLegacyTemplatesAsync(databaseConnectionString);

            await using (var currentDb = CreateDbContext(databaseConnectionString))
            {
                await currentDb.Database.MigrateAsync();
            }

            await using var conn = new SqlConnection(databaseConnectionString);
            await conn.OpenAsync();

            var modularTableCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.tables t
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = 'dbo'
                  AND t.name IN ('licence_types', 'modules', 'licence_module_matrix', 'tenant_licence_types', 'module_versions', 'inter_module_data_flows');
                """);
            modularTableCount.Should().Be(6);

            var seededLicenceTypes = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.licence_types;");
            var seededModules = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.modules;");
            var seededMatrixRows = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.licence_module_matrix;");

            seededLicenceTypes.Should().Be(11);
            seededModules.Should().BeGreaterOrEqualTo(15);
            seededMatrixRows.Should().BeGreaterThan(0);

            var legacyTenant = await conn.QuerySingleAsync<(string TenantStatus, string TenantType)>(
                """
                SELECT TenantStatus, TenantType
                FROM dbo.tenants
                WHERE TenantId = @tenantId;
                """,
                new { tenantId = LegacyTenantId });

            legacyTenant.TenantStatus.Should().Be("Active");
            legacyTenant.TenantType.Should().Be("Institution");

            var legacyTenantFcLicenceCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM dbo.tenant_licence_types tlt
                INNER JOIN dbo.licence_types lt ON lt.Id = tlt.LicenceTypeId
                WHERE tlt.TenantId = @tenantId
                  AND lt.Code = 'FC'
                  AND tlt.IsActive = 1;
                """,
                new { tenantId = LegacyTenantId });

            legacyTenantFcLicenceCount.Should().Be(1);

            var linkedTemplateModules = (await conn.QueryAsync<string>(
                """
                SELECT m.ModuleCode
                FROM meta.return_templates rt
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE rt.ReturnCode IN @returnCodes
                ORDER BY rt.ReturnCode;
                """,
                new
                {
                    returnCodes = new[]
                    {
                        "MFCR_COMPAT_001",
                        "MFCR_COMPAT_002"
                    }
                })).ToList();

            linkedTemplateModules.Should().Equal("FC_RETURNS", "FC_RETURNS");

            var crossSheetRuleColumns = (await conn.QueryAsync<string>(
                """
                SELECT c.name
                FROM sys.columns c
                WHERE c.object_id = OBJECT_ID(N'meta.cross_sheet_rules')
                  AND c.name IN ('ModuleId', 'SourceModuleId', 'TargetModuleId', 'SourceTemplateCode', 'SourceFieldCode', 'TargetTemplateCode', 'TargetFieldCode', 'Operator', 'ToleranceAmount', 'TolerancePercent');
                """)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            crossSheetRuleColumns.Should().BeEquivalentTo(
                new[]
                {
                    "ModuleId",
                    "SourceModuleId",
                    "TargetModuleId",
                    "SourceTemplateCode",
                    "SourceFieldCode",
                    "TargetTemplateCode",
                    "TargetFieldCode",
                    "Operator",
                    "ToleranceAmount",
                    "TolerancePercent"
                });

            var tenantLicencePredicateCount = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM sys.security_predicates sp
                INNER JOIN sys.tables t ON t.object_id = sp.target_object_id
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = 'dbo'
                  AND t.name = 'tenant_licence_types';
                """);

            tenantLicencePredicateCount.Should().BeGreaterOrEqualTo(2);
        }
        finally
        {
            await DropDatabaseAsync(masterConnectionString, databaseName);
        }
    }

    private static MetadataDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.CommandTimeout(60);
                sql.EnableRetryOnFailure(3);
            })
            .Options;

        return new MetadataDbContext(options);
    }

    private static string BuildDatabaseConnectionString(string baselineConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baselineConnectionString)
        {
            InitialCatalog = databaseName
        };

        return builder.ConnectionString;
    }

    private static async Task CreateDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using var conn = new SqlConnection(masterConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync($"""
            IF DB_ID(N'{databaseName}') IS NULL
                CREATE DATABASE [{databaseName}];
            """);
    }

    private static async Task DropDatabaseAsync(string masterConnectionString, string databaseName)
    {
        await using var conn = new SqlConnection(masterConnectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync($"""
            IF DB_ID(N'{databaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """);
    }

    private static async Task SeedLegacyTemplatesAsync(string connectionString)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO meta.return_templates
                (ReturnCode, Name, Description, Frequency, StructuralCategory, PhysicalTableName, XmlRootElement, XmlNamespace,
                 IsSystemTemplate, OwnerDepartment, InstitutionType, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, TenantId)
            VALUES
                ('MFCR_COMPAT_001', 'Compatibility Template 1', NULL, 'Monthly', 'FixedRow', 'mfcr_compat_001', 'MFCR_COMPAT_001', 'urn:cbn:compat:mfcr1', 1, 'DFIS', 'FC', SYSUTCDATETIME(), 'compat-test', SYSUTCDATETIME(), 'compat-test', NULL),
                ('MFCR_COMPAT_002', 'Compatibility Template 2', NULL, 'Quarterly', 'FixedRow', 'mfcr_compat_002', 'MFCR_COMPAT_002', 'urn:cbn:compat:mfcr2', 1, 'DFIS', 'FC', SYSUTCDATETIME(), 'compat-test', SYSUTCDATETIME(), 'compat-test', NULL);
            """);
    }
}
