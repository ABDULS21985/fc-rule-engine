namespace FC.Engine.Domain.Abstractions;

public interface IPermissionService
{
    Task<IReadOnlyList<string>> GetPermissions(Guid? tenantId, string roleName, CancellationToken ct = default);
    Task<bool> HasPermission(Guid? tenantId, string roleName, string permissionCode, CancellationToken ct = default);
}
