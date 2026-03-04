using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using Microsoft.AspNetCore.Http;

namespace FC.Engine.Infrastructure.MultiTenancy;

/// <summary>
/// Resolves tenant context from the current HTTP request.
/// TenantId is stored in HttpContext.Items by TenantContextMiddleware
/// or resolved from user claims.
/// </summary>
public class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid? CurrentTenantId
    {
        get
        {
            var httpContext = _accessor.HttpContext;
            if (httpContext == null) return null;

            // Check impersonation first (set by PlatformAdmin)
            if (httpContext.Items.TryGetValue("ImpersonatingTenantId", out var impersonated)
                && impersonated is Guid impersonatedId)
            {
                return impersonatedId;
            }

            // Check middleware-resolved tenant
            if (httpContext.Items.TryGetValue("TenantId", out var tenantObj)
                && tenantObj is Guid tenantId)
            {
                return tenantId;
            }

            // Fallback: resolve from claims
            var claim = httpContext.User?.FindFirst("TenantId");
            if (claim != null && Guid.TryParse(claim.Value, out var claimTenantId))
            {
                return claimTenantId;
            }

            return null;
        }
    }

    public bool IsPlatformAdmin
    {
        get
        {
            var httpContext = _accessor.HttpContext;
            if (httpContext == null) return false;

            return httpContext.User?.IsInRole("PlatformAdmin") == true
                || httpContext.User?.HasClaim("IsPlatformAdmin", "true") == true;
        }
    }

    public Guid? ImpersonatingTenantId
    {
        get
        {
            var httpContext = _accessor.HttpContext;
            if (httpContext == null) return null;

            if (httpContext.Items.TryGetValue("ImpersonatingTenantId", out var val)
                && val is Guid id)
            {
                return id;
            }

            return null;
        }
    }
}
