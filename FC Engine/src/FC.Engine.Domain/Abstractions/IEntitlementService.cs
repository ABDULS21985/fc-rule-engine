using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Domain.Abstractions;

public interface IEntitlementService
{
    Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default);
    Task<bool> HasModuleAccess(Guid tenantId, string moduleCode, CancellationToken ct = default);
    Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default);
    Task InvalidateCache(Guid tenantId);
}
