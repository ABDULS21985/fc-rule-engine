using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace FC.Engine.Api.Endpoints;

public static class DataProtectionEndpoints
{
    public static void MapDataProtectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/dspm")
            .WithTags("Continuous DSPM")
            .RequireAuthorization();

        group.MapPost("/sources", async (
            [FromBody] DataSourceRegistrationRequest request,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await service.UpsertDataSourceAsync(tenantContext.CurrentTenantId.Value, request, ct);
            return Results.Ok(result);
        })
        .WithSummary("Register or update a DSPM data source.");

        group.MapPost("/pipelines", async (
            [FromBody] DataPipelineDefinitionRequest request,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await service.UpsertPipelineAsync(tenantContext.CurrentTenantId.Value, request, ct);
            return Results.Ok(result);
        })
        .WithSummary("Register or update a DSPM pipeline definition.");

        group.MapPost("/assets", async (
            [FromBody] CyberAssetRegistrationRequest request,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await service.UpsertAssetAsync(tenantContext.CurrentTenantId.Value, request, ct);
            return Results.Ok(result);
        })
        .WithSummary("Register or update a cyber asset used in RCA impact analysis.");

        group.MapPost("/assets/{assetId:guid}/dependencies/{dependsOnAssetId:guid}", async (
            Guid assetId,
            Guid dependsOnAssetId,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            await service.AddAssetDependencyAsync(tenantContext.CurrentTenantId.Value, assetId, dependsOnAssetId, ct);
            return Results.Ok(new { success = true });
        })
        .WithSummary("Declare an asset dependency for blast-radius analysis.");

        group.MapPost("/pipeline-events", async (
            [FromBody] PipelineEventReport request,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await service.RecordPipelineEventAsync(tenantContext.CurrentTenantId.Value, request, ct);
            return Results.Accepted($"/api/v1/dspm/pipeline-events/{result.ExecutionId}", result);
        })
        .WithSummary("Record a pipeline lifecycle event and trigger continuous DSPM watchers.");

        group.MapPost("/security/alerts", async (
            [FromBody] SecurityAlertReport request,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var result = await service.ReportSecurityAlertAsync(tenantContext.CurrentTenantId.Value, request, ct);
            return Results.Ok(result);
        })
        .WithSummary("Create a persisted security alert for RCA.");

        group.MapPost("/security/events", async (
            [FromBody] SecurityEventReport request,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            await service.RecordSecurityEventAsync(tenantContext.CurrentTenantId.Value, request, ct);
            return Results.Ok(new { success = true });
        })
        .WithSummary("Record a security telemetry event for RCA correlation.");

        group.MapPost("/scan/at-rest", async (
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            await service.RunAtRestScanAsync(tenantContext.CurrentTenantId.Value, ct);
            return Results.Accepted();
        })
        .WithSummary("Run an on-demand at-rest DSPM re-scan.");

        group.MapPost("/scan/shadow", async (
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            await service.RunShadowCopyDetectionAsync(tenantContext.CurrentTenantId.Value, ct);
            return Results.Accepted();
        })
        .WithSummary("Run on-demand shadow copy detection.");

        group.MapGet("/scans", async (
            Guid? sourceId,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var scans = await service.GetScanHistoryAsync(tenantContext.CurrentTenantId.Value, sourceId, ct);
            return Results.Ok(scans);
        })
        .WithSummary("Get DSPM scan history for the current tenant.");

        group.MapGet("/shadow-copies", async (
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var results = await service.GetShadowCopiesAsync(tenantContext.CurrentTenantId.Value, ct);
            return Results.Ok(results);
        })
        .WithSummary("Get detected shadow copy matches for the current tenant.");

        group.MapGet("/alerts", async (
            string? alertType,
            IDataProtectionService service,
            ITenantContext tenantContext,
            CancellationToken ct) =>
        {
            if (!tenantContext.CurrentTenantId.HasValue)
            {
                return Results.Forbid();
            }

            var results = await service.GetSecurityAlertsAsync(tenantContext.CurrentTenantId.Value, alertType, ct);
            return Results.Ok(results);
        })
        .WithSummary("Get DSPM and security alerts for the current tenant.");
    }
}
