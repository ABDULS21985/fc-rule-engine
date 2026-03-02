using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Api.Endpoints;

public static class SchemaEndpoints
{
    public static void MapSchemaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/schemas").WithTags("Schemas");

        group.MapGet("/{returnCode}/xsd", async (
            string returnCode,
            IXsdGenerator xsdGenerator,
            CancellationToken ct) =>
        {
            var xml = await xsdGenerator.GenerateSchemaXml(returnCode, ct);
            return Results.Content(xml, "application/xml");
        })
        .Produces<string>(contentType: "application/xml")
        .WithName("GetXsdSchema")
        .WithSummary("Get the XSD schema for a return template");

        group.MapPost("/seed", async (
            SeedService seedService,
            IConfiguration config,
            CancellationToken ct) =>
        {
            var schemaPath = config["Seeding:SchemaFilePath"] ?? "/app/schema.sql";
            var result = await seedService.SeedFromSchema(schemaPath, "system", ct);

            return Results.Ok(new
            {
                created = result.Created.Count,
                skipped = result.Skipped.Count,
                errors = result.Errors.Count,
                details = result
            });
        })
        .WithName("SeedTemplates")
        .WithSummary("Seed templates from schema.sql file (run once)");

        group.MapGet("/published", async (
            ITemplateMetadataCache cache,
            CancellationToken ct) =>
        {
            var templates = await cache.GetAllPublishedTemplates(ct);
            return Results.Ok(templates.Select(t => new
            {
                t.ReturnCode,
                t.Name,
                t.StructuralCategory,
                t.PhysicalTableName,
                FieldCount = t.CurrentVersion.Fields.Count,
                FormulaCount = t.CurrentVersion.IntraSheetFormulas.Count
            }));
        })
        .WithName("GetPublishedTemplates")
        .WithSummary("Get all published templates from cache");
    }
}
