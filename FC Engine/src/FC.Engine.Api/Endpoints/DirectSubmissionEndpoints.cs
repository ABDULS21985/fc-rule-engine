using System.Security.Claims;
using FC.Engine.Application.DTOs;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;

namespace FC.Engine.Api.Endpoints;

public static class DirectSubmissionEndpoints
{
    public static void MapDirectSubmissionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/submissions/{submissionId:int}/direct")
            .WithTags("DirectSubmission")
            .RequireAuthorization();

        // POST /submissions/{submissionId}/direct — trigger direct API submission
        group.MapPost("/", async (
            int submissionId,
            SubmitToRegulatorRequest request,
            IRegulatorySubmissionService service,
            ClaimsPrincipal principal,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty) return Results.Forbid();

            var submittedBy = ResolveUserName(principal);
            var result = await service.SubmitToRegulatorAsync(
                submissionId, request.RegulatorCode, submittedBy, ct);

            return result.Success
                ? Results.Ok(result)
                : Results.UnprocessableEntity(result);
        })
        .RequireAuthorization("CanDirectSubmit")
        .WithName("SubmitToRegulator")
        .WithSummary("Submit directly to regulator via API");

        // GET /submissions/{submissionId}/direct — list direct submission history
        group.MapGet("/", async (
            int submissionId,
            IRegulatorySubmissionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty) return Results.Forbid();

            var history = await service.GetSubmissionHistoryAsync(tenantId, submissionId, ct);
            return Results.Ok(history.Select(MapToDto));
        })
        .RequireAuthorization("CanViewDirectStatus")
        .WithName("GetDirectSubmissionHistory")
        .WithSummary("Get direct submission history for a submission");

        // GET /submissions/{submissionId}/direct/{directId}/status — check status
        group.MapGet("/{directId:int}/status", async (
            int submissionId,
            int directId,
            IRegulatorySubmissionService service,
            CancellationToken ct) =>
        {
            var result = await service.CheckStatusAsync(directId, ct);
            return Results.Ok(result);
        })
        .RequireAuthorization("CanViewDirectStatus")
        .WithName("CheckDirectSubmissionStatus")
        .WithSummary("Check real-time status from regulator");

        // POST /submissions/{submissionId}/direct/{directId}/retry — manual retry
        group.MapPost("/{directId:int}/retry", async (
            int submissionId,
            int directId,
            IRegulatorySubmissionService service,
            CancellationToken ct) =>
        {
            var result = await service.RetrySubmissionAsync(directId, ct);
            return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
        })
        .RequireAuthorization("CanDirectSubmit")
        .WithName("RetryDirectSubmission")
        .WithSummary("Manually retry a failed direct submission");
    }

    private static string ResolveUserName(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? "unknown";
    }

    private static DirectSubmissionDto MapToDto(DirectSubmission ds) => new()
    {
        Id = ds.Id,
        SubmissionId = ds.SubmissionId,
        RegulatorCode = ds.RegulatorCode,
        Channel = ds.Channel.ToString(),
        Status = ds.Status.ToString(),
        RegulatorReference = ds.RegulatorReference,
        AttemptCount = ds.AttemptCount,
        MaxAttempts = ds.MaxAttempts,
        SubmittedAt = ds.SubmittedAt,
        AcknowledgedAt = ds.AcknowledgedAt,
        ErrorMessage = ds.ErrorMessage,
        SignatureAlgorithm = ds.SignatureAlgorithm,
        CertificateThumbprint = ds.CertificateThumbprint,
        CreatedAt = ds.CreatedAt
    };
}
