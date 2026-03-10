using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed record PlatformRegulatorTenantContext(
    Guid TenantId,
    string TenantName,
    string RegulatorCode);

public interface IPlatformRegulatorTenantResolver
{
    Task<PlatformRegulatorTenantContext?> TryResolveAsync(CancellationToken ct = default);
}

public sealed class PlatformRegulatorTenantResolver : IPlatformRegulatorTenantResolver
{
    private const string DefaultRegulatorCode = "CBN";
    private const string DefaultRegulatorName = "Central Bank of Nigeria";

    private readonly MetadataDbContext? _db;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformRegulatorTenantResolver> _logger;

    [ActivatorUtilitiesConstructor]
    public PlatformRegulatorTenantResolver(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<PlatformRegulatorTenantResolver> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    internal PlatformRegulatorTenantResolver(
        MetadataDbContext db,
        IConfiguration configuration,
        ILogger<PlatformRegulatorTenantResolver> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PlatformRegulatorTenantContext?> TryResolveAsync(CancellationToken ct = default)
    {
        await using var scope = _scopeFactory?.CreateAsyncScope();
        var db = _db ?? scope?.ServiceProvider.GetRequiredService<MetadataDbContext>()
            ?? throw new InvalidOperationException("Metadata database context is unavailable.");

        var activeRegulators = await db.Tenants
            .AsNoTracking()
            .Where(t => t.TenantType == TenantType.Regulator)
            .Where(t => t.Status == TenantStatus.Active)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new TenantCandidate(
                t.TenantId,
                t.TenantName,
                t.TenantSlug,
                t.Status))
            .ToListAsync(ct);

        if (activeRegulators.Count == 0)
        {
            var bootstrapped = await EnsureDefaultTenantAsync(db, ct);
            return ToContext(bootstrapped);
        }

        if (TryResolveConfiguredTenantId(out var configuredTenantId))
        {
            var byId = activeRegulators.FirstOrDefault(t => t.TenantId == configuredTenantId);
            if (byId is not null)
            {
                return ToContext(byId);
            }
        }

        var configuredCode = ResolveConfiguredRegulatorCode();
        var byCode = activeRegulators.FirstOrDefault(t =>
            string.Equals(t.TenantSlug, configuredCode.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.TenantName, configuredCode, StringComparison.OrdinalIgnoreCase)
            || t.TenantName.Contains(configuredCode, StringComparison.OrdinalIgnoreCase));
        if (byCode is not null)
        {
            return ToContext(byCode);
        }

        return activeRegulators.Count == 1 ? ToContext(activeRegulators[0]) : null;
    }

    private async Task<TenantCandidate> EnsureDefaultTenantAsync(MetadataDbContext db, CancellationToken ct)
    {
        var regulatorCode = ResolveConfiguredRegulatorCode();
        var regulatorSlug = regulatorCode.ToLowerInvariant();
        var regulatorName = (_configuration["RegulatorPortal:DefaultRegulatorName"] ?? DefaultRegulatorName).Trim();
        var contactEmail = (_configuration["RegulatorPortal:DefaultRegulatorEmail"] ?? $"supervision@{regulatorSlug}.regos.local").Trim();

        var existing = await db.Tenants
            .FirstOrDefaultAsync(t => t.TenantType == TenantType.Regulator && t.TenantSlug == regulatorSlug, ct);

        if (existing is not null)
        {
            if (existing.Status == TenantStatus.PendingActivation)
            {
                existing.Activate();
                await db.SaveChangesAsync(ct);
            }

            return new TenantCandidate(existing.TenantId, existing.TenantName, existing.TenantSlug, existing.Status);
        }

        var tenant = Tenant.Create(regulatorName, regulatorSlug, TenantType.Regulator, contactEmail);
        tenant.Activate();

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Bootstrapped default regulator tenant {TenantId} ({RegulatorCode}) because no active regulator tenant existed.",
            tenant.TenantId,
            regulatorCode);

        return new TenantCandidate(tenant.TenantId, tenant.TenantName, tenant.TenantSlug, tenant.Status);
    }

    private string ResolveConfiguredRegulatorCode()
    {
        var configured = _configuration["RegulatorPortal:DefaultRegulatorCode"];
        return string.IsNullOrWhiteSpace(configured)
            ? DefaultRegulatorCode
            : configured.Trim().ToUpperInvariant();
    }

    private bool TryResolveConfiguredTenantId(out Guid tenantId)
    {
        return Guid.TryParse(_configuration["RegulatorPortal:DefaultTenantId"], out tenantId);
    }

    private static PlatformRegulatorTenantContext ToContext(TenantCandidate candidate)
    {
        return new PlatformRegulatorTenantContext(
            candidate.TenantId,
            candidate.TenantName,
            candidate.TenantSlug.ToUpperInvariant());
    }

    private sealed record TenantCandidate(
        Guid TenantId,
        string TenantName,
        string TenantSlug,
        TenantStatus Status);
}
