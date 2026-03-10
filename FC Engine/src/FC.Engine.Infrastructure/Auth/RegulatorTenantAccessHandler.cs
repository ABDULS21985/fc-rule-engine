using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;

namespace FC.Engine.Infrastructure.Auth;

public sealed class RegulatorTenantAccessRequirement : IAuthorizationRequirement;

public sealed class RegulatorTenantAccessHandler : AuthorizationHandler<RegulatorTenantAccessRequirement>
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantAccessContextResolver _tenantAccessContextResolver;
    private readonly IPlatformRegulatorTenantResolver _platformRegulatorTenantResolver;

    public RegulatorTenantAccessHandler(
        ITenantContext tenantContext,
        ITenantAccessContextResolver tenantAccessContextResolver,
        IPlatformRegulatorTenantResolver platformRegulatorTenantResolver)
    {
        _tenantContext = tenantContext;
        _tenantAccessContextResolver = tenantAccessContextResolver;
        _platformRegulatorTenantResolver = platformRegulatorTenantResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RegulatorTenantAccessRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var tenantId = ResolveTenantId(context.User) ?? _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue && IsPlatformAdmin(context.User))
        {
            tenantId = (await _platformRegulatorTenantResolver.TryResolveAsync())?.TenantId;
        }

        if (!tenantId.HasValue)
        {
            return;
        }

        var accessContext = await _tenantAccessContextResolver.TryResolveAsync(tenantId.Value, context.User);
        if (accessContext?.TenantType == TenantType.Regulator)
        {
            context.Succeed(requirement);
        }
    }

    private static Guid? ResolveTenantId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst("TenantId")?.Value
            ?? principal.FindFirst("tid")?.Value;

        return Guid.TryParse(raw, out var tenantId) ? tenantId : null;
    }

    private static bool IsPlatformAdmin(ClaimsPrincipal principal)
    {
        return principal.IsInRole("PlatformAdmin")
            || principal.HasClaim("IsPlatformAdmin", "true");
    }
}
