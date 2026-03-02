using FC.Engine.Application.Services;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.DynamicSchema;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<SeedService>();

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
        await db.Database.ExecuteSqlRawAsync(schemaSql);
        logger.LogInformation("Metadata schema script executed successfully");
    }

    // Step 3: Seed templates from schema.sql (if configured)
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
    }

    logger.LogInformation("FC Engine Migrator completed successfully");
}
catch (Exception ex)
{
    logger.LogError(ex, "FC Engine Migrator failed");
    Environment.ExitCode = 1;
}
