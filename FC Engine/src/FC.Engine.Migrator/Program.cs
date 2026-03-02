using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<SeedService>();
builder.Services.AddScoped<FormulaSeedService>();
builder.Services.AddScoped<FormulaCatalogSeeder>();
builder.Services.AddScoped<CrossSheetRuleSeedService>();
builder.Services.AddScoped<AuthService>();

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("FC Engine Migrator starting...");

    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

    // Step 1: Apply EF Core migrations (metadata + operational tables)
    logger.LogInformation("Applying EF Core migrations...");
    await db.Database.MigrateAsync();
    logger.LogInformation("EF Core migrations applied successfully");

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
        logger.LogInformation("Seeding cross-sheet rules...");
        var crossSheetSeedService = scope.ServiceProvider.GetRequiredService<CrossSheetRuleSeedService>();
        var crossSheetCount = await crossSheetSeedService.SeedCrossSheetRules("migrator");
        logger.LogInformation("Cross-sheet rules seeded: {Count}", crossSheetCount);
    }

    // Step 6: Seed default admin user (if no users exist)
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

    logger.LogInformation("FC Engine Migrator completed successfully");
}
catch (Exception ex)
{
    logger.LogError(ex, "FC Engine Migrator failed");
    Environment.ExitCode = 1;
}
