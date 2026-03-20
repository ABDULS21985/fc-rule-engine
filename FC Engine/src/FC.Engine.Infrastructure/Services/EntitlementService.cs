using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class EntitlementService : IEntitlementService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyList<string> DefaultFeatures = new[] { "xml_submission", "validation", "reporting" };

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<EntitlementService> _logger;

    public EntitlementService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        IMemoryCache cache,
        ILogger<EntitlementService> logger)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"entitlement:{tenantId}";
        if (_cache.TryGetValue(cacheKey, out TenantEntitlement? cached) && cached is not null)
        {
            return cached;
        }

        await using var db = _dbFactory.CreateDbContext();

        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        var tenantLicences = await db.TenantLicenceTypes
            .AsNoTracking()
            .Where(tlt => tlt.TenantId == tenantId && tlt.IsActive)
            .Include(tlt => tlt.LicenceType)
            .ToListAsync(ct);

        var licenceTypeIds = tenantLicences.Select(tlt => tlt.LicenceTypeId).ToList();
        var licenceCodes = tenantLicences
            .Where(tlt => tlt.LicenceType is not null)
            .Select(tlt => tlt.LicenceType!.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();

        var matrixEntries = await db.LicenceModuleMatrix
            .AsNoTracking()
            .Where(lmm => licenceTypeIds.Contains(lmm.LicenceTypeId))
            .Include(lmm => lmm.Module)
            .ThenInclude(m => m!.Jurisdiction)
            .Where(lmm => lmm.Module != null && lmm.Module.IsActive)
            .ToListAsync(ct);

        var tenantJurisdictionIds = await db.Institutions
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .Select(i => i.JurisdictionId)
            .Distinct()
            .ToListAsync(ct);

        if (tenantJurisdictionIds.Count == 0)
        {
            var nigeriaId = await db.Jurisdictions
                .AsNoTracking()
                .Where(j => j.CountryCode == "NG")
                .Select(j => j.Id)
                .FirstOrDefaultAsync(ct);
            if (nigeriaId > 0)
            {
                tenantJurisdictionIds.Add(nigeriaId);
            }
        }

        matrixEntries = matrixEntries
            .Where(lmm => lmm.Module is not null
                       && (!lmm.Module.JurisdictionId.HasValue
                           || tenantJurisdictionIds.Contains(lmm.Module.JurisdictionId.Value)))
            .ToList();

        var eligibleModules = matrixEntries
            .GroupBy(lmm => lmm.ModuleId)
            .Select(g =>
            {
                var first = g.First();
                return new EntitledModule
                {
                    ModuleId = first.ModuleId,
                    JurisdictionId = first.Module!.JurisdictionId,
                    JurisdictionCode = first.Module.Jurisdiction?.CountryCode,
                    ModuleCode = first.Module!.ModuleCode,
                    ModuleName = first.Module.ModuleName,
                    RegulatorCode = first.Module.RegulatorCode,
                    IsRequired = g.Any(x => x.IsRequired),
                    IsActive = false,
                    SheetCount = first.Module.SheetCount,
                    DefaultFrequency = first.Module.DefaultFrequency
                };
            })
            .OrderBy(m => m.ModuleCode)
            .ToList();

        var subscriptionResolution = await ResolveActiveModules(db, tenantId, eligibleModules, ct);

        var entitlement = new TenantEntitlement
        {
            TenantId = tenantId,
            TenantStatus = tenant.Status,
            LicenceTypeCodes = licenceCodes.AsReadOnly(),
            EligibleModules = eligibleModules.AsReadOnly(),
            ActiveModules = subscriptionResolution.ActiveModules.AsReadOnly(),
            Features = subscriptionResolution.Features,
            PlanCode = subscriptionResolution.PlanCode,
            ResolvedAt = DateTime.UtcNow
        };

        _cache.Set(cacheKey, entitlement, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTtl
        });

        _logger.LogDebug(
            "Resolved entitlements for tenant {TenantId}: {EligibleCount} eligible, {ActiveCount} active",
            tenantId,
            entitlement.EligibleModules.Count,
            entitlement.ActiveModules.Count);

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
        return entitlement.Features.Contains(featureCode, StringComparer.OrdinalIgnoreCase)
            || entitlement.Features.Contains("all_features", StringComparer.OrdinalIgnoreCase);
    }

    public Task InvalidateCache(Guid tenantId)
    {
        _cache.Remove($"entitlement:{tenantId}");
        _logger.LogInformation("Invalidated entitlement cache for tenant {TenantId}", tenantId);
        return Task.CompletedTask;
    }

    private async Task<SubscriptionResolution> ResolveActiveModules(
        MetadataDbContext db,
        Guid tenantId,
        IReadOnlyList<EntitledModule> eligibleModules,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId)
            .Where(s => SubscriptionStatusRules.EntitlementEligibleStatuses.Contains(s.Status))
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            _logger.LogWarning("Tenant {TenantId} has no active subscription", tenantId);
            return new SubscriptionResolution(
                "NONE",
                Array.Empty<string>(),
                new List<EntitledModule>());
        }

        var subscribedModuleIds = await db.SubscriptionModules
            .AsNoTracking()
            .Where(sm => sm.SubscriptionId == subscription.Id && sm.IsActive)
            .Select(sm => sm.ModuleId)
            .ToListAsync(ct);

        var activeModules = eligibleModules
            .Where(e => subscribedModuleIds.Contains(e.ModuleId))
            .Select(ToActive)
            .ToList();

        var features = subscription.Plan?.GetFeatures();
        if (features is null || features.Count == 0)
        {
            features = DefaultFeatures.ToList();
        }

        return new SubscriptionResolution(
            subscription.Plan?.PlanCode ?? "DEFAULT",
            features.AsReadOnly(),
            activeModules);
    }

    private static EntitledModule ToActive(EntitledModule module)
    {
        return new EntitledModule
        {
            ModuleId = module.ModuleId,
            JurisdictionId = module.JurisdictionId,
            JurisdictionCode = module.JurisdictionCode,
            ModuleCode = module.ModuleCode,
            ModuleName = module.ModuleName,
            RegulatorCode = module.RegulatorCode,
            IsRequired = module.IsRequired,
            IsActive = true,
            SheetCount = module.SheetCount,
            DefaultFrequency = module.DefaultFrequency
        };
    }

    private sealed record SubscriptionResolution(
        string PlanCode,
        IReadOnlyList<string> Features,
        List<EntitledModule> ActiveModules);
}
