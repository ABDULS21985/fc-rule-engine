using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Security;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public class PermissionService : IPermissionService
{
    private readonly MetadataDbContext _db;

    public PermissionService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> GetPermissions(Guid? tenantId, string roleName, CancellationToken ct = default)
    {
        var role = await _db.Roles
            .AsNoTracking()
            .Where(r => r.IsActive && r.RoleName == roleName && (r.TenantId == tenantId || r.TenantId == null))
            .OrderByDescending(r => r.TenantId.HasValue)
            .FirstOrDefaultAsync(ct);

        if (role is null)
        {
            return PermissionCatalog.DefaultRolePermissions.TryGetValue(roleName, out var fallback)
                ? fallback
                : Array.Empty<string>();
        }

        var permissions = await _db.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == role.Id)
            .Join(
                _db.Permissions.AsNoTracking().Where(p => p.IsActive),
                rp => rp.PermissionId,
                p => p.Id,
                (_, p) => p.PermissionCode)
            .Distinct()
            .ToListAsync(ct);

        if (permissions.Count > 0)
        {
            return permissions;
        }

        return PermissionCatalog.DefaultRolePermissions.TryGetValue(roleName, out var defaultPermissions)
            ? defaultPermissions
            : Array.Empty<string>();
    }

    public async Task<bool> HasPermission(Guid? tenantId, string roleName, string permissionCode, CancellationToken ct = default)
    {
        var permissions = await GetPermissions(tenantId, roleName, ct);
        return permissions.Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }
}
