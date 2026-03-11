using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public class TenantManagementService
{
    private readonly MetadataDbContext _db;
    private readonly ITenantOnboardingService _onboardingService;
    private readonly IEntitlementService _entitlementService;
    private readonly SubscriptionModuleEntitlementBootstrapService _subscriptionModuleEntitlementBootstrapService;
    private readonly IAuditLogger _auditLogger;
    private readonly ITenantContext _tenantContext;

    public TenantManagementService(
        MetadataDbContext db,
        ITenantOnboardingService onboardingService,
        IEntitlementService entitlementService,
        SubscriptionModuleEntitlementBootstrapService subscriptionModuleEntitlementBootstrapService,
        IAuditLogger auditLogger,
        ITenantContext tenantContext)
    {
        _db = db;
        _onboardingService = onboardingService;
        _entitlementService = entitlementService;
        _subscriptionModuleEntitlementBootstrapService = subscriptionModuleEntitlementBootstrapService;
        _auditLogger = auditLogger;
        _tenantContext = tenantContext;
    }

    public async Task<List<Tenant>> GetAllTenantsAsync(CancellationToken ct = default)
    {
        return await _db.Tenants
            .OrderBy(t => t.TenantName)
            .ToListAsync(ct);
    }

    public async Task<Tenant?> GetTenantByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.Tenants.FindAsync(new object[] { tenantId }, ct);
    }

    public async Task<TenantDashboardStats> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var tenants = await _db.Tenants.ToListAsync(ct);
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
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Activate();
        await _db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantActivated", tenantId, ct);
    }

    public async Task SuspendTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Suspend("Admin action");
        await _db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantSuspended", tenantId, ct);
    }

    public async Task ReactivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Reactivate();
        await _db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantReactivated", tenantId, ct);
    }

    public async Task DeactivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Deactivate();
        await _db.SaveChangesAsync(ct);
        await LogPlatformAction("TenantDeactivated", tenantId, ct);
    }

    public async Task<List<TenantLicenceType>> GetTenantLicencesAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.TenantLicenceTypes
            .Include(tlt => tlt.LicenceType)
            .Where(tlt => tlt.TenantId == tenantId)
            .OrderByDescending(tlt => tlt.IsActive)
            .ThenBy(tlt => tlt.LicenceType!.DisplayOrder)
            .ThenBy(tlt => tlt.LicenceType!.Code)
            .ToListAsync(ct);
    }

    public async Task<TenantLicenceChangeResult> AssignLicenceAsync(
        Guid tenantId,
        int licenceTypeId,
        string? registrationNumber = null,
        DateTime? effectiveDate = null,
        CancellationToken ct = default)
    {
        _ = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");

        var licenceType = await _db.LicenceTypes
            .FirstOrDefaultAsync(x => x.Id == licenceTypeId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Licence type not found or inactive");

        var tenantLicence = await _db.TenantLicenceTypes
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
            _db.TenantLicenceTypes.Add(tenantLicence);
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
            await _db.SaveChangesAsync(ct);
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
        var tenantLicence = await _db.TenantLicenceTypes
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.LicenceTypeId == licenceTypeId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Active tenant licence not found");

        tenantLicence.IsActive = false;
        tenantLicence.ExpiryDate = tenantLicence.ExpiryDate ?? DateTime.UtcNow.Date;
        await _db.SaveChangesAsync(ct);

        var reconciliation = await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForTenantAsync(tenantId, ct);
        await _entitlementService.InvalidateCache(tenantId);
        await LogPlatformAction("TenantLicenceRemoved", tenantId, ct);

        return new TenantLicenceChangeResult
        {
            TenantLicence = tenantLicence,
            Reconciliation = reconciliation
        };
    }

    public async Task<List<LicenceType>> GetAllLicenceTypesAsync(CancellationToken ct = default)
    {
        return await _db.LicenceTypes
            .Where(lt => lt.IsActive)
            .OrderBy(lt => lt.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<List<Module>> GetAllModulesAsync(CancellationToken ct = default)
    {
        return await _db.Modules
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<string?> GetTenantName(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.TenantName)
            .FirstOrDefaultAsync(ct);
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
