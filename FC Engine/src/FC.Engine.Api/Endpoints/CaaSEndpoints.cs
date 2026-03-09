using System.Security.Claims;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Api.Endpoints;

public static class CaaSEndpoints
{
    public static void MapCaaSEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/caas")
            .WithTags("Compliance-as-a-Service")
            .RequireAuthorization();

        // POST /caas/validate — Validate data against any module template
        group.MapPost("/validate", async (
            CaaSValidateRequest request,
            ITenantContext tenantContext,
            ICaaSService caaSService,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(request.ReturnCode))
                return Results.BadRequest(new { error = "returnCode is required." });

            if (request.Records.Count == 0)
                return Results.BadRequest(new { error = "At least one record is required." });

            try
            {
                var result = await caaSService.ValidateAsync(
                    tenantContext.CurrentTenantId.Value, request, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .Produces<CaaSValidationResponse>()
        .WithName("CaaSValidate")
        .WithSummary("Validate data against any module template");

        // POST /caas/submit — Submit a complete return via API
        group.MapPost("/submit", async (
            CaaSSubmitRequest request,
            ITenantContext tenantContext,
            ICaaSService caaSService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(request.ReturnCode))
                return Results.BadRequest(new { error = "returnCode is required." });

            if (string.IsNullOrWhiteSpace(request.PeriodCode))
                return Results.BadRequest(new { error = "periodCode is required." });

            var institutionId = ResolveInstitutionId(principal);

            try
            {
                var result = await caaSService.SubmitReturnAsync(
                    tenantContext.CurrentTenantId.Value, institutionId, request, ct);

                return result.Success
                    ? Results.Ok(result)
                    : Results.UnprocessableEntity(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .Produces<CaaSSubmitResponse>()
        .Produces<CaaSSubmitResponse>(422)
        .RequireAuthorization("CanCreateSubmission")
        .WithName("CaaSSubmit")
        .WithSummary("Submit a complete return via API");

        // GET /caas/templates/{module} — Get template structure for any module
        group.MapGet("/templates/{module}", async (
            string module,
            ITenantContext tenantContext,
            ICaaSService caaSService,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
                return Results.Forbid();

            var result = await caaSService.GetTemplateStructureAsync(
                tenantContext.CurrentTenantId.Value, module, ct);

            return result == null
                ? Results.NotFound(new { error = $"No templates found for module '{module}'." })
                : Results.Ok(result);
        })
        .Produces<CaaSTemplateResponse>()
        .WithName("CaaSGetTemplate")
        .WithSummary("Get template structure for any module");

        // GET /caas/deadlines — Get filing deadlines for entitled modules
        group.MapGet("/deadlines", async (
            ITenantContext tenantContext,
            ICaaSService caaSService,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
                return Results.Forbid();

            var result = await caaSService.GetDeadlinesAsync(
                tenantContext.CurrentTenantId.Value, ct);

            return Results.Ok(result);
        })
        .Produces<List<CaaSDeadlineItem>>()
        .WithName("CaaSGetDeadlines")
        .WithSummary("Get filing deadlines for entitled modules");

        // POST /caas/score — Get compliance health score
        group.MapPost("/score", async (
            CaaSScoreRequest request,
            ITenantContext tenantContext,
            ICaaSService caaSService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
                return Results.Forbid();

            var institutionId = ResolveInstitutionId(principal);

            var result = await caaSService.GetComplianceScoreAsync(
                tenantContext.CurrentTenantId.Value, institutionId, request, ct);

            return Results.Ok(result);
        })
        .Produces<CaaSScoreResponse>()
        .WithName("CaaSGetScore")
        .WithSummary("Get compliance health score");

        // GET /caas/changes — Get regulatory changes affecting this institution
        group.MapGet("/changes", async (
            ITenantContext tenantContext,
            ICaaSService caaSService,
            string? module,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
                return Results.Forbid();

            var result = await caaSService.GetRegulatoryChangesAsync(
                tenantContext.CurrentTenantId.Value, module, ct);

            return Results.Ok(result);
        })
        .Produces<List<CaaSRegulatoryChange>>()
        .WithName("CaaSGetChanges")
        .WithSummary("Get regulatory changes affecting this institution");

        // POST /caas/simulate — Run a scenario simulation
        group.MapPost("/simulate", async (
            CaaSSimulateRequest request,
            ITenantContext tenantContext,
            ICaaSService caaSService,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
                return Results.Forbid();

            if (string.IsNullOrWhiteSpace(request.ReturnCode))
                return Results.BadRequest(new { error = "returnCode is required." });

            try
            {
                var result = await caaSService.SimulateAsync(
                    tenantContext.CurrentTenantId.Value, request, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .Produces<CaaSSimulateResponse>()
        .WithName("CaaSSimulate")
        .WithSummary("Run a scenario simulation");
    }

    private static int ResolveInstitutionId(ClaimsPrincipal principal)
    {
        var candidate = principal.FindFirstValue("institution_id")
                       ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(candidate, out var id) ? id : 0;
    }
}
