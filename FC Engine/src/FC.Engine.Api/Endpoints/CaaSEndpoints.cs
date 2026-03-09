using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace FC.Engine.Api.Endpoints;

public static class CaaSEndpoints
{
    public static WebApplication MapCaaSEndpoints(this WebApplication app)
    {
        // ── Partner API routes (auth via CaaSAuthMiddleware API key) ─────
        var group = app.MapGroup("/api/v1/caas")
            .RequireCaaSAuth()
            .WithTags("CaaS API");

        group.MapPost("/validate", ValidateAsync)
            .WithName("CaaS-Validate")
            .WithSummary("Validate data against a module template");

        group.MapPost("/submit", SubmitAsync)
            .WithName("CaaS-Submit")
            .WithSummary("Submit a complete return via API");

        group.MapGet("/templates/{moduleCode}", GetTemplateAsync)
            .WithName("CaaS-GetTemplate")
            .WithSummary("Get template structure for a module");

        group.MapGet("/deadlines", GetDeadlinesAsync)
            .WithName("CaaS-GetDeadlines")
            .WithSummary("Get filing deadlines for entitled modules");

        group.MapPost("/score", GetScoreAsync)
            .WithName("CaaS-GetScore")
            .WithSummary("Get compliance health score");

        group.MapGet("/changes", GetChangesAsync)
            .WithName("CaaS-GetChanges")
            .WithSummary("Get regulatory changes affecting this institution");

        group.MapPost("/simulate", SimulateAsync)
            .WithName("CaaS-Simulate")
            .WithSummary("Run a scenario simulation");

        // ── Admin routes (RegOS JWT auth, CaaSAdmin policy) ─────────────
        var adminGroup = app.MapGroup("/api/v1/caas/admin")
            .RequireAuthorization("CaaSAdmin")
            .WithTags("CaaS Admin");

        adminGroup.MapPost("/partners/{partnerId:int}/keys", CreateApiKeyAsync);
        adminGroup.MapGet("/partners/{partnerId:int}/keys", ListApiKeysAsync);
        adminGroup.MapDelete("/partners/{partnerId:int}/keys/{keyId:long}", RevokeApiKeyAsync);

        return app;
    }

    // ── Endpoint handlers ─────────────────────────────────────────────────

    private static async Task<IResult> ValidateAsync(
        [FromBody] CaaSValidateRequest request,
        HttpContext ctx, ICaaSService svc, ICaaSRateLimiter rateLimiter,
        CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var rl      = await rateLimiter.CheckAndIncrementAsync(partner.PartnerId, partner.Tier, ct);

        ctx.Response.Headers["X-RateLimit-Limit"]     = rl.Limit.ToString();
        ctx.Response.Headers["X-RateLimit-Remaining"] = rl.Remaining.ToString();

        if (!rl.Allowed)
        {
            ctx.Response.Headers["Retry-After"] = rl.RetryAfterSeconds.ToString();
            return Results.StatusCode(429);
        }

        var requestId = GetRequestId(ctx);
        try
        {
            var result = await svc.ValidateAsync(partner, request, requestId, ct);
            return Results.Ok(result);
        }
        catch (CaaSModuleNotEntitledException)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> SubmitAsync(
        [FromBody] CaaSSubmitRequest request,
        HttpContext ctx, ICaaSService svc, ICaaSRateLimiter rateLimiter,
        CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var rl      = await rateLimiter.CheckAndIncrementAsync(partner.PartnerId, partner.Tier, ct);

        ctx.Response.Headers["X-RateLimit-Limit"]     = rl.Limit.ToString();
        ctx.Response.Headers["X-RateLimit-Remaining"] = rl.Remaining.ToString();

        if (!rl.Allowed)
        {
            ctx.Response.Headers["Retry-After"] = rl.RetryAfterSeconds.ToString();
            return Results.StatusCode(429);
        }

        var requestId = GetRequestId(ctx);
        var result    = await svc.SubmitAsync(partner, request, requestId, ct);

        return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
    }

    private static async Task<IResult> GetTemplateAsync(
        string moduleCode, HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var partner   = GetPartner(ctx);
        var requestId = GetRequestId(ctx);
        try
        {
            var result = await svc.GetTemplateAsync(partner, moduleCode, requestId, ct);
            return Results.Ok(result);
        }
        catch (CaaSModuleNotEntitledException)
        {
            return Results.Forbid();
        }
    }

    private static async Task<IResult> GetDeadlinesAsync(
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var result  = await svc.GetDeadlinesAsync(partner, GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetScoreAsync(
        [FromBody] CaaSScoreRequest request,
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var result = await svc.GetScoreAsync(GetPartner(ctx), request, GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetChangesAsync(
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var result = await svc.GetChangesAsync(GetPartner(ctx), GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SimulateAsync(
        [FromBody] CaaSSimulateRequest request,
        HttpContext ctx, ICaaSService svc, CancellationToken ct)
    {
        var partner = GetPartner(ctx);
        var result  = await svc.SimulateAsync(partner, request, GetRequestId(ctx), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateApiKeyAsync(
        int partnerId, [FromBody] CreateApiKeyRequest request,
        ICaaSApiKeyService keyService, ClaimsPrincipal user, CancellationToken ct)
    {
        var userId    = int.Parse(user.FindFirst("user_id")!.Value);
        var (rawKey, info) = await keyService.CreateKeyAsync(
            partnerId, request.DisplayName,
            Enum.Parse<CaaSEnvironment>(request.Environment, ignoreCase: true),
            request.ExpiresAt, userId, ct);

        return Results.Ok(new
        {
            rawKey,
            info,
            warning = "This is the only time the full API key will be shown. Store it securely."
        });
    }

    private static async Task<IResult> ListApiKeysAsync(
        int partnerId, ICaaSApiKeyService keyService, CancellationToken ct)
    {
        var keys = await keyService.ListKeysAsync(partnerId, ct);
        return Results.Ok(keys);
    }

    private static async Task<IResult> RevokeApiKeyAsync(
        int partnerId, long keyId, ICaaSApiKeyService keyService,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var userId = int.Parse(user.FindFirst("user_id")!.Value);
        await keyService.RevokeKeyAsync(partnerId, keyId, userId, ct);
        return Results.NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static ResolvedPartner GetPartner(HttpContext ctx)
        => ctx.Items["caas_partner"] as ResolvedPartner
           ?? throw new UnauthorizedAccessException("CaaS partner not resolved.");

    private static Guid GetRequestId(HttpContext ctx)
        => ctx.Items["caas_request_id"] is Guid id ? id : Guid.NewGuid();
}

/// <summary>Endpoint filter that enforces CaaS partner resolution for API key routes.</summary>
public static class CaaSAuthMiddlewareExtensions
{
    public static RouteGroupBuilder RequireCaaSAuth(this RouteGroupBuilder builder)
    {
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            if (ctx.HttpContext.Items["caas_partner"] is null)
                return Results.Unauthorized();
            return await next(ctx);
        });
        return builder;
    }
}

public sealed record CreateApiKeyRequest(
    string DisplayName,
    string Environment,
    DateTimeOffset? ExpiresAt);
