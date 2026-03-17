using Dapper;
using FC.Engine.Application.Models;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using SubmissionEntity = FC.Engine.Domain.Entities.Submission;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Validation;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Caching;
using FC.Engine.Infrastructure.DynamicSchema;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Metadata.Repositories;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Persistence;
using FC.Engine.Infrastructure.Services;
using FC.Engine.Infrastructure.Validation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace FC.Engine.Integration.Tests.ModuleLoading;

/// <summary>
/// Real SQL integration coverage for RG-08:
/// 1) imports + publishes all 3 RG-08 modules,
/// 2) proves 36 physical tables exist with FILTER+BLOCK RLS predicates,
/// 3) proves RG-08 dataflow + cross-sheet + cross-module validation lifecycle on real dynamic tables.
/// </summary>
public class Rg08ModuleLoadingSqlTests : IAsyncLifetime
{
    private readonly List<Guid> _createdTenantIds = new();
    private string _connectionString = null!;

    private static readonly (string ModuleCode, string FileName, int ExpectedTemplates)[] Rg08Definitions =
    [
        ("BDC_CBN", "rg08-bdc-cbn-module-definition.json", 12),
        ("MFB_PAR", "rg08-mfb-par-module-definition.json", 12),
        ("NFIU_AML", "rg08-nfiu-aml-module-definition.json", 12)
    ];

    private static readonly HashSet<string> SqlIsolatedModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "BDC_CBN",
        "MFB_PAR",
        "NFIU_AML"
    };

    public async Task InitializeAsync()
    {
        _connectionString = await TestSqlConnectionResolver.ResolveAsync();
        await EnsureSchemaCompatibilityAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupTenantDataAsync();
    }

    [Fact]
    public async Task Rg08_RealSql_Publish_Rls_DataFlow_And_Validation_Are_EndToEnd_Green()
    {
        await using var db = CreateDbContext();
        await EnsureRg08ModulesExistAsync(db);
        await EnsureRg08IsImportedAndPublishedAsync(db, CancellationToken.None);

        var moduleCodes = Rg08Definitions.Select(d => ToSqlTestModuleCode(d.ModuleCode)).ToArray();
        var moduleCodeList = moduleCodes.ToList();
        var bdcModuleCode = ToSqlTestModuleCode("BDC_CBN");
        var mfbModuleCode = ToSqlTestModuleCode("MFB_PAR");
        var nfiuModuleCode = ToSqlTestModuleCode("NFIU_AML");
        var bdcAmlCode = ToSqlTestReturnCode("BDC_AML");
        var mfbAmlCode = ToSqlTestReturnCode("MFB_AML");
        var nfiuStrCode = ToSqlTestReturnCode("NFIU_STR");
        var nfiuCtrCode = ToSqlTestReturnCode("NFIU_CTR");

        var templates = await db.ReturnTemplates
            .AsNoTracking()
            .Include(t => t.Module)
            .Where(t => t.ModuleId != null && t.Module != null && moduleCodeList.Contains(t.Module.ModuleCode))
            .ToListAsync();

        templates.Should().HaveCount(36);

        var publishedVersions = await db.TemplateVersions
            .Join(
                db.ReturnTemplates.Include(t => t.Module).Where(t => t.Module != null && moduleCodeList.Contains(t.Module.ModuleCode)),
                v => v.TemplateId,
                t => t.Id,
                (v, _) => v)
            .CountAsync(v => v.Status == TemplateStatus.Published);
        publishedVersions.Should().Be(36);

        var tableNames = templates
            .Select(t => t.PhysicalTableName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        tableNames.Should().HaveCount(36);

        await using (var conn = new SqlConnection(_connectionString))
        {
            await conn.OpenAsync();

            foreach (var tableName in tableNames)
            {
                var exists = await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM sys.tables t
                    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = 'dbo' AND t.name = @tableName;
                    """,
                    new { tableName });

                var predicateCount = await conn.ExecuteScalarAsync<int>(
                    """
                    SELECT COUNT(*)
                    FROM sys.security_predicates sp
                    INNER JOIN sys.tables t ON t.object_id = sp.target_object_id
                    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = 'dbo' AND t.name = @tableName;
                    """,
                    new { tableName });

                exists.Should().Be(1, $"dynamic table dbo.{tableName} must exist");
                predicateCount.Should().BeGreaterOrEqualTo(2, $"dbo.{tableName} must have FILTER and BLOCK predicates");
            }
        }

        var tenant = Tenant.Create(
            $"RG08 SQL Tenant {Guid.NewGuid():N}"[..30],
            $"rg08-sql-{Guid.NewGuid():N}"[..20],
            TenantType.Institution,
            "rg08-sql@test.local");
        tenant.Activate();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        _createdTenantIds.Add(tenant.TenantId);

        var institution = new Institution
        {
            TenantId = tenant.TenantId,
            InstitutionCode = $"RG08{Guid.NewGuid():N}"[..10],
            InstitutionName = "RG08 SQL Institution",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Institutions.Add(institution);

        var returnPeriod = new ReturnPeriod
        {
            TenantId = tenant.TenantId,
            Year = 2026,
            Month = 1,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2026, 1, 31),
            IsOpen = true,
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(returnPeriod);
        await db.SaveChangesAsync();

        var bdcSubmission = SubmissionEntity.Create(institution.Id, returnPeriod.Id, bdcAmlCode, tenant.TenantId);
        var mfbSubmission = SubmissionEntity.Create(institution.Id, returnPeriod.Id, mfbAmlCode, tenant.TenantId);
        var nfiuStrSubmission = SubmissionEntity.Create(institution.Id, returnPeriod.Id, nfiuStrCode, tenant.TenantId);
        var nfiuCtrSubmission = SubmissionEntity.Create(institution.Id, returnPeriod.Id, nfiuCtrCode, tenant.TenantId);
        db.Submissions.AddRange(bdcSubmission, mfbSubmission, nfiuStrSubmission, nfiuCtrSubmission);
        await db.SaveChangesAsync();

        var tenantContext = new MutableTenantContext(tenant.TenantId);
        await using var cacheProvider = BuildCacheServiceProvider(tenantContext);
        var templateCache = new TemplateMetadataCache(cacheProvider);
        var dataRepo = new GenericDataRepository(
            new TenantAwareConnectionFactory(new StaticDataResidencyRouter(_connectionString), new HttpContextAccessor()),
            tenantContext,
            templateCache,
            new DynamicSqlBuilder(),
            new TestDbContextFactory(db));

        var bdcRecord = await BuildDefaultRecord(
            templateCache,
            bdcAmlCode,
            new Dictionary<string, object?>
            {
                ["str_filed_count"] = 8m,
                ["ctr_filed_count"] = 4m
            });
        await dataRepo.Save(bdcRecord, bdcSubmission.Id);

        var mfbRecord = await BuildDefaultRecord(
            templateCache,
            mfbAmlCode,
            new Dictionary<string, object?>
            {
                ["str_filed_count"] = 5m,
                ["ctr_filed_count"] = 6m
            });
        await dataRepo.Save(mfbRecord, mfbSubmission.Id);

        var nfiuStrRecord = await BuildDefaultRecord(
            templateCache,
            nfiuStrCode,
            new Dictionary<string, object?>
            {
                ["str_filed_count"] = 0m
            });
        await dataRepo.Save(nfiuStrRecord, nfiuStrSubmission.Id);

        var nfiuCtrRecord = await BuildDefaultRecord(
            templateCache,
            nfiuCtrCode,
            new Dictionary<string, object?>
            {
                ["ctr_filed_count"] = 0m
            });
        await dataRepo.Save(nfiuCtrRecord, nfiuCtrSubmission.Id);

        var entitlement = new AllowAllEntitlementService();
        var dataFlowEngine = new InterModuleDataFlowEngine(
            db,
            entitlement,
            dataRepo,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await dataFlowEngine.ProcessDataFlows(
            tenant.TenantId,
            bdcSubmission.Id,
            bdcModuleCode,
            bdcAmlCode,
            institution.Id,
            returnPeriod.Id,
            CancellationToken.None);

        await dataFlowEngine.ProcessDataFlows(
            tenant.TenantId,
            mfbSubmission.Id,
            mfbModuleCode,
            mfbAmlCode,
            institution.Id,
            returnPeriod.Id,
            CancellationToken.None);

        var nfiuStrAfterFlow = await dataRepo.ReadFieldValue(nfiuStrCode, nfiuStrSubmission.Id, "str_filed_count");
        var nfiuCtrAfterFlow = await dataRepo.ReadFieldValue(nfiuCtrCode, nfiuCtrSubmission.Id, "ctr_filed_count");

        Convert.ToDecimal(nfiuStrAfterFlow).Should().Be(13m);
        Convert.ToDecimal(nfiuCtrAfterFlow).Should().Be(10m);

        var flowMetadata = await db.SubmissionFieldSources
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenant.TenantId
                && x.ReturnCode == nfiuStrCode
                && x.SubmissionId == nfiuStrSubmission.Id
                && x.FieldName == "str_filed_count");

        flowMetadata.Should().NotBeNull();
        flowMetadata!.DataSource.Should().Be("InterModule");
        flowMetadata.SourceDetail.Should().EndWith("/str_filed_count");

        var bdcModuleId = await db.Modules.Where(m => m.ModuleCode == bdcModuleCode).Select(m => m.Id).SingleAsync();
        var nfiuModuleId = await db.Modules.Where(m => m.ModuleCode == nfiuModuleCode).Select(m => m.Id).SingleAsync();

        var ruleCode = $"RG08_SQL_RULE_{Guid.NewGuid():N}"[..30];
        var crossRule = new CrossSheetRule
        {
            TenantId = null,
            RuleCode = ruleCode,
            RuleName = "RG08 BDC vs NFIU STR reconciliation",
            Description = "BDC STR count must equal NFIU STR count for same period",
            ModuleId = bdcModuleId,
            SourceModuleId = bdcModuleId,
            TargetModuleId = nfiuModuleId,
            SourceTemplateCode = bdcAmlCode,
            SourceFieldCode = "str_filed_count",
            TargetTemplateCode = nfiuStrCode,
            TargetFieldCode = "str_filed_count",
            Operator = "Equals",
            ToleranceAmount = 0m,
            Severity = ValidationSeverity.Error,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "rg08-sql-test",
            Expression = new CrossSheetRuleExpression
            {
                Expression = "A = B",
                ToleranceAmount = 0m,
                ErrorMessage = "RG08 SQL reconciliation failed"
            }
        };

        crossRule.AddOperand(new CrossSheetRuleOperand
        {
            OperandAlias = "A",
            TemplateReturnCode = bdcAmlCode,
            FieldName = "str_filed_count",
            SortOrder = 1
        });
        crossRule.AddOperand(new CrossSheetRuleOperand
        {
            OperandAlias = "B",
            TemplateReturnCode = nfiuStrCode,
            FieldName = "str_filed_count",
            SortOrder = 2
        });

        db.CrossSheetRules.Add(crossRule);
        await db.SaveChangesAsync();

        await dataRepo.WriteFieldValue(
            nfiuStrCode,
            nfiuStrSubmission.Id,
            "str_filed_count",
            1m,
            "Manual",
            "rg08-sql-override",
            changedBy: "TestUser",
            CancellationToken.None);

        var formulaRepo = new FormulaRepository(db);
        var validator = new CrossSheetValidator(formulaRepo, dataRepo, templateCache, db, entitlement);

        var sourceRecord = await dataRepo.GetBySubmission(bdcAmlCode, bdcSubmission.Id);
        sourceRecord.Should().NotBeNull();

        var crossSheetErrors = await validator.Validate(
            sourceRecord!,
            institution.Id,
            returnPeriod.Id,
            CancellationToken.None);
        crossSheetErrors.Should().Contain(e => e.RuleId == ruleCode);

        var crossModuleErrors = await validator.ValidateCrossModule(
            tenant.TenantId,
            bdcSubmission.Id,
            bdcModuleCode,
            institution.Id,
            returnPeriod.Id,
            CancellationToken.None);
        crossModuleErrors.Should().Contain(e => e.RuleId == ruleCode);
    }

    private async Task EnsureRg08ModulesExistAsync(MetadataDbContext db)
    {
        foreach (var (baseModuleCode, _, _) in Rg08Definitions)
        {
            var moduleCode = ToSqlTestModuleCode(baseModuleCode);
            var exists = await db.Modules.AnyAsync(m => m.ModuleCode == moduleCode);
            if (exists)
            {
                continue;
            }

            db.Modules.Add(new Module
            {
                ModuleCode = moduleCode,
                ModuleName = $"{moduleCode} Module",
                RegulatorCode = moduleCode.StartsWith("NFIU", StringComparison.OrdinalIgnoreCase) ? "NFIU" : "CBN",
                Description = $"Seeded for RG-08 SQL integration test ({DateTime.UtcNow:O}).",
                DefaultFrequency = "Monthly",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Keep this available for MFB -> NDIC inter-module flow FK references.
        if (!await db.Modules.AnyAsync(m => m.ModuleCode == "NDIC_RETURNS"))
        {
            db.Modules.Add(new Module
            {
                ModuleCode = "NDIC_RETURNS",
                ModuleName = "NDIC_RETURNS Module",
                RegulatorCode = "NDIC",
                Description = "Seeded NDIC module for RG-08 SQL integration flow references.",
                DefaultFrequency = "Quarterly",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task EnsureRg08IsImportedAndPublishedAsync(MetadataDbContext db, CancellationToken ct)
    {
        var moduleCodes = Rg08Definitions.Select(d => ToSqlTestModuleCode(d.ModuleCode)).ToArray();
        var moduleCodeList = moduleCodes.ToList();

        var cache = new NoopTemplateMetadataCache();
        var importService = new ModuleImportService(
            db,
            new DdlEngine(new SqlTypeMapper()),
            new DdlMigrationExecutor(db),
            cache,
            new SqlTypeMapper(),
            NullLogger<ModuleImportService>.Instance,
            notificationOrchestrator: null);

        foreach (var (moduleCode, fileName, expectedTemplates) in Rg08Definitions)
        {
            var sqlModuleCode = ToSqlTestModuleCode(moduleCode);
            var currentTemplateCount = await db.ReturnTemplates
                .Include(t => t.Module)
                .CountAsync(t => t.Module != null && t.Module.ModuleCode == sqlModuleCode, ct);

            if (currentTemplateCount == expectedTemplates)
            {
                var hasPublished = await db.TemplateVersions
                    .Join(
                        db.ReturnTemplates.Where(t => t.Module != null && t.Module.ModuleCode == sqlModuleCode),
                        v => v.TemplateId,
                        t => t.Id,
                        (v, _) => v)
                    .AnyAsync(v => v.Status == TemplateStatus.Published, ct);

                if (hasPublished)
                {
                    continue;
                }
            }

            var definition = await LoadDefinition(fileName, applySqlIsolation: true);
            var validation = await importService.ValidateDefinition(definition, ct);
            validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));

            var importResult = await importService.ImportModule(definition, "rg08-sql-test", ct);
            importResult.Success.Should().BeTrue(string.Join(" | ", importResult.Errors));

            var publishResult = await importService.PublishModule(sqlModuleCode, "rg08-sql-approver", ct);
            publishResult.Success.Should().BeTrue(string.Join(" | ", publishResult.Errors));
            publishResult.TablesCreated.Should().Be(expectedTemplates);
        }
    }

    private async Task CleanupRg08MetadataAndTablesAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var moduleCodes = Rg08Definitions.Select(d => d.ModuleCode).ToArray();
        var tableNames = (await conn.QueryAsync<string>(
            """
            SELECT rt.PhysicalTableName
            FROM meta.return_templates rt
            INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
            WHERE m.ModuleCode IN @moduleCodes
            """,
            new { moduleCodes }))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tableName in tableNames)
        {
            var safeTable = tableName.Replace("]", "]]", StringComparison.Ordinal);
            var predicates = (await conn.QueryAsync<SecurityPredicateRow>(
                """
                SELECT
                    OBJECT_SCHEMA_NAME(sp.object_id) AS PolicySchema,
                    sp.name AS PolicyName,
                    spred.operation AS Operation,
                    spred.predicate_definition AS PredicateDefinition
                FROM sys.security_predicates spred
                INNER JOIN sys.security_policies sp ON sp.object_id = spred.object_id
                INNER JOIN sys.tables t ON t.object_id = spred.target_object_id
                INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
                WHERE s.name = 'dbo' AND t.name = @tableName
                """,
                new { tableName }))
                .ToList();

            foreach (var predicate in predicates)
            {
                var operation = predicate.Operation == 2 ? "BLOCK" : "FILTER";
                var predicateDef = NormalizePredicateDefinition(predicate.PredicateDefinition);
                var dropSql = $"""
                    BEGIN TRY
                        ALTER SECURITY POLICY [{predicate.PolicySchema}].[{predicate.PolicyName}]
                        DROP {operation} PREDICATE {predicateDef} ON [dbo].[{safeTable}];
                    END TRY
                    BEGIN CATCH
                    END CATCH;
                    """;
                await conn.ExecuteAsync(dropSql);
            }

            await conn.ExecuteAsync(
                $"""
                IF OBJECT_ID(N'dbo.[{safeTable}]', N'U') IS NOT NULL
                    DROP TABLE dbo.[{safeTable}];
                """);
        }

        await conn.ExecuteAsync(
            """
            DELETE FROM meta.cross_sheet_rule_operands
            WHERE RuleId IN (
                SELECT Id
                FROM meta.cross_sheet_rules
                WHERE ModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
                   OR SourceModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
                   OR TargetModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
            );

            DELETE FROM meta.cross_sheet_rule_expressions
            WHERE RuleId IN (
                SELECT Id
                FROM meta.cross_sheet_rules
                WHERE ModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
                   OR SourceModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
                   OR TargetModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
            );

            DELETE FROM meta.cross_sheet_rules
            WHERE ModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
               OR SourceModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
               OR TargetModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes);

            DELETE FROM dbo.inter_module_data_flows
            WHERE SourceModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes)
               OR TargetModuleCode IN @moduleCodes;

            DELETE FROM meta.submission_field_sources
            WHERE ReturnCode IN (
                SELECT rt.ReturnCode
                FROM meta.return_templates rt
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE m.ModuleCode IN @moduleCodes
            );

            DELETE FROM meta.ddl_migrations
            WHERE TemplateId IN (
                SELECT rt.Id
                FROM meta.return_templates rt
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE m.ModuleCode IN @moduleCodes
            );

            DELETE FROM meta.intra_sheet_formulas
            WHERE TemplateVersionId IN (
                SELECT tv.Id
                FROM meta.template_versions tv
                INNER JOIN meta.return_templates rt ON rt.Id = tv.TemplateId
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE m.ModuleCode IN @moduleCodes
            );

            DELETE FROM meta.template_fields
            WHERE TemplateVersionId IN (
                SELECT tv.Id
                FROM meta.template_versions tv
                INNER JOIN meta.return_templates rt ON rt.Id = tv.TemplateId
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE m.ModuleCode IN @moduleCodes
            );

            DELETE FROM meta.template_item_codes
            WHERE TemplateVersionId IN (
                SELECT tv.Id
                FROM meta.template_versions tv
                INNER JOIN meta.return_templates rt ON rt.Id = tv.TemplateId
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE m.ModuleCode IN @moduleCodes
            );

            DELETE FROM meta.template_sections
            WHERE TemplateVersionId IN (
                SELECT tv.Id
                FROM meta.template_versions tv
                INNER JOIN meta.return_templates rt ON rt.Id = tv.TemplateId
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE m.ModuleCode IN @moduleCodes
            );

            DELETE FROM meta.template_versions
            WHERE TemplateId IN (
                SELECT rt.Id
                FROM meta.return_templates rt
                INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
                WHERE m.ModuleCode IN @moduleCodes
            );

            DELETE FROM meta.return_templates
            WHERE ModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes);

            DELETE FROM dbo.module_versions
            WHERE ModuleId IN (SELECT Id FROM dbo.modules WHERE ModuleCode IN @moduleCodes);
            """,
            new { moduleCodes });
    }

    private async Task CleanupTenantDataAsync()
    {
        if (_createdTenantIds.Count == 0)
        {
            return;
        }

        var tenantIds = _createdTenantIds.Distinct().ToArray();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var moduleCodes = Rg08Definitions.Select(d => ToSqlTestModuleCode(d.ModuleCode)).ToArray();
        var dynamicTables = (await conn.QueryAsync<string>(
            """
            SELECT rt.PhysicalTableName
            FROM meta.return_templates rt
            INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
            WHERE m.ModuleCode IN @moduleCodes
            """,
            new { moduleCodes }))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tableName in dynamicTables)
        {
            var safeTable = tableName.Replace("]", "]]", StringComparison.Ordinal);
            await conn.ExecuteAsync(
                $"""
                IF OBJECT_ID(N'dbo.[{safeTable}]', N'U') IS NOT NULL
                    DELETE FROM dbo.[{safeTable}] WHERE TenantId IN @tenantIds;
                """,
                new { tenantIds });
        }

        await conn.ExecuteAsync(
            """
            DELETE FROM meta.submission_field_sources WHERE TenantId IN @tenantIds;
            DELETE FROM dbo.return_submissions WHERE TenantId IN @tenantIds;
            DELETE FROM dbo.return_periods WHERE TenantId IN @tenantIds;
            DELETE FROM dbo.institutions WHERE TenantId IN @tenantIds;
            DELETE FROM dbo.subscription_modules WHERE SubscriptionId IN (SELECT Id FROM dbo.subscriptions WHERE TenantId IN @tenantIds);
            DELETE FROM dbo.subscriptions WHERE TenantId IN @tenantIds;
            DELETE FROM dbo.tenant_licence_types WHERE TenantId IN @tenantIds;
            DELETE FROM dbo.tenants WHERE TenantId IN @tenantIds;
            """,
            new { tenantIds });
    }

    private async Task EnsureSchemaCompatibilityAsync()
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await conn.ExecuteAsync(
            """
            IF OBJECT_ID('dbo.module_versions', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.module_versions (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    ModuleId INT NOT NULL,
                    VersionCode NVARCHAR(20) NOT NULL,
                    Status NVARCHAR(20) NOT NULL DEFAULT 'Draft',
                    PublishedAt DATETIME2 NULL,
                    DeprecatedAt DATETIME2 NULL,
                    ReleaseNotes NVARCHAR(MAX) NULL,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID('dbo.inter_module_data_flows', 'U') IS NULL
            BEGIN
                CREATE TABLE dbo.inter_module_data_flows (
                    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    SourceModuleId INT NOT NULL,
                    SourceTemplateCode NVARCHAR(50) NOT NULL,
                    SourceFieldCode NVARCHAR(50) NOT NULL,
                    TargetModuleCode NVARCHAR(30) NOT NULL,
                    TargetTemplateCode NVARCHAR(50) NOT NULL,
                    TargetFieldCode NVARCHAR(50) NOT NULL,
                    TransformationType NVARCHAR(20) NOT NULL DEFAULT 'DirectCopy',
                    TransformFormula NVARCHAR(500) NULL,
                    Description NVARCHAR(500) NULL,
                    IsActive BIT NOT NULL DEFAULT 1,
                    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF COL_LENGTH('meta.return_templates', 'ModuleId') IS NULL
                ALTER TABLE meta.return_templates ADD ModuleId INT NULL;

            IF COL_LENGTH('meta.cross_sheet_rules', 'ModuleId') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD ModuleId INT NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'SourceModuleId') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD SourceModuleId INT NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'TargetModuleId') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD TargetModuleId INT NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'SourceTemplateCode') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD SourceTemplateCode NVARCHAR(50) NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'SourceFieldCode') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD SourceFieldCode NVARCHAR(50) NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'TargetTemplateCode') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD TargetTemplateCode NVARCHAR(50) NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'TargetFieldCode') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD TargetFieldCode NVARCHAR(50) NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'Operator') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD Operator NVARCHAR(30) NULL;
            IF COL_LENGTH('meta.cross_sheet_rules', 'ToleranceAmount') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD ToleranceAmount DECIMAL(20,2) NOT NULL DEFAULT 0;
            IF COL_LENGTH('meta.cross_sheet_rules', 'TolerancePercent') IS NULL
                ALTER TABLE meta.cross_sheet_rules ADD TolerancePercent DECIMAL(10,4) NULL;

            IF OBJECT_ID('meta.submission_field_sources', 'U') IS NULL
            BEGIN
                CREATE TABLE meta.submission_field_sources (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId UNIQUEIDENTIFIER NOT NULL,
                    ReturnCode NVARCHAR(20) NOT NULL,
                    SubmissionId INT NOT NULL,
                    FieldName NVARCHAR(128) NOT NULL,
                    DataSource NVARCHAR(40) NOT NULL,
                    SourceDetail NVARCHAR(500) NULL,
                    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT UQ_submission_field_sources UNIQUE (TenantId, ReturnCode, SubmissionId, FieldName)
                );
            END;

            IF OBJECT_ID('meta.submission_field_sources', 'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1
                   FROM sys.columns c
                   INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
                   WHERE c.object_id = OBJECT_ID('meta.submission_field_sources')
                     AND c.name = 'Id'
                     AND t.name <> 'bigint')
            BEGIN
                DROP TABLE meta.submission_field_sources;

                CREATE TABLE meta.submission_field_sources (
                    Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    TenantId UNIQUEIDENTIFIER NOT NULL,
                    ReturnCode NVARCHAR(20) NOT NULL,
                    SubmissionId INT NOT NULL,
                    FieldName NVARCHAR(128) NOT NULL,
                    DataSource NVARCHAR(40) NOT NULL,
                    SourceDetail NVARCHAR(500) NULL,
                    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT UQ_submission_field_sources UNIQUE (TenantId, ReturnCode, SubmissionId, FieldName)
                );
            END;
            """);
    }

    private MetadataDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        return new MetadataDbContext(options);
    }

    private ServiceProvider BuildCacheServiceProvider(ITenantContext tenantContext)
    {
        var services = new ServiceCollection();
        services.AddSingleton(tenantContext);
        services.AddDbContext<MetadataDbContext>(options => options.UseSqlServer(_connectionString));
        return services.BuildServiceProvider();
    }

    private static async Task<ReturnDataRecord> BuildDefaultRecord(
        ITemplateMetadataCache cache,
        string returnCode,
        IReadOnlyDictionary<string, object?> overrides,
        CancellationToken ct = default)
    {
        var template = await cache.GetPublishedTemplate(returnCode, ct);
        var category = Enum.TryParse<StructuralCategory>(template.StructuralCategory, true, out var parsed)
            ? parsed
            : StructuralCategory.FixedRow;

        category.Should().Be(StructuralCategory.FixedRow, "RG-08 lifecycle test seeds fixed-row templates only");

        var record = new ReturnDataRecord(returnCode, template.CurrentVersion.Id, category);
        var row = new ReturnDataRow();

        foreach (var field in template.CurrentVersion.Fields.OrderBy(f => f.FieldOrder))
        {
            if (overrides.TryGetValue(field.FieldName, out var overrideValue))
            {
                row.SetValue(field.FieldName, overrideValue);
                continue;
            }

            row.SetValue(field.FieldName, DefaultValueFor(field.DataType));
        }

        record.AddRow(row);
        return record;
    }

    private static object DefaultValueFor(FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Text => "N/A",
            FieldDataType.Date => DateTime.UtcNow.Date,
            FieldDataType.Boolean => false,
            FieldDataType.Integer => 0,
            _ => 0m
        };
    }

    private static async Task<string> LoadDefinition(string fileName, bool applySqlIsolation)
    {
        var root = FindSolutionRoot();
        var path = Path.Combine(root, "src", "FC.Engine.Migrator", "SeedData", "ModuleDefinitions", fileName);
        File.Exists(path).Should().BeTrue($"Expected RG-08 definition file at {path}");
        var raw = await File.ReadAllTextAsync(path);
        if (!applySqlIsolation)
        {
            return raw;
        }

        var definition = JsonSerializer.Deserialize<ModuleDefinition>(raw, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        definition.Should().NotBeNull();
        ApplySqlIsolation(definition!);
        return JsonSerializer.Serialize(definition);
    }

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "FCEngine.sln");
            if (File.Exists(candidate))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate FCEngine.sln from integration test base directory.");
    }

    private static string NormalizePredicateDefinition(string? definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            return "dbo.fn_TenantFilter(TenantId)";
        }

        var normalized = definition.Trim();
        while (normalized.Length > 1 && normalized.StartsWith('(') && normalized.EndsWith(')'))
        {
            normalized = normalized[1..^1].Trim();
        }

        return normalized;
    }

    private static void ApplySqlIsolation(ModuleDefinition definition)
    {
        var templateCodeMap = definition.Templates
            .Select(t => t.ReturnCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(code => code, ToSqlTestReturnCode, StringComparer.OrdinalIgnoreCase);

        definition.ModuleCode = ToSqlTestModuleCode(definition.ModuleCode);

        foreach (var template in definition.Templates)
        {
            if (templateCodeMap.TryGetValue(template.ReturnCode, out var mappedTemplateCode))
            {
                template.ReturnCode = mappedTemplateCode;
            }

            foreach (var rule in template.CrossSheetRules)
            {
                if (templateCodeMap.TryGetValue(rule.SourceTemplate, out var mappedSource))
                {
                    rule.SourceTemplate = mappedSource;
                }

                if (templateCodeMap.TryGetValue(rule.TargetTemplate, out var mappedTarget))
                {
                    rule.TargetTemplate = mappedTarget;
                }
            }
        }

        foreach (var flow in definition.InterModuleDataFlows)
        {
            flow.SourceTemplate = ToSqlTestReturnCode(flow.SourceTemplate);
            flow.TargetTemplate = ToSqlTestReturnCode(flow.TargetTemplate);
            flow.TargetModule = ToSqlTestModuleCode(flow.TargetModule);
        }
    }

    private static string ToSqlTestModuleCode(string moduleCode)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            return moduleCode;
        }

        if (!SqlIsolatedModules.Contains(moduleCode))
        {
            return moduleCode;
        }

        return moduleCode.EndsWith("_Z", StringComparison.OrdinalIgnoreCase)
            ? moduleCode
            : $"{moduleCode}_Z";
    }

    private static string ToSqlTestReturnCode(string returnCode)
    {
        if (string.IsNullOrWhiteSpace(returnCode))
        {
            return returnCode;
        }

        if (returnCode.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
        {
            return returnCode;
        }

        return returnCode.Length >= 20
            ? $"{returnCode[..19]}Z"
            : $"{returnCode}Z";
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public MutableTenantContext(Guid tenantId)
        {
            CurrentTenantId = tenantId;
        }

        public Guid? CurrentTenantId { get; set; }
        public bool IsPlatformAdmin => false;
        public Guid? ImpersonatingTenantId => null;
    }

    private sealed class AllowAllEntitlementService : IEntitlementService
    {
        public Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default)
        {
            return Task.FromResult(new TenantEntitlement
            {
                TenantId = tenantId,
                TenantStatus = TenantStatus.Active,
                ResolvedAt = DateTime.UtcNow
            });
        }

        public Task<bool> HasModuleAccess(Guid tenantId, string moduleCode, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task InvalidateCache(Guid tenantId) => Task.CompletedTask;
    }

    private sealed class StaticDataResidencyRouter : IDataResidencyRouter
    {
        private readonly string _connectionString;

        public StaticDataResidencyRouter(string connectionString)
        {
            _connectionString = connectionString;
        }

        public Task<string> ResolveConnectionString(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult(_connectionString);

        public Task<string> ResolveRegion(Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult("SQL-Test");
    }

    private sealed class NoopTemplateMetadataCache : ITemplateMetadataCache
    {
        public Task<CachedTemplate> GetPublishedTemplate(string returnCode, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CachedTemplate> GetPublishedTemplate(Guid tenantId, string returnCode, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CachedTemplate>> GetAllPublishedTemplates(Guid tenantId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public void Invalidate(string returnCode)
        {
        }

        public void Invalidate(Guid? tenantId, string returnCode)
        {
        }

        public void InvalidateModule(int moduleId)
        {
        }

        public void InvalidateModule(string moduleCode)
        {
        }

        public void InvalidateAll()
        {
        }
    }

    private sealed class SecurityPredicateRow
    {
        public string PolicySchema { get; set; } = "dbo";
        public string PolicyName { get; set; } = string.Empty;
        public int Operation { get; set; }
        public string PredicateDefinition { get; set; } = string.Empty;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<MetadataDbContext>
    {
        private readonly MetadataDbContext _db;

        public TestDbContextFactory(MetadataDbContext db) => _db = db;

        public MetadataDbContext CreateDbContext() => _db;

        public Task<MetadataDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_db);
    }
}
