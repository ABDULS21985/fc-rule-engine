using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services.CrossBorder;

public sealed class DivergenceDetectionService : IDivergenceDetectionService
{
    private readonly MetadataDbContext _db;
    private readonly IHarmonisationAuditLogger _audit;
    private readonly ILogger<DivergenceDetectionService> _log;

    public DivergenceDetectionService(MetadataDbContext db, IHarmonisationAuditLogger audit, ILogger<DivergenceDetectionService> log)
    {
        _db = db; _audit = audit; _log = log;
    }

    public async Task<IReadOnlyList<DivergenceAlert>> DetectDivergencesAsync(CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        var alerts = new List<DivergenceAlert>();

        var mappings = await _db.RegulatoryEquivalenceMappings
            .AsNoTracking()
            .Include(m => m.Entries)
            .Where(m => m.IsActive)
            .ToListAsync(ct);

        foreach (var mapping in mappings)
        {
            if (mapping.Entries.Count < 2) continue;

            // Check for threshold divergences — find entries where thresholds differ significantly
            var thresholds = mapping.Entries
                .Where(e => e.ThresholdUnit == "PERCENTAGE")
                .OrderBy(e => e.LocalThreshold)
                .ToList();

            if (thresholds.Count < 2) continue;

            var minThreshold = thresholds.First();
            var maxThreshold = thresholds.Last();
            var gap = maxThreshold.LocalThreshold - minThreshold.LocalThreshold;

            // Report divergence if gap > 2 percentage points
            if (gap > 2.0m)
            {
                var sourceJurisdiction = maxThreshold.JurisdictionCode;
                var affected = thresholds
                    .Where(t => t.JurisdictionCode != sourceJurisdiction)
                    .Select(t => t.JurisdictionCode)
                    .ToList();

                // Check if this divergence already exists
                var existing = await _db.RegulatoryDivergences
                    .AnyAsync(d => d.MappingId == mapping.Id
                        && d.SourceJurisdiction == sourceJurisdiction
                        && d.Status != DivergenceStatus.Resolved
                        && d.Status != DivergenceStatus.Superseded, ct);

                if (!existing)
                {
                    var severity = gap > 5.0m ? DivergenceSeverity.High
                        : gap > 3.0m ? DivergenceSeverity.Medium
                        : DivergenceSeverity.Low;

                    var divergence = new RegulatoryDivergence
                    {
                        MappingId = mapping.Id,
                        ConceptDomain = mapping.ConceptDomain,
                        DivergenceType = DivergenceType.ThresholdChange,
                        SourceJurisdiction = sourceJurisdiction,
                        AffectedJurisdictions = string.Join(",", affected),
                        PreviousValue = minThreshold.LocalThreshold.ToString("F6"),
                        NewValue = maxThreshold.LocalThreshold.ToString("F6"),
                        Description = $"{mapping.MappingName}: {sourceJurisdiction} threshold ({maxThreshold.LocalThreshold}%) diverges from other jurisdictions (min {minThreshold.LocalThreshold}%)",
                        Severity = severity
                    };

                    _db.RegulatoryDivergences.Add(divergence);
                    await _db.SaveChangesAsync(ct);

                    alerts.Add(MapToAlert(divergence));

                    await _audit.LogAsync(null, sourceJurisdiction, correlationId, "DIVERGENCE_DETECTED",
                        new { divergenceId = divergence.Id, mapping.ConceptDomain, sourceJurisdiction, severity, gap },
                        null, ct);
                }
            }

            // Check for framework divergences
            var frameworks = mapping.Entries
                .Select(e => e.RegulatoryFramework)
                .Distinct()
                .ToList();

            if (frameworks.Count > 1)
            {
                var latestFramework = frameworks.Contains("BASEL_III") ? "BASEL_III" : frameworks.First();
                var lagging = mapping.Entries
                    .Where(e => e.RegulatoryFramework != latestFramework)
                    .ToList();

                foreach (var lag in lagging)
                {
                    var existing = await _db.RegulatoryDivergences
                        .AnyAsync(d => d.MappingId == mapping.Id
                            && d.DivergenceType == DivergenceType.FrameworkUpgrade
                            && d.SourceJurisdiction == lag.JurisdictionCode
                            && d.Status != DivergenceStatus.Resolved, ct);

                    if (!existing)
                    {
                        var divergence = new RegulatoryDivergence
                        {
                            MappingId = mapping.Id,
                            ConceptDomain = mapping.ConceptDomain,
                            DivergenceType = DivergenceType.FrameworkUpgrade,
                            SourceJurisdiction = lag.JurisdictionCode,
                            AffectedJurisdictions = string.Join(",",
                                mapping.Entries.Where(e => e.JurisdictionCode != lag.JurisdictionCode).Select(e => e.JurisdictionCode)),
                            PreviousValue = lag.RegulatoryFramework,
                            NewValue = latestFramework,
                            Description = $"{lag.JurisdictionCode} uses {lag.RegulatoryFramework} while others use {latestFramework}",
                            Severity = DivergenceSeverity.Medium
                        };

                        _db.RegulatoryDivergences.Add(divergence);
                        await _db.SaveChangesAsync(ct);
                        alerts.Add(MapToAlert(divergence));
                    }
                }
            }
        }

        _log.LogInformation("Divergence detection completed. Found {Count} new divergences.", alerts.Count);
        return alerts;
    }

    public async Task AcknowledgeDivergenceAsync(long divergenceId, int userId, CancellationToken ct = default)
    {
        var divergence = await _db.RegulatoryDivergences.FindAsync([divergenceId], ct)
            ?? throw new InvalidOperationException($"Divergence {divergenceId} not found.");

        divergence.Status = DivergenceStatus.Acknowledged;
        divergence.AcknowledgedByUserId = userId;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(null, divergence.SourceJurisdiction, Guid.NewGuid(), "DIVERGENCE_ACKNOWLEDGED",
            new { divergenceId }, userId, ct);
    }

    public async Task ResolveDivergenceAsync(long divergenceId, string resolution, int userId, CancellationToken ct = default)
    {
        var divergence = await _db.RegulatoryDivergences.FindAsync([divergenceId], ct)
            ?? throw new InvalidOperationException($"Divergence {divergenceId} not found.");

        divergence.Status = DivergenceStatus.Resolved;
        divergence.ResolvedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(null, divergence.SourceJurisdiction, Guid.NewGuid(), "DIVERGENCE_RESOLVED",
            new { divergenceId, resolution }, userId, ct);
    }

    public async Task<IReadOnlyList<DivergenceAlert>> GetOpenDivergencesAsync(
        string? conceptDomain, DivergenceSeverity? minSeverity, CancellationToken ct = default)
    {
        var query = _db.RegulatoryDivergences
            .AsNoTracking()
            .Where(d => d.Status == DivergenceStatus.Open || d.Status == DivergenceStatus.Acknowledged || d.Status == DivergenceStatus.Tracking);

        if (!string.IsNullOrEmpty(conceptDomain))
            query = query.Where(d => d.ConceptDomain == conceptDomain);
        if (minSeverity.HasValue)
            query = query.Where(d => d.Severity >= minSeverity.Value);

        var divergences = await query.OrderByDescending(d => d.Severity).ThenByDescending(d => d.DetectedAt).ToListAsync(ct);
        return divergences.Select(MapToAlert).ToList();
    }

    public async Task<IReadOnlyList<DivergenceAlert>> GetGroupDivergencesAsync(int groupId, CancellationToken ct = default)
    {
        var groupJurisdictions = await _db.GroupSubsidiaries
            .AsNoTracking()
            .Where(s => s.GroupId == groupId && s.IsActive)
            .Select(s => s.JurisdictionCode)
            .Distinct()
            .ToListAsync(ct);

        var divergences = await _db.RegulatoryDivergences
            .AsNoTracking()
            .Where(d => d.Status != DivergenceStatus.Resolved && d.Status != DivergenceStatus.Superseded)
            .ToListAsync(ct);

        // Filter to divergences affecting this group's jurisdictions
        var relevant = divergences.Where(d =>
            groupJurisdictions.Contains(d.SourceJurisdiction) ||
            d.AffectedJurisdictions.Split(',').Any(j => groupJurisdictions.Contains(j.Trim())))
            .ToList();

        return relevant.Select(MapToAlert).ToList();
    }

    public async Task NotifyGroupsAsync(long divergenceId, CancellationToken ct = default)
    {
        var divergence = await _db.RegulatoryDivergences
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == divergenceId, ct)
            ?? throw new InvalidOperationException($"Divergence {divergenceId} not found.");

        var affectedCodes = divergence.AffectedJurisdictions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Append(divergence.SourceJurisdiction)
            .Distinct()
            .ToList();

        // Find groups with subsidiaries in affected jurisdictions
        var affectedGroups = await _db.GroupSubsidiaries
            .AsNoTracking()
            .Where(s => affectedCodes.Contains(s.JurisdictionCode) && s.IsActive)
            .Select(s => s.GroupId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var gid in affectedGroups)
        {
            // Check if notification already sent
            var alreadyNotified = await _db.DivergenceNotifications
                .AnyAsync(n => n.DivergenceId == divergenceId && n.GroupId == gid, ct);

            if (!alreadyNotified)
            {
                _db.DivergenceNotifications.Add(new DivergenceNotification
                {
                    DivergenceId = divergenceId, GroupId = gid,
                    NotifiedUserId = 0, // System notification
                    NotificationChannel = "IN_APP"
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private static DivergenceAlert MapToAlert(RegulatoryDivergence d) => new()
    {
        DivergenceId = d.Id, ConceptDomain = d.ConceptDomain,
        Type = d.DivergenceType, SourceJurisdiction = d.SourceJurisdiction,
        AffectedJurisdictions = d.AffectedJurisdictions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
        Description = d.Description, Severity = d.Severity, DetectedAt = d.DetectedAt
    };
}
