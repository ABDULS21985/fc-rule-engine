using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace FC.Engine.Api.Endpoints;

public static class PrivacyEndpoints
{
    public static void MapPrivacyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/privacy").WithTags("Privacy");

        group.MapPost("/dsar", async (
            [FromBody] CreateDsarRequest request,
            IDsarService dsarService,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Results.Forbid();
            }

            var userType = httpContext.User.HasClaim(c => c.Type == "InstitutionId")
                ? "InstitutionUser"
                : "PortalUser";

            var dsar = await dsarService.CreateRequest(
                tenantContext.CurrentTenantId.Value,
                request.RequestType,
                userId,
                userType,
                request.Description,
                ct);
            return Results.Ok(dsar);
        })
        .RequireAuthorization("CanCreateSubmission")
        .WithSummary("Create a DSAR request.");

        group.MapGet("/dsar", async (
            IDsarService dsarService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var requests = await dsarService.GetRequests(tenantContext.CurrentTenantId.Value, ct);
            return Results.Ok(requests);
        })
        .RequireAuthorization("CanViewSubmissions")
        .WithSummary("Get DSAR history for tenant.");

        group.MapPost("/dsar/{dsarId:int}/access-package", async (
            int dsarId,
            IDsarService dsarService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Results.Forbid();
            }

            var path = await dsarService.GenerateAccessPackage(dsarId, userId, ct);
            return Results.Ok(new { dataPackagePath = path });
        })
        .RequireAuthorization("CanApproveSubmissions")
        .WithSummary("Generate DSAR access package.");

        group.MapPost("/dsar/{dsarId:int}/erasure", async (
            int dsarId,
            IDsarService dsarService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Results.Forbid();
            }

            await dsarService.ProcessErasure(dsarId, userId, ct);
            return Results.Ok(new { success = true });
        })
        .RequireAuthorization("CanApproveSubmissions")
        .WithSummary("Process DSAR erasure after DPO approval.");

        group.MapPost("/breaches", async (
            [FromBody] DataBreachReport report,
            IDataBreachService breachService,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            report.TenantId ??= tenantContext.CurrentTenantId;
            var incident = await breachService.ReportBreach(report, ct);
            return Results.Ok(incident);
        })
        .RequireAuthorization("CanApproveSubmissions")
        .WithSummary("Report a data breach incident.");

        group.MapPost("/breaches/{incidentId:int}/nitda-notified", async (
            int incidentId,
            [FromBody] NitdaNotificationRequest request,
            IDataBreachService breachService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                return Results.Forbid();
            }

            var incident = await breachService.MarkNitdaNotified(incidentId, userId, request.Notes, ct);
            return Results.Ok(incident);
        })
        .RequireAuthorization("CanApproveSubmissions")
        .WithSummary("Mark incident as notified to NITDA.");
    }
}

public class CreateDsarRequest
{
    public DataSubjectRequestType RequestType { get; set; } = DataSubjectRequestType.Access;
    public string? Description { get; set; }
}

public class NitdaNotificationRequest
{
    public string? Notes { get; set; }
}
