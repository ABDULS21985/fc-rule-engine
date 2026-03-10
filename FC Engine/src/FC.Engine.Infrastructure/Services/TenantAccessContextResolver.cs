using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed record TenantAccessContext(
    Guid TenantId,
    TenantType TenantType,
    string TenantName,
    string TenantSlug,
    string? RegulatorCode,
    int? RegulatorId);

public interface ITenantAccessContextResolver
{
    Task<TenantAccessContext?> TryResolveAsync(
        Guid tenantId,
        ClaimsPrincipal? principal = null,
        CancellationToken ct = default);
}

public sealed class TenantAccessContextResolver : ITenantAccessContextResolver
{
    private readonly MetadataDbContext _db;

    public TenantAccessContextResolver(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<TenantAccessContext?> TryResolveAsync(
        Guid tenantId,
        ClaimsPrincipal? principal = null,
        CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => new
            {
                x.TenantId,
                x.TenantType,
                x.TenantName,
                x.TenantSlug
            })
            .FirstOrDefaultAsync(ct);

        if (tenant is null)
        {
            return null;
        }

        var regulatorCode = ResolveRegulatorCode(principal, tenant.TenantType, tenant.TenantSlug);
        var regulatorId = ResolveRegulatorId(principal, tenant.TenantType, tenant.TenantId);

        return new TenantAccessContext(
            tenant.TenantId,
            tenant.TenantType,
            tenant.TenantName,
            tenant.TenantSlug,
            regulatorCode,
            regulatorId);
    }

    private static string? ResolveRegulatorCode(
        ClaimsPrincipal? principal,
        TenantType tenantType,
        string tenantSlug)
    {
        if (tenantType != TenantType.Regulator)
        {
            return null;
        }

        var claimValue = principal?.FindFirst("RegulatorCode")?.Value;
        if (!string.IsNullOrWhiteSpace(claimValue))
        {
            return claimValue.Trim().ToUpperInvariant();
        }

        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            return null;
        }

        return tenantSlug.Trim().ToUpperInvariant();
    }

    private static int? ResolveRegulatorId(
        ClaimsPrincipal? principal,
        TenantType tenantType,
        Guid tenantId)
    {
        if (tenantType != TenantType.Regulator)
        {
            return null;
        }

        var claimValue = principal?.FindFirst("RegulatorId")?.Value;
        if (int.TryParse(claimValue, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return ComputeStableRegulatorId(tenantId);
    }

    internal static int ComputeStableRegulatorId(Guid tenantId)
    {
        var hash = SHA256.HashData(tenantId.ToByteArray());
        var value = BinaryPrimitives.ReadInt32LittleEndian(hash.AsSpan(0, sizeof(int)));
        if (value == int.MinValue)
        {
            return int.MaxValue;
        }

        value = Math.Abs(value);
        return value == 0 ? 1 : value;
    }
}
