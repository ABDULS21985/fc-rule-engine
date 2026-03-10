using System.Security.Claims;
using FC.Engine.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;

namespace FC.Engine.Infrastructure.Auth;

public sealed class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly ITenantAccessContextResolver _tenantAccessContextResolver;

    public TenantClaimsTransformation(ITenantAccessContextResolver tenantAccessContextResolver)
    {
        _tenantAccessContextResolver = tenantAccessContextResolver;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (!TryGetWritableIdentity(principal, out var identity)
            || !TryGetTenantId(principal, out var tenantId))
        {
            return principal;
        }

        var context = await _tenantAccessContextResolver.TryResolveAsync(tenantId, principal);
        if (context is null)
        {
            return principal;
        }

        SetClaim(identity, "TenantType", context.TenantType.ToString());
        SetClaim(identity, "TenantSlug", context.TenantSlug);

        if (!string.IsNullOrWhiteSpace(context.RegulatorCode))
        {
            SetClaim(identity, "RegulatorCode", context.RegulatorCode);
        }
        else
        {
            RemoveClaims(identity, "RegulatorCode");
        }

        if (context.RegulatorId.HasValue)
        {
            SetClaim(identity, "RegulatorId", context.RegulatorId.Value.ToString());
        }
        else
        {
            RemoveClaims(identity, "RegulatorId");
        }

        return principal;
    }

    private static bool TryGetTenantId(ClaimsPrincipal principal, out Guid tenantId)
    {
        var raw = principal.FindFirst("TenantId")?.Value
            ?? principal.FindFirst("tid")?.Value;

        return Guid.TryParse(raw, out tenantId);
    }

    private static bool TryGetWritableIdentity(
        ClaimsPrincipal principal,
        out ClaimsIdentity identity)
    {
        identity = principal.Identities
            .OfType<ClaimsIdentity>()
            .FirstOrDefault(x => x.IsAuthenticated)
            ?? new ClaimsIdentity();

        return identity.IsAuthenticated;
    }

    private static void SetClaim(ClaimsIdentity identity, string type, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            RemoveClaims(identity, type);
            return;
        }

        var current = identity.FindFirst(type)?.Value;
        if (string.Equals(current, value, StringComparison.Ordinal))
        {
            return;
        }

        RemoveClaims(identity, type);
        identity.AddClaim(new Claim(type, value));
    }

    private static void RemoveClaims(ClaimsIdentity identity, string type)
    {
        foreach (var claim in identity.FindAll(type).ToList())
        {
            identity.RemoveClaim(claim);
        }
    }
}
