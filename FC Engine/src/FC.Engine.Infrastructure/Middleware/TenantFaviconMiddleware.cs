using FC.Engine.Domain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FC.Engine.Infrastructure.Middleware;

public class TenantFaviconMiddleware
{
    private readonly RequestDelegate _next;

    public TenantFaviconMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantBrandingService brandingService,
        ITenantContext tenantContext)
    {
        if ((context.Request.Path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
             || context.Request.Path.Equals("/favicon.svg", StringComparison.OrdinalIgnoreCase))
            && tenantContext.CurrentTenantId.HasValue)
        {
            var branding = await brandingService.GetBrandingConfig(tenantContext.CurrentTenantId.Value, context.RequestAborted);
            if (!string.IsNullOrWhiteSpace(branding.FaviconUrl)
                && !branding.FaviconUrl.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase)
                && !branding.FaviconUrl.Equals("/favicon.svg", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect(branding.FaviconUrl, permanent: false);
                return;
            }
        }

        await _next(context);
    }
}

public static class TenantFaviconMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantFavicon(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantFaviconMiddleware>();
    }
}
