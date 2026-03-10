using System.Security.Claims;
using FC.Engine.Infrastructure.Services;
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

    public async Task InvokeAsync(
        HttpContext context,
        ITenantAccessContextResolver tenantAccessContextResolver,
        IPlatformRegulatorTenantResolver platformRegulatorTenantResolver)
    {
        // Skip tenant resolution for health, metrics endpoints and static files
        var path = context.Request.Path.Value ?? "";
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Skip for unauthenticated requests (login pages, etc.)
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            // API key middleware may already have resolved tenant context for this request.
            if (context.Items.ContainsKey("TenantId"))
            {
                await _next(context);
                return;
            }

            // API request authenticated by key but missing tenant resolution.
            if (context.Items.TryGetValue("ApiKeyValidated", out var apiKeyValidated) &&
                apiKeyValidated is true)
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "No tenant context available for API key request" });
                return;
            }

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
            TenantAccessContext? accessContext = null;
            if (impersonateTenantId.HasValue)
            {
                context.Items["TenantId"] = impersonateTenantId.Value;
                context.Items["ImpersonatingTenantId"] = impersonateTenantId.Value;
                _logger.LogDebug("PlatformAdmin impersonating tenant {TenantId}", impersonateTenantId.Value);
                accessContext = await tenantAccessContextResolver.TryResolveAsync(
                    impersonateTenantId.Value,
                    context.User,
                    context.RequestAborted);
            }
            else if (IsRegulatorPortalRequest(path))
            {
                var regulatorTenant = await platformRegulatorTenantResolver.TryResolveAsync(context.RequestAborted);
                if (regulatorTenant is not null)
                {
                    context.Items["TenantId"] = regulatorTenant.TenantId;
                    context.Items["ImpersonatingTenantId"] = regulatorTenant.TenantId;
                    EnsureImpersonationCookie(context, regulatorTenant.TenantId);

                    accessContext = await tenantAccessContextResolver.TryResolveAsync(
                        regulatorTenant.TenantId,
                        context.User,
                        context.RequestAborted);

                    _logger.LogDebug(
                        "PlatformAdmin auto-bound to regulator tenant {TenantId} for path {Path}",
                        regulatorTenant.TenantId,
                        path);
                }
            }
            // else: no TenantId set → RLS fn_TenantFilter allows all data

            PopulateTenantSessionAttributes(context, accessContext);
            await _next(context);
            return;
        }

        // Normal user: resolve TenantId from claims
        var tenantClaim = context.User.FindFirst("TenantId") ?? context.User.FindFirst("tid");
        if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
        {
            context.Items["TenantId"] = tenantId;
            var accessContext = await tenantAccessContextResolver.TryResolveAsync(
                tenantId,
                context.User,
                context.RequestAborted);
            PopulateTenantSessionAttributes(context, accessContext);
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

    private static bool IsRegulatorPortalRequest(string path)
    {
        return path.StartsWith("/regulator", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/scenarios/macro", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureImpersonationCookie(HttpContext context, Guid tenantId)
    {
        if (context.Request.Cookies.TryGetValue("ImpersonateTenantId", out var existingValue)
            && Guid.TryParse(existingValue, out var existingTenantId)
            && existingTenantId == tenantId)
        {
            return;
        }

        context.Response.Cookies.Append("ImpersonateTenantId", tenantId.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddHours(2)
        });
    }

    private static void PopulateTenantSessionAttributes(HttpContext context, TenantAccessContext? accessContext)
    {
        var tenantType = accessContext?.TenantType.ToString()
            ?? context.User.FindFirst("TenantType")?.Value;
        if (!string.IsNullOrWhiteSpace(tenantType))
        {
            context.Items["TenantType"] = tenantType;
        }

        var regulatorCode = accessContext?.RegulatorCode
            ?? context.User.FindFirst("RegulatorCode")?.Value;
        if (!string.IsNullOrWhiteSpace(regulatorCode))
        {
            context.Items["RegulatorCode"] = regulatorCode;
        }
    }
}

public static class TenantContextMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantContextMiddleware>();
    }
}
