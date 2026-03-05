using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class ExportRequestRepository : IExportRequestRepository
{
    private readonly MetadataDbContext _db;

    public ExportRequestRepository(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<ExportRequest> Add(ExportRequest request, CancellationToken ct = default)
    {
        _db.ExportRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public Task<ExportRequest?> GetById(int id, CancellationToken ct = default)
    {
        return _db.ExportRequests
            .Include(x => x.Submission)
            .ThenInclude(x => x!.Institution)
            .Include(x => x.Submission)
            .ThenInclude(x => x!.ReturnPeriod)
            .Include(x => x.Submission)
            .ThenInclude(x => x!.ValidationReport)
            .ThenInclude(x => x!.Errors)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<ExportRequest>> GetBySubmission(Guid tenantId, int submissionId, CancellationToken ct = default)
    {
        return await _db.ExportRequests
            .Where(x => x.TenantId == tenantId && x.SubmissionId == submissionId)
            .OrderByDescending(x => x.RequestedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExportRequest>> GetQueuedBatch(int batchSize, CancellationToken ct = default)
    {
        return await _db.ExportRequests
            .Include(x => x.Submission)
            .ThenInclude(x => x!.Institution)
            .Include(x => x.Submission)
            .ThenInclude(x => x!.ReturnPeriod)
            .Include(x => x.Submission)
            .ThenInclude(x => x!.ValidationReport)
            .ThenInclude(x => x!.Errors)
            .Where(x => x.Status == ExportRequestStatus.Queued)
            .OrderBy(x => x.RequestedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExportRequest>> GetExpired(DateTime asOfUtc, int batchSize, CancellationToken ct = default)
    {
        return await _db.ExportRequests
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= asOfUtc && x.FilePath != null)
            .OrderBy(x => x.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task Update(ExportRequest request, CancellationToken ct = default)
    {
        _db.ExportRequests.Update(request);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ExistsForSubmission(
        Guid tenantId,
        int submissionId,
        ExportFormat format,
        ExportRequestStatus status,
        CancellationToken ct = default)
    {
        return _db.ExportRequests.AnyAsync(x =>
            x.TenantId == tenantId
            && x.SubmissionId == submissionId
            && x.Format == format
            && x.Status == status, ct);
    }
}
