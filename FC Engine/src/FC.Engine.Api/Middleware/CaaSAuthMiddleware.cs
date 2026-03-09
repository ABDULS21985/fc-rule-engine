using Dapper;
using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Api.Middleware;

/// <summary>
/// Resolves partner from Bearer token (CaaS API key) in Authorization header.
/// Sets X-CaaS-Request-Id on every request.
/// Logs every CaaS request to CaaSRequests table (fire-and-forget).
/// Only activates for paths starting with /api/v1/caas.
/// Admin sub-routes (/api/v1/caas/admin) bypass API key auth and use RegOS JWT instead.
/// </summary>
public sealed class CaaSAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CaaSAuthMiddleware> _log;

    public CaaSAuthMiddleware(RequestDelegate next, ILogger<CaaSAuthMiddleware> log)
    {
        _next = next;
        _log  = log;
    }

    public async Task InvokeAsync(HttpContext ctx, ICaaSApiKeyService keyService,
        IDbConnectionFactory db)
    {
        var requestId = Guid.NewGuid();
        ctx.Items["caas_request_id"] = requestId;
        ctx.Response.Headers["X-CaaS-Request-Id"] = requestId.ToString();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Only intercept CaaS API routes; admin routes use RegOS JWT auth
        if (!ctx.Request.Path.StartsWithSegments("/api/v1/caas") ||
             ctx.Request.Path.StartsWithSegments("/api/v1/caas/admin"))
        {
            await _next(ctx);
            return;
        }

        // Extract API key from Authorization: Bearer regos_live_...
        var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
        ResolvedPartner? partner = null;

        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
        {
            var rawKey = authHeader[7..].Trim();
            partner = await keyService.ValidateKeyAsync(rawKey);
        }

        if (partner is null)
        {
            ctx.Response.StatusCode  = 401;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(
                """{"error":"Invalid or missing API key.","code":"UNAUTHORIZED"}""");
            return;
        }

        ctx.Items["caas_partner"] = partner;

        _log.LogInformation(
            "CaaS request: Partner={Partner} Endpoint={Path} RequestId={RequestId}",
            partner.PartnerCode, ctx.Request.Path, requestId);

        await _next(ctx);

        sw.Stop();

        // Audit log — fire-and-forget, never blocks the response
        _ = LogRequestAsync(db, partner, ctx, requestId, sw.ElapsedMilliseconds);
    }

    private static async Task LogRequestAsync(
        IDbConnectionFactory db, ResolvedPartner partner,
        HttpContext ctx, Guid requestId, long durationMs)
    {
        try
        {
            using var conn = await db.OpenAsync();
            var moduleCode = ctx.Request.RouteValues.TryGetValue("moduleCode", out var mc)
                ? mc?.ToString() : null;

            var agent = ctx.Request.Headers.UserAgent.ToString();

            await conn.ExecuteAsync(
                """
                INSERT INTO CaaSRequests
                    (PartnerId, ApiKeyId, RequestId, Endpoint, HttpMethod,
                     ModuleCode, ResponseStatusCode, DurationMs, IpAddress, UserAgent)
                VALUES (@PartnerId, 0, @RequestId, @Endpoint, @Method,
                        @ModuleCode, @StatusCode, @Duration, @Ip, @Agent)
                """,
                new
                {
                    PartnerId  = partner.PartnerId,
                    RequestId  = requestId,
                    Endpoint   = ctx.Request.Path.Value?[..Math.Min(100,
                                     ctx.Request.Path.Value.Length)],
                    Method     = ctx.Request.Method,
                    ModuleCode = moduleCode,
                    StatusCode = ctx.Response.StatusCode,
                    Duration   = durationMs,
                    Ip         = ctx.Connection.RemoteIpAddress?.ToString(),
                    Agent      = agent[..Math.Min(300, agent.Length)]
                });
        }
        catch
        {
            // Audit failures must never break the request — swallow silently
        }
    }
}
