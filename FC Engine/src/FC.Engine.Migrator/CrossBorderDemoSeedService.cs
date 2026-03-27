using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Migrator;

public sealed class CrossBorderDemoSeedService
{
    private readonly MetadataDbContext _db;
    private readonly ILogger<CrossBorderDemoSeedService> _logger;

    public CrossBorderDemoSeedService(
        MetadataDbContext db,
        ILogger<CrossBorderDemoSeedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CrossBorderDemoSeedResult> SeedAsync(CancellationToken ct = default)
    {
        var groupDefinitions = BuildGroupDefinitions();
        var groupCodes = groupDefinitions.Select(x => x.GroupCode).ToList();

        var groups = await _db.FinancialGroups
            .Where(x => groupCodes.Contains(x.GroupCode) && x.IsActive)
            .ToDictionaryAsync(x => x.GroupCode, StringComparer.OrdinalIgnoreCase, ct);

        if (groups.Count != groupDefinitions.Count)
        {
            var missing = string.Join(", ", groupCodes.Where(code => !groups.ContainsKey(code)));
            throw new InvalidOperationException($"Cross-border demo groups are missing: {missing}.");
        }

        var jurisdictionMap = await _db.RegulatoryJurisdictions
            .AsNoTracking()
            .ToDictionaryAsync(x => x.JurisdictionCode, StringComparer.OrdinalIgnoreCase, ct);

        var mapping = await _db.RegulatoryEquivalenceMappings
            .Include(x => x.Entries)
            .FirstOrDefaultAsync(x => x.MappingCode == "CAR_MAPPING" && x.IsActive, ct)
            ?? throw new InvalidOperationException("CAR_MAPPING equivalence map was not found.");

        var institutionPool = await ResolveInstitutionPoolAsync(ct);
        if (institutionPool.Count == 0)
        {
            throw new InvalidOperationException("No DMB institutions with accepted submissions were found for cross-border demo seeding.");
        }

        await EnsureMappingEntriesAsync(mapping, jurisdictionMap, ct);

        var periods = BuildPeriodDefinitions(DateTime.UtcNow);
        await EnsureFxRatesAsync(periods, ct);

        var groupIds = groups.Values.Select(x => x.Id).ToList();
        await CleanupExistingDemoDataAsync(groupIds, ct);

        var seededSubsidiaries = await SeedSubsidiariesAsync(
            groupDefinitions,
            groups,
            jurisdictionMap,
            institutionPool,
            ct);

        var runResult = await SeedConsolidationRunsAsync(
            groupDefinitions,
            groups,
            seededSubsidiaries,
            periods,
            ct);

        var deadlineCount = await SeedDeadlinesAsync(
            groupDefinitions,
            groups,
            jurisdictionMap,
            ct);

        var divergenceResult = await SeedDivergencesAsync(
            mapping.Id,
            groupDefinitions,
            seededSubsidiaries,
            ct);

        var flowResult = await SeedDataFlowsAsync(
            groupDefinitions,
            groups,
            jurisdictionMap,
            periods[^1],
            ct);

        _logger.LogInformation(
            "Cross-border demo seeded: {Groups} groups, {Subsidiaries} subsidiaries, {Runs} runs, {Snapshots} snapshots, {Deadlines} deadlines, {Divergences} divergences, {Flows} flows, {Executions} executions.",
            groups.Count,
            seededSubsidiaries.Count,
            runResult.RunCount,
            runResult.SnapshotCount,
            deadlineCount,
            divergenceResult.DivergenceCount,
            flowResult.FlowCount,
            flowResult.ExecutionCount);

        return new CrossBorderDemoSeedResult
        {
            GroupsSeeded = groups.Count,
            SubsidiariesSeeded = seededSubsidiaries.Count,
            ConsolidationRunsSeeded = runResult.RunCount,
            ConsolidationSnapshotsSeeded = runResult.SnapshotCount,
            DeadlinesSeeded = deadlineCount,
            DivergencesSeeded = divergenceResult.DivergenceCount,
            NotificationsSeeded = divergenceResult.NotificationCount,
            FlowsSeeded = flowResult.FlowCount,
            ExecutionsSeeded = flowResult.ExecutionCount
        };
    }

    private async Task<List<InstitutionSeedReference>> ResolveInstitutionPoolAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var currentQuarter = ((now.Month - 1) / 3) + 1;

        var preferredRows = await (
                from submission in _db.Submissions.AsNoTracking()
                join period in _db.ReturnPeriods.AsNoTracking() on submission.ReturnPeriodId equals period.Id
                join institution in _db.Institutions.AsNoTracking() on submission.InstitutionId equals institution.Id
                where institution.LicenseType == "DMB"
                    && submission.ParsedDataJson != null
                    && period.Year == now.Year
                    && period.Quarter == currentQuarter
                    && (submission.Status == SubmissionStatus.Accepted
                        || submission.Status == SubmissionStatus.AcceptedWithWarnings
                        || submission.Status == SubmissionStatus.RegulatorAcknowledged
                        || submission.Status == SubmissionStatus.RegulatorAccepted)
                select new
                {
                    institution.Id,
                    institution.InstitutionCode,
                    institution.InstitutionName
                })
            .ToListAsync(ct);

        var preferred = preferredRows
            .GroupBy(x => x.Id)
            .Select(group => group.First())
            .OrderBy(x => x.InstitutionCode, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(x => new InstitutionSeedReference(x.Id, x.InstitutionCode, x.InstitutionName))
            .ToList();

        if (preferred.Count >= 5)
        {
            return preferred;
        }

        var fallback = await _db.Institutions
            .AsNoTracking()
            .Where(x => x.LicenseType == "DMB")
            .OrderBy(x => x.InstitutionCode)
            .Select(x => new InstitutionSeedReference(x.Id, x.InstitutionCode, x.InstitutionName))
            .Take(5)
            .ToListAsync(ct);

        return fallback;
    }

    private async Task EnsureMappingEntriesAsync(
        RegulatoryEquivalenceMapping mapping,
        IReadOnlyDictionary<string, RegulatoryJurisdiction> jurisdictionMap,
        CancellationToken ct)
    {
        var desiredEntries = new[]
        {
            new MappingEntrySeed("CI", 11.00m, "BCEAO_BASEL_TRANSITIONAL"),
            new MappingEntrySeed("RW", 10.25m, "BASEL_III_TRANSITIONAL"),
            new MappingEntrySeed("TZ", 10.75m, "LOCAL_BASEL_TRANSITIONAL")
        };

        var nextDisplayOrder = mapping.Entries.Count == 0
            ? 1
            : mapping.Entries.Max(x => x.DisplayOrder) + 1;

        foreach (var desired in desiredEntries)
        {
            if (!jurisdictionMap.TryGetValue(desired.JurisdictionCode, out var jurisdiction))
            {
                throw new InvalidOperationException($"Jurisdiction {desired.JurisdictionCode} was not found.");
            }

            var existing = mapping.Entries.FirstOrDefault(x =>
                string.Equals(x.JurisdictionCode, desired.JurisdictionCode, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.RegulatorCode = jurisdiction.RegulatorCode;
                existing.LocalThreshold = desired.LocalThreshold;
                existing.RegulatoryFramework = desired.RegulatoryFramework;
                existing.LocalParameterCode = "CAR";
                existing.LocalParameterName = "Capital Adequacy Ratio";
                existing.ThresholdUnit = "PERCENTAGE";
                existing.CalculationBasis = "Tier 1 plus Tier 2 capital divided by risk-weighted assets.";
                existing.ReturnFormCode = "PRUDENTIAL";
                existing.ReturnLineReference = "CAR_TOTAL";
                existing.Notes = "Cross-border demo harmonisation threshold.";
                continue;
            }

            mapping.Entries.Add(new EquivalenceMappingEntry
            {
                JurisdictionCode = desired.JurisdictionCode,
                RegulatorCode = jurisdiction.RegulatorCode,
                LocalParameterCode = "CAR",
                LocalParameterName = "Capital Adequacy Ratio",
                LocalThreshold = desired.LocalThreshold,
                ThresholdUnit = "PERCENTAGE",
                CalculationBasis = "Tier 1 plus Tier 2 capital divided by risk-weighted assets.",
                ReturnFormCode = "PRUDENTIAL",
                ReturnLineReference = "CAR_TOTAL",
                RegulatoryFramework = desired.RegulatoryFramework,
                Notes = "Cross-border demo harmonisation threshold.",
                DisplayOrder = nextDisplayOrder++
            });
        }

        mapping.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureFxRatesAsync(
        IReadOnlyList<CrossBorderPeriodDefinition> periods,
        CancellationToken ct)
    {
        var baseRates = new[]
        {
            new FxRateSeed("NGN", "USD", 0.00064000m, "CBN_DEMO"),
            new FxRateSeed("GHS", "NGN", 126.58227848m, "BOG_DEMO"),
            new FxRateSeed("KES", "NGN", 11.69590643m, "CBK_DEMO"),
            new FxRateSeed("ZAR", "NGN", 84.74576271m, "SARB_DEMO"),
            new FxRateSeed("RWF", "NGN", 1.15606936m, "BNR_DEMO"),
            new FxRateSeed("EGP", "NGN", 31.15264798m, "CBE_DEMO"),
            new FxRateSeed("TZS", "NGN", 0.61728395m, "BOT_DEMO"),
            new FxRateSeed("NGN", "XOF", 0.38950000m, "BCEAO_DEMO"),
            new FxRateSeed("GHS", "XOF", 49.30000000m, "BCEAO_DEMO"),
            new FxRateSeed("KES", "XOF", 4.56000000m, "BCEAO_DEMO"),
            new FxRateSeed("EGP", "XOF", 12.13000000m, "BCEAO_DEMO"),
            new FxRateSeed("ZAR", "XOF", 33.01000000m, "BCEAO_DEMO")
        };

        foreach (var period in periods)
        {
            foreach (var baseRate in baseRates)
            {
                var adjustedRate = Math.Round(baseRate.Rate * period.RateFactor, 8);
                var inverseRate = adjustedRate > 0m ? Math.Round(1m / adjustedRate, 8) : 0m;

                var existing = await _db.CrossBorderFxRates.FirstOrDefaultAsync(x =>
                    x.BaseCurrency == baseRate.BaseCurrency
                    && x.QuoteCurrency == baseRate.QuoteCurrency
                    && x.RateDate == period.SnapshotDate
                    && x.RateType == FxRateType.PeriodEnd, ct);

                if (existing is null)
                {
                    _db.CrossBorderFxRates.Add(new CrossBorderFxRate
                    {
                        BaseCurrency = baseRate.BaseCurrency,
                        QuoteCurrency = baseRate.QuoteCurrency,
                        RateDate = period.SnapshotDate,
                        Rate = adjustedRate,
                        InverseRate = inverseRate,
                        RateSource = baseRate.RateSource,
                        RateType = FxRateType.PeriodEnd,
                        IsActive = true,
                        CreatedAt = period.CreatedAtUtc
                    });
                    continue;
                }

                existing.Rate = adjustedRate;
                existing.InverseRate = inverseRate;
                existing.RateSource = baseRate.RateSource;
                existing.RateType = FxRateType.PeriodEnd;
                existing.IsActive = true;
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task CleanupExistingDemoDataAsync(
        IReadOnlyCollection<int> groupIds,
        CancellationToken ct)
    {
        var flowIds = await _db.CrossBorderDataFlows
            .Where(x => groupIds.Contains(x.GroupId))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (flowIds.Count > 0)
        {
            var executions = await _db.DataFlowExecutions
                .Where(x => flowIds.Contains(x.FlowId))
                .ToListAsync(ct);
            _db.DataFlowExecutions.RemoveRange(executions);
        }

        var flows = await _db.CrossBorderDataFlows
            .Where(x => groupIds.Contains(x.GroupId))
            .ToListAsync(ct);
        _db.CrossBorderDataFlows.RemoveRange(flows);

        var notifications = await _db.DivergenceNotifications.ToListAsync(ct);
        _db.DivergenceNotifications.RemoveRange(notifications);

        var divergences = await _db.RegulatoryDivergences.ToListAsync(ct);
        _db.RegulatoryDivergences.RemoveRange(divergences);

        var deadlines = await _db.RegulatoryDeadlines
            .Where(x => x.GroupId.HasValue && groupIds.Contains(x.GroupId.Value))
            .ToListAsync(ct);
        _db.RegulatoryDeadlines.RemoveRange(deadlines);

        var adjustments = await _db.GroupConsolidationAdjustments
            .Where(x => groupIds.Contains(x.GroupId))
            .ToListAsync(ct);
        _db.GroupConsolidationAdjustments.RemoveRange(adjustments);

        var snapshots = await _db.ConsolidationSubsidiarySnapshots
            .Where(x => groupIds.Contains(x.GroupId))
            .ToListAsync(ct);
        _db.ConsolidationSubsidiarySnapshots.RemoveRange(snapshots);

        var runs = await _db.ConsolidationRuns
            .Where(x => groupIds.Contains(x.GroupId))
            .ToListAsync(ct);
        _db.ConsolidationRuns.RemoveRange(runs);

        var subsidiaries = await _db.GroupSubsidiaries
            .Where(x => groupIds.Contains(x.GroupId))
            .ToListAsync(ct);
        _db.GroupSubsidiaries.RemoveRange(subsidiaries);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<GroupSubsidiary>> SeedSubsidiariesAsync(
        IReadOnlyList<CrossBorderGroupSeedDefinition> groupDefinitions,
        IReadOnlyDictionary<string, FinancialGroup> groups,
        IReadOnlyDictionary<string, RegulatoryJurisdiction> jurisdictions,
        IReadOnlyList<InstitutionSeedReference> institutionPool,
        CancellationToken ct)
    {
        var seeded = new List<GroupSubsidiary>();
        var institutionIndex = 0;

        foreach (var groupDefinition in groupDefinitions)
        {
            var group = groups[groupDefinition.GroupCode];
            foreach (var subsidiaryDefinition in groupDefinition.Subsidiaries)
            {
                if (!jurisdictions.TryGetValue(subsidiaryDefinition.JurisdictionCode, out var jurisdiction))
                {
                    throw new InvalidOperationException($"Jurisdiction {subsidiaryDefinition.JurisdictionCode} was not found.");
                }

                var institution = institutionPool[institutionIndex % institutionPool.Count];
                institutionIndex++;

                seeded.Add(new GroupSubsidiary
                {
                    GroupId = group.Id,
                    InstitutionId = institution.InstitutionId,
                    JurisdictionCode = subsidiaryDefinition.JurisdictionCode,
                    SubsidiaryCode = subsidiaryDefinition.SubsidiaryCode,
                    SubsidiaryName = subsidiaryDefinition.SubsidiaryName,
                    EntityType = subsidiaryDefinition.EntityType,
                    LocalCurrency = jurisdiction.CurrencyCode,
                    OwnershipPercentage = subsidiaryDefinition.OwnershipPercentage,
                    ConsolidationMethod = subsidiaryDefinition.ConsolidationMethod,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-120)
                });
            }
        }

        _db.GroupSubsidiaries.AddRange(seeded);
        await _db.SaveChangesAsync(ct);

        return seeded;
    }

    private async Task<RunSeedResult> SeedConsolidationRunsAsync(
        IReadOnlyList<CrossBorderGroupSeedDefinition> groupDefinitions,
        IReadOnlyDictionary<string, FinancialGroup> groups,
        IReadOnlyList<GroupSubsidiary> seededSubsidiaries,
        IReadOnlyList<CrossBorderPeriodDefinition> periods,
        CancellationToken ct)
    {
        var subsidiariesByCode = seededSubsidiaries.ToDictionary(x => x.SubsidiaryCode, StringComparer.OrdinalIgnoreCase);
        var runCount = 0;
        var snapshotCount = 0;

        foreach (var groupDefinition in groupDefinitions)
        {
            var group = groups[groupDefinition.GroupCode];

            for (var index = 0; index < periods.Count; index++)
            {
                var period = periods[index];
                var run = new ConsolidationRun
                {
                    GroupId = group.Id,
                    RunNumber = index + 1,
                    ReportingPeriod = period.ReportingPeriod,
                    SnapshotDate = period.SnapshotDate,
                    BaseCurrency = group.BaseCurrency,
                    Status = ConsolidationRunStatus.Completed,
                    TotalSubsidiaries = groupDefinition.Subsidiaries.Count,
                    SubsidiariesCollected = groupDefinition.Subsidiaries.Count,
                    CorrelationId = Guid.NewGuid(),
                    ExecutionTimeMs = 380L + (group.Id * 11L) + (index * 23L),
                    CreatedByUserId = 1,
                    CreatedAt = period.CreatedAtUtc.AddMinutes(group.Id * 5),
                    CompletedAt = period.CreatedAtUtc.AddMinutes(group.Id * 5 + 2)
                };

                _db.ConsolidationRuns.Add(run);
                await _db.SaveChangesAsync(ct);

                decimal totalAdjustedAssets = 0m;
                decimal totalAdjustedCapital = 0m;
                decimal totalAdjustedRwa = 0m;
                var totalAdjustments = 0;

                foreach (var subsidiaryDefinition in groupDefinition.Subsidiaries)
                {
                    var subsidiary = subsidiariesByCode[subsidiaryDefinition.SubsidiaryCode];
                    var fxRate = ResolveFxRate(
                        subsidiaryDefinition.LocalCurrency,
                        group.BaseCurrency,
                        period.RateFactor);

                    var localAssets = Math.Round(subsidiaryDefinition.LocalTotalAssets * period.AssetFactor, 2);
                    var localCapital = Math.Round(subsidiaryDefinition.LocalTotalCapital * period.CapitalFactor, 2);
                    var localRwa = Math.Round(subsidiaryDefinition.LocalRwa * period.RwaFactor, 2);
                    var localLiabilities = Math.Max(localAssets - localCapital, 0m);
                    var localCar = localRwa > 0m
                        ? Math.Round(localCapital / localRwa * 100m, 4)
                        : 0m;

                    var convertedAssets = Math.Round(localAssets * fxRate, 2);
                    var convertedLiabilities = Math.Round(localLiabilities * fxRate, 2);
                    var convertedCapital = Math.Round(localCapital * fxRate, 2);
                    var convertedRwa = Math.Round(localRwa * fxRate, 2);

                    var ownershipFactor = subsidiaryDefinition.ConsolidationMethod == ConsolidationMethod.Full
                        ? 1m
                        : subsidiaryDefinition.OwnershipPercentage / 100m;

                    var adjustedAssets = Math.Round(convertedAssets * ownershipFactor, 2);
                    var adjustedCapital = Math.Round(convertedCapital * ownershipFactor, 2);
                    var adjustedRwa = Math.Round(convertedRwa * ownershipFactor, 2);

                    _db.ConsolidationSubsidiarySnapshots.Add(new ConsolidationSubsidiarySnapshot
                    {
                        RunId = run.Id,
                        SubsidiaryId = subsidiary.Id,
                        GroupId = group.Id,
                        JurisdictionCode = subsidiaryDefinition.JurisdictionCode,
                        LocalCurrency = subsidiaryDefinition.LocalCurrency,
                        LocalTotalAssets = localAssets,
                        LocalTotalLiabilities = localLiabilities,
                        LocalTotalCapital = localCapital,
                        LocalRWA = localRwa,
                        LocalCAR = localCar,
                        LocalLCR = subsidiaryDefinition.LocalLcr,
                        LocalNSFR = subsidiaryDefinition.LocalNsfr,
                        FxRateUsed = fxRate,
                        FxRateDate = period.SnapshotDate,
                        FxRateSource = "DEMO_PERIOD_END",
                        ConvertedTotalAssets = convertedAssets,
                        ConvertedTotalLiabilities = convertedLiabilities,
                        ConvertedTotalCapital = convertedCapital,
                        ConvertedRWA = convertedRwa,
                        OwnershipPercentage = subsidiaryDefinition.OwnershipPercentage,
                        ConsolidationMethodUsed = subsidiaryDefinition.ConsolidationMethod.ToString(),
                        AdjustedTotalAssets = adjustedAssets,
                        AdjustedTotalCapital = adjustedCapital,
                        AdjustedRWA = adjustedRwa,
                        SourceReturnInstanceId = null,
                        DataCollectedAt = period.CreatedAtUtc.AddMinutes(1)
                    });

                    totalAdjustedAssets += adjustedAssets;
                    totalAdjustedCapital += adjustedCapital;
                    totalAdjustedRwa += adjustedRwa;
                    snapshotCount++;

                    if (subsidiaryDefinition.ConsolidationMethod == ConsolidationMethod.Full
                        && subsidiaryDefinition.OwnershipPercentage < 100m)
                    {
                        var minorityPercentage = (100m - subsidiaryDefinition.OwnershipPercentage) / 100m;
                        var amount = Math.Round(convertedCapital * minorityPercentage, 2);

                        _db.GroupConsolidationAdjustments.Add(new GroupConsolidationAdjustment
                        {
                            RunId = run.Id,
                            GroupId = group.Id,
                            AdjustmentType = "MINORITY_INTEREST",
                            Description = $"Minority interest allocation for {subsidiaryDefinition.SubsidiaryCode}.",
                            AffectedSubsidiaryId = subsidiary.Id,
                            DebitAccount = "EQUITY",
                            CreditAccount = "MINORITY_INTEREST_RESERVE",
                            Amount = amount,
                            Currency = group.BaseCurrency,
                            IsAutomatic = true,
                            AppliedByUserId = 1,
                            CreatedAt = period.CreatedAtUtc.AddMinutes(2)
                        });
                        totalAdjustments++;
                    }
                }

                run.TotalAdjustments = totalAdjustments;
                run.ConsolidatedTotalAssets = totalAdjustedAssets;
                run.ConsolidatedTotalCapital = totalAdjustedCapital;
                run.ConsolidatedCAR = totalAdjustedRwa > 0m
                    ? Math.Round(totalAdjustedCapital / totalAdjustedRwa * 100m, 4)
                    : 0m;

                runCount++;
                await _db.SaveChangesAsync(ct);
            }
        }

        return new RunSeedResult(runCount, snapshotCount);
    }

    private async Task<int> SeedDeadlinesAsync(
        IReadOnlyList<CrossBorderGroupSeedDefinition> groupDefinitions,
        IReadOnlyDictionary<string, FinancialGroup> groups,
        IReadOnlyDictionary<string, RegulatoryJurisdiction> jurisdictions,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var deadlines = new List<RegulatoryDeadline>();

        foreach (var groupDefinition in groupDefinitions)
        {
            var group = groups[groupDefinition.GroupCode];
            foreach (var deadlineDefinition in groupDefinition.Deadlines)
            {
                var jurisdiction = jurisdictions[deadlineDefinition.JurisdictionCode];
                var deadlineUtc = now.Date.AddDays(deadlineDefinition.DaysFromNow).AddHours(deadlineDefinition.HourUtc);
                var status = deadlineDefinition.DaysFromNow <= 14
                    ? DeadlineStatus.DueSoon
                    : DeadlineStatus.Upcoming;

                deadlines.Add(new RegulatoryDeadline
                {
                    JurisdictionCode = deadlineDefinition.JurisdictionCode,
                    RegulatorCode = jurisdiction.RegulatorCode,
                    ReturnCode = deadlineDefinition.ReturnCode,
                    ReturnName = deadlineDefinition.ReturnName,
                    ReportingPeriod = deadlineDefinition.ReportingPeriod,
                    DeadlineUtc = deadlineUtc,
                    LocalTimeZone = jurisdiction.TimeZoneId,
                    Frequency = deadlineDefinition.Frequency,
                    GroupId = group.Id,
                    Status = status,
                    CreatedAt = now.UtcDateTime.AddDays(-4)
                });
            }
        }

        _db.RegulatoryDeadlines.AddRange(deadlines);
        await _db.SaveChangesAsync(ct);
        return deadlines.Count;
    }

    private async Task<DivergenceSeedResult> SeedDivergencesAsync(
        long mappingId,
        IReadOnlyList<CrossBorderGroupSeedDefinition> groupDefinitions,
        IReadOnlyList<GroupSubsidiary> subsidiaries,
        CancellationToken ct)
    {
        var groupJurisdictions = subsidiaries
            .GroupBy(x => x.GroupId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(x => x.JurisdictionCode).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var divergences = new[]
        {
            new DivergenceSeed(
                "CAPITAL",
                DivergenceType.ThresholdChange,
                "EG",
                "NG,GH,KE,ZA,CI",
                "10.00%",
                "12.50%",
                "Egypt maintains a 12.5% consolidated capital floor while peer jurisdictions remain between 10.0% and 11.0%, forcing multinational groups to warehouse extra buffer in the North Africa corridor.",
                DivergenceSeverity.High,
                DivergenceStatus.Open,
                6),
            new DivergenceSeed(
                "LIQUIDITY",
                DivergenceType.CalculationMethodChange,
                "RW",
                "NG,GH,KE,ZA",
                "30-day stressed outflow basis",
                "21-day stressed outflow basis",
                "Rwanda shortened its stressed-liquidity look-through window from 30 days to 21 days, making group liquidity comparisons materially less consistent for East African subsidiaries.",
                DivergenceSeverity.Critical,
                DivergenceStatus.Open,
                3),
            new DivergenceSeed(
                "RECOVERY",
                DivergenceType.NewRequirement,
                "CI",
                "GH,NG,EG",
                "None",
                "Mandatory group recovery plan attestation",
                "BCEAO introduced a new group recovery-plan attestation for cross-border banking subsidiaries effective Q2 2026, creating an incremental supervisory pack for WAEMU-led groups.",
                DivergenceSeverity.High,
                DivergenceStatus.Tracking,
                9),
            new DivergenceSeed(
                "MARKET_RISK",
                DivergenceType.FrameworkUpgrade,
                "ZA",
                "NG,GH,KE,TZ",
                "Basel III",
                "Basel 3.1 market-risk package",
                "South Africa is already implementing the Basel 3.1 market-risk uplift while several peer jurisdictions remain on transitional Basel III treatment, widening treasury comparability gaps.",
                DivergenceSeverity.High,
                DivergenceStatus.Acknowledged,
                12),
            new DivergenceSeed(
                "REPORTING",
                DivergenceType.ReportingFrequencyChange,
                "TZ",
                "NG,GH,ZA",
                "Monthly FX liquidity filing",
                "Weekly FX liquidity filing",
                "Tanzania moved cross-border FX liquidity reporting from monthly to weekly cadence for higher-risk banking groups, raising operational coordination pressure for regional compliance teams.",
                DivergenceSeverity.Medium,
                DivergenceStatus.Open,
                8),
            new DivergenceSeed(
                "CAPITAL",
                DivergenceType.ThresholdChange,
                "KE",
                "NG,GH,RW",
                "10.00%",
                "10.50%",
                "Kenya continues to maintain a slightly higher capital adequacy trigger than adjacent markets, which keeps Kenyan subsidiaries at the edge of the cross-border watchlist.",
                DivergenceSeverity.Medium,
                DivergenceStatus.Open,
                5)
        };

        var now = DateTime.UtcNow;
        var entities = new List<RegulatoryDivergence>(divergences.Length);
        foreach (var divergence in divergences)
        {
            entities.Add(new RegulatoryDivergence
            {
                MappingId = mappingId,
                ConceptDomain = divergence.ConceptDomain,
                DivergenceType = divergence.DivergenceType,
                SourceJurisdiction = divergence.SourceJurisdiction,
                AffectedJurisdictions = divergence.AffectedJurisdictions,
                PreviousValue = divergence.PreviousValue,
                NewValue = divergence.NewValue,
                Description = divergence.Description,
                Severity = divergence.Severity,
                Status = divergence.Status,
                DetectedAt = now.AddDays(-divergence.DetectedDaysAgo),
                DetectedBySystem = true
            });
        }

        _db.RegulatoryDivergences.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        var notifications = new List<DivergenceNotification>();
        foreach (var divergence in entities)
        {
            var affectedJurisdictions = divergence.AffectedJurisdictions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Append(divergence.SourceJurisdiction)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupDefinitions)
            {
                var groupId = subsidiaries.First(x => x.SubsidiaryCode == group.Subsidiaries[0].SubsidiaryCode).GroupId;
                if (!groupJurisdictions.TryGetValue(groupId, out var jurisdictionsForGroup))
                {
                    continue;
                }

                if (!jurisdictionsForGroup.Overlaps(affectedJurisdictions))
                {
                    continue;
                }

                notifications.Add(new DivergenceNotification
                {
                    DivergenceId = divergence.Id,
                    GroupId = groupId,
                    NotifiedUserId = 0,
                    NotificationChannel = "IN_APP",
                    Status = "SENT",
                    SentAt = divergence.DetectedAt.AddHours(2)
                });
            }
        }

        _db.DivergenceNotifications.AddRange(notifications);
        await _db.SaveChangesAsync(ct);

        return new DivergenceSeedResult(entities.Count, notifications.Count);
    }

    private async Task<DataFlowSeedResult> SeedDataFlowsAsync(
        IReadOnlyList<CrossBorderGroupSeedDefinition> groupDefinitions,
        IReadOnlyDictionary<string, FinancialGroup> groups,
        IReadOnlyDictionary<string, RegulatoryJurisdiction> jurisdictions,
        CrossBorderPeriodDefinition currentPeriod,
        CancellationToken ct)
    {
        var flows = new List<CrossBorderDataFlow>();
        foreach (var groupDefinition in groupDefinitions)
        {
            var group = groups[groupDefinition.GroupCode];
            foreach (var flowDefinition in groupDefinition.FlowDefinitions)
            {
                flows.Add(new CrossBorderDataFlow
                {
                    GroupId = group.Id,
                    FlowCode = flowDefinition.FlowCode,
                    FlowName = flowDefinition.FlowName,
                    SourceJurisdiction = flowDefinition.SourceJurisdiction,
                    SourceReturnCode = flowDefinition.SourceReturnCode,
                    SourceLineCode = flowDefinition.SourceLineCode,
                    TargetJurisdiction = flowDefinition.TargetJurisdiction,
                    TargetReturnCode = flowDefinition.TargetReturnCode,
                    TargetLineCode = flowDefinition.TargetLineCode,
                    TransformationType = flowDefinition.TransformationType,
                    TransformationFormula = flowDefinition.TransformationFormula,
                    RequiresCurrencyConversion = flowDefinition.RequiresCurrencyConversion,
                    IsActive = true,
                    CreatedByUserId = 1,
                    CreatedAt = currentPeriod.CreatedAtUtc.AddHours(-4)
                });
            }
        }

        _db.CrossBorderDataFlows.AddRange(flows);
        await _db.SaveChangesAsync(ct);

        var executions = new List<DataFlowExecution>();
        foreach (var flow in flows)
        {
            var sourceCurrency = jurisdictions[flow.SourceJurisdiction].CurrencyCode;
            var targetCurrency = jurisdictions[flow.TargetJurisdiction].CurrencyCode;
            var sourceValue = ResolveDemoSourceValue(flow.FlowCode);
            decimal? fxRate = null;
            decimal? convertedValue = null;
            var targetValue = sourceValue;

            if (flow.RequiresCurrencyConversion && !string.Equals(sourceCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
            {
                fxRate = ResolveFxRate(sourceCurrency, targetCurrency, currentPeriod.RateFactor);
                convertedValue = Math.Round(sourceValue * fxRate.Value, 6);
                targetValue = convertedValue.Value;
            }

            if (flow.TransformationType == DataFlowTransformation.Proportional)
            {
                var ownership = await _db.GroupSubsidiaries
                    .Where(x => x.GroupId == flow.GroupId && x.JurisdictionCode == flow.TargetJurisdiction)
                    .Select(x => (decimal?)x.OwnershipPercentage)
                    .FirstOrDefaultAsync(ct) ?? 100m;

                targetValue = Math.Round(targetValue * (ownership / 100m), 6);
            }
            else if (flow.TransformationType == DataFlowTransformation.Formula)
            {
                targetValue = Math.Round(targetValue * 1.03m, 6);
            }

            executions.Add(new DataFlowExecution
            {
                FlowId = flow.Id,
                GroupId = flow.GroupId,
                ReportingPeriod = currentPeriod.ReportingPeriod,
                SourceValue = sourceValue,
                SourceCurrency = sourceCurrency,
                FxRateApplied = fxRate,
                ConvertedValue = convertedValue,
                TargetValue = targetValue,
                TargetCurrency = targetCurrency,
                Status = "SUCCESS",
                CorrelationId = Guid.NewGuid(),
                ExecutedAt = currentPeriod.CreatedAtUtc.AddHours(-1)
            });
        }

        _db.DataFlowExecutions.AddRange(executions);
        await _db.SaveChangesAsync(ct);

        return new DataFlowSeedResult(flows.Count, executions.Count);
    }

    private static decimal ResolveFxRate(string sourceCurrency, string targetCurrency, decimal rateFactor)
    {
        if (string.Equals(sourceCurrency, targetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        var baseRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            { "GHS->NGN", 126.58227848m },
            { "KES->NGN", 11.69590643m },
            { "ZAR->NGN", 84.74576271m },
            { "RWF->NGN", 1.15606936m },
            { "EGP->NGN", 31.15264798m },
            { "TZS->NGN", 0.61728395m },
            { "NGN->XOF", 0.38950000m },
            { "GHS->XOF", 49.30000000m },
            { "KES->XOF", 4.56000000m },
            { "EGP->XOF", 12.13000000m },
            { "ZAR->XOF", 33.01000000m },
            { "NGN->USD", 0.00064000m }
        };

        var directKey = $"{sourceCurrency}->{targetCurrency}";
        if (baseRates.TryGetValue(directKey, out var directRate))
        {
            return Math.Round(directRate * rateFactor, 8);
        }

        var reverseKey = $"{targetCurrency}->{sourceCurrency}";
        if (baseRates.TryGetValue(reverseKey, out var reverseRate))
        {
            return Math.Round((1m / reverseRate) * rateFactor, 8);
        }

        throw new InvalidOperationException($"No demo FX rate is configured for {sourceCurrency}/{targetCurrency}.");
    }

    private static decimal ResolveDemoSourceValue(string flowCode)
        => flowCode switch
        {
            "ACCESS_GH_CAP" => 2_900_000m,
            "ACCESS_KE_RWA" => 18_400_000m,
            "ACCESS_ZA_LCR" => 3_000_000m,
            "UBA_GH_CAP" => 2_100_000m,
            "UBA_TZ_LIQ" => 1_240_000_000m,
            "UBA_ZA_TREASURY" => 2_800_000m,
            "ECO_GH_CAP" => 1_750_000m,
            "ECO_NG_LIQ" => 420_000_000m,
            "ECO_EG_CAP" => 14_500_000m,
            _ => 10m
        };

    private static IReadOnlyList<CrossBorderPeriodDefinition> BuildPeriodDefinitions(DateTime utcNow)
    {
        var currentQuarter = ((utcNow.Month - 1) / 3) + 1;
        var currentQuarterStartMonth = ((currentQuarter - 1) * 3) + 1;
        var currentQuarterStart = new DateOnly(utcNow.Year, currentQuarterStartMonth, 1);

        var periods = new List<CrossBorderPeriodDefinition>(3);
        for (var offset = 2; offset >= 0; offset--)
        {
            var periodStart = currentQuarterStart.AddMonths(-3 * offset);
            var periodQuarter = ((periodStart.Month - 1) / 3) + 1;
            var reportingPeriod = $"{periodStart.Year}-Q{periodQuarter}";
            var snapshotDate = offset == 0
                ? DateOnly.FromDateTime(utcNow.Date)
                : periodStart.AddMonths(3).AddDays(-1);

            var factorIndex = 2 - offset;
            periods.Add(factorIndex switch
            {
                0 => new CrossBorderPeriodDefinition(
                    reportingPeriod,
                    snapshotDate,
                    0.9600m,
                    0.9300m,
                    0.9400m,
                    0.9800m,
                    DateTime.SpecifyKind(snapshotDate.ToDateTime(TimeOnly.MinValue).AddDays(1), DateTimeKind.Utc)),
                1 => new CrossBorderPeriodDefinition(
                    reportingPeriod,
                    snapshotDate,
                    0.9850m,
                    0.9700m,
                    0.9750m,
                    0.9900m,
                    DateTime.SpecifyKind(snapshotDate.ToDateTime(TimeOnly.MinValue).AddDays(1), DateTimeKind.Utc)),
                _ => new CrossBorderPeriodDefinition(
                    reportingPeriod,
                    snapshotDate,
                    1.0000m,
                    1.0000m,
                    1.0000m,
                    1.0000m,
                    DateTime.SpecifyKind(snapshotDate.ToDateTime(TimeOnly.MinValue).AddDays(1), DateTimeKind.Utc))
            });
        }

        return periods;
    }

    private static List<CrossBorderGroupSeedDefinition> BuildGroupDefinitions()
    {
        return
        [
            new CrossBorderGroupSeedDefinition(
                "ACCESSGRP",
                [
                    new CrossBorderSubsidiarySeedDefinition("ACCESS-NG", "Access Bank Plc (Nigeria)", "NG", "Commercial Bank", "NGN", 100m, ConsolidationMethod.Full, 4_800_000_000m, 720_000_000m, 4_250_000_000m, 128.0m, 119.0m),
                    new CrossBorderSubsidiarySeedDefinition("ACCESS-GH", "Access Bank Ghana Plc", "GH", "Universal Bank", "GHS", 97.50m, ConsolidationMethod.Full, 24_000_000m, 2_900_000m, 27_500_000m, 116.0m, 111.0m),
                    new CrossBorderSubsidiarySeedDefinition("ACCESS-KE", "Access Bank Kenya Plc", "KE", "Commercial Bank", "KES", 100m, ConsolidationMethod.Full, 225_000_000m, 19_000_000m, 186_000_000m, 98.0m, 95.0m),
                    new CrossBorderSubsidiarySeedDefinition("ACCESS-ZA", "Access Bank South Africa Ltd", "ZA", "Banking Subsidiary", "ZAR", 95.00m, ConsolidationMethod.Full, 31_000_000m, 3_000_000m, 24_000_000m, 121.0m, 113.0m),
                    new CrossBorderSubsidiarySeedDefinition("ACCESS-RW", "Access Bank Rwanda Plc", "RW", "Commercial Bank", "RWF", 100m, ConsolidationMethod.Full, 3_200_000_000m, 230_000_000m, 2_450_000_000m, 93.0m, 89.0m)
                ],
                [
                    new DeadlineSeedDefinition("NG", "CBN_PRU_MAR", "Monthly prudential return", "2026-M03", 5, 10, "Monthly"),
                    new DeadlineSeedDefinition("GH", "AML_Q1_GH", "Quarterly AML and sanctions certification", "2026-Q1", 12, 12, "Quarterly"),
                    new DeadlineSeedDefinition("KE", "CAP_KE_Q1", "Capital adequacy pack", "2026-Q1", 18, 9, "Quarterly"),
                    new DeadlineSeedDefinition("ZA", "LCR_ZA_APR", "Liquidity coverage return", "2026-M04", 29, 14, "Monthly"),
                    new DeadlineSeedDefinition("RW", "FX_RW_W14", "FX exposure schedule", "2026-W14", 8, 8, "Weekly"),
                    new DeadlineSeedDefinition("NG", "GOV_NG_Q2", "Board governance attestation", "2026-Q2", 41, 11, "Quarterly")
                ],
                [
                    new DataFlowSeedDefinition("ACCESS_GH_CAP", "Ghana capital buffer feed into group capital staging", "GH", "GH_CAPITAL", "tier1_total", "NG", "CBN_GRP_CAP", "gh_capital_buffer", DataFlowTransformation.CurrencyConvert, true, null),
                    new DataFlowSeedDefinition("ACCESS_KE_RWA", "Kenya RWA feed into Nigerian group workbook", "KE", "KE_PRUDENTIAL", "rwa_total", "NG", "CBN_GRP_CAP", "ke_rwa_input", DataFlowTransformation.CurrencyConvert, true, null),
                    new DataFlowSeedDefinition("ACCESS_ZA_LCR", "South Africa liquidity line fed proportionally into group treasury view", "ZA", "ZA_TREASURY", "lcr_buffer_amt", "NG", "CBN_GRP_LIQ", "za_liquidity_share", DataFlowTransformation.Proportional, true, null)
                ]),
            new CrossBorderGroupSeedDefinition(
                "UBAGRP",
                [
                    new CrossBorderSubsidiarySeedDefinition("UBA-NG", "United Bank for Africa Plc (Nigeria)", "NG", "Commercial Bank", "NGN", 100m, ConsolidationMethod.Full, 5_400_000_000m, 700_000_000m, 5_000_000_000m, 132.0m, 118.0m),
                    new CrossBorderSubsidiarySeedDefinition("UBA-GH", "UBA Ghana Ltd", "GH", "Universal Bank", "GHS", 95.00m, ConsolidationMethod.Full, 18_000_000m, 2_100_000m, 20_000_000m, 111.0m, 104.0m),
                    new CrossBorderSubsidiarySeedDefinition("UBA-KE", "UBA Kenya Plc", "KE", "Commercial Bank", "KES", 90.00m, ConsolidationMethod.Proportional, 210_000_000m, 23_000_000m, 200_000_000m, 114.0m, 108.0m),
                    new CrossBorderSubsidiarySeedDefinition("UBA-TZ", "UBA Tanzania Ltd", "TZ", "Regional Bank", "TZS", 100m, ConsolidationMethod.Full, 4_900_000_000m, 420_000_000m, 4_400_000_000m, 91.0m, 87.0m),
                    new CrossBorderSubsidiarySeedDefinition("UBA-ZA", "UBA South Africa Ltd", "ZA", "Banking Subsidiary", "ZAR", 85.00m, ConsolidationMethod.Proportional, 28_000_000m, 2_800_000m, 23_000_000m, 118.0m, 109.0m)
                ],
                [
                    new DeadlineSeedDefinition("NG", "CBN_PRU_UBA", "Monthly prudential return", "2026-M03", 6, 10, "Monthly"),
                    new DeadlineSeedDefinition("GH", "FX_GH_Q1", "FX position and large exposure filing", "2026-Q1", 15, 9, "Quarterly"),
                    new DeadlineSeedDefinition("TZ", "AML_TZ_Q1", "Quarterly AML and fraud pack", "2026-Q1", 9, 8, "Quarterly"),
                    new DeadlineSeedDefinition("ZA", "REC_ZA_Q2", "Recovery and resolution attestation", "2026-Q2", 33, 13, "Quarterly"),
                    new DeadlineSeedDefinition("KE", "CONC_KE_Q1", "Concentration risk annex", "2026-Q1", 20, 11, "Quarterly")
                ],
                [
                    new DataFlowSeedDefinition("UBA_GH_CAP", "Ghana capital summary into Nigerian holding-company pack", "GH", "GH_CAPITAL", "capital_buffer", "NG", "UBA_GRP_CAP", "gh_capital_input", DataFlowTransformation.CurrencyConvert, true, null),
                    new DataFlowSeedDefinition("UBA_TZ_LIQ", "Tanzania liquidity pack into Nigerian treasury dashboard", "TZ", "TZ_LIQUIDITY", "liquidity_buffer", "NG", "UBA_GRP_LIQ", "tz_liquidity_input", DataFlowTransformation.CurrencyConvert, true, null),
                    new DataFlowSeedDefinition("UBA_ZA_TREASURY", "South Africa treasury feed apportioned to group market-risk view", "ZA", "ZA_TREASURY", "market_liquidity_buffer", "NG", "UBA_GRP_MKT", "za_treasury_share", DataFlowTransformation.Proportional, true, null)
                ]),
            new CrossBorderGroupSeedDefinition(
                "ECOGRP",
                [
                    new CrossBorderSubsidiarySeedDefinition("ECO-CI", "Ecobank Cote d'Ivoire", "CI", "Universal Bank", "XOF", 100m, ConsolidationMethod.Full, 14_000_000_000m, 1_500_000_000m, 12_000_000_000m, 127.0m, 116.0m),
                    new CrossBorderSubsidiarySeedDefinition("ECO-GH", "Ecobank Ghana Plc", "GH", "Universal Bank", "GHS", 90.00m, ConsolidationMethod.Proportional, 16_000_000m, 1_750_000m, 18_500_000m, 95.0m, 90.0m),
                    new CrossBorderSubsidiarySeedDefinition("ECO-NG", "Ecobank Nigeria Ltd", "NG", "Commercial Bank", "NGN", 80.00m, ConsolidationMethod.Proportional, 3_900_000_000m, 420_000_000m, 3_600_000_000m, 109.0m, 101.0m),
                    new CrossBorderSubsidiarySeedDefinition("ECO-KE", "Ecobank Kenya Ltd", "KE", "Regional Bank", "KES", 100m, ConsolidationMethod.Full, 170_000_000m, 15_000_000m, 156_000_000m, 92.0m, 88.0m),
                    new CrossBorderSubsidiarySeedDefinition("ECO-EG", "Ecobank Egypt SAE", "EG", "Commercial Bank", "EGP", 100m, ConsolidationMethod.Full, 128_000_000m, 14_500_000m, 110_000_000m, 121.0m, 113.0m)
                ],
                [
                    new DeadlineSeedDefinition("CI", "BCEAO_SOLV_Q1", "WAEMU solvency return", "2026-Q1", 4, 10, "Quarterly"),
                    new DeadlineSeedDefinition("GH", "GH_PRU_Q1", "Quarterly prudential package", "2026-Q1", 17, 11, "Quarterly"),
                    new DeadlineSeedDefinition("NG", "NG_FX_Q1", "FX control and position return", "2026-Q1", 11, 9, "Quarterly"),
                    new DeadlineSeedDefinition("EG", "EG_CAP_Q1", "Capital conservation buffer schedule", "2026-Q1", 23, 13, "Quarterly"),
                    new DeadlineSeedDefinition("KE", "KE_RES_Q2", "Resolution planning annex", "2026-Q2", 36, 12, "Quarterly")
                ],
                [
                    new DataFlowSeedDefinition("ECO_GH_CAP", "Ghana capital feed into WAEMU consolidated capital bridge", "GH", "GH_CAPITAL", "capital_buffer", "CI", "WAEMU_GRP_CAP", "gh_capital_bridge", DataFlowTransformation.CurrencyConvert, true, null),
                    new DataFlowSeedDefinition("ECO_NG_LIQ", "Nigeria liquidity reserves apportioned into WAEMU liquidity workbook", "NG", "NG_LIQUIDITY", "liquidity_buffer", "CI", "WAEMU_GRP_LIQ", "ng_liquidity_share", DataFlowTransformation.Proportional, true, null),
                    new DataFlowSeedDefinition("ECO_EG_CAP", "Egypt capital buffer converted into WAEMU supervisory view", "EG", "EG_CAPITAL", "capital_buffer", "CI", "WAEMU_GRP_CAP", "eg_capital_input", DataFlowTransformation.Formula, true, "TARGET = SOURCE * FX * 1.03")
                ])
        ];
    }
}

public sealed class CrossBorderDemoSeedResult
{
    public int GroupsSeeded { get; init; }
    public int SubsidiariesSeeded { get; init; }
    public int ConsolidationRunsSeeded { get; init; }
    public int ConsolidationSnapshotsSeeded { get; init; }
    public int DeadlinesSeeded { get; init; }
    public int DivergencesSeeded { get; init; }
    public int NotificationsSeeded { get; init; }
    public int FlowsSeeded { get; init; }
    public int ExecutionsSeeded { get; init; }
}

internal sealed record InstitutionSeedReference(
    int InstitutionId,
    string InstitutionCode,
    string InstitutionName);

internal sealed record MappingEntrySeed(
    string JurisdictionCode,
    decimal LocalThreshold,
    string RegulatoryFramework);

internal sealed record FxRateSeed(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string RateSource);

internal sealed record CrossBorderPeriodDefinition(
    string ReportingPeriod,
    DateOnly SnapshotDate,
    decimal RateFactor,
    decimal AssetFactor,
    decimal CapitalFactor,
    decimal RwaFactor,
    DateTime CreatedAtUtc);

internal sealed record CrossBorderGroupSeedDefinition(
    string GroupCode,
    IReadOnlyList<CrossBorderSubsidiarySeedDefinition> Subsidiaries,
    IReadOnlyList<DeadlineSeedDefinition> Deadlines,
    IReadOnlyList<DataFlowSeedDefinition> FlowDefinitions);

internal sealed record CrossBorderSubsidiarySeedDefinition(
    string SubsidiaryCode,
    string SubsidiaryName,
    string JurisdictionCode,
    string EntityType,
    string LocalCurrency,
    decimal OwnershipPercentage,
    ConsolidationMethod ConsolidationMethod,
    decimal LocalTotalAssets,
    decimal LocalTotalCapital,
    decimal LocalRwa,
    decimal LocalLcr,
    decimal LocalNsfr);

internal sealed record DeadlineSeedDefinition(
    string JurisdictionCode,
    string ReturnCode,
    string ReturnName,
    string ReportingPeriod,
    int DaysFromNow,
    int HourUtc,
    string Frequency);

internal sealed record DivergenceSeed(
    string ConceptDomain,
    DivergenceType DivergenceType,
    string SourceJurisdiction,
    string AffectedJurisdictions,
    string PreviousValue,
    string NewValue,
    string Description,
    DivergenceSeverity Severity,
    DivergenceStatus Status,
    int DetectedDaysAgo);

internal sealed record DataFlowSeedDefinition(
    string FlowCode,
    string FlowName,
    string SourceJurisdiction,
    string SourceReturnCode,
    string SourceLineCode,
    string TargetJurisdiction,
    string TargetReturnCode,
    string TargetLineCode,
    DataFlowTransformation TransformationType,
    bool RequiresCurrencyConversion,
    string? TransformationFormula);

internal sealed record RunSeedResult(
    int RunCount,
    int SnapshotCount);

internal sealed record DivergenceSeedResult(
    int DivergenceCount,
    int NotificationCount);

internal sealed record DataFlowSeedResult(
    int FlowCount,
    int ExecutionCount);
