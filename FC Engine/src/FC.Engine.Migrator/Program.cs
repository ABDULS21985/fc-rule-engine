using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Export;
using FC.Engine.Migrator;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Query", LogLevel.Warning);
var configurationBasePath = ResolveConfigurationBasePath();
builder.Configuration
    .AddJsonFile(Path.Combine(configurationBasePath, "appsettings.json"), optional: true, reloadOnChange: false)
    .AddJsonFile(
        Path.Combine(configurationBasePath, $"appsettings.{builder.Environment.EnvironmentName}.json"),
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services.AddSingleton<IWebHostEnvironment>(_ =>
{
    var webRootPath = Path.Combine(configurationBasePath, "wwwroot");
    Directory.CreateDirectory(webRootPath);

    return new MigratorWebHostEnvironment
    {
        ApplicationName = typeof(Program).Assembly.GetName().Name ?? "FC.Engine.Migrator",
        ContentRootPath = configurationBasePath,
        ContentRootFileProvider = new PhysicalFileProvider(configurationBasePath),
        EnvironmentName = builder.Environment.EnvironmentName,
        WebRootPath = webRootPath,
        WebRootFileProvider = new PhysicalFileProvider(webRootPath)
    };
});

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<FormulaSeedService>();
builder.Services.AddScoped<FormulaCatalogSeeder>();
builder.Services.AddScoped<CrossSheetRuleSeedService>();
builder.Services.AddScoped<BusinessRuleSeedService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<InstitutionAuthService>();
builder.Services.AddScoped<DmbDemoWorkspaceService>();
builder.Services.AddScoped<DemoCredentialSeedService>();
builder.Services.AddScoped<EndToEndDemoSeedService>();
builder.Services.AddScoped<BulkInstitutionDemoSeedService>();
builder.Services.AddScoped<CrossBorderDemoSeedService>();
builder.Services.AddScoped<PortalTenantDemoSeedService>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var command = args.FirstOrDefault()?.Trim();

try
{
    logger.LogInformation("RegOS™ Migrator starting...");

    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

    if (string.Equals(command, "backfill-anomalies", StringComparison.OrdinalIgnoreCase))
    {
        var (backfilled, skipped) = await BackfillMissingAnomalyReportsAsync(scope.ServiceProvider, logger);
        logger.LogInformation(
            "Anomaly backfill completed: {Backfilled} reports created, {Skipped} submissions skipped",
            backfilled,
            skipped);
        return;
    }

    // Step 1: Apply EF Core migrations (metadata + operational tables)
    logger.LogInformation("Applying EF Core migrations...");
    await db.Database.MigrateAsync();
    logger.LogInformation("EF Core migrations applied successfully");

    if (string.Equals(command, "repair-cross-sheet-rules", StringComparison.OrdinalIgnoreCase))
    {
        await RunCrossSheetRuleRepairAsync(host.Services, logger);
        logger.LogInformation("RegOS™ Migrator completed successfully");
        return;
    }

    // Step 2: Execute metadata schema SQL (meta.* tables with additional indexes/constraints)
    var schemaScriptPath = builder.Configuration["Seeding:MetadataSchemaPath"];
    if (!string.IsNullOrEmpty(schemaScriptPath) && File.Exists(schemaScriptPath))
    {
        logger.LogInformation("Executing metadata schema script: {Path}", schemaScriptPath);
        var schemaSql = await File.ReadAllTextAsync(schemaScriptPath);
        // Split by GO batch separators (not supported by ExecuteSqlRawAsync)
        var batches = System.Text.RegularExpressions.Regex
            .Split(schemaSql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(b => !string.IsNullOrWhiteSpace(b));
        foreach (var batch in batches)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(batch);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2714 or 1913 or 1779 or 2601 or 2627)
            {
                // 2714=Object already exists, 1913=Index already exists, 1779=Table already has PK
                // 2601/2627=Duplicate key (for INSERT of seed data)
                logger.LogWarning("Schema object already exists, skipping: {Message}", ex.Message);
            }
        }
        logger.LogInformation("Metadata schema script executed successfully");
    }

    // Step 3: Seed reference data (institutions, return periods)
    var referenceDataPath = builder.Configuration["Seeding:ReferenceDataPath"];
    if (!string.IsNullOrEmpty(referenceDataPath) && File.Exists(referenceDataPath))
    {
        logger.LogInformation("Seeding reference data from: {Path}", referenceDataPath);
        var refSql = await File.ReadAllTextAsync(referenceDataPath);
        var refBatches = System.Text.RegularExpressions.Regex
            .Split(refSql, @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Where(b => !string.IsNullOrWhiteSpace(b));
        foreach (var batch in refBatches)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(batch);
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2601 or 2627)
            {
                logger.LogWarning("Reference data already exists, skipping: {Message}", ex.Message);
            }
        }
        logger.LogInformation("Reference data seeded successfully");
    }

    // Step 4: Seed templates from schema.sql (if configured)
    var seedSchemaPath = builder.Configuration["Seeding:SchemaFilePath"];
    var autoSeed = builder.Configuration.GetValue<bool>("Seeding:AutoSeed");

    if (autoSeed && !string.IsNullOrEmpty(seedSchemaPath) && File.Exists(seedSchemaPath))
    {
        logger.LogInformation("Seeding templates from: {Path}", seedSchemaPath);
        var seedService = scope.ServiceProvider.GetRequiredService<SeedService>();
        var result = await seedService.SeedFromSchema(seedSchemaPath, "migrator");

        logger.LogInformation(
            "Seeding complete: {Created} created, {Skipped} skipped, {Errors} errors",
            result.Created.Count, result.Skipped.Count, result.Errors.Count);

        foreach (var err in result.Errors)
            logger.LogWarning("Seed error: {Error}", err);

        // Step 4: Seed intra-sheet formulas from schema column patterns
        logger.LogInformation("Seeding intra-sheet formulas...");
        var formulaSeedService = scope.ServiceProvider.GetRequiredService<FormulaSeedService>();
        var formulaResult = await formulaSeedService.SeedFormulasFromSchema(seedSchemaPath, "migrator");

        logger.LogInformation(
            "Formula seeding complete: {Templates} templates, {Formulas} formulas, {Errors} errors",
            formulaResult.TemplatesWithFormulas.Count, formulaResult.TotalFormulasCreated,
            formulaResult.Errors.Count);

        foreach (var err in formulaResult.Errors)
            logger.LogWarning("Formula seed error: {Error}", err);

        // Step 4b: Seed formulas from Excel-extracted catalog (CBN-defined formulas using item codes)
        var formulaCatalogPath = builder.Configuration["Seeding:FormulaCatalogPath"];
        if (!string.IsNullOrEmpty(formulaCatalogPath) && File.Exists(formulaCatalogPath))
        {
            logger.LogInformation("Seeding formulas from catalog: {Path}", formulaCatalogPath);
            var catalogSeeder = scope.ServiceProvider.GetRequiredService<FormulaCatalogSeeder>();
            var catalogResult = await catalogSeeder.SeedFromCatalog(formulaCatalogPath, "migrator");

            logger.LogInformation(
                "Catalog seeding complete: {Templates} templates, {Formulas} formulas, " +
                "{CrossSheet} cross-sheet rules, {Skipped} skipped, {Warnings} warnings, {Errors} errors",
                catalogResult.TemplatesSeeded.Count, catalogResult.TotalFormulasCreated,
                catalogResult.TotalCrossSheetRulesCreated, catalogResult.Skipped.Count,
                catalogResult.Warnings.Count, catalogResult.Errors.Count);

            foreach (var warn in catalogResult.Warnings)
                logger.LogWarning("Catalog warning: {Warning}", warn);
            foreach (var err in catalogResult.Errors)
                logger.LogWarning("Catalog error: {Error}", err);
        }

        // Step 5: Seed cross-sheet validation rules
        await RunCrossSheetRuleRepairAsync(host.Services, logger);
    }

    // Step 6: Create physical data tables for all published templates
    logger.LogInformation("Creating physical data tables for published templates...");
    var templateRepo = scope.ServiceProvider.GetRequiredService<ITemplateRepository>();
    var ddlEngine = scope.ServiceProvider.GetRequiredService<IDdlEngine>();
    var allTemplates = await templateRepo.GetAll();
    var tablesCreated = 0;
    var tablesSkipped = 0;
    foreach (var tmpl in allTemplates)
    {
        var publishedVersion = tmpl.Versions
            .FirstOrDefault(v => v.Status == FC.Engine.Domain.Enums.TemplateStatus.Published);
        if (publishedVersion == null) continue;

        if (await PhysicalTableExistsAsync(db, tmpl.PhysicalTableName))
        {
            tablesSkipped++;
            continue;
        }

        try
        {
            var ddl = ddlEngine.GenerateCreateTable(tmpl, publishedVersion);
            await db.Database.ExecuteSqlRawAsync(ddl.ForwardSql);
            tablesCreated++;
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 2714)
        {
            // Table already exists
            tablesSkipped++;
        }
    }
    logger.LogInformation(
        "Physical tables: {Created} created, {Skipped} already existed",
        tablesCreated, tablesSkipped);

    // Step 6b: Validate XML export/XSD coverage across published module templates
    logger.LogInformation("Validating XML export coverage across published modules...");
    var xmlCoverageValidator = scope.ServiceProvider.GetRequiredService<XmlExportCoverageValidator>();
    var coverage = await xmlCoverageValidator.Validate();
    logger.LogInformation(
        "XML export coverage: {Modules} modules, {Templates} templates, {Failed} failed templates",
        coverage.ModuleCount,
        coverage.TemplateCount,
        coverage.FailedTemplateCount);

    foreach (var module in coverage.Modules.Where(m => m.Templates.Any(t => !t.Success)))
    {
        foreach (var failed in module.Templates.Where(t => !t.Success))
        {
            logger.LogError(
                "XML coverage failure: Module={ModuleCode}, ReturnCode={ReturnCode}, Error={Error}",
                module.ModuleCode,
                failed.ReturnCode,
                failed.Error);
        }
    }

    var expectedModuleCount = builder.Configuration.GetValue<int?>("Seeding:ExpectedXmlCoverageModuleCount");
    if (expectedModuleCount.HasValue && coverage.ModuleCount < expectedModuleCount.Value)
    {
        logger.LogWarning(
            "XML export coverage check: expected at least {ExpectedModuleCount} modules, found {ModuleCount}. Continuing.",
            expectedModuleCount.Value,
            coverage.ModuleCount);
    }

    if (!coverage.Success)
    {
        logger.LogWarning(
            "XML export coverage check: {FailedCount} template(s) failed schema validation. Continuing.",
            coverage.FailedTemplateCount);
    }

    // Step 7: Seed default admin user (if no users exist)
    var userRepo = scope.ServiceProvider.GetRequiredService<IPortalUserRepository>();
    var existingUsers = await userRepo.GetAll();
    if (existingUsers.Count == 0)
    {
        logger.LogInformation("Seeding default admin user...");
        var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
        var defaultPassword = builder.Configuration["DefaultAdmin:Password"] ?? "Admin@123";
        await authService.CreateUser("admin", "System Administrator", "admin@fcengine.local",
            defaultPassword, PortalRole.Admin);
        logger.LogInformation("Default admin user created (username: admin)");
    }

    // Step 7b: Seed default institution users for FC001 (if none exist)
    var fc001 = await db.Institutions.FirstOrDefaultAsync(i => i.InstitutionCode == "FC001");
    if (fc001 != null)
    {
        var instUserRepo = scope.ServiceProvider.GetRequiredService<IInstitutionUserRepository>();
        var existingInstUsers = await instUserRepo.GetByInstitution(fc001.Id);
        if (existingInstUsers.Count == 0)
        {
            logger.LogInformation("Seeding default institution users for FC001...");
            var instAuthService = scope.ServiceProvider.GetRequiredService<InstitutionAuthService>();
            var instPassword = builder.Configuration["DefaultAdmin:Password"] ?? "Admin@123";

            await instAuthService.CreateUser(fc001.Id, "admin", "admin@fc001.com", "Admin User", instPassword, InstitutionRole.Admin);
            await instAuthService.CreateUser(fc001.Id, "maker1", "maker1@fc001.com", "John Maker", instPassword, InstitutionRole.Maker);
            await instAuthService.CreateUser(fc001.Id, "checker1", "checker1@fc001.com", "Jane Checker", instPassword, InstitutionRole.Checker);
            await instAuthService.CreateUser(fc001.Id, "viewer1", "viewer1@fc001.com", "Bob Viewer", instPassword, InstitutionRole.Viewer);

            logger.LogInformation("Institution users seeded: admin, maker1, checker1, viewer1");
        }
        else
        {
            // Fix password hashes for existing users (in case they were seeded incorrectly)
            var instPassword = builder.Configuration["DefaultAdmin:Password"] ?? "Admin@123";
            var fixedCount = 0;
            foreach (var instUser in existingInstUsers)
            {
                instUser.PasswordHash = InstitutionAuthService.HashPassword(instPassword);
                instUser.MustChangePassword = false;
                instUser.FailedLoginAttempts = 0;
                instUser.LockedUntil = null;
                await instUserRepo.Update(instUser);
                fixedCount++;
            }
            if (fixedCount > 0)
                logger.LogInformation("Reset passwords for {Count} existing institution users", fixedCount);
        }
    }

    // Step 8: Seed default business rules
    logger.LogInformation("Seeding default business rules...");
    var businessRuleSeeder = scope.ServiceProvider.GetRequiredService<BusinessRuleSeedService>();
    var rulesCreated = await businessRuleSeeder.SeedDefaultRules();
    logger.LogInformation("Business rules seeded: {Created} created", rulesCreated);

    if (string.Equals(command, "seed-demo-credentials", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "prepare-dmb-demo", StringComparison.OrdinalIgnoreCase))
    {
        var demoCredentialService = scope.ServiceProvider.GetRequiredService<DemoCredentialSeedService>();
        var demoPassword = builder.Configuration["Demo:SharedPassword"]
            ?? builder.Configuration["DefaultAdmin:Password"]
            ?? "Admin@FcEngine2026!";

        var demoCredentialResult = await demoCredentialService.SeedAsync(demoPassword);
        var demoCredentialPath = ResolveDemoCredentialPackPath(builder.Configuration, configurationBasePath);
        await WriteDemoCredentialPackAsync(demoCredentialResult, demoCredentialPath);

        logger.LogInformation(
            "Demo credentials seeded and written to {Path}: {PlatformCount} platform accounts, {RegulatorCount} regulator accounts, {InstitutionCount} institution accounts",
            demoCredentialPath,
            demoCredentialResult.PlatformAccounts.Count,
            demoCredentialResult.RegulatorGroups.Sum(x => x.Accounts.Count),
            demoCredentialResult.InstitutionGroups.Sum(x => x.Accounts.Count));

        if (string.Equals(command, "seed-demo-credentials", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
    }

    if (string.Equals(command, "prepare-dmb-demo", StringComparison.OrdinalIgnoreCase))
    {
        var dmbDemoService = scope.ServiceProvider.GetRequiredService<DmbDemoWorkspaceService>();
        var templatesDirectory = ResolveDmbTemplatesDirectory(builder.Configuration, configurationBasePath);
        var demoPassword = builder.Configuration["Demo:DmbPassword"]
            ?? builder.Configuration["DefaultAdmin:Password"]
            ?? "Admin@FcEngine2026!";

        var result = await dmbDemoService.PrepareAsync(templatesDirectory, demoPassword);
        logger.LogInformation(
            "DMB demo prepared: {FilesWritten} sample files written, {FilesDeleted} removed, {HistoricalPeriodsCreated} periods created, {HistoricalSubmissionsCreated} submissions created, verification sample {VerificationSamplePath}",
            result.SampleFilesWritten,
            result.SampleFilesDeleted,
            result.HistoricalPeriodsCreated,
            result.HistoricalSubmissionsCreated,
            result.VerificationSamplePath);
    }

    if (string.Equals(command, "seed-end-to-end-demo", StringComparison.OrdinalIgnoreCase))
    {
        var endToEndDemoSeeder = scope.ServiceProvider.GetRequiredService<EndToEndDemoSeedService>();
        var sharedPassword = builder.Configuration["Demo:SharedPassword"]
            ?? builder.Configuration["DefaultAdmin:Password"]
            ?? "Admin@FcEngine2026!";
        var templatesDirectory = ResolveDmbTemplatesDirectory(builder.Configuration, configurationBasePath);

        var result = await endToEndDemoSeeder.SeedAsync(sharedPassword, templatesDirectory);
        var demoCredentialPath = ResolveDemoCredentialPackPath(builder.Configuration, configurationBasePath);
        await WriteDemoCredentialPackAsync(result.Credentials, demoCredentialPath);

        logger.LogInformation(
            "End-to-end demo seeded for {RegulatorCode} ({PeriodCode}): {PrudentialRows} prudential rows, {ExposureRows} interbank exposures, {StressRuns} stress runs, {PolicyScenarios} policy scenarios, {WhistleblowerCases} whistleblower cases, {ExamProjects} examination project(s). Credentials written to {Path}",
            result.RegulatorCode,
            result.CurrentPeriodCode,
            result.PrudentialRowsUpserted,
            result.InterbankExposureRowsUpserted,
            result.StressRunsCreated,
            result.PolicyScenariosSeeded,
            result.WhistleblowerCasesSeeded,
            result.ExaminationProjectsSeeded,
            demoCredentialPath);
        return;
    }

    if (string.Equals(command, "seed-bulk-bdc-dmb-demo", StringComparison.OrdinalIgnoreCase))
    {
        var bulkDemoSeeder = scope.ServiceProvider.GetRequiredService<BulkInstitutionDemoSeedService>();
        var sharedPassword = builder.Configuration["Demo:SharedPassword"]
            ?? builder.Configuration["DefaultAdmin:Password"]
            ?? "Admin@FcEngine2026!";
        var templatesDirectory = ResolveDmbTemplatesDirectory(builder.Configuration, configurationBasePath);

        var result = await bulkDemoSeeder.SeedAsync(templatesDirectory, sharedPassword);
        var demoCredentialPath = ResolveDemoCredentialPackPath(builder.Configuration, configurationBasePath);
        await WriteDemoCredentialPackAsync(result.Credentials, demoCredentialPath);

        logger.LogInformation(
            "Bulk demo seeded: {BdcCount} BDC institutions, {DmbCount} DMB institutions, {InstitutionsCreated} new institutions, {PeriodsCreated} return periods, {SubmissionsCreated} submissions. Credentials written to {Path}",
            result.BdcInstitutionsProcessed,
            result.DmbInstitutionsProcessed,
            result.InstitutionsCreated,
            result.PeriodsCreated,
            result.SubmissionsCreated,
            demoCredentialPath);
        return;
    }

    if (string.Equals(command, "seed-bulk-fc-demo", StringComparison.OrdinalIgnoreCase))
    {
        var bulkDemoSeeder = scope.ServiceProvider.GetRequiredService<BulkInstitutionDemoSeedService>();
        var sharedPassword = builder.Configuration["Demo:SharedPassword"]
            ?? builder.Configuration["DefaultAdmin:Password"]
            ?? "Admin@FcEngine2026!";
        var templatesDirectory = ResolveDmbTemplatesDirectory(builder.Configuration, configurationBasePath);
        var fcCount = builder.Configuration.GetValue<int?>("Demo:FcCount") ?? 120;

        var result = await bulkDemoSeeder.SeedAsync(
            templatesDirectory,
            sharedPassword,
            bdcCount: 0,
            dmbCount: 0,
            fcCount: fcCount);
        var demoCredentialPath = ResolveDemoCredentialPackPath(builder.Configuration, configurationBasePath);
        await WriteDemoCredentialPackAsync(result.Credentials, demoCredentialPath);

        logger.LogInformation(
            "Bulk FC demo seeded: {FcCount} Finance Company institutions, {InstitutionsCreated} new institutions, {PeriodsCreated} return periods, {SubmissionsCreated} submissions. Credentials written to {Path}",
            result.FcInstitutionsProcessed,
            result.InstitutionsCreated,
            result.PeriodsCreated,
            result.SubmissionsCreated,
            demoCredentialPath);
        return;
    }

    if (string.Equals(command, "seed-platform-demo-expansion", StringComparison.OrdinalIgnoreCase))
    {
        var bulkDemoSeeder = scope.ServiceProvider.GetRequiredService<BulkInstitutionDemoSeedService>();
        var sharedPassword = builder.Configuration["Demo:SharedPassword"]
            ?? builder.Configuration["DefaultAdmin:Password"]
            ?? "Admin@FcEngine2026!";
        var templatesDirectory = ResolveDmbTemplatesDirectory(builder.Configuration, configurationBasePath);
        var mfbCount = builder.Configuration.GetValue<int?>("Demo:MfbCount") ?? 24;
        var overlayInstitutionCountPerType = builder.Configuration.GetValue<int?>("Demo:OverlayInstitutionCountPerType") ?? 5;

        var result = await bulkDemoSeeder.SeedAsync(
            templatesDirectory,
            sharedPassword,
            bdcCount: 0,
            dmbCount: 0,
            fcCount: 0,
            mfbCount: mfbCount,
            overlayInstitutionCountPerType: overlayInstitutionCountPerType);
        var demoCredentialPath = ResolveDemoCredentialPackPath(builder.Configuration, configurationBasePath);
        await WriteDemoCredentialPackAsync(result.Credentials, demoCredentialPath);

        logger.LogInformation(
            "Platform demo expansion seeded: {MfbCount} MFB institutions, {OverlayInstitutionCount} overlay institutions, {InstitutionsCreated} new institutions, {PeriodsCreated} return periods, {SubmissionsCreated} submissions. Credentials written to {Path}",
            result.MfbInstitutionsProcessed,
            result.OverlayInstitutionsProcessed,
            result.InstitutionsCreated,
            result.PeriodsCreated,
            result.SubmissionsCreated,
            demoCredentialPath);
        return;
    }

    if (string.Equals(command, "seed-cross-border-demo", StringComparison.OrdinalIgnoreCase))
    {
        var crossBorderSeeder = scope.ServiceProvider.GetRequiredService<CrossBorderDemoSeedService>();
        var result = await crossBorderSeeder.SeedAsync();

        logger.LogInformation(
            "Cross-border demo seeded: {Groups} groups, {Subsidiaries} subsidiaries, {Runs} runs, {Snapshots} snapshots, {Deadlines} deadlines, {Divergences} divergences, {Notifications} notifications, {Flows} flows, {Executions} executions.",
            result.GroupsSeeded,
            result.SubsidiariesSeeded,
            result.ConsolidationRunsSeeded,
            result.ConsolidationSnapshotsSeeded,
            result.DeadlinesSeeded,
            result.DivergencesSeeded,
            result.NotificationsSeeded,
            result.FlowsSeeded,
            result.ExecutionsSeeded);
        return;
    }

    if (string.Equals(command, "seed-portal-demo-tenant", StringComparison.OrdinalIgnoreCase))
    {
        var portalTenantSeeder = scope.ServiceProvider.GetRequiredService<PortalTenantDemoSeedService>();
        var result = await portalTenantSeeder.SeedComplianceIqAsync();

        logger.LogInformation(
            "Portal demo tenant seeded: {PeriodsCreated} periods, {SubmissionsCreated} submissions created, {SubmissionsUpdated} submissions updated, {ValidationReportsSeeded} validation reports, {SlaRecordsUpserted} SLA rows, {ChsSnapshotsUpserted} CHS snapshots, {PeerStatsUpserted} peer stats, {InstitutionUsersUpdated} institution users normalized.",
            result.PeriodsCreated,
            result.SubmissionsCreated,
            result.SubmissionsUpdated,
            result.ValidationReportsSeeded,
            result.SlaRecordsUpserted,
            result.ChsSnapshotsUpserted,
            result.PeerStatsUpserted,
            result.InstitutionUsersUpdated);
        return;
    }

    logger.LogInformation("RegOS™ Migrator completed successfully");
}
catch (Exception ex)
{
    logger.LogError(ex, "RegOS™ Migrator failed");
    Environment.ExitCode = 1;
}

static async Task<bool> PhysicalTableExistsAsync(MetadataDbContext db, string tableName, CancellationToken ct = default)
{
    return await db.Database
        .SqlQueryRaw<int>(
            """
            SELECT TOP 1 1 AS [Value]
            FROM sys.tables t
            INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = 'dbo' AND t.name = {0}
            """,
            tableName)
        .AnyAsync(ct);
}

static async Task RunCrossSheetRuleRepairAsync(
    IServiceProvider services,
    ILogger logger,
    CancellationToken ct = default)
{
    logger.LogInformation("Seeding cross-sheet rules...");

    using var scope = services.CreateScope();
    var crossSheetSeedService = scope.ServiceProvider.GetRequiredService<CrossSheetRuleSeedService>();
    var crossSheetCount = await crossSheetSeedService.SeedCrossSheetRules("migrator", ct);

    logger.LogInformation("Cross-sheet rules seeded: {Count}", crossSheetCount);
}

static string ResolveDmbTemplatesDirectory(IConfiguration configuration, string configurationBasePath)
{
    var configured = configuration["Demo:DmbTemplatesDirectory"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured);
    }

    var cwdTemplates = Path.Combine(Directory.GetCurrentDirectory(), "Templates");
    if (Directory.Exists(cwdTemplates))
    {
        return cwdTemplates;
    }

    var repoTemplates = Path.GetFullPath(Path.Combine(configurationBasePath, "..", "..", "..", "Templates"));
    if (Directory.Exists(repoTemplates))
    {
        return repoTemplates;
    }

    return cwdTemplates;
}

static string ResolveDemoCredentialPackPath(IConfiguration configuration, string configurationBasePath)
{
    var configured = configuration["Demo:CredentialPackPath"];
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Path.GetFullPath(configured);
    }

    return Path.GetFullPath(Path.Combine(configurationBasePath, "..", "..", "..", "DEMO_CREDENTIALS.md"));
}

static async Task WriteDemoCredentialPackAsync(DemoCredentialSeedResult result, string path, CancellationToken ct = default)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    var builder = new StringBuilder();
    builder.AppendLine("# Demo Credentials");
    builder.AppendLine();
    builder.AppendLine($"Generated: {result.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
    builder.AppendLine();
    builder.AppendLine("## Shared Password");
    builder.AppendLine();
    builder.AppendLine($"All seeded demo accounts use `{result.SharedPassword}`.");
    builder.AppendLine();
    builder.AppendLine("## URLs");
    builder.AppendLine();
    builder.AppendLine("- Admin portal: `http://localhost:5200/login`");
    builder.AppendLine("- Institution portal: `http://localhost:5300/login`");
    builder.AppendLine();
    builder.AppendLine("## Platform Accounts");
    builder.AppendLine();
    builder.AppendLine("| Username | Role | Email | Password | MFA |");
    builder.AppendLine("| --- | --- | --- | --- | --- |");
    foreach (var account in result.PlatformAccounts.OrderBy(x => x.Role).ThenBy(x => x.Username))
    {
        builder.AppendLine($"| `{account.Username}` | `{account.Role}` | `{account.Email}` | `{account.Password}` | {(account.MfaRequired ? "Required" : "Not required")} |");
    }

    foreach (var account in result.PlatformAccounts.Where(x => x.MfaRequired))
    {
        AppendMfaSection(builder, $"{account.DisplayName} ({account.Username})", account);
    }

    foreach (var group in result.RegulatorGroups.OrderBy(x => x.TenantSlug))
    {
        builder.AppendLine();
        builder.AppendLine($"## {group.TenantName} ({group.TenantSlug.ToUpperInvariant()})");
        builder.AppendLine();
        builder.AppendLine($"- Audience: `{group.Audience}`");
        builder.AppendLine($"- Login URL: `{group.LoginUrl}`");
        builder.AppendLine($"- Notes: {group.Notes}");
        builder.AppendLine();
        builder.AppendLine("| Username | Role | Email | Password | MFA |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var account in group.Accounts.OrderBy(x => x.Role).ThenBy(x => x.Username))
        {
            builder.AppendLine($"| `{account.Username}` | `{account.Role}` | `{account.Email}` | `{account.Password}` | {(account.MfaRequired ? "Required" : "Not required")} |");
        }

        foreach (var account in group.Accounts.Where(x => x.MfaRequired))
        {
            AppendMfaSection(builder, $"{account.DisplayName} ({account.Username})", account);
        }
    }

    foreach (var group in result.InstitutionGroups.OrderBy(x => x.InstitutionCode))
    {
        builder.AppendLine();
        builder.AppendLine($"## {group.InstitutionName} ({group.InstitutionCode})");
        builder.AppendLine();
        builder.AppendLine($"- Licence type: `{group.LicenseType}`");
        builder.AppendLine($"- Login URL: `{group.LoginUrl}`");
        builder.AppendLine($"- Notes: {group.Notes}");
        builder.AppendLine();
        builder.AppendLine("| Username | Role | Email | Password | MFA |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var account in group.Accounts.OrderBy(x => x.Role).ThenBy(x => x.Username))
        {
            builder.AppendLine($"| `{account.Username}` | `{account.Role}` | `{account.Email}` | `{account.Password}` | {(account.MfaRequired ? "Required" : "Not required")} |");
        }

        foreach (var account in group.Accounts.Where(x => x.MfaRequired))
        {
            AppendMfaSection(builder, $"{account.DisplayName} ({account.Username})", account);
        }
    }

    builder.AppendLine();
    builder.AppendLine("## Notes");
    builder.AppendLine();
    builder.AppendLine("- Backup codes are one-time use. If you exhaust them, rerun `seed-demo-credentials` to rotate MFA material.");
    builder.AppendLine("- The institution `Checker` and `Approver` roles require MFA by design, so this pack includes both a TOTP secret and backup codes for those users.");
    builder.AppendLine("- The admin portal still treats tenantless users as platform-level sessions. `Admin`, `Approver`, and `Viewer` accounts are seeded and usable, but platform least-privilege separation is not strict yet.");

    await File.WriteAllTextAsync(path, builder.ToString(), ct);
}

static void AppendMfaSection(StringBuilder builder, string heading, DemoCredentialAccount account)
{
    builder.AppendLine();
    builder.AppendLine($"### MFA: {heading}");
    builder.AppendLine();
    builder.AppendLine($"- TOTP secret: `{account.TotpSecret}`");
    builder.AppendLine($"- Backup codes: {string.Join(", ", account.BackupCodes.Select(x => $"`{x}`"))}");
}

static async Task<(int Backfilled, int Skipped)> BackfillMissingAnomalyReportsAsync(
    IServiceProvider services,
    ILogger logger,
    CancellationToken ct = default)
{
    var db = services.GetRequiredService<MetadataDbContext>();
    var dataRepository = services.GetRequiredService<IGenericDataRepository>();
    var anomalyDetectionService = services.GetRequiredService<IAnomalyDetectionService>();

    var submissionIds = await db.Submissions
        .AsNoTracking()
        .Where(s =>
            (s.Status == SubmissionStatus.Accepted || s.Status == SubmissionStatus.AcceptedWithWarnings)
            && !db.AnomalyReports.Any(r => r.SubmissionId == s.Id))
        .OrderBy(s => s.Id)
        .Select(s => s.Id)
        .ToListAsync(ct);

    var backfilled = 0;
    var skipped = 0;

    foreach (var submissionId in submissionIds)
    {
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct);
        if (submission is null || submission.TenantId == Guid.Empty)
        {
            skipped++;
            continue;
        }

        if (string.IsNullOrWhiteSpace(submission.ParsedDataJson))
        {
            try
            {
                var record = await dataRepository.GetBySubmission(submission.ReturnCode, submission.Id, ct);
                if (record is not null)
                {
                    submission.StoreParsedDataJson(SubmissionPayloadSerializer.Serialize(record));
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to rebuild parsed payload for submission {SubmissionId}", submission.Id);
            }
        }

        try
        {
            await anomalyDetectionService.AnalyzeSubmissionAsync(
                submission.Id,
                submission.TenantId,
                "migrator-backfill",
                ct);
            backfilled++;
        }
        catch (Exception ex)
        {
            skipped++;
            logger.LogWarning(ex, "Failed to backfill anomaly report for submission {SubmissionId}", submission.Id);
        }
    }

    return (backfilled, skipped);
}

static string ResolveConfigurationBasePath()
{
    var currentDirectory = Directory.GetCurrentDirectory();
    var appBaseDirectory = AppContext.BaseDirectory;

    var candidates = new[]
    {
        currentDirectory,
        Path.Combine(currentDirectory, "FC Engine", "src", "FC.Engine.Migrator"),
        Path.Combine(currentDirectory, "src", "FC.Engine.Migrator"),
        Path.Combine(appBaseDirectory, "..", "..", "..", ".."),
        Path.Combine(appBaseDirectory, "..", "..", "..", "..", "..", "src", "FC.Engine.Migrator")
    };

    foreach (var candidate in candidates)
    {
        var fullPath = Path.GetFullPath(candidate);
        if (File.Exists(Path.Combine(fullPath, "appsettings.json")))
        {
            return fullPath;
        }
    }

    return currentDirectory;
}

file sealed class MigratorWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = string.Empty;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public string ContentRootPath { get; set; } = string.Empty;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
