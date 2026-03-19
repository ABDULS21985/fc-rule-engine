using System.Security.Claims;
using FC.Engine.Application.DTOs;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Api.Endpoints;

public static class TemplateEndpoints
{
    public static void MapTemplateEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/templates").WithTags("Templates");

        // Read endpoints — require CanReadTemplates
        group.MapGet("/", async (
            TemplateService templateService,
            CancellationToken ct) =>
        {
            var templates = await templateService.GetAllTemplates(ct);
            return Results.Ok(templates);
        })
        .Produces<IReadOnlyList<TemplateDto>>()
        .RequireAuthorization("CanReadTemplates")
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
        .RequireAuthorization("CanReadTemplates")
        .WithName("GetTemplate")
        .WithSummary("Get template details with versions and fields");

        // Write endpoints — require CanEditTemplates
        group.MapPost("/", async (
            CreateTemplateRequest request,
            TemplateService templateService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var actingUser = ResolveUserIdentity(principal);
            if (string.IsNullOrWhiteSpace(request.CreatedBy))
                request.CreatedBy = actingUser;
            var template = await templateService.CreateTemplate(request, ct);
            return Results.Created($"/api/v1/templates/{template.ReturnCode}", template);
        })
        .Produces<TemplateDto>(201)
        .RequireAuthorization("CanEditTemplates")
        .WithName("CreateTemplate")
        .WithSummary("Create a new return template");

        group.MapPost("/{templateId:int}/versions/{versionId:int}/fields", async (
            int templateId, int versionId,
            AddFieldRequest request,
            TemplateService templateService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var actingUser = ResolveUserIdentity(principal);
            await templateService.AddFieldToVersion(templateId, versionId, request, actingUser, ct);
            return Results.Ok();
        })
        .RequireAuthorization("CanEditTemplates")
        .WithName("AddField")
        .WithSummary("Add a field to a draft template version");

        // Version lifecycle
        group.MapPost("/{templateId:int}/versions", async (
            int templateId,
            TemplateVersioningService versionService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var actingUser = ResolveUserIdentity(principal);
            var version = await versionService.CreateNewDraftVersion(templateId, actingUser, ct);
            return Results.Created($"/api/v1/templates/{templateId}/versions/{version.Id}",
                new { version.Id, version.VersionNumber, Status = version.Status.ToString() });
        })
        .RequireAuthorization("CanEditTemplates")
        .WithName("CreateDraftVersion")
        .WithSummary("Create a new draft version from current published");

        group.MapPost("/{templateId:int}/versions/{versionId:int}/submit", async (
            int templateId, int versionId,
            TemplateVersioningService versionService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var actingUser = ResolveUserIdentity(principal);
            await versionService.SubmitForReview(templateId, versionId, actingUser, ct);
            return Results.Ok();
        })
        .RequireAuthorization("CanEditTemplates")
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
        .RequireAuthorization("CanPublishTemplates")
        .WithName("PreviewDdl")
        .WithSummary("Preview DDL that will be executed on publish");

        // Publish — requires elevated CanPublishTemplates
        group.MapPost("/{templateId:int}/versions/{versionId:int}/publish", async (
            int templateId, int versionId,
            TemplateVersioningService versionService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var actingUser = ResolveUserIdentity(principal);
            await versionService.Publish(templateId, versionId, actingUser, ct);
            return Results.Ok();
        })
        .RequireAuthorization("CanPublishTemplates")
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
        .RequireAuthorization("CanReadTemplates")
        .WithName("GetFormulas")
        .WithSummary("Get intra-sheet formulas for a template version");
    }

    private static string ResolveUserIdentity(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "system";
    }
}
