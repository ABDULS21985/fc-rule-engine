using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;

namespace FC.Engine.Api.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/webhooks")
            .WithTags("Webhooks")
            .RequireAuthorization("InstitutionApi");

        group.MapGet("/", async (
            ITenantContext tenantContext,
            IWebhookService webhookService,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            var endpoints = await webhookService.GetEndpointsAsync(tenantId, ct);
            return Results.Ok(endpoints.Select(e => new
            {
                e.Id,
                e.Url,
                e.Description,
                e.EventTypes,
                e.IsActive,
                e.FailureCount,
                e.DisabledReason,
                e.LastDeliveryAt,
                e.CreatedAt
            }));
        })
        .WithName("ListWebhookEndpoints")
        .WithSummary("List all webhook endpoints for the current tenant");

        group.MapPost("/", async (
            CreateWebhookRequest request,
            ITenantContext tenantContext,
            IWebhookService webhookService,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            // Resolve CreatedBy from auth context, never trust client-supplied value
            var createdBy = ResolveUserId(principal);

            var endpoint = await webhookService.CreateEndpointAsync(
                tenantId, request.Url, request.Description,
                request.EventTypes, createdBy, ct);

            return Results.Created($"/api/v1/webhooks/{endpoint.Id}", new
            {
                endpoint.Id,
                endpoint.Url,
                endpoint.Description,
                endpoint.EventTypes,
                endpoint.SecretKey,
                endpoint.IsActive,
                endpoint.CreatedAt
            });
        })
        .WithName("CreateWebhookEndpoint")
        .WithSummary("Create a new webhook endpoint (returns secret — store it securely)");

        group.MapPut("/{id:int}", async (
            int id,
            UpdateWebhookRequest request,
            ITenantContext tenantContext,
            IWebhookService webhookService,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            // Verify the endpoint belongs to the current tenant
            var existing = await webhookService.GetEndpointAsync(id, ct);
            if (existing is null || existing.TenantId != tenantId)
                return Results.NotFound();

            try
            {
                await webhookService.UpdateEndpointAsync(
                    id, request.Url, request.Description,
                    request.EventTypes, request.IsActive, ct);
                return Results.NoContent();
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ex.Message);
            }
        })
        .WithName("UpdateWebhookEndpoint")
        .WithSummary("Update an existing webhook endpoint");

        group.MapDelete("/{id:int}", async (
            int id,
            ITenantContext tenantContext,
            IWebhookService webhookService,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            // Verify the endpoint belongs to the current tenant
            var existing = await webhookService.GetEndpointAsync(id, ct);
            if (existing is null || existing.TenantId != tenantId)
                return Results.NotFound();

            await webhookService.DeleteEndpointAsync(id, ct);
            return Results.NoContent();
        })
        .WithName("DeleteWebhookEndpoint")
        .WithSummary("Delete a webhook endpoint and its delivery history");

        group.MapPost("/{id:int}/rotate-secret", async (
            int id,
            ITenantContext tenantContext,
            IWebhookService webhookService,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            // Verify the endpoint belongs to the current tenant
            var existing = await webhookService.GetEndpointAsync(id, ct);
            if (existing is null || existing.TenantId != tenantId)
                return Results.NotFound();

            try
            {
                var newSecret = await webhookService.RotateSecretAsync(id, ct);
                return Results.Ok(new { SecretKey = newSecret });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ex.Message);
            }
        })
        .WithName("RotateWebhookSecret")
        .WithSummary("Rotate the HMAC signing secret for a webhook endpoint");

        group.MapPost("/{id:int}/test", async (
            int id,
            ITenantContext tenantContext,
            IWebhookService webhookService,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            // Verify the endpoint belongs to the current tenant
            var existing = await webhookService.GetEndpointAsync(id, ct);
            if (existing is null || existing.TenantId != tenantId)
                return Results.NotFound();

            try
            {
                await webhookService.SendTestWebhookAsync(id, ct);
                return Results.Ok(new { Message = "Test webhook sent." });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ex.Message);
            }
        })
        .WithName("SendTestWebhook")
        .WithSummary("Send a test webhook ping to verify endpoint connectivity");

        group.MapGet("/{id:int}/deliveries", async (
            int id,
            ITenantContext tenantContext,
            IWebhookService webhookService,
            int take = 50,
            CancellationToken ct = default) =>
        {
            var tenantId = tenantContext.CurrentTenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
                return Results.Forbid();

            // Cap take to prevent excessive payloads
            if (take <= 0) take = 50;
            if (take > 500) take = 500;

            var endpoint = await webhookService.GetEndpointAsync(id, ct);
            if (endpoint is null || endpoint.TenantId != tenantId)
            {
                return Results.NotFound();
            }

            var deliveries = await webhookService.GetDeliveryLogAsync(id, take, ct);
            return Results.Ok(deliveries.Select(d => new
            {
                d.Id,
                d.EventType,
                d.Status,
                d.HttpStatus,
                d.AttemptCount,
                d.MaxAttempts,
                d.DurationMs,
                d.NextRetryAt,
                d.DeliveredAt,
                d.CreatedAt
            }));
        })
        .WithName("GetWebhookDeliveries")
        .WithSummary("Get the delivery log for a webhook endpoint");
    }

    private static int ResolveUserId(ClaimsPrincipal principal)
    {
        var candidate = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? principal.FindFirstValue("sub");
        return int.TryParse(candidate, out var userId) ? userId : 0;
    }
}

public record CreateWebhookRequest(
    string Url,
    string? Description,
    List<string> EventTypes);

public record UpdateWebhookRequest(
    string? Url = null,
    string? Description = null,
    List<string>? EventTypes = null,
    bool? IsActive = null);
