using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public class TenantManagementService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ITenantOnboardingService _onboardingService;
    private readonly IEntitlementService _entitlementService;
    private readonly SubscriptionModuleEntitlementBootstrapService _subscriptionModuleEntitlementBootstrapService;
    private readonly IAuditLogger _auditLogger;
    private readonly ITenantContext _tenantContext;

    public TenantManagementService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        ITenantOnboardingService onboardingService,
        IEntitlementService entitlementService,
        SubscriptionModuleEntitlementBootstrapService subscriptionModuleEntitlementBootstrapService,
        IAuditLogger auditLogger,
        ITenantContext tenantContext)
    {
        _dbFactory = dbFactory;
        _onboardingService = onboardingService;
        _entitlementService = entitlementService;
        _subscriptionModuleEntitlementBootstrapService = subscriptionModuleEntitlementBootstrapService;
        _auditLogger = auditLogger;
        _tenantContext = tenantContext;
    }

    public async Task<List<Tenant>> GetAllTenantsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tenants
            .OrderBy(t => t.TenantName)
            .ToListAsync(ct);
    }

    public async Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Tenants.FindAsync(new object[] { tenantId }, ct);
    }

    public async Task<TenantDashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenants = await db.Tenants.ToListAsync(ct);
        return new TenantDashboardStats
        {
            TotalTenants = tenants.Count,
            ActiveTenants = tenants.Count(t => t.Status == TenantStatus.Active),
            PendingTenants = tenants.Count(t => t.Status == TenantStatus.PendingActivation),
            SuspendedTenants = tenants.Count(t => t.Status == TenantStatus.Suspended)
        };
    }

    public async Task<TenantOnboardingResult> OnboardTenantAsync(TenantOnboardingRequest request, CancellationToken ct = default)
    {
        return await _onboardingService.OnboardTenant(request, ct);
    }

    public async Task ActivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Activate();
        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantActivated", tenantId, ct);
    }

    public async Task SuspendTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Suspend("Admin action");
        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantSuspended", tenantId, ct);
    }

    public async Task ReactivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Reactivate();
        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantReactivated", tenantId, ct);
    }

    public async Task DeactivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Deactivate();
        await db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantDeactivated", tenantId, ct);
    }

    public async Task<List<TenantLicenceType>> GetTenantLicencesAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await db.TenantLicenceTypes
            .Include(tlt => tlt.LicenceType)
            .Where(tlt => tlt.TenantId == tenantId)
            .OrderByDescending(tlt => tlt.IsActive)
            .ThenBy(tlt => tlt.LicenceType!.DisplayOrder)
            .ThenBy(tlt => tlt.LicenceType!.Code)
            .ToListAsync(ct);
    }

    public Task<TenantLicenceImpactPreview> PreviewAssignLicenceAsync(Guid tenantId, int licenceTypeId, CancellationToken ct = default)
        => BuildLicenceImpactPreviewAsync(tenantId, licenceTypeId, activate: true, ct);

    public Task<TenantLicenceImpactPreview> PreviewRemoveLicenceAsync(Guid tenantId, int licenceTypeId, CancellationToken ct = default)
        => BuildLicenceImpactPreviewAsync(tenantId, licenceTypeId, activate: false, ct);

    public async Task<TenantLicenceChangeResult> AssignLicenceAsync(
        Guid tenantId,
        int licenceTypeId,
        string? registrationNumber = null,
        DateTime? effectiveDate = null,
        CancellationToken ct = default)
    {
        _ = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        var licenceType = await db.LicenceTypes
            .FirstOrDefaultAsync(x => x.Id == licenceTypeId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Licence type not found or inactive");

        var tenantLicence = await db.TenantLicenceTypes
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LicenceTypeId == licenceTypeId, ct);

        var changed = false;
        if (tenantLicence is null)
        {
            tenantLicence = new TenantLicenceType
            {
                TenantId = tenantId,
                LicenceTypeId = licenceTypeId,
                RegistrationNumber = registrationNumber,
                EffectiveDate = effectiveDate ?? DateTime.UtcNow.Date,
                IsActive = true
            };
            db.TenantLicenceTypes.Add(tenantLicence);
            changed = true;
        }
        else
        {
            if (!tenantLicence.IsActive)
            {
                tenantLicence.IsActive = true;
                tenantLicence.ExpiryDate = null;
                changed = true;
            }

            if (!string.Equals(tenantLicence.RegistrationNumber, registrationNumber, StringComparison.Ordinal))
            {
                tenantLicence.RegistrationNumber = registrationNumber;
                changed = true;
            }

            var resolvedEffectiveDate = effectiveDate ?? tenantLicence.EffectiveDate;
            if (tenantLicence.EffectiveDate != resolvedEffectiveDate)
            {
                tenantLicence.EffectiveDate = resolvedEffectiveDate;
                changed = true;
            }
        }

        var reconciliation = new SubscriptionModuleEntitlementBootstrapResult();
        if (changed)
        {
            await db.SaveChangesAsync(ct);
            reconciliation = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
            await _entitlementService.InvalidateCache(tenantId);
            await LogPlatformAction("TenantLicenceAssigned", tenantId, ct);
        }

        tenantLicence.LicenceType = licenceType;
        return new TenantLicenceChangeResult
        {
            TenantLicence = tenantLicence,
            Reconciliation = reconciliation
        };
    }

    public async Task<TenantLicenceChangeResult> RemoveLicenceAsync(Guid tenantId, int licenceTypeId, CancellationToken ct = default)
    {
        var tenantLicence = await db.TenantLicenceTypes
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LicenceTypeId == licenceTypeId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Active tenant licence not found");

        tenantLicence.IsActive = false;
        tenantLicence.ExpiryDate = tenantLicence.ExpiryDate ?? DateTime.UtcNow.Date;
        await db.SaveChangesAsync(ct);

        var reconciliation = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
        await _entitlementService.InvalidateCache(tenantId);
        await LogPlatformAction("TenantLicenceRemoved", tenantId, ct);

        return new TenantLicenceChangeResult
        {
            TenantLicence = tenantLicence,
            Reconciliation = reconciliation
        };
    }

    public async Task<SubscriptionModuleEntitlementBootstrapResult> ReconcileTenantModulesAsync(
        Guid tenantId,
        CancellationToken ct = default)
    {
        _ = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        var reconciliation = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
        await _entitlementService.InvalidateCache(tenantId);
        await LogPlatformAction("TenantModulesReconciled", tenantId, ct);
        return reconciliation;
    }

    public async Task<TenantModuleReconciliationBatchResult> ReconcileTenantModulesAsync(
        IEnumerable<Guid> tenantIds,
        CancellationToken ct = default)
    {
        var requestedTenantIds = tenantIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (requestedTenantIds.Count == 0)
        {
            return new TenantModuleReconciliationBatchResult();
        }

        var existingTenantIds = await db.Tenants
            .AsNoTracking()
            .Where(x => requestedTenantIds.Contains(x.TenantId))
            .Select(x => x.TenantId)
            .ToListAsync(ct);

        var aggregate = new SubscriptionModuleEntitlementBootstrapResult();
        var processed = 0;

        foreach (var tenantId in existingTenantIds)
        {
            var result = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
            await _entitlementService.InvalidateCache(tenantId);
            await LogPlatformAction("TenantModulesReconciled", tenantId, ct);

            aggregate = new SubscriptionModuleEntitlementBootstrapResult
            {
                ModulesCreated = aggregate.ModulesCreated + result.ModulesCreated,
                ModulesReactivated = aggregate.ModulesReactivated + result.ModulesReactivated,
                ModulesUpdated = aggregate.ModulesUpdated + result.ModulesUpdated,
                ModulesDeactivated = aggregate.ModulesDeactivated + result.ModulesDeactivated,
                TenantsTouched = aggregate.TenantsTouched + result.TenantsTouched
            };

            processed++;
        }

        return new TenantModuleReconciliationBatchResult
        {
            RequestedTenants = requestedTenantIds.Count,
            ProcessedTenants = processed,
            Reconciliation = aggregate
        };
    }

    public async Task<List<LicenceType>> GetAllLicenceTypesAsync(CancellationToken ct = default)
    {
        return await db.LicenceTypes
            .Where(lt => lt.IsActive)
            .OrderBy(lt => lt.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<List<Module>> GetAllModulesAsync(CancellationToken ct = default)
    {
        return await db.Modules
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<string?> GetTenantName(Guid tenantId, CancellationToken ct = default)
    {
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TenantName)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<TenantLicenceImpactPreview> BuildLicenceImpactPreviewAsync(
        Guid tenantId,
        int licenceTypeId,
        bool activate,
        CancellationToken ct)
    {
        _ = await db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        var licenceType = await db.LicenceTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == licenceTypeId, ct)
            ?? throw new InvalidOperationException("Licence type not found");

        var subscription = await db.Subscriptions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Where(x => x.Status != SubscriptionStatus.Cancelled && x.Status != SubscriptionStatus.Expired)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Active subscription not found");

        var currentLicenceIds = await db.TenantLicenceTypes
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .Select(x => x.LicenceTypeId)
            .ToListAsync(ct);

        var targetLicenceIds = currentLicenceIds
            .ToHashSet();

        if (activate)
        {
            targetLicenceIds.Add(licenceTypeId);
        }
        else
        {
            targetLicenceIds.Remove(licenceTypeId);
        }

        var pricingRows = await db.PlanModulePricing
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.PlanId == subscription.PlanId && x.Module != null && x.Module.IsActive)
            .ToListAsync(ct);

        var eligibleModuleIds = await (
            from licenceModule in db.LicenceModuleMatrix.AsNoTracking()
            join pricing in db.PlanModulePricing.AsNoTracking() on licenceModule.ModuleId equals pricing.ModuleId
            join module in db.Modules.AsNoTracking() on licenceModule.ModuleId equals module.Id
            where targetLicenceIds.Contains(licenceModule.LicenceTypeId)
                  && pricing.PlanId == subscription.PlanId
                  && module.IsActive
            select licenceModule.ModuleId)
            .Distinct()
            .ToListAsync(ct);

        var includedPricingRows = pricingRows
            .Where(x => x.IsIncludedInBase && targetLicenceIds.Count > 0)
            .Where(x => eligibleModuleIds.Contains(x.ModuleId))
            .ToList();

        var subscriptionModules = await db.SubscriptionModules
            .AsNoTracking()
            .Include(x => x.Module)
            .Where(x => x.SubscriptionId == subscription.Id)
            .ToListAsync(ct);

        var moduleRows = new List<TenantLicenceImpactModuleRow>();
        foreach (var pricing in includedPricingRows)
        {
            var existing = subscriptionModules.FirstOrDefault(x => x.ModuleId == pricing.ModuleId);
            if (existing is null)
            {
                moduleRows.Add(new TenantLicenceImpactModuleRow
                {
                    ModuleCode = pricing.Module!.ModuleCode,
                    ModuleName = pricing.Module.ModuleName,
                    Action = "Activate",
                    PriceMonthly = pricing.PriceMonthly,
                    PriceAnnual = pricing.PriceAnnual,
                    Reason = "Included in base for the current plan and licence mix."
                });
                continue;
            }

            if (!existing.IsActive)
            {
                moduleRows.Add(new TenantLicenceImpactModuleRow
                {
                    ModuleCode = existing.Module?.ModuleCode ?? pricing.Module!.ModuleCode,
                    ModuleName = existing.Module?.ModuleName ?? pricing.Module!.ModuleName,
                    Action = "Reactivate",
                    PriceMonthly = pricing.PriceMonthly,
                    PriceAnnual = pricing.PriceAnnual,
                    Reason = "Module becomes included-in-base again under the target licence mix."
                });
                continue;
            }

            if (existing.PriceMonthly != pricing.PriceMonthly || existing.PriceAnnual != pricing.PriceAnnual)
            {
                moduleRows.Add(new TenantLicenceImpactModuleRow
                {
                    ModuleCode = existing.Module?.ModuleCode ?? pricing.Module!.ModuleCode,
                    ModuleName = existing.Module?.ModuleName ?? pricing.Module!.ModuleName,
                    Action = "Reprice",
                    PriceMonthly = pricing.PriceMonthly,
                    PriceAnnual = pricing.PriceAnnual,
                    Reason = "Plan pricing for the module will be refreshed during reconciliation."
                });
            }
        }

        foreach (var activeModule in subscriptionModules.Where(x => x.IsActive))
        {
            if (eligibleModuleIds.Contains(activeModule.ModuleId))
            {
                continue;
            }

            moduleRows.Add(new TenantLicenceImpactModuleRow
            {
                ModuleCode = activeModule.Module?.ModuleCode ?? $"M-{activeModule.ModuleId}",
                ModuleName = activeModule.Module?.ModuleName ?? "Unknown Module",
                Action = "Deactivate",
                PriceMonthly = activeModule.PriceMonthly,
                PriceAnnual = activeModule.PriceAnnual,
                Reason = "Module would no longer be licence-eligible after this change."
            });
        }

        moduleRows = moduleRows
            .OrderByDescending(x => x.Action == "Deactivate")
            .ThenBy(x => x.ModuleCode)
            .ToList();

        return new TenantLicenceImpactPreview
        {
            TenantId = tenantId,
            LicenceTypeId = licenceTypeId,
            LicenceCode = licenceType.Code,
            LicenceName = licenceType.Name,
            Operation = activate ? "Assign" : "Remove",
            ModulesToActivate = moduleRows.Count(x => x.Action == "Activate"),
            ModulesToReactivate = moduleRows.Count(x => x.Action == "Reactivate"),
            ModulesToReprice = moduleRows.Count(x => x.Action == "Reprice"),
            ModulesToDeactivate = moduleRows.Count(x => x.Action == "Deactivate"),
            Modules = moduleRows
        };
    }

    private async Task LogPlatformAction(string action, Guid tenantId, CancellationToken ct)
    {
        await _auditLogger.Log(
            "Tenant",
            0,
            action,
            null,
            new
            {
                IsPlatformAdmin = _tenantContext.IsPlatformAdmin,
                ImpersonatedTenantId = _tenantContext.ImpersonatingTenantId,
                TenantId = tenantId
            },
            "platform-admin",
            ct);
    }
}

public class TenantDashboardStats
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int PendingTenants { get; set; }
    public int SuspendedTenants { get; set; }
}

public class TenantLicenceChangeResult
{
    public TenantLicenceType TenantLicence { get; set; } = new();
    public SubscriptionModuleEntitlementBootstrapResult Reconciliation { get; set; } = new();
}

public class TenantLicenceImpactPreview
{
    public Guid TenantId { get; set; }
    public int LicenceTypeId { get; set; }
    public string LicenceCode { get; set; } = string.Empty;
    public string LicenceName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public int ModulesToActivate { get; set; }
    public int ModulesToReactivate { get; set; }
    public int ModulesToReprice { get; set; }
    public int ModulesToDeactivate { get; set; }
    public List<TenantLicenceImpactModuleRow> Modules { get; set; } = new();
}

public class TenantLicenceImpactModuleRow
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public decimal PriceAnnual { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class TenantModuleReconciliationBatchResult
{
    public int RequestedTenants { get; set; }
    public int ProcessedTenants { get; set; }
    public SubscriptionModuleEntitlementBootstrapResult Reconciliation { get; set; } = new();
}
