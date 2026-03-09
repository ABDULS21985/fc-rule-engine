using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace FC.Engine.Api.Endpoints;

public static class RootCauseAnalysisEndpoints
{
    public static void MapRootCauseAnalysisEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/rca")
            .WithTags("Root Cause Analysis")
            .RequireAuthorization();

        group.MapPost("/analyze", async (
            [FromBody] AnalyzeRootCauseRequest request,
            IRootCauseAnalysisService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            if (!TryParseType(request.Type, out var incidentType))
            {
                return Results.BadRequest(new { error = $"Unsupported RCA type '{request.Type}'." });
            }

            var result = await service.AnalyzeAsync(
                tenantContext.CurrentTenantId.Value,
                incidentType,
                request.IncidentId,
                request.ForceRefresh,
                ct);

            return Results.Ok(result);
        })
        .WithSummary("Trigger RCA generation or refresh for an incident.");

        group.MapGet("/{type}/{incidentId:guid}", async (
            string type,
            Guid incidentId,
            IRootCauseAnalysisService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            if (!TryParseType(type, out var incidentType))
            {
                return Results.BadRequest(new { error = $"Unsupported RCA type '{type}'." });
            }

            var result = await service.GetCachedAsync(tenantContext.CurrentTenantId.Value, incidentType, incidentId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        })
        .WithSummary("Get a cached RCA result for an incident.");

        group.MapGet("/{type}/{incidentId:guid}/timeline", async (
            string type,
            Guid incidentId,
            IRootCauseAnalysisService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            if (!TryParseType(type, out var incidentType))
            {
                return Results.BadRequest(new { error = $"Unsupported RCA type '{type}'." });
            }

            var result = await service.GetTimelineAsync(tenantContext.CurrentTenantId.Value, incidentType, incidentId, ct);
            return Results.Ok(result);
        })
        .WithSummary("Get the RCA event timeline for an incident.");
    }

    private static bool TryParseType(string value, out RcaIncidentType type)
    {
        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "security_alert":
            case "security-alert":
                type = RcaIncidentType.SecurityAlert;
                return true;
            case "pipeline_failure":
            case "pipeline-failure":
                type = RcaIncidentType.PipelineFailure;
                return true;
            case "quality_issue":
            case "quality-issue":
                type = RcaIncidentType.QualityIssue;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
