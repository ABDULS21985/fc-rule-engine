using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class SubmissionApprovalRepository : ISubmissionApprovalRepository
{
    private readonly MetadataDbContext _db;

    public SubmissionApprovalRepository(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<SubmissionApproval?> GetBySubmission(int submissionId, CancellationToken ct = default)
    {
        return await _db.SubmissionApprovals
            .Include(a => a.RequestedBy)
            .Include(a => a.ReviewedBy)
            .FirstOrDefaultAsync(a => a.SubmissionId == submissionId, ct);
    }

    public async Task<IReadOnlyList<SubmissionApproval>> GetPendingByInstitution(int institutionId, CancellationToken ct = default)
    {
        return await _db.SubmissionApprovals
            .Include(a => a.Submission)
            .Include(a => a.RequestedBy)
            .Where(a => a.Status == ApprovalStatus.Pending
                     && a.RequestedBy != null
                     && a.RequestedBy.InstitutionId == institutionId)
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync(ct);
    }

    public async Task Create(SubmissionApproval approval, CancellationToken ct = default)
    {
        _db.SubmissionApprovals.Add(approval);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Update(SubmissionApproval approval, CancellationToken ct = default)
    {
        _db.SubmissionApprovals.Update(approval);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Delete(SubmissionApproval approval, CancellationToken ct = default)
    {
        _db.SubmissionApprovals.Remove(approval);
        await _db.SaveChangesAsync(ct);
    }
}
