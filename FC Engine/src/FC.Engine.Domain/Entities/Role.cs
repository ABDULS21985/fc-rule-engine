namespace FC.Engine.Domain.Entities;

public class Role
{
    public int Id { get; set; }
    public Guid? TenantId { get; set; } // null for system role
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
