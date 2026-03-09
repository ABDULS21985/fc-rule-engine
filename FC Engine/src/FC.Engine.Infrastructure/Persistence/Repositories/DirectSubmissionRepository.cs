using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Persistence.Repositories;

public class DirectSubmissionRepository : IDirectSubmissionRepository
{
    private readonly MetadataDbContext _db;

    public DirectSubmissionRepository(MetadataDbContext db) => _db = db;

    public async Task<DirectSubmission> Add(DirectSubmission entity, CancellationToken ct = default)
    {
        _db.DirectSubmissions.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task Update(DirectSubmission entity, CancellationToken ct = default)
    {
        _db.DirectSubmissions.Update(entity);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<DirectSubmission?> GetById(int id, CancellationToken ct = default)
    {
        return await _db.DirectSubmissions.FindAsync(new object[] { id }, ct);
    }

    public async Task<DirectSubmission?> GetByIdWithSubmission(int id, CancellationToken ct = default)
    {
        return await _db.DirectSubmissions
            .Include(d => d.Submission).ThenInclude(s => s!.Institution)
            .Include(d => d.Submission).ThenInclude(s => s!.ReturnPeriod)
            .FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    public async Task<List<DirectSubmission>> GetBySubmission(int submissionId, CancellationToken ct = default)
    {
        return await _db.DirectSubmissions
            .Where(d => d.SubmissionId == submissionId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<DirectSubmission>> GetByTenantAndSubmission(Guid tenantId, int submissionId, CancellationToken ct = default)
    {
        return await _db.DirectSubmissions
            .Where(d => d.TenantId == tenantId && d.SubmissionId == submissionId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<DirectSubmission>> GetPendingRetries(int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.DirectSubmissions
            .Include(d => d.Submission).ThenInclude(s => s!.Institution)
            .Where(d => d.Status == DirectSubmissionStatus.RetryScheduled
                && d.NextRetryAt != null && d.NextRetryAt <= now
                && d.AttemptCount < d.MaxAttempts)
            .OrderBy(d => d.NextRetryAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<List<DirectSubmission>> GetSubmittedAwaitingStatus(int batchSize, CancellationToken ct = default)
    {
        return await _db.DirectSubmissions
            .Where(d => d.Status == DirectSubmissionStatus.Submitted
                || d.Status == DirectSubmissionStatus.Acknowledged)
            .OrderBy(d => d.SubmittedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }
}
