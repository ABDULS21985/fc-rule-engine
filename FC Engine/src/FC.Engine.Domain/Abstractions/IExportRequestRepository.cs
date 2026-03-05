using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Abstractions;

public interface IExportRequestRepository
{
    Task<ExportRequest> Add(ExportRequest request, CancellationToken ct = default);
    Task<ExportRequest?> GetById(int id, CancellationToken ct = default);
    Task<IReadOnlyList<ExportRequest>> GetBySubmission(Guid tenantId, int submissionId, CancellationToken ct = default);
    Task<IReadOnlyList<ExportRequest>> GetQueuedBatch(int batchSize, CancellationToken ct = default);
    Task<IReadOnlyList<ExportRequest>> GetExpired(DateTime asOfUtc, int batchSize, CancellationToken ct = default);
    Task Update(ExportRequest request, CancellationToken ct = default);
    Task<bool> ExistsForSubmission(Guid tenantId, int submissionId, ExportFormat format, ExportRequestStatus status, CancellationToken ct = default);
}
