using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Repository for managing submission approval records (maker-checker workflow).
/// </summary>
public interface ISubmissionApprovalRepository
{
    Task<SubmissionApproval?> GetBySubmission(int submissionId, CancellationToken ct = default);
    Task<IReadOnlyList<SubmissionApproval>> GetPendingByInstitution(int institutionId, CancellationToken ct = default);
    Task Create(SubmissionApproval approval, CancellationToken ct = default);
    Task Update(SubmissionApproval approval, CancellationToken ct = default);
}
