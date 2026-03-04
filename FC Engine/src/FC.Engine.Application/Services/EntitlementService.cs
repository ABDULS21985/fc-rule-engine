using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Application.Services;

public class EntitlementService : IEntitlementService
{
    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EntitlementService> _logger;
    private static readonly TimeSpan CacheTTL = TimeSpan.FromMinutes(5);

    public EntitlementService(
        MetadataDbContext db,
        IMemoryCache cache,
        ILogger<EntitlementService> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"entitlement:{tenantId}";

        if (_cache.TryGetValue(cacheKey, out TenantEntitlement? cached) && cached is not null)
            return cached;

        // 1. Load tenant
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        // 2. Load active licence types for this tenant
        var tenantLicences = await _db.TenantLicenceTypes
            .Where(tlt => tlt.TenantId == tenantId && tlt.IsActive)
            .Include(tlt => tlt.LicenceType)
            .ToListAsync(ct);

        var licenceTypeIds = tenantLicences.Select(tlt => tlt.LicenceTypeId).ToList();
        var licenceCodes = tenantLicences
            .Where(tlt => tlt.LicenceType != null)
            .Select(tlt => tlt.LicenceType!.Code)
            .ToList();

        // 3. Load eligible modules via licence-module matrix
        var matrixEntries = await _db.LicenceModuleMatrix
            .Where(lmm => licenceTypeIds.Contains(lmm.LicenceTypeId))
            .Include(lmm => lmm.Module)
            .Where(lmm => lmm.Module!.IsActive)
            .ToListAsync(ct);

        // Deduplicate by ModuleId (a module may appear in multiple licence types)
        var eligibleModules = matrixEntries
            .GroupBy(lmm => lmm.ModuleId)
            .Select(g =>
            {
                var first = g.First();
                return new EntitledModule
                {
                    ModuleId = first.ModuleId,
                    ModuleCode = first.Module!.ModuleCode,
                    ModuleName = first.Module.ModuleName,
                    RegulatorCode = first.Module.RegulatorCode,
                    IsRequired = g.Any(lmm => lmm.IsRequired),
                    IsActive = true, // Pre-RG-03: all eligible modules are active
                    SheetCount = first.Module.SheetCount,
                    DefaultFrequency = first.Module.DefaultFrequency
                };
            })
            .OrderBy(m => m.ModuleCode)
            .ToList();

        // 4. Pre-RG-03: all eligible = active (no subscription filtering yet)
        var activeModules = eligibleModules;

        // 5. Build entitlement result
        var entitlement = new TenantEntitlement
        {
            TenantId = tenantId,
            TenantStatus = tenant.Status,
            LicenceTypeCodes = licenceCodes.AsReadOnly(),
            EligibleModules = eligibleModules.AsReadOnly(),
            ActiveModules = activeModules.AsReadOnly(),
            Features = GetDefaultFeatures(),
            PlanCode = "DEFAULT",
            ResolvedAt = DateTime.UtcNow
        };

        _cache.Set(cacheKey, entitlement, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTTL
        });

        _logger.LogDebug("Resolved entitlements for tenant {TenantId}: {ModuleCount} active modules",
            tenantId, activeModules.Count);

        return entitlement;
    }

    public async Task<bool> HasModuleAccess(Guid tenantId, string moduleCode, CancellationToken ct = default)
    {
        var entitlement = await ResolveEntitlements(tenantId, ct);
        return entitlement.ActiveModules.Any(m =>
            string.Equals(m.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        var entitlement = await ResolveEntitlements(tenantId, ct);
        return entitlement.Features.Contains(featureCode, StringComparer.OrdinalIgnoreCase);
    }

    public Task InvalidateCache(Guid tenantId)
    {
        _cache.Remove($"entitlement:{tenantId}");
        _logger.LogInformation("Invalidated entitlement cache for tenant {TenantId}", tenantId);
        return Task.CompletedTask;
    }

    private static IReadOnlyList<string> GetDefaultFeatures()
    {
        // Default features available to all tenants pre-RG-03
        return new[] { "xml_submission", "validation", "reporting" };
    }
}
