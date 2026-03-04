using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.MultiTenancy;

/// <summary>
/// ASP.NET Core middleware that resolves TenantId for the current request
/// and stores it in HttpContext.Items for downstream use.
/// </summary>
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip tenant resolution for health endpoints and static files
        var path = context.Request.Path.Value ?? "";
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Skip for unauthenticated requests (login pages, etc.)
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Check for PlatformAdmin role
        var isPlatformAdmin = context.User.IsInRole("PlatformAdmin")
            || context.User.HasClaim("IsPlatformAdmin", "true");

        if (isPlatformAdmin)
        {
            // PlatformAdmin: check for impersonation header/cookie
            var impersonateTenantId = ResolveImpersonation(context);
            if (impersonateTenantId.HasValue)
            {
                context.Items["TenantId"] = impersonateTenantId.Value;
                context.Items["ImpersonatingTenantId"] = impersonateTenantId.Value;
                _logger.LogDebug("PlatformAdmin impersonating tenant {TenantId}", impersonateTenantId.Value);
            }
            // else: no TenantId set → RLS fn_TenantFilter allows all data

            await _next(context);
            return;
        }

        // Normal user: resolve TenantId from claims
        var tenantClaim = context.User.FindFirst("TenantId");
        if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
        {
            context.Items["TenantId"] = tenantId;
            await _next(context);
            return;
        }

        // No tenant context for authenticated non-PlatformAdmin user → 403
        _logger.LogWarning("Authenticated user {User} has no TenantId claim",
            context.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown");
        context.Response.StatusCode = 403;
        await context.Response.WriteAsJsonAsync(new { error = "No tenant context available" });
    }

    private static Guid? ResolveImpersonation(HttpContext context)
    {
        // Check header first (API usage)
        if (context.Request.Headers.TryGetValue("X-Impersonate-Tenant", out var headerVal)
            && Guid.TryParse(headerVal, out var headerTenantId))
        {
            return headerTenantId;
        }

        // Check cookie (Blazor admin usage)
        if (context.Request.Cookies.TryGetValue("ImpersonateTenantId", out var cookieVal)
            && Guid.TryParse(cookieVal, out var cookieTenantId))
        {
            return cookieTenantId;
        }

        return null;
    }
}

public static class TenantContextMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantContextMiddleware>();
    }
}
