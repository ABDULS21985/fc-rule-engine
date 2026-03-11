using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class SubscriptionModuleEntitlementBootstrapService
{
    private readonly MetadataDbContext _db;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<SubscriptionModuleEntitlementBootstrapService> _logger;

    public SubscriptionModuleEntitlementBootstrapService(
        MetadataDbContext db,
        IEntitlementService entitlementService,
        ILogger<SubscriptionModuleEntitlementBootstrapService> logger)
    {
        _db = db;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    public Task<SubscriptionModuleEntitlementBootstrapResult> EnsureIncludedModulesAsync(CancellationToken ct = default)
        => EnsureIncludedModulesInternalAsync(null, ct);

    public Task<SubscriptionModuleEntitlementBootstrapResult> EnsureIncludedModulesForSubscriptionAsync(
        int subscriptionId,
        CancellationToken ct = default)
        => EnsureIncludedModulesInternalAsync(subscriptionId, ct);

    private async Task<SubscriptionModuleEntitlementBootstrapResult> EnsureIncludedModulesInternalAsync(
        int? subscriptionId,
        CancellationToken ct)
    {
        var candidateQuery =
            from subscription in _db.Subscriptions
            join tenantLicence in _db.TenantLicenceTypes on subscription.TenantId equals tenantLicence.TenantId
            join licenceModule in _db.LicenceModuleMatrix on tenantLicence.LicenceTypeId equals licenceModule.LicenceTypeId
            join pricing in _db.PlanModulePricing on new { subscription.PlanId, licenceModule.ModuleId }
                equals new { pricing.PlanId, pricing.ModuleId }
            join module in _db.Modules on licenceModule.ModuleId equals module.Id
            where (subscription.Status == SubscriptionStatus.Trial
                   || subscription.Status == SubscriptionStatus.Active
                   || subscription.Status == SubscriptionStatus.PastDue
                   || subscription.Status == SubscriptionStatus.Suspended)
                  && tenantLicence.IsActive
                  && pricing.IsIncludedInBase
                  && module.IsActive
            select new IncludedModuleCandidate(
                subscription.Id,
                subscription.TenantId,
                licenceModule.ModuleId,
                pricing.PriceMonthly,
                pricing.PriceAnnual);

        if (subscriptionId.HasValue)
        {
            candidateQuery = candidateQuery.Where(x => x.SubscriptionId == subscriptionId.Value);
        }

        var candidates = (await candidateQuery
                .AsNoTracking()
                .ToListAsync(ct))
            .GroupBy(x => new { x.SubscriptionId, x.ModuleId })
            .Select(g => g.First())
            .ToList();

        if (candidates.Count == 0)
        {
            return new SubscriptionModuleEntitlementBootstrapResult();
        }

        var subscriptionIds = candidates
            .Select(x => x.SubscriptionId)
            .Distinct()
            .ToList();

        var existingRows = await _db.SubscriptionModules
            .Where(x => subscriptionIds.Contains(x.SubscriptionId))
            .ToListAsync(ct);

        var existingByKey = existingRows.ToDictionary(x => (x.SubscriptionId, x.ModuleId));
        var created = 0;
        var reactivated = 0;
        var updated = 0;
        var touchedTenantIds = new HashSet<Guid>();

        foreach (var candidate in candidates)
        {
            if (!existingByKey.TryGetValue((candidate.SubscriptionId, candidate.ModuleId), out var row))
            {
                row = new SubscriptionModule
                {
                    SubscriptionId = candidate.SubscriptionId,
                    ModuleId = candidate.ModuleId,
                    PriceMonthly = candidate.PriceMonthly,
                    PriceAnnual = candidate.PriceAnnual,
                    IsActive = true
                };

                _db.SubscriptionModules.Add(row);
                existingByKey[(candidate.SubscriptionId, candidate.ModuleId)] = row;
                created++;
                touchedTenantIds.Add(candidate.TenantId);
                continue;
            }

            if (!row.IsActive)
            {
                row.Reactivate(candidate.PriceMonthly, candidate.PriceAnnual);
                reactivated++;
                touchedTenantIds.Add(candidate.TenantId);
                continue;
            }

            var changed = false;
            if (row.PriceMonthly != candidate.PriceMonthly)
            {
                row.PriceMonthly = candidate.PriceMonthly;
                changed = true;
            }

            if (row.PriceAnnual != candidate.PriceAnnual)
            {
                row.PriceAnnual = candidate.PriceAnnual;
                changed = true;
            }

            if (row.DeactivatedAt is not null)
            {
                row.DeactivatedAt = null;
                changed = true;
            }

            if (changed)
            {
                updated++;
                touchedTenantIds.Add(candidate.TenantId);
            }
        }

        if (created == 0 && reactivated == 0 && updated == 0)
        {
            return new SubscriptionModuleEntitlementBootstrapResult();
        }

        await _db.SaveChangesAsync(ct);

        foreach (var tenantId in touchedTenantIds)
        {
            await _entitlementService.InvalidateCache(tenantId);
        }

        _logger.LogInformation(
            "Subscription module entitlement bootstrap completed. Created={Created} Reactivated={Reactivated} Updated={Updated} TenantsTouched={TenantsTouched}",
            created,
            reactivated,
            updated,
            touchedTenantIds.Count);

        return new SubscriptionModuleEntitlementBootstrapResult
        {
            ModulesCreated = created,
            ModulesReactivated = reactivated,
            ModulesUpdated = updated,
            TenantsTouched = touchedTenantIds.Count
        };
    }

    private sealed record IncludedModuleCandidate(
        int SubscriptionId,
        Guid TenantId,
        int ModuleId,
        decimal PriceMonthly,
        decimal PriceAnnual);
}

public sealed class SubscriptionModuleEntitlementBootstrapResult
{
    public int ModulesCreated { get; init; }
    public int ModulesReactivated { get; init; }
    public int ModulesUpdated { get; init; }
    public int TenantsTouched { get; init; }
}
