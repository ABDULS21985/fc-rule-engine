using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services.CrossBorder;

public sealed class PanAfricanDashboardService : IPanAfricanDashboardService
{
    private readonly MetadataDbContext _db;

    public PanAfricanDashboardService(MetadataDbContext db) => _db = db;

    public async Task<GroupComplianceOverview?> GetGroupOverviewAsync(int groupId, CancellationToken ct = default)
    {
        var group = await _db.FinancialGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId, ct);

        if (group is null) return null;

        var subsidiaries = await _db.GroupSubsidiaries
            .AsNoTracking()
            .Where(s => s.GroupId == groupId && s.IsActive)
            .ToListAsync(ct);

        var jurisdictionCodes = subsidiaries.Select(s => s.JurisdictionCode).Distinct().ToList();

        var jurisdictions = await _db.RegulatoryJurisdictions
            .AsNoTracking()
            .Where(j => jurisdictionCodes.Contains(j.JurisdictionCode))
            .ToListAsync(ct);

        // Get latest consolidation run
        var latestRun = await _db.ConsolidationRuns
            .AsNoTracking()
            .Where(r => r.GroupId == groupId && r.Status == ConsolidationRunStatus.Completed)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Get open divergences for this group's jurisdictions
        var openDivergences = await _db.RegulatoryDivergences
            .AsNoTracking()
            .Where(d => d.Status != DivergenceStatus.Resolved && d.Status != DivergenceStatus.Superseded)
            .ToListAsync(ct);

        var groupDivergences = openDivergences.Where(d =>
            jurisdictionCodes.Contains(d.SourceJurisdiction) ||
            d.AffectedJurisdictions.Split(',').Any(j => jurisdictionCodes.Contains(j.Trim())))
            .ToList();

        // Get upcoming deadlines
        var now = DateTimeOffset.UtcNow;
        var upcomingDeadlines = await _db.RegulatoryDeadlines
            .AsNoTracking()
            .Where(d => jurisdictionCodes.Contains(d.JurisdictionCode)
                && (d.GroupId == null || d.GroupId == groupId)
                && d.DeadlineUtc > now
                && (d.Status == DeadlineStatus.Upcoming || d.Status == DeadlineStatus.DueSoon))
            .CountAsync(ct);

        // Get latest snapshots for compliance status
        var snapshots = latestRun is not null
            ? await _db.ConsolidationSubsidiarySnapshots
                .AsNoTracking()
                .Where(s => s.RunId == latestRun.Id)
                .ToListAsync(ct)
            : new List<ConsolidationSubsidiarySnapshot>();

        // Get CAR thresholds per jurisdiction
        var carMapping = await _db.RegulatoryEquivalenceMappings
            .AsNoTracking()
            .Include(m => m.Entries)
            .FirstOrDefaultAsync(m => m.MappingCode == "CAR_MAPPING" && m.IsActive, ct);

        var thresholdsByJurisdiction = carMapping?.Entries
            .ToDictionary(e => e.JurisdictionCode, e => e.LocalThreshold)
            ?? new Dictionary<string, decimal>();

        int compliant = 0, inBreach = 0;
        foreach (var snap in snapshots)
        {
            var threshold = thresholdsByJurisdiction.GetValueOrDefault(snap.JurisdictionCode, 10.0m);
            if (snap.LocalCAR >= threshold) compliant++; else inBreach++;
        }

        var byJurisdiction = jurisdictions.Select(j =>
        {
            var jSubs = subsidiaries.Where(s => s.JurisdictionCode == j.JurisdictionCode).ToList();
            var jSnaps = snapshots.Where(s => s.JurisdictionCode == j.JurisdictionCode).ToList();
            var jThreshold = thresholdsByJurisdiction.GetValueOrDefault(j.JurisdictionCode, 10.0m);
            var jDivs = groupDivergences.Count(d =>
                d.SourceJurisdiction == j.JurisdictionCode ||
                d.AffectedJurisdictions.Contains(j.JurisdictionCode));

            return new JurisdictionSummary
            {
                JurisdictionCode = j.JurisdictionCode,
                CountryName = j.CountryName,
                RegulatorCode = j.RegulatorCode,
                CurrencyCode = j.CurrencyCode,
                SubsidiaryCount = jSubs.Count,
                AllCompliant = jSnaps.All(s => s.LocalCAR >= jThreshold),
                AggregateCAR = jSnaps.Count > 0 ? jSnaps.Average(s => s.LocalCAR) : null,
                UpcomingDeadlines = 0, // Could be computed per jurisdiction
                OpenDivergences = jDivs
            };
        }).ToList();

        return new GroupComplianceOverview
        {
            GroupId = group.Id, GroupCode = group.GroupCode,
            GroupName = group.GroupName, BaseCurrency = group.BaseCurrency,
            TotalSubsidiaries = subsidiaries.Count,
            TotalJurisdictions = jurisdictionCodes.Count,
            SubsidiariesCompliant = compliant,
            SubsidiariesInBreach = inBreach,
            OpenDivergences = groupDivergences.Count,
            UpcomingDeadlines = upcomingDeadlines,
            ConsolidatedCAR = latestRun?.ConsolidatedCAR,
            ConsolidatedLCR = null,
            ByJurisdiction = byJurisdiction
        };
    }

    public async Task<IReadOnlyList<SubsidiaryComplianceSnapshot>> GetSubsidiarySnapshotsAsync(
        int groupId, string? reportingPeriod, CancellationToken ct = default)
    {
        var latestRun = await _db.ConsolidationRuns
            .AsNoTracking()
            .Where(r => r.GroupId == groupId && r.Status == ConsolidationRunStatus.Completed)
            .Where(r => reportingPeriod == null || r.ReportingPeriod == reportingPeriod)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (latestRun is null) return [];

        var snapshots = await _db.ConsolidationSubsidiarySnapshots
            .AsNoTracking()
            .Include(s => s.Subsidiary)
            .Where(s => s.RunId == latestRun.Id)
            .ToListAsync(ct);

        var carMapping = await _db.RegulatoryEquivalenceMappings
            .AsNoTracking()
            .Include(m => m.Entries)
            .FirstOrDefaultAsync(m => m.MappingCode == "CAR_MAPPING" && m.IsActive, ct);

        var thresholds = carMapping?.Entries
            .ToDictionary(e => e.JurisdictionCode, e => e.LocalThreshold)
            ?? new Dictionary<string, decimal>();

        return snapshots.Select(s =>
        {
            var threshold = thresholds.GetValueOrDefault(s.JurisdictionCode, 10.0m);
            return new SubsidiaryComplianceSnapshot
            {
                SubsidiaryId = s.SubsidiaryId,
                SubsidiaryCode = s.Subsidiary?.SubsidiaryCode ?? string.Empty,
                JurisdictionCode = s.JurisdictionCode,
                LocalCurrency = s.LocalCurrency,
                LocalCAR = s.LocalCAR,
                LocalThreshold = threshold,
                IsCompliant = s.LocalCAR >= threshold,
                Gap = s.LocalCAR - threshold,
                ConvertedCapital = new ConvertedAmount
                {
                    SourceAmount = s.LocalTotalCapital, SourceCurrency = s.LocalCurrency,
                    ConvertedValue = s.ConvertedTotalCapital, TargetCurrency = latestRun.BaseCurrency,
                    FxRate = s.FxRateUsed, RateDate = s.FxRateDate, RateSource = s.FxRateSource
                }
            };
        }).ToList();
    }

    public async Task<IReadOnlyList<RegulatoryDeadlineDto>> GetDeadlineCalendarAsync(
        int groupId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
    {
        var jurisdictionCodes = await _db.GroupSubsidiaries
            .AsNoTracking()
            .Where(s => s.GroupId == groupId && s.IsActive)
            .Select(s => s.JurisdictionCode)
            .Distinct()
            .ToListAsync(ct);

        var fromOffset = new DateTimeOffset(fromDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var toOffset = new DateTimeOffset(toDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);

        var deadlines = await _db.RegulatoryDeadlines
            .AsNoTracking()
            .Where(d => jurisdictionCodes.Contains(d.JurisdictionCode)
                && (d.GroupId == null || d.GroupId == groupId)
                && d.DeadlineUtc >= fromOffset && d.DeadlineUtc <= toOffset)
            .OrderBy(d => d.DeadlineUtc)
            .ToListAsync(ct);

        var jurisdictions = await _db.RegulatoryJurisdictions
            .AsNoTracking()
            .Where(j => jurisdictionCodes.Contains(j.JurisdictionCode))
            .ToDictionaryAsync(j => j.JurisdictionCode, j => j.CountryName, ct);

        var now = DateTimeOffset.UtcNow;

        return deadlines.Select(d => new RegulatoryDeadlineDto
        {
            Id = d.Id, JurisdictionCode = d.JurisdictionCode,
            CountryName = jurisdictions.GetValueOrDefault(d.JurisdictionCode, d.JurisdictionCode),
            RegulatorCode = d.RegulatorCode,
            ReturnCode = d.ReturnCode, ReturnName = d.ReturnName,
            ReportingPeriod = d.ReportingPeriod, DeadlineUtc = d.DeadlineUtc,
            LocalTimeZone = d.LocalTimeZone, Frequency = d.Frequency,
            Status = d.Status,
            DaysUntilDeadline = (int)(d.DeadlineUtc - now).TotalDays
        }).ToList();
    }

    public async Task<CrossBorderRiskMetrics?> GetConsolidatedRiskMetricsAsync(
        int groupId, string reportingPeriod, CancellationToken ct = default)
    {
        var run = await _db.ConsolidationRuns
            .AsNoTracking()
            .Where(r => r.GroupId == groupId && r.ReportingPeriod == reportingPeriod && r.Status == ConsolidationRunStatus.Completed)
            .OrderByDescending(r => r.RunNumber)
            .FirstOrDefaultAsync(ct);

        if (run is null) return null;

        var snapshots = await _db.ConsolidationSubsidiarySnapshots
            .AsNoTracking()
            .Include(s => s.Subsidiary)
            .Where(s => s.RunId == run.Id)
            .ToListAsync(ct);

        var totalAssets = snapshots.Sum(s => s.AdjustedTotalAssets);
        var totalRWA = snapshots.Sum(s => s.AdjustedRWA);

        return new CrossBorderRiskMetrics
        {
            GroupId = groupId, ReportingPeriod = reportingPeriod,
            BaseCurrency = run.BaseCurrency,
            ConsolidatedTotalAssets = run.ConsolidatedTotalAssets ?? 0,
            ConsolidatedTotalCapital = run.ConsolidatedTotalCapital ?? 0,
            ConsolidatedRWA = totalRWA,
            ConsolidatedCAR = run.ConsolidatedCAR ?? 0,
            BySubsidiary = snapshots.Select(s => new SubsidiaryRiskContribution
            {
                SubsidiaryCode = s.Subsidiary?.SubsidiaryCode ?? string.Empty,
                JurisdictionCode = s.JurisdictionCode,
                ContributionToAssets = s.AdjustedTotalAssets,
                ContributionToRWA = s.AdjustedRWA,
                ContributionPercentage = totalRWA > 0 ? Math.Round(s.AdjustedRWA / totalRWA * 100m, 2) : 0,
                LocalCAR = s.LocalCAR
            }).ToList()
        };
    }
}
