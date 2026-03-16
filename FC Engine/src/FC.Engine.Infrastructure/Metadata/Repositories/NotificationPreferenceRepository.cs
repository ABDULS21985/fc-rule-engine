using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata.Repositories;

public class NotificationPreferenceRepository : INotificationPreferenceRepository
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;

    public NotificationPreferenceRepository(IDbContextFactory<MetadataDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<NotificationPreference?> GetPreference(
        Guid tenantId,
        int userId,
        string eventType,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.NotificationPreferences.FirstOrDefaultAsync(p =>
            p.TenantId == tenantId &&
            p.UserId == userId &&
            p.EventType == eventType, ct);
    }

    public async Task<IReadOnlyList<NotificationPreference>> GetByUser(
        Guid tenantId,
        int userId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.NotificationPreferences
            .Where(p => p.TenantId == tenantId && p.UserId == userId)
            .OrderBy(p => p.EventType)
            .ToListAsync(ct);
    }

    public async Task<NotificationPreference> Upsert(NotificationPreference preference, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.NotificationPreferences.FirstOrDefaultAsync(p =>
            p.TenantId == preference.TenantId &&
            p.UserId == preference.UserId &&
            p.EventType == preference.EventType, ct);

        if (existing is null)
        {
            db.NotificationPreferences.Add(preference);
            await db.SaveChangesAsync(ct);
            return preference;
        }

        existing.InAppEnabled = preference.InAppEnabled;
        existing.EmailEnabled = preference.EmailEnabled;
        existing.SmsEnabled = preference.SmsEnabled;
        existing.SmsQuietHoursStart = preference.SmsQuietHoursStart;
        existing.SmsQuietHoursEnd = preference.SmsQuietHoursEnd;

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task UpsertRange(IEnumerable<NotificationPreference> preferences, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        foreach (var preference in preferences)
        {
            var existing = await db.NotificationPreferences.FirstOrDefaultAsync(p =>
                p.TenantId == preference.TenantId &&
                p.UserId == preference.UserId &&
                p.EventType == preference.EventType, ct);

            if (existing is null)
            {
                db.NotificationPreferences.Add(preference);
                continue;
            }

            existing.InAppEnabled = preference.InAppEnabled;
            existing.EmailEnabled = preference.EmailEnabled;
            existing.SmsEnabled = preference.SmsEnabled;
            existing.SmsQuietHoursStart = preference.SmsQuietHoursStart;
            existing.SmsQuietHoursEnd = preference.SmsQuietHoursEnd;
        }

        await db.SaveChangesAsync(ct);
    }
}
