using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FC.Engine.Infrastructure.Middleware;

public class TenantResolutionMiddleware
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        MetadataDbContext db,
        IMemoryCache cache,
        ISubscriptionService subscriptionService)
    {
        if (context.Items.ContainsKey("TenantId"))
        {
            await _next(context);
            return;
        }

        var host = (context.Request.Host.Host ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host))
        {
            await _next(context);
            return;
        }

        var cacheKey = $"domain:{host}";
        if (!cache.TryGetValue(cacheKey, out DomainResolution? resolution) || resolution is null)
        {
            resolution = await ResolveDomainAsync(host, db, context.RequestAborted);
            cache.Set(cacheKey, resolution, CacheTtl);
        }

        if (resolution.TenantId.HasValue)
        {
            if (resolution.IsCustomDomain)
            {
                var hasFeature = await subscriptionService.HasFeature(
                    resolution.TenantId.Value,
                    "custom_domain",
                    context.RequestAborted);

                if (!hasFeature)
                {
                    var slug = await db.Tenants
                        .Where(t => t.TenantId == resolution.TenantId.Value)
                        .Select(t => t.TenantSlug)
                        .FirstOrDefaultAsync(context.RequestAborted);

                    if (!string.IsNullOrWhiteSpace(slug))
                    {
                        var destination = $"https://{slug}.regos.app{context.Request.Path}{context.Request.QueryString}";
                        context.Response.Redirect(destination, permanent: false);
                        return;
                    }
                }
            }

            context.Items["TenantId"] = resolution.TenantId.Value;
        }

        await _next(context);
    }

    private static async Task<DomainResolution> ResolveDomainAsync(
        string host,
        MetadataDbContext db,
        CancellationToken ct)
    {
        // Strategy 1: explicit custom-domain mapping.
        var direct = await db.Tenants
            .Where(t => t.CustomDomain != null && t.CustomDomain.ToLower() == host)
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => t.TenantId)
            .FirstOrDefaultAsync(ct);

        if (direct != Guid.Empty)
        {
            return new DomainResolution(direct, true);
        }

        // Strategy 2: slug-based subdomain mapping (<slug>.regos.app).
        const string suffix = ".regos.app";
        if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            var slug = host[..^suffix.Length].Trim();
            if (!string.IsNullOrWhiteSpace(slug) && !slug.Contains('.'))
            {
                var subdomain = await db.Tenants
                    .Where(t => t.TenantSlug == slug)
                    .Where(t => t.Status == TenantStatus.Active)
                    .Select(t => t.TenantId)
                    .FirstOrDefaultAsync(ct);

                if (subdomain != Guid.Empty)
                {
                    return new DomainResolution(subdomain, false);
                }
            }
        }

        return new DomainResolution(null, false);
    }

    private sealed record DomainResolution(Guid? TenantId, bool IsCustomDomain);
}

public static class TenantResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TenantResolutionMiddleware>();
    }
}
