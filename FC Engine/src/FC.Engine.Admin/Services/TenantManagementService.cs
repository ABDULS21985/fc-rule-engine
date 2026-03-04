using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

/// <summary>
/// Service for PlatformAdmin tenant management operations.
/// </summary>
public class TenantManagementService
{
    private readonly MetadataDbContext _db;

    public TenantManagementService(MetadataDbContext db)
    {
        _db = db;
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
            ActiveTenants = tenants.Count(t => t.TenantStatus == "Active"),
            PendingTenants = tenants.Count(t => t.TenantStatus == "PendingActivation"),
            SuspendedTenants = tenants.Count(t => t.TenantStatus == "Suspended")
        };
    }

    public async Task<Tenant> CreateTenantAsync(string name, string slug, string? contactEmail, CancellationToken ct = default)
    {
        var tenant = new Tenant
        {
            TenantName = name,
            TenantSlug = slug,
            TenantStatus = "PendingActivation",
            ContactEmail = contactEmail,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);
        return tenant;
    }

    public async Task ActivateTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.TenantStatus = "Active";
        tenant.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SuspendTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FindAsync(new object[] { tenantId }, ct)
            ?? throw new InvalidOperationException("Tenant not found");
        tenant.TenantStatus = "Suspended";
        tenant.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}

public class TenantDashboardStats
{
    public int TotalTenants { get; set; }
    public int ActiveTenants { get; set; }
    public int PendingTenants { get; set; }
    public int SuspendedTenants { get; set; }
}
