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
        => EnsureIncludedModulesInternalAsync(null, null, ct);

    public Task<SubscriptionModuleEntitlementBootstrapResult> EnsureIncludedModulesForSubscriptionAsync(
        int subscriptionId,
        CancellationToken ct = default)
        => EnsureIncludedModulesInternalAsync(subscriptionId, null, ct);

    public Task<SubscriptionModuleEntitlementBootstrapResult> EnsureIncludedModulesForTenantAsync(
        Guid tenantId,
        CancellationToken ct = default)
        => EnsureIncludedModulesInternalAsync(null, tenantId, ct);

    private async Task<SubscriptionModuleEntitlementBootstrapResult> EnsureIncludedModulesInternalAsync(
        int? subscriptionId,
        Guid? tenantId,
        CancellationToken ct)
    {
        var subscriptionQuery = _db.Subscriptions
            .Where(subscription =>
                subscription.Status == SubscriptionStatus.Trial
                || subscription.Status == SubscriptionStatus.Active
                || subscription.Status == SubscriptionStatus.PastDue
                || subscription.Status == SubscriptionStatus.Suspended);

        if (subscriptionId.HasValue)
        {
            subscriptionQuery = subscriptionQuery.Where(x => x.Id == subscriptionId.Value);
        }

        if (tenantId.HasValue)
        {
            subscriptionQuery = subscriptionQuery.Where(x => x.TenantId == tenantId.Value);
        }

        var subscriptions = await subscriptionQuery
            .Select(x => new { x.Id, x.TenantId })
            .ToListAsync(ct);

        if (subscriptions.Count == 0)
        {
            return new SubscriptionModuleEntitlementBootstrapResult();
        }

        var subscriptionIds = subscriptions.Select(x => x.Id).ToList();
        var tenantIdsBySubscription = subscriptions.ToDictionary(x => x.Id, x => x.TenantId);

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
                pricing.PriceAnnual,
                true);

        if (subscriptionId.HasValue)
        {
            candidateQuery = candidateQuery.Where(x => x.SubscriptionId == subscriptionId.Value);
        }

        if (tenantId.HasValue)
        {
            candidateQuery = candidateQuery.Where(x => x.TenantId == tenantId.Value);
        }

        var candidates = (await candidateQuery
                .AsNoTracking()
                .ToListAsync(ct))
            .GroupBy(x => new { x.SubscriptionId, x.ModuleId })
            .Select(g => g.First())
            .ToList();

        var existingRows = await _db.SubscriptionModules
            .Where(x => subscriptionIds.Contains(x.SubscriptionId))
            .ToListAsync(ct);

        var existingByKey = existingRows.ToDictionary(x => (x.SubscriptionId, x.ModuleId));
        var eligibleModuleQuery =
            from subscription in _db.Subscriptions
            join tenantLicence in _db.TenantLicenceTypes on subscription.TenantId equals tenantLicence.TenantId
            join licenceModule in _db.LicenceModuleMatrix on tenantLicence.LicenceTypeId equals licenceModule.LicenceTypeId
            join pricing in _db.PlanModulePricing on new { subscription.PlanId, licenceModule.ModuleId }
                equals new { pricing.PlanId, pricing.ModuleId }
            join module in _db.Modules on licenceModule.ModuleId equals module.Id
            where subscriptionIds.Contains(subscription.Id)
                  && tenantLicence.IsActive
                  && module.IsActive
            select new IncludedModuleCandidate(
                subscription.Id,
                subscription.TenantId,
                licenceModule.ModuleId,
                pricing.PriceMonthly,
                pricing.PriceAnnual,
                pricing.IsIncludedInBase);

        var eligibleCandidates = (await eligibleModuleQuery
                .AsNoTracking()
                .ToListAsync(ct))
            .GroupBy(x => new { x.SubscriptionId, x.ModuleId })
            .Select(g => g.First())
            .ToList();

        var eligibleModuleIdsBySubscription = eligibleCandidates
            .GroupBy(x => x.SubscriptionId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ModuleId).ToHashSet());

        var eligiblePricingByKey = eligibleCandidates.ToDictionary(
            x => (x.SubscriptionId, x.ModuleId),
            x => x);

        var created = 0;
        var reactivated = 0;
        var updated = 0;
        var deactivated = 0;
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

        foreach (var row in existingRows.Where(x => x.IsActive))
        {
            var isStillEligible = eligibleModuleIdsBySubscription.TryGetValue(row.SubscriptionId, out var eligibleModuleIds)
                                  && eligibleModuleIds.Contains(row.ModuleId);

            if (!isStillEligible)
            {
                row.Deactivate();
                deactivated++;
                touchedTenantIds.Add(tenantIdsBySubscription[row.SubscriptionId]);
                continue;
            }

            if (!eligiblePricingByKey.TryGetValue((row.SubscriptionId, row.ModuleId), out var eligibleCandidate))
            {
                continue;
            }

            var changed = false;
            if (row.PriceMonthly != eligibleCandidate.PriceMonthly)
            {
                row.PriceMonthly = eligibleCandidate.PriceMonthly;
                changed = true;
            }

            if (row.PriceAnnual != eligibleCandidate.PriceAnnual)
            {
                row.PriceAnnual = eligibleCandidate.PriceAnnual;
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
                touchedTenantIds.Add(tenantIdsBySubscription[row.SubscriptionId]);
            }
        }

        if (created == 0 && reactivated == 0 && updated == 0 && deactivated == 0)
        {
            return new SubscriptionModuleEntitlementBootstrapResult();
        }

        await _db.SaveChangesAsync(ct);

        foreach (var touchedTenantId in touchedTenantIds)
        {
            await _entitlementService.InvalidateCache(touchedTenantId);
        }

        _logger.LogInformation(
            "Subscription module entitlement bootstrap completed. Created={Created} Reactivated={Reactivated} Updated={Updated} Deactivated={Deactivated} TenantsTouched={TenantsTouched}",
            created,
            reactivated,
            updated,
            deactivated,
            touchedTenantIds.Count);

        return new SubscriptionModuleEntitlementBootstrapResult
        {
            ModulesCreated = created,
            ModulesReactivated = reactivated,
            ModulesUpdated = updated,
            ModulesDeactivated = deactivated,
            TenantsTouched = touchedTenantIds.Count
        };
    }

    private sealed record IncludedModuleCandidate(
        int SubscriptionId,
        Guid TenantId,
        int ModuleId,
        decimal PriceMonthly,
        decimal PriceAnnual,
        bool IsIncludedInBase);
}

public sealed class SubscriptionModuleEntitlementBootstrapResult
{
    public int ModulesCreated { get; init; }
    public int ModulesReactivated { get; init; }
    public int ModulesUpdated { get; init; }
    public int ModulesDeactivated { get; init; }
    public int TenantsTouched { get; init; }
}
