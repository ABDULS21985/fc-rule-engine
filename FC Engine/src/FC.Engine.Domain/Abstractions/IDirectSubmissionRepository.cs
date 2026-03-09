using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

public interface IDirectSubmissionRepository
{
    Task<DirectSubmission> Add(DirectSubmission entity, CancellationToken ct = default);
    Task Update(DirectSubmission entity, CancellationToken ct = default);
    Task<DirectSubmission?> GetById(int id, CancellationToken ct = default);
    Task<DirectSubmission?> GetByIdWithSubmission(int id, CancellationToken ct = default);
    Task<List<DirectSubmission>> GetBySubmission(int submissionId, CancellationToken ct = default);
    Task<List<DirectSubmission>> GetByTenantAndSubmission(Guid tenantId, int submissionId, CancellationToken ct = default);
    Task<List<DirectSubmission>> GetPendingRetries(int batchSize, CancellationToken ct = default);
    Task<List<DirectSubmission>> GetSubmittedAwaitingStatus(int batchSize, CancellationToken ct = default);
}
