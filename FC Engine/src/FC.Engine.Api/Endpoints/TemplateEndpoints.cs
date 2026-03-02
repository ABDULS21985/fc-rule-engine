using FC.Engine.Application.DTOs;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Api.Endpoints;

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/templates").WithTags("Templates");

        group.MapGet("/", async (
            TemplateService templateService,
            CancellationToken ct) =>
        {
            var templates = await templateService.GetAllTemplates(ct);
            return Results.Ok(templates);
        })
        .Produces<IReadOnlyList<TemplateDto>>()
        .WithName("GetAllTemplates")
        .WithSummary("Get all return templates");

        group.MapGet("/{returnCode}", async (
            string returnCode,
            TemplateService templateService,
            CancellationToken ct) =>
        {
            var template = await templateService.GetTemplateDetail(returnCode, ct);
            if (template == null) return Results.NotFound();
            return Results.Ok(template);
        })
        .Produces<TemplateDetailDto>()
        .WithName("GetTemplate")
        .WithSummary("Get template details with versions and fields");

        group.MapPost("/", async (
            CreateTemplateRequest request,
            TemplateService templateService,
            CancellationToken ct) =>
        {
            var template = await templateService.CreateTemplate(request, ct);
            return Results.Created($"/api/templates/{template.ReturnCode}", template);
        })
        .Produces<TemplateDto>(201)
        .WithName("CreateTemplate")
        .WithSummary("Create a new return template");

        group.MapPost("/{templateId:int}/versions/{versionId:int}/fields", async (
            int templateId, int versionId,
            AddFieldRequest request,
            TemplateService templateService,
            CancellationToken ct) =>
        {
            await templateService.AddFieldToVersion(templateId, versionId, request, "system", ct);
            return Results.Ok();
        })
        .WithName("AddField")
        .WithSummary("Add a field to a draft template version");

        // Version lifecycle
        group.MapPost("/{templateId:int}/versions", async (
            int templateId,
            TemplateVersioningService versionService,
            CancellationToken ct) =>
        {
            var version = await versionService.CreateNewDraftVersion(templateId, "system", ct);
            return Results.Created($"/api/templates/{templateId}/versions/{version.Id}",
                new { version.Id, version.VersionNumber, Status = version.Status.ToString() });
        })
        .WithName("CreateDraftVersion")
        .WithSummary("Create a new draft version from current published");

        group.MapPost("/{templateId:int}/versions/{versionId:int}/submit", async (
            int templateId, int versionId,
            TemplateVersioningService versionService,
            CancellationToken ct) =>
        {
            await versionService.SubmitForReview(templateId, versionId, "system", ct);
            return Results.Ok();
        })
        .WithName("SubmitForReview")
        .WithSummary("Submit a draft version for review");

        group.MapPost("/{templateId:int}/versions/{versionId:int}/preview-ddl", async (
            int templateId, int versionId,
            TemplateVersioningService versionService,
            CancellationToken ct) =>
        {
            var ddl = await versionService.PreviewDdl(templateId, versionId, ct);
            return Results.Ok(new { ddl.ForwardSql, ddl.RollbackSql });
        })
        .WithName("PreviewDdl")
        .WithSummary("Preview DDL that will be executed on publish");

        group.MapPost("/{templateId:int}/versions/{versionId:int}/publish", async (
            int templateId, int versionId,
            TemplateVersioningService versionService,
            CancellationToken ct) =>
        {
            await versionService.Publish(templateId, versionId, "system", ct);
            return Results.Ok();
        })
        .WithName("PublishVersion")
        .WithSummary("Publish a version (executes DDL and activates template)");

        // Formulas
        group.MapGet("/{templateId:int}/versions/{versionId:int}/formulas", async (
            int templateId, int versionId,
            FormulaService formulaService,
            CancellationToken ct) =>
        {
            var formulas = await formulaService.GetIntraSheetFormulas(versionId, ct);
            return Results.Ok(formulas);
        })
        .Produces<IReadOnlyList<FormulaDto>>()
        .WithName("GetFormulas")
        .WithSummary("Get intra-sheet formulas for a template version");
    }
}
