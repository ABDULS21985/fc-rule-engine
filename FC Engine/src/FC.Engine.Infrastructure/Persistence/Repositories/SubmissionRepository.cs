using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Persistence.Repositories;

public class SubmissionRepository : ISubmissionRepository
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ITenantContext _tenantContext;

    public SubmissionRepository(IDbContextFactory<MetadataDbContext> dbFactory, ITenantContext tenantContext)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Applies automatic tenant scoping to queries. Platform admins (no tenant) see all data.
    /// </summary>
    private IQueryable<Submission> ApplyTenantFilter(IQueryable<Submission> query)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (tenantId.HasValue)
            query = query.Where(s => s.TenantId == tenantId.Value);
        return query;
    }

    public async Task<Submission?> GetById(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await ApplyTenantFilter(db.Submissions)
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Submission?> GetByIdWithReport(int id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await ApplyTenantFilter(db.Submissions)
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
            .Include(s => s.ValidationReport)
                .ThenInclude(r => r!.Errors)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Submission>> GetAll(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await ApplyTenantFilter(db.Submissions)
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var query = ApplyTenantFilter(db.Submissions)
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await ApplyTenantFilter(db.Submissions)
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await ApplyTenantFilter(db.Submissions)
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
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await ApplyTenantFilter(db.Submissions).CountAsync(s => s.Status == status, ct);
    }

    public async Task<int> GetTotalCount(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await ApplyTenantFilter(db.Submissions).CountAsync(ct);
    }

    public async Task Add(Submission submission, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        db.Submissions.Add(submission);
        await db.SaveChangesAsync(ct);
    }

    public async Task Update(Submission submission, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        db.Submissions.Update(submission);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatus(int submissionId, SubmissionStatus status, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Verify submission exists and belongs to current tenant before updating
        var existing = await ApplyTenantFilter(db.Submissions)
            .Where(s => s.Id == submissionId)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (existing == 0)
            throw new InvalidOperationException($"Submission {submissionId} not found or not accessible.");

        var submission = new Submission
        {
            Id = submissionId,
            Status = status
        };

        var entry = db.Attach(submission);
        entry.Property(s => s.Status).IsModified = true;

        await db.SaveChangesAsync(ct);
    }
}
