using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class SavedReportRepository : ISavedReportRepository
{
    private readonly MetadataDbContext _db;

    public SavedReportRepository(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<SavedReport?> GetById(int id, CancellationToken ct = default)
    {
        return await _db.SavedReports.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<SavedReport>> GetByTenant(
        Guid tenantId, int institutionId, CancellationToken ct = default)
    {
        return await _db.SavedReports
            .Where(x => x.TenantId == tenantId &&
                        (x.InstitutionId == institutionId || x.IsShared))
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SavedReport>> GetScheduledDue(CancellationToken ct = default)
    {
        return await _db.SavedReports
            .Where(x => x.IsScheduleActive && x.ScheduleCron != null)
            .ToListAsync(ct);
    }

    public async Task Add(SavedReport report, CancellationToken ct = default)
    {
        _db.SavedReports.Add(report);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Update(SavedReport report, CancellationToken ct = default)
    {
        report.UpdatedAt = DateTime.UtcNow;
        _db.SavedReports.Update(report);
        await _db.SaveChangesAsync(ct);
    }

    public async Task Delete(int id, CancellationToken ct = default)
    {
        var report = await _db.SavedReports.FindAsync(new object[] { id }, ct);
        if (report is not null)
        {
            _db.SavedReports.Remove(report);
            await _db.SaveChangesAsync(ct);
        }
    }
}
