using System.Security.Claims;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace FC.Engine.Admin.Services;

public sealed record RegulatorSessionContext(
    Guid TenantId,
    int UserId,
    string RegulatorCode,
    int RegulatorId,
    string TenantName);

public sealed class RegulatorSessionService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantAccessContextResolver _tenantAccessContextResolver;
    private readonly IPlatformRegulatorTenantResolver _platformRegulatorTenantResolver;
    private RegulatorSessionContext? _cached;

    public RegulatorSessionService(
        AuthenticationStateProvider authStateProvider,
        ITenantContext tenantContext,
        ITenantAccessContextResolver tenantAccessContextResolver,
        IPlatformRegulatorTenantResolver platformRegulatorTenantResolver)
    {
        _authStateProvider = authStateProvider;
        _tenantContext = tenantContext;
        _tenantAccessContextResolver = tenantAccessContextResolver;
        _platformRegulatorTenantResolver = platformRegulatorTenantResolver;
    }

    public async Task<RegulatorSessionContext> GetRequiredAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            throw new InvalidOperationException("An authenticated regulator session is required.");
        }

        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            throw new InvalidOperationException("Regulator user identifier is unavailable for this session.");
        }

        var tenantId = ResolveTenantId(principal) ?? _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue && IsPlatformAdmin(principal))
        {
            tenantId = (await _platformRegulatorTenantResolver.TryResolveAsync(ct))?.TenantId;
        }

        if (!tenantId.HasValue)
        {
            throw new InvalidOperationException("Regulator tenant context is unavailable for this session.");
        }

        var accessContext = await _tenantAccessContextResolver.TryResolveAsync(tenantId.Value, principal, ct);
        if (accessContext is null)
        {
            throw new InvalidOperationException("Unable to resolve tenant metadata for this session.");
        }

        if (accessContext.TenantType != TenantType.Regulator)
        {
            throw new InvalidOperationException("The current session does not belong to a regulator tenant.");
        }

        if (string.IsNullOrWhiteSpace(accessContext.RegulatorCode))
        {
            throw new InvalidOperationException("Regulator code claim is missing.");
        }

        if (!accessContext.RegulatorId.HasValue)
        {
            throw new InvalidOperationException("Regulator numeric context is unavailable for this session.");
        }

        _cached = new RegulatorSessionContext(
            accessContext.TenantId,
            userId,
            accessContext.RegulatorCode,
            accessContext.RegulatorId.Value,
            accessContext.TenantName);

        return _cached;
    }

    private Guid? ResolveTenantId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirst("TenantId")?.Value
            ?? principal.FindFirst("tid")?.Value;

        if (Guid.TryParse(raw, out var tenantId))
        {
            return tenantId;
        }

        return _tenantContext.CurrentTenantId;
    }

    private static bool IsPlatformAdmin(ClaimsPrincipal principal)
    {
        return principal.IsInRole("PlatformAdmin")
            || principal.HasClaim("IsPlatformAdmin", "true");
    }
}
