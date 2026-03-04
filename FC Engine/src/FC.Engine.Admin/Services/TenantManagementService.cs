using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public class TenantManagementService
{
    private readonly MetadataDbContext _db;
    private readonly ITenantOnboardingService _onboardingService;

    public TenantManagementService(MetadataDbContext db, ITenantOnboardingService onboardingService)
    {
        _db = db;
        _onboardingService = onboardingService;
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
    }

    public async Task SuspendTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Suspend("Admin action");
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReactivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Reactivate();
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeactivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.Deactivate();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<TenantLicenceType>> GetTenantLicencesAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.TenantLicenceTypes
            .Include(tlt => tlt.LicenceType)
            .Where(tlt => tlt.TenantId == tenantId)
            .ToListAsync(ct);
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
}

public class TenantDashboardStats
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int PendingTenants { get; set; }
    public int SuspendedTenants { get; set; }
}
