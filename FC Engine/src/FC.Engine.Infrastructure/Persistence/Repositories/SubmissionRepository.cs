using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Persistence.Repositories;

public class SubmissionRepository : ISubmissionRepository
{
    private readonly MetadataDbContext _db;
    private readonly ITenantContext _tenantContext;

    public SubmissionRepository(MetadataDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

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
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Submission>> GetFiltered(
        string? search, string? status, int take = 500, CancellationToken ct = default)
    {
        var query = _db.Submissions
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .Include(s => s.ValidationReport)
                .ThenInclude(r => r!.Errors)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s =>
                s.ReturnCode.Contains(search) ||
                (s.Institution != null && s.Institution.InstitutionName.Contains(search)));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<SubmissionStatus>(status, out var parsed))
            query = query.Where(s => s.Status == parsed);

        return await query
            .OrderByDescending(s => s.SubmittedAt)
            .Take(take)
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
                .ThenInclude(r => r!.Errors)
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
