namespace FC.Engine.Domain.Abstractions;

public interface IDataResidencyRouter
{
    Task<string> ResolveConnectionString(Guid? tenantId, CancellationToken ct = default);
    Task<string> ResolveRegion(Guid? tenantId, CancellationToken ct = default);
}
