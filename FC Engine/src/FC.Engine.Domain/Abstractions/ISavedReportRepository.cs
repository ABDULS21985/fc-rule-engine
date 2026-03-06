using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

public interface ISavedReportRepository
{
    Task<SavedReport?> GetById(int id, CancellationToken ct = default);
    Task<IReadOnlyList<SavedReport>> GetByTenant(Guid tenantId, int institutionId, CancellationToken ct = default);
    Task<IReadOnlyList<SavedReport>> GetScheduledDue(CancellationToken ct = default);
    Task Add(SavedReport report, CancellationToken ct = default);
    Task Update(SavedReport report, CancellationToken ct = default);
    Task Delete(int id, CancellationToken ct = default);
}
