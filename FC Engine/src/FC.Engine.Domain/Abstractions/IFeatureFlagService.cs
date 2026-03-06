using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

public interface IFeatureFlagService
{
    Task<bool> IsEnabled(string flagCode, Guid? tenantId = null);
    Task<IReadOnlyList<FeatureFlag>> GetAll(CancellationToken ct = default);
    Task<FeatureFlag> Upsert(
        string flagCode,
        string description,
        bool isEnabled,
        int rolloutPercent,
        string? allowedTenants,
        string? allowedPlans,
        CancellationToken ct = default);
}
