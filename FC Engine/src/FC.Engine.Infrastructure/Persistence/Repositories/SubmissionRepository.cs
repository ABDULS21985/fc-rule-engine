using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Persistence.Repositories;

public class SubmissionRepository : ISubmissionRepository
{
    private readonly MetadataDbContext _db;

    public SubmissionRepository(MetadataDbContext db) => _db = db;

    public async Task<Submission?> GetById(int id, CancellationToken ct = default)
    {
        return await _db.Submissions
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Submission?> GetByIdWithReport(int id, CancellationToken ct = default)
    {
        return await _db.Submissions
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .Include(s => s.ValidationReport)
                .ThenInclude(r => r!.Errors)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Submission>> GetAll(CancellationToken ct = default)
    {
        return await _db.Submissions
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .Include(s => s.ValidationReport)
                .ThenInclude(r => r!.Errors)
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Submission>> GetByInstitution(int institutionId, CancellationToken ct = default)
    {
        return await _db.Submissions
            .Where(s => s.InstitutionId == institutionId)
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .Include(s => s.ValidationReport)
                .ThenInclude(r => r!.Errors)
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Submission>> GetRecent(int count = 10, CancellationToken ct = default)
    {
        return await _db.Submissions
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .Include(s => s.ValidationReport)
            .OrderByDescending(s => s.SubmittedAt)
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<int> GetCountByStatus(SubmissionStatus status, CancellationToken ct = default)
    {
        return await _db.Submissions.CountAsync(s => s.Status == status, ct);
    }

    public async Task<int> GetTotalCount(CancellationToken ct = default)
    {
        return await _db.Submissions.CountAsync(ct);
    }

    public async Task Add(Submission submission, CancellationToken ct = default)
    {
        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Update(Submission submission, CancellationToken ct = default)
    {
        _db.Submissions.Update(submission);
        await _db.SaveChangesAsync(ct);
    }
}
