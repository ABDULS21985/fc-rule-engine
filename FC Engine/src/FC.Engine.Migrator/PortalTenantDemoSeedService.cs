using System.Globalization;
using FC.Engine.Application.Services;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Migrator;

public sealed class PortalTenantDemoSeedService
{
    private const string TargetTenantSlug = "buzzwallet-bdc-ltd";
    private const string TargetInstitutionCode = "BUZZWALL";
    private const string TargetModuleCode = "BDC_CBN";
    private const string SharedDemoPassword = "Admin@FcEngine2026!";
    private readonly MetadataDbContext _db;
    private readonly ILogger<PortalTenantDemoSeedService> _logger;

    public PortalTenantDemoSeedService(
        MetadataDbContext db,
        ILogger<PortalTenantDemoSeedService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PortalTenantDemoSeedResult> SeedComplianceIqAsync(CancellationToken ct = default)
    {
        var target = await (
                from institution in _db.Institutions
                join tenant in _db.Tenants on institution.TenantId equals tenant.TenantId
                where tenant.TenantSlug == TargetTenantSlug
                select new TenantInstitutionRef(
                    institution.Id,
                    institution.TenantId,
                    institution.InstitutionCode,
                    institution.InstitutionName,
                    tenant.TenantSlug))
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Portal demo tenant '{TargetTenantSlug}' was not found.");

        var module = await _db.Modules
            .SingleOrDefaultAsync(x => x.ModuleCode == TargetModuleCode, ct)
            ?? throw new InvalidOperationException($"Module '{TargetModuleCode}' was not found.");

        var historicalSeries = BuildHistoricalSeries();
        var currentSnapshot = BuildCurrentDraftSnapshot();

        var result = new PortalTenantDemoSeedResult();
        result.InstitutionUsersUpdated = await EnsureInstitutionUsersAsync(target, ct);

        foreach (var historical in historicalSeries)
        {
            var period = await EnsureMonthlyPeriodAsync(
                target.TenantId,
                module,
                historical.Year,
                historical.Month,
                isOpen: false,
                status: "Completed",
                ct);

            if (period.WasCreated)
            {
                result.PeriodsCreated++;
            }

            var submission = await UpsertSubmissionAsync(
                target,
                period.Period,
                historical,
                ct);

            if (submission.WasCreated)
            {
                result.SubmissionsCreated++;
            }
            else
            {
                result.SubmissionsUpdated++;
            }

            result.ValidationReportsSeeded += await UpsertValidationReportAsync(
                submission.Submission,
                historical.Status,
                historical.ValidationNote,
                ct);

            if (await UpsertSlaRecordAsync(submission.Submission, period.Period, ct))
            {
                result.SlaRecordsUpserted++;
            }
        }

        foreach (var peerSeed in BuildAcceptedPeerSnapshots())
        {
            var peerInstitution = await ResolveInstitutionAsync(peerSeed.InstitutionCode, ct);
            var peerPeriod = await EnsureMonthlyPeriodAsync(
                peerInstitution.TenantId,
                module,
                peerSeed.Snapshot.Year,
                peerSeed.Snapshot.Month,
                isOpen: true,
                status: "Open",
                ct);

            if (peerPeriod.WasCreated)
            {
                result.PeriodsCreated++;
            }

            var peerSubmission = await UpsertCurrentAcceptedAsync(
                peerInstitution,
                peerPeriod.Period,
                peerSeed.Snapshot,
                ct);

            if (peerSubmission.WasCreated)
            {
                result.SubmissionsCreated++;
            }
            else
            {
                result.SubmissionsUpdated++;
            }

            result.ValidationReportsSeeded += await UpsertValidationReportAsync(
                peerSubmission.Submission,
                peerSeed.Snapshot.Status,
                peerSeed.Snapshot.ValidationNote,
                ct);

            if (await UpsertSlaRecordAsync(peerSubmission.Submission, peerPeriod.Period, ct))
            {
                result.SlaRecordsUpserted++;
            }
        }

        var currentPeriodResult = await EnsureMonthlyPeriodAsync(
            target.TenantId,
            module,
            currentSnapshot.Year,
            currentSnapshot.Month,
            isOpen: true,
            status: "Open",
            ct);

        if (currentPeriodResult.WasCreated)
        {
            result.PeriodsCreated++;
        }

        var currentSubmission = await UpsertCurrentDraftAsync(
            target,
            currentPeriodResult.Period,
            currentSnapshot,
            ct);

        if (currentSubmission.WasCreated)
        {
            result.SubmissionsCreated++;
        }
        else
        {
            result.SubmissionsUpdated++;
        }

        result.ValidationReportsSeeded += await UpsertValidationReportAsync(
            currentSubmission.Submission,
            currentSnapshot.Status,
            currentSnapshot.ValidationNote,
            ct);

        if (await UpsertChsSnapshotAsync(target, currentSnapshot, ct))
        {
            result.ChsSnapshotsUpserted++;
        }

        result.PeerStatsUpserted += await UpsertPeerStatisticsAsync(
            target.TenantId,
            currentSnapshot.Year,
            currentSnapshot.Month,
            ct);

        _logger.LogInformation(
            "Portal tenant demo seeded for {TenantSlug}: {PeriodsCreated} periods, {SubmissionsCreated} submissions created, {SubmissionsUpdated} submissions updated, {PeerStatsUpserted} peer stats refreshed.",
            target.TenantSlug,
            result.PeriodsCreated,
            result.SubmissionsCreated,
            result.SubmissionsUpdated,
            result.PeerStatsUpserted);

        return result;
    }

    private async Task<int> EnsureInstitutionUsersAsync(TenantInstitutionRef target, CancellationToken ct)
    {
        var users = await _db.InstitutionUsers
            .Where(x => x.InstitutionId == target.InstitutionId)
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            _db.InstitutionUsers.Add(new InstitutionUser
            {
                TenantId = target.TenantId,
                InstitutionId = target.InstitutionId,
                Username = "lukman",
                Email = "lukman@buzz.com",
                DisplayName = "Sadiq Lukman",
                PasswordHash = InstitutionAuthService.HashPassword(SharedDemoPassword),
                Role = InstitutionRole.Admin,
                IsActive = true,
                MustChangePassword = false,
                FailedLoginAttempts = 0,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            return 1;
        }

        foreach (var user in users)
        {
            user.PasswordHash = InstitutionAuthService.HashPassword(SharedDemoPassword);
            user.MustChangePassword = false;
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            user.IsActive = true;
        }

        await _db.SaveChangesAsync(ct);
        return users.Count;
    }

    private async Task<PeriodSeedResult> EnsureMonthlyPeriodAsync(
        Guid tenantId,
        Module module,
        int year,
        int month,
        bool isOpen,
        string status,
        CancellationToken ct)
    {
        var existing = await _db.ReturnPeriods
            .FirstOrDefaultAsync(x =>
                x.TenantId == tenantId &&
                x.ModuleId == module.Id &&
                x.Year == year &&
                x.Month == month,
                ct);

        if (existing is not null)
        {
            existing.IsOpen = isOpen;
            existing.Status = status;
            existing.Frequency = "Monthly";
            existing.ReportingDate = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);
            existing.DeadlineDate = existing.ReportingDate.AddDays(15);
            await _db.SaveChangesAsync(ct);
            return new PeriodSeedResult(existing, false);
        }

        var period = new ReturnPeriod
        {
            TenantId = tenantId,
            ModuleId = module.Id,
            Module = module,
            Year = year,
            Month = month,
            Quarter = null,
            Frequency = "Monthly",
            ReportingDate = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc),
            DeadlineDate = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc).AddDays(15),
            IsOpen = isOpen,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

        _db.ReturnPeriods.Add(period);
        await _db.SaveChangesAsync(ct);
        return new PeriodSeedResult(period, true);
    }

    private async Task<SubmissionSeedResult> UpsertSubmissionAsync(
        TenantInstitutionRef target,
        ReturnPeriod period,
        ComplianceSnapshotSeed snapshot,
        CancellationToken ct)
    {
        var existing = await _db.Submissions
            .FirstOrDefaultAsync(x =>
                x.TenantId == target.TenantId &&
                x.ReturnPeriodId == period.Id &&
                x.ReturnCode == TargetModuleCode,
                ct);

        var recordJson = BuildSubmissionPayload(target, snapshot);
        if (existing is null)
        {
            existing = Submission.Create(target.InstitutionId, period.Id, TargetModuleCode, target.TenantId);
            existing.CreatedAt = snapshot.SubmittedAt.AddMinutes(-12);
            existing.SubmittedAt = snapshot.SubmittedAt;
            existing.ApprovalRequired = false;
            existing.ProcessingDurationMs = 180;
            existing.StoreParsedDataJson(recordJson);
            existing.Status = snapshot.Status;
            _db.Submissions.Add(existing);
            await _db.SaveChangesAsync(ct);
            return new SubmissionSeedResult(existing, true);
        }

        existing.InstitutionId = target.InstitutionId;
        existing.ReturnCode = TargetModuleCode;
        existing.CreatedAt = snapshot.SubmittedAt.AddMinutes(-12);
        existing.SubmittedAt = snapshot.SubmittedAt;
        existing.ApprovalRequired = false;
        existing.ProcessingDurationMs = 180;
        existing.StoreParsedDataJson(recordJson);
        existing.Status = snapshot.Status;
        await _db.SaveChangesAsync(ct);
        return new SubmissionSeedResult(existing, false);
    }

    private async Task<SubmissionSeedResult> UpsertCurrentAcceptedAsync(
        TenantInstitutionRef target,
        ReturnPeriod period,
        ComplianceSnapshotSeed snapshot,
        CancellationToken ct)
    {
        var existing = await _db.Submissions
            .Where(x =>
                x.TenantId == target.TenantId &&
                x.ReturnPeriodId == period.Id &&
                x.ReturnCode == TargetModuleCode)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var recordJson = BuildSubmissionPayload(target, snapshot);
        if (existing is null)
        {
            existing = Submission.Create(target.InstitutionId, period.Id, TargetModuleCode, target.TenantId);
            existing.CreatedAt = snapshot.SubmittedAt.AddMinutes(-28);
            existing.SubmittedAt = snapshot.SubmittedAt;
            existing.ApprovalRequired = false;
            existing.ProcessingDurationMs = 146;
            existing.StoreParsedDataJson(recordJson);
            existing.Status = snapshot.Status;
            _db.Submissions.Add(existing);
            await _db.SaveChangesAsync(ct);
            return new SubmissionSeedResult(existing, true);
        }

        existing.InstitutionId = target.InstitutionId;
        existing.ReturnCode = TargetModuleCode;
        existing.CreatedAt = snapshot.SubmittedAt.AddMinutes(-28);
        existing.SubmittedAt = snapshot.SubmittedAt;
        existing.ApprovalRequired = false;
        existing.ProcessingDurationMs = 146;
        existing.StoreParsedDataJson(recordJson);
        existing.Status = snapshot.Status;
        await _db.SaveChangesAsync(ct);
        return new SubmissionSeedResult(existing, false);
    }

    private async Task<SubmissionSeedResult> UpsertCurrentDraftAsync(
        TenantInstitutionRef target,
        ReturnPeriod period,
        ComplianceSnapshotSeed snapshot,
        CancellationToken ct)
    {
        var existing = await _db.Submissions
            .Where(x =>
                x.TenantId == target.TenantId &&
                x.ReturnPeriodId == period.Id &&
                x.ReturnCode == TargetModuleCode)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        var recordJson = BuildSubmissionPayload(target, snapshot);
        if (existing is null)
        {
            existing = Submission.Create(target.InstitutionId, period.Id, TargetModuleCode, target.TenantId);
            existing.CreatedAt = DateTime.UtcNow.AddHours(-4);
            existing.SubmittedAt = DateTime.UtcNow.AddHours(-2);
            existing.ApprovalRequired = false;
            existing.ProcessingDurationMs = 125;
            existing.StoreParsedDataJson(recordJson);
            existing.Status = SubmissionStatus.Draft;
            _db.Submissions.Add(existing);
            await _db.SaveChangesAsync(ct);
            return new SubmissionSeedResult(existing, true);
        }

        existing.InstitutionId = target.InstitutionId;
        existing.ReturnCode = TargetModuleCode;
        existing.ApprovalRequired = false;
        existing.ProcessingDurationMs = 125;
        existing.StoreParsedDataJson(recordJson);
        existing.Status = SubmissionStatus.Draft;
        existing.SubmittedAt ??= DateTime.UtcNow.AddHours(-2);
        await _db.SaveChangesAsync(ct);
        return new SubmissionSeedResult(existing, false);
    }

    private async Task<int> UpsertValidationReportAsync(
        Submission submission,
        SubmissionStatus submissionStatus,
        string? validationNote,
        CancellationToken ct)
    {
        var existingReportIds = await _db.ValidationReports
            .Where(x => x.SubmissionId == submission.Id)
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (existingReportIds.Count > 0)
        {
            var existingErrors = await _db.ValidationErrors
                .Where(x => existingReportIds.Contains(x.ValidationReportId))
                .ToListAsync(ct);
            _db.ValidationErrors.RemoveRange(existingErrors);

            var existingReports = await _db.ValidationReports
                .Where(x => existingReportIds.Contains(x.Id))
                .ToListAsync(ct);
            _db.ValidationReports.RemoveRange(existingReports);
            await _db.SaveChangesAsync(ct);
        }

        var report = ValidationReport.Create(submission.Id, submission.TenantId);
        if (!string.IsNullOrWhiteSpace(validationNote))
        {
            report.AddError(new ValidationError
            {
                RuleId = submissionStatus == SubmissionStatus.Draft ? "BDC-WIP-001" : "BDC-QA-001",
                Field = "CarRatio",
                Message = validationNote,
                Severity = ValidationSeverity.Warning,
                Category = ValidationCategory.Business,
                ExpectedValue = "Regulatory evidence pack attached",
                ActualValue = "Pending workspace finalisation"
            });
        }

        report.FinalizeAt(DateTime.UtcNow);
        _db.ValidationReports.Add(report);
        await _db.SaveChangesAsync(ct);
        return 1;
    }

    private async Task<bool> UpsertSlaRecordAsync(Submission submission, ReturnPeriod period, CancellationToken ct)
    {
        if (submission.SubmittedAt is null)
        {
            return false;
        }

        var existing = await _db.FilingSlaRecords
            .FirstOrDefaultAsync(x =>
                x.TenantId == submission.TenantId &&
                x.ModuleId == period.ModuleId &&
                x.PeriodId == period.Id,
                ct);

        var submittedDate = submission.SubmittedAt.Value.Date;
        var daysToDeadline = (period.EffectiveDeadline.Date - submittedDate).Days;
        var periodEndDate = new DateTime(period.Year, period.Month, DateTime.DaysInMonth(period.Year, period.Month), 0, 0, 0, DateTimeKind.Utc);

        if (existing is null)
        {
            _db.FilingSlaRecords.Add(new FilingSlaRecord
            {
                TenantId = submission.TenantId,
                ModuleId = period.ModuleId ?? 0,
                PeriodId = period.Id,
                SubmissionId = submission.Id,
                PeriodEndDate = periodEndDate,
                DeadlineDate = period.EffectiveDeadline,
                SubmittedDate = submittedDate,
                DaysToDeadline = daysToDeadline,
                OnTime = daysToDeadline >= 0
            });
        }
        else
        {
            existing.SubmissionId = submission.Id;
            existing.PeriodEndDate = periodEndDate;
            existing.DeadlineDate = period.EffectiveDeadline;
            existing.SubmittedDate = submittedDate;
            existing.DaysToDeadline = daysToDeadline;
            existing.OnTime = daysToDeadline >= 0;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> UpsertChsSnapshotAsync(
        TenantInstitutionRef target,
        ComplianceSnapshotSeed snapshot,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var periodLabel = $"{now.Year}-W{ISOWeek.GetWeekOfYear(now):00}";

        var existing = await _db.ChsScoreSnapshots
            .OrderByDescending(x => x.ComputedAt)
            .FirstOrDefaultAsync(x => x.TenantId == target.TenantId && x.PeriodLabel == periodLabel, ct);

        if (existing is null)
        {
            existing = new ChsScoreSnapshot
            {
                TenantId = target.TenantId,
                PeriodLabel = periodLabel
            };
            _db.ChsScoreSnapshots.Add(existing);
        }

        existing.ComputedAt = now;
        existing.OverallScore = snapshot.ComplianceHealthScore;
        existing.Rating = MapChsRating(snapshot.ComplianceHealthScore);
        existing.FilingTimeliness = snapshot.FilingTimeliness;
        existing.DataQuality = snapshot.DataQuality;
        existing.RegulatoryCapital = snapshot.RegulatoryCapital;
        existing.AuditGovernance = snapshot.AuditGovernance;
        existing.Engagement = snapshot.Engagement;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<int> UpsertPeerStatisticsAsync(
        Guid currentTenantId,
        int year,
        int month,
        CancellationToken ct)
    {
        var activeVersion = await _db.AnomalyModelVersions
            .AsNoTracking()
            .Where(x => x.ModuleCode == TargetModuleCode && x.Status == "ACTIVE")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (activeVersion is null)
        {
            _logger.LogWarning(
                "Portal demo peer stats could not be refreshed because no active anomaly model version exists for module {ModuleCode}.",
                TargetModuleCode);
            return 0;
        }

        var rawRows = await _db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
            .ThenInclude(x => x!.Module)
            .Include(x => x.Institution)
            .Where(x =>
                x.ParsedDataJson != null &&
                x.ReturnPeriod != null &&
                x.ReturnPeriod.Module != null &&
                x.ReturnPeriod.Module.ModuleCode == TargetModuleCode &&
                x.ReturnPeriod.Year == year &&
                x.ReturnPeriod.Month == month &&
                x.Institution != null &&
                !string.IsNullOrWhiteSpace(x.Institution.LicenseType) &&
                (x.Status == SubmissionStatus.Accepted ||
                 x.Status == SubmissionStatus.AcceptedWithWarnings ||
                 x.Status == SubmissionStatus.RegulatorAcknowledged ||
                 x.Status == SubmissionStatus.RegulatorAccepted ||
                 x.Status == SubmissionStatus.RegulatorQueriesRaised ||
                 x.Status == SubmissionStatus.Historical))
            .Select(x => new PeerSeedRow(
                x.TenantId,
                x.Institution!.LicenseType!,
                x.ParsedDataJson!,
                x.SubmittedAt ?? x.CreatedAt))
            .ToListAsync(ct);

        var cohort = rawRows
            .Where(x => x.TenantId != currentTenantId)
            .GroupBy(x => x.TenantId)
            .Select(group => group
                .OrderByDescending(x => x.EffectiveAt)
                .First())
            .ToList();

        if (cohort.Count == 0)
        {
            _logger.LogWarning(
                "Portal demo peer stats could not be refreshed because no accepted peer cohort was found for module {ModuleCode} {Year}-{Month:00}.",
                TargetModuleCode,
                year,
                month);
            return 0;
        }

        var monthPeriodCode = $"{year:D4}-{month:D2}";
        var quarterPeriodCode = $"{year:D4}-Q{((month - 1) / 3) + 1}";
        var periodCodes = new[]
        {
            monthPeriodCode,
            quarterPeriodCode
        };

        var metricGroups = cohort
            .SelectMany(row =>
                ExtractSubmissionMetrics(row.ParsedDataJson).Select(metric => new
                {
                    row.LicenseType,
                    metric.Key,
                    metric.Value
                }))
            .GroupBy(
                x => new { FieldCode = x.Key, x.LicenseType },
                x => x.Value)
            .ToList();

        if (metricGroups.Count == 0)
        {
            _logger.LogWarning(
                "Portal demo peer stats could not be refreshed because no metrics were extractable from the accepted BDC cohort for {Year}-{Month:00}.",
                year,
                month);
            return 0;
        }

        var existingRows = await _db.AnomalyPeerGroupStatistics
            .Where(x =>
                x.ModelVersionId == activeVersion.Id &&
                x.ModuleCode == TargetModuleCode &&
                (x.PeriodCode == monthPeriodCode || x.PeriodCode == quarterPeriodCode))
            .ToListAsync(ct);

        var upsertCount = 0;
        foreach (var periodCode in periodCodes)
        {
            foreach (var group in metricGroups)
            {
                var values = group
                    .OrderBy(x => x)
                    .ToList();

                if (values.Count == 0)
                {
                    continue;
                }

                var existing = existingRows.FirstOrDefault(x =>
                    x.FieldCode == group.Key.FieldCode &&
                    x.LicenceCategory == group.Key.LicenseType &&
                    x.PeriodCode == periodCode &&
                    x.InstitutionSizeBand == "ALL");

                if (existing is null)
                {
                    existing = new AnomalyPeerGroupStatistic
                    {
                        ModelVersionId = activeVersion.Id,
                        ModuleCode = TargetModuleCode,
                        FieldCode = group.Key.FieldCode,
                        LicenceCategory = group.Key.LicenseType,
                        InstitutionSizeBand = "ALL",
                        PeriodCode = periodCode,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.AnomalyPeerGroupStatistics.Add(existing);
                    existingRows.Add(existing);
                }

                existing.PeerCount = values.Count;
                existing.PeerMean = decimal.Round(values.Average(), 2);
                existing.PeerMedian = decimal.Round(Median(values), 2);
                existing.PeerStdDev = values.Count <= 1 ? null : decimal.Round(StandardDeviation(values), 2);
                existing.PeerQ1 = decimal.Round(Percentile(values, 25m), 2);
                existing.PeerQ3 = decimal.Round(Percentile(values, 75m), 2);
                existing.PeerMin = values.First();
                existing.PeerMax = values.Last();
                upsertCount++;
            }
        }

        await _db.SaveChangesAsync(ct);
        return upsertCount;
    }

    private static string BuildSubmissionPayload(TenantInstitutionRef target, ComplianceSnapshotSeed snapshot)
    {
        var record = new ReturnDataRecord(TargetModuleCode, 0, StructuralCategory.FixedRow);
        var row = new ReturnDataRow();
        row.SetValue("InstitutionCode", string.IsNullOrWhiteSpace(target.InstitutionCode) ? TargetInstitutionCode : target.InstitutionCode);
        row.SetValue("InstitutionName", target.InstitutionName);
        row.SetValue("CarRatio", snapshot.CarRatio);
        row.SetValue("NplRatio", snapshot.NplRatio);
        row.SetValue("LiquidityRatio", snapshot.LiquidityRatio);
        row.SetValue("LoanDepositRatio", snapshot.LoanDepositRatio);
        row.SetValue("TotalAssets", snapshot.TotalAssets);
        row.SetValue("RiskWeightedAssets", snapshot.RiskWeightedAssets);
        row.SetValue("CapitalBase", snapshot.CapitalBase);
        row.SetValue("FxNetPositionRatio", snapshot.FxNetPositionRatio);
        row.SetValue("OpenTradeCount", snapshot.OpenTradeCount);
        row.SetValue("ComplianceBreaches", snapshot.ComplianceBreaches);
        record.AddRow(row);
        return SubmissionPayloadSerializer.Serialize(record);
    }

    private static Dictionary<string, decimal> ExtractSubmissionMetrics(string? json)
    {
        var metrics = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return metrics;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Rows", out var rows) ||
                rows.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return metrics;
            }

            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("Fields", out var fields) ||
                    fields.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var field in fields.EnumerateObject())
                {
                    if (!TryReadDecimal(field.Value, out var value))
                    {
                        continue;
                    }

                    var normalized = NormalizeFieldCode(field.Name);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    metrics[normalized] = metrics.TryGetValue(normalized, out var current)
                        ? current + value
                        : value;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        return metrics;
    }

    private static bool TryReadDecimal(System.Text.Json.JsonElement value, out decimal number)
    {
        switch (value.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Number:
                return value.TryGetDecimal(out number);
            case System.Text.Json.JsonValueKind.String:
                return decimal.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
            default:
                number = 0m;
                return false;
        }
    }

    private static string NormalizeFieldCode(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder(fieldName.Length);
        foreach (var character in fieldName)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(char.ToLowerInvariant(character));
            }
        }

        return buffer.ToString();
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var ordered = values.OrderBy(x => x).ToArray();
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    private static decimal Percentile(IReadOnlyList<decimal> values, decimal percentile)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var ordered = values.OrderBy(x => x).ToArray();
        if (ordered.Length == 1)
        {
            return ordered[0];
        }

        var rank = (percentile / 100m) * (ordered.Length - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
        {
            return ordered[lowerIndex];
        }

        var weight = rank - lowerIndex;
        return ordered[lowerIndex] + ((ordered[upperIndex] - ordered[lowerIndex]) * weight);
    }

    private static decimal StandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count <= 1)
        {
            return 0m;
        }

        var mean = values.Average();
        decimal variance = 0m;
        foreach (var value in values)
        {
            var diff = value - mean;
            variance += diff * diff;
        }

        variance /= values.Count - 1;
        return (decimal)Math.Sqrt((double)variance);
    }

    private static int MapChsRating(decimal overallScore)
        => (int)(overallScore switch
        {
            >= 90m => FC.Engine.Domain.Models.ChsRating.APlus,
            >= 80m => FC.Engine.Domain.Models.ChsRating.A,
            >= 70m => FC.Engine.Domain.Models.ChsRating.B,
            >= 60m => FC.Engine.Domain.Models.ChsRating.C,
            >= 50m => FC.Engine.Domain.Models.ChsRating.D,
            _ => FC.Engine.Domain.Models.ChsRating.F
        });

    private static List<ComplianceSnapshotSeed> BuildHistoricalSeries() =>
    [
        new ComplianceSnapshotSeed(2025, 8, 13.8m, 7.1m, 29.4m, 84.2m, 1_080_000_000m, 760_000_000m, 108_500_000m, 7.8m, 84, 5, SubmissionStatus.Historical, new DateTime(2025, 9, 10, 10, 0, 0, DateTimeKind.Utc), 52.4m, 54m, 51m, 48m, 49m, 60m, "Legacy quarter-end evidence archived."),
        new ComplianceSnapshotSeed(2025, 9, 14.1m, 6.7m, 30.2m, 82.1m, 1_110_000_000m, 774_000_000m, 112_200_000m, 7.1m, 79, 4, SubmissionStatus.Historical, new DateTime(2025, 10, 10, 10, 0, 0, DateTimeKind.Utc), 54.6m, 56m, 53m, 51m, 51m, 62m, null),
        new ComplianceSnapshotSeed(2025, 10, 14.6m, 6.3m, 31.0m, 80.0m, 1_145_000_000m, 790_000_000m, 115_600_000m, 6.5m, 73, 4, SubmissionStatus.Historical, new DateTime(2025, 11, 10, 10, 0, 0, DateTimeKind.Utc), 56.9m, 58m, 55m, 54m, 53m, 64m, null),
        new ComplianceSnapshotSeed(2025, 11, 15.1m, 5.9m, 32.4m, 78.8m, 1_190_000_000m, 804_000_000m, 120_400_000m, 5.9m, 69, 3, SubmissionStatus.Historical, new DateTime(2025, 12, 10, 10, 0, 0, DateTimeKind.Utc), 58.8m, 60m, 57m, 56m, 55m, 66m, null),
        new ComplianceSnapshotSeed(2025, 12, 15.7m, 5.4m, 34.3m, 77.1m, 1_245_000_000m, 826_000_000m, 126_800_000m, 5.4m, 61, 2, SubmissionStatus.Accepted, new DateTime(2026, 1, 12, 10, 0, 0, DateTimeKind.Utc), 60.7m, 62m, 59m, 58m, 58m, 67m, null),
        new ComplianceSnapshotSeed(2026, 1, 16.3m, 5.0m, 36.1m, 75.0m, 1_310_000_000m, 850_000_000m, 133_200_000m, 4.8m, 54, 2, SubmissionStatus.Accepted, new DateTime(2026, 2, 11, 10, 0, 0, DateTimeKind.Utc), 62.4m, 64m, 61m, 60m, 59m, 68m, null),
        new ComplianceSnapshotSeed(2026, 2, 17.1m, 4.7m, 38.4m, 72.6m, 1_380_000_000m, 872_000_000m, 141_500_000m, 4.3m, 49, 1, SubmissionStatus.AcceptedWithWarnings, new DateTime(2026, 3, 12, 10, 0, 0, DateTimeKind.Utc), 64.9m, 66m, 64m, 62m, 61m, 71m, "Minor supporting schedule variance remains open.")
    ];

    private static ComplianceSnapshotSeed BuildCurrentDraftSnapshot() =>
        new(
            2026,
            3,
            18.65m,
            4.46m,
            40.95m,
            76.2m,
            1_445_000_000m,
            894_000_000m,
            152_700_000m,
            3.8m,
            43,
            1,
            SubmissionStatus.Draft,
            DateTime.UtcNow.AddHours(-2),
            67.3m,
            68m,
            66m,
            65m,
            63m,
            74m,
            "Workspace draft is ready, but the board paper attachment is still pending.");

    private async Task<TenantInstitutionRef> ResolveInstitutionAsync(string institutionCode, CancellationToken ct)
        => await (
                from institution in _db.Institutions
                join tenant in _db.Tenants on institution.TenantId equals tenant.TenantId
                where institution.InstitutionCode == institutionCode
                select new TenantInstitutionRef(
                    institution.Id,
                    institution.TenantId,
                    institution.InstitutionCode,
                    institution.InstitutionName,
                    tenant.TenantSlug))
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Portal demo peer institution '{institutionCode}' was not found.");

    private static List<PeerSnapshotSeed> BuildAcceptedPeerSnapshots() =>
    [
        new(
            "BDC001",
            new ComplianceSnapshotSeed(2026, 3, 15.42m, 5.03m, 35.62m, 74.3m, 1_228_000_000m, 805_000_000m, 124_100_000m, 4.5m, 38, 1, SubmissionStatus.Accepted, new DateTime(2026, 3, 24, 10, 15, 0, DateTimeKind.Utc), 70.6m, 72m, 69m, 67m, 68m, 77m, null)),
        new(
            "BDC002",
            new ComplianceSnapshotSeed(2026, 3, 16.18m, 4.91m, 37.18m, 72.9m, 1_301_000_000m, 836_000_000m, 135_300_000m, 4.1m, 41, 1, SubmissionStatus.Accepted, new DateTime(2026, 3, 24, 12, 40, 0, DateTimeKind.Utc), 72.4m, 73m, 71m, 70m, 69m, 79m, null)),
        new(
            "BDC003",
            new ComplianceSnapshotSeed(2026, 3, 17.06m, 4.72m, 38.76m, 71.4m, 1_356_000_000m, 858_000_000m, 144_200_000m, 3.9m, 35, 0, SubmissionStatus.Accepted, new DateTime(2026, 3, 25, 9, 50, 0, DateTimeKind.Utc), 75.8m, 77m, 74m, 73m, 72m, 83m, null)),
        new(
            "BDC004",
            new ComplianceSnapshotSeed(2026, 3, 15.87m, 5.11m, 36.41m, 73.8m, 1_267_000_000m, 824_000_000m, 130_800_000m, 4.4m, 40, 1, SubmissionStatus.Accepted, new DateTime(2026, 3, 25, 14, 20, 0, DateTimeKind.Utc), 71.2m, 72m, 70m, 69m, 68m, 78m, null))
    ];

    private sealed record TenantInstitutionRef(
        int InstitutionId,
        Guid TenantId,
        string InstitutionCode,
        string InstitutionName,
        string TenantSlug);

    private sealed record PeriodSeedResult(ReturnPeriod Period, bool WasCreated);

    private sealed record SubmissionSeedResult(Submission Submission, bool WasCreated);

    private sealed record PeerSeedRow(
        Guid TenantId,
        string LicenseType,
        string ParsedDataJson,
        DateTime EffectiveAt);

    private sealed record ComplianceSnapshotSeed(
        int Year,
        int Month,
        decimal CarRatio,
        decimal NplRatio,
        decimal LiquidityRatio,
        decimal LoanDepositRatio,
        decimal TotalAssets,
        decimal RiskWeightedAssets,
        decimal CapitalBase,
        decimal FxNetPositionRatio,
        int OpenTradeCount,
        int ComplianceBreaches,
        SubmissionStatus Status,
        DateTime SubmittedAt,
        decimal ComplianceHealthScore,
        decimal FilingTimeliness,
        decimal DataQuality,
        decimal RegulatoryCapital,
        decimal AuditGovernance,
        decimal Engagement,
        string? ValidationNote);

    private sealed record PeerSnapshotSeed(string InstitutionCode, ComplianceSnapshotSeed Snapshot);
}

public sealed class PortalTenantDemoSeedResult
{
    public int PeriodsCreated { get; set; }
    public int SubmissionsCreated { get; set; }
    public int SubmissionsUpdated { get; set; }
    public int ValidationReportsSeeded { get; set; }
    public int SlaRecordsUpserted { get; set; }
    public int ChsSnapshotsUpserted { get; set; }
    public int PeerStatsUpserted { get; set; }
    public int InstitutionUsersUpdated { get; set; }
}
