using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class AnomalyModelTrainingService : IAnomalyModelTrainingService
{
    private readonly MetadataDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<AnomalyModelTrainingService> _logger;

    public AnomalyModelTrainingService(
        MetadataDbContext db,
        IAuditLogger auditLogger,
        ILogger<AnomalyModelTrainingService> logger)
    {
        _db = db;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<AnomalyModelVersion> TrainModuleModelAsync(
        string moduleCode,
        string initiatedBy,
        bool promoteImmediately = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            throw new ArgumentException("Module code is required.", nameof(moduleCode));
        }

        var normalizedModuleCode = moduleCode.Trim().ToUpperInvariant();
        var module = await _db.Modules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleCode == normalizedModuleCode, ct)
            ?? throw new InvalidOperationException($"Module '{normalizedModuleCode}' was not found.");

        var nextVersion = (await _db.AnomalyModelVersions
            .Where(x => x.ModuleCode == normalizedModuleCode)
            .MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;

        var version = new AnomalyModelVersion
        {
            ModuleCode = normalizedModuleCode,
            RegulatorCode = module.RegulatorCode,
            VersionNumber = nextVersion,
            Status = "TRAINING",
            TrainingStartedAt = DateTime.UtcNow,
            Notes = $"Anomaly model training initiated by {initiatedBy}."
        };

        _db.AnomalyModelVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        await _auditLogger.Log(
            "AnomalyModel",
            version.Id,
            "ModelTrainingStarted",
            null,
            new { version.ModuleCode, version.VersionNumber, version.Status },
            initiatedBy,
            ct);

        try
        {
            var config = await LoadConfigAsync(ct);
            var coldStartMin = (int)config.GetValueOrDefault("coldstart.min_observations", 30m);
            var minRSquared = config.GetValueOrDefault("correlation.min_r_squared", 0.60m);

            var rows = await LoadTrainingRowsAsync(normalizedModuleCode, ct);

            version.SubmissionCount = rows.Count;
            version.ObservationCount = rows.Sum(x => x.Metrics.Count);
            version.TenantCount = rows.Select(x => x.TenantId).Distinct().Count();
            version.PeriodCount = rows.Select(x => x.PeriodCode).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            await BuildFieldModelsAsync(version, rows, coldStartMin, ct);
            await BuildCorrelationRulesAsync(version, rows, coldStartMin, minRSquared, ct);
            await BuildPeerStatisticsAsync(version, rows, ct);

            version.TrainingCompletedAt = DateTime.UtcNow;
            version.Status = "SHADOW";
            version.Notes = $"Trained from {version.SubmissionCount:N0} submissions, {version.ObservationCount:N0} observations, {version.TenantCount:N0} tenants.";
            await _db.SaveChangesAsync(ct);

            await _auditLogger.Log(
                "AnomalyModel",
                version.Id,
                "ModelTrainingCompleted",
                null,
                new
                {
                    version.ModuleCode,
                    version.VersionNumber,
                    version.Status,
                    version.SubmissionCount,
                    version.ObservationCount,
                    version.TenantCount,
                    version.PeriodCount
                },
                initiatedBy,
                ct);

            if (promoteImmediately)
            {
                await PromoteModelAsync(version.Id, initiatedBy, ct);
                await _db.Entry(version).ReloadAsync(ct);
            }

            return version;
        }
        catch (Exception ex)
        {
            version.Status = "FAILED";
            version.TrainingCompletedAt = DateTime.UtcNow;
            version.Notes = $"Training failed: {ex.Message}";
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Anomaly model training failed for module {ModuleCode}", normalizedModuleCode);
            throw;
        }
    }

    public async Task PromoteModelAsync(int modelVersionId, string promotedBy, CancellationToken ct = default)
    {
        var model = await _db.AnomalyModelVersions
            .FirstOrDefaultAsync(x => x.Id == modelVersionId, ct)
            ?? throw new InvalidOperationException($"Anomaly model version #{modelVersionId} was not found.");

        if (!string.Equals(model.Status, "SHADOW", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Only SHADOW anomaly models can be promoted. Current status: {model.Status}.");
        }

        var activeModels = await _db.AnomalyModelVersions
            .Where(x => x.ModuleCode == model.ModuleCode && x.Status == "ACTIVE" && x.Id != model.Id)
            .ToListAsync(ct);

        foreach (var active in activeModels)
        {
            active.Status = "RETIRED";
            active.RetiredAt = DateTime.UtcNow;
        }

        model.Status = "ACTIVE";
        model.PromotedAt = DateTime.UtcNow;
        model.PromotedBy = promotedBy;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "AnomalyModel",
            model.Id,
            "ModelPromoted",
            null,
            new { model.ModuleCode, model.VersionNumber, model.Status },
            promotedBy,
            ct);
    }

    public async Task RollbackModelAsync(string moduleCode, string rolledBackBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            throw new ArgumentException("Module code is required.", nameof(moduleCode));
        }

        var normalizedModuleCode = moduleCode.Trim().ToUpperInvariant();
        var previous = await _db.AnomalyModelVersions
            .Where(x => x.ModuleCode == normalizedModuleCode && x.Status == "RETIRED")
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"No retired anomaly model is available for module '{normalizedModuleCode}'.");

        var active = await _db.AnomalyModelVersions
            .Where(x => x.ModuleCode == normalizedModuleCode && x.Status == "ACTIVE")
            .ToListAsync(ct);

        foreach (var current in active)
        {
            current.Status = "RETIRED";
            current.RetiredAt = DateTime.UtcNow;
        }

        previous.Status = "ACTIVE";
        previous.RetiredAt = null;
        previous.PromotedAt = DateTime.UtcNow;
        previous.PromotedBy = rolledBackBy;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "AnomalyModel",
            previous.Id,
            "ModelRolledBack",
            null,
            new { previous.ModuleCode, previous.VersionNumber, previous.Status },
            rolledBackBy,
            ct);
    }

    public async Task<List<AnomalyModelTrainingSummary>> GetModelHistoryAsync(string moduleCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            return new List<AnomalyModelTrainingSummary>();
        }

        var normalizedModuleCode = moduleCode.Trim().ToUpperInvariant();
        return await _db.AnomalyModelVersions
            .AsNoTracking()
            .Where(x => x.ModuleCode == normalizedModuleCode)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new AnomalyModelTrainingSummary
            {
                ModelVersionId = x.Id,
                ModuleCode = x.ModuleCode,
                RegulatorCode = x.RegulatorCode,
                VersionNumber = x.VersionNumber,
                Status = x.Status,
                SubmissionCount = x.SubmissionCount,
                ObservationCount = x.ObservationCount,
                TenantCount = x.TenantCount,
                PeriodCount = x.PeriodCount,
                TrainingStartedAt = x.TrainingStartedAt,
                TrainingCompletedAt = x.TrainingCompletedAt,
                Notes = x.Notes
            })
            .ToListAsync(ct);
    }

    private async Task<Dictionary<string, decimal>> LoadConfigAsync(CancellationToken ct)
    {
        var rows = await _db.AnomalyThresholdConfigs
            .AsNoTracking()
            .Where(x => x.EffectiveTo == null)
            .ToListAsync(ct);
        return AnomalySupport.BuildConfigMap(rows);
    }

    private async Task<List<TrainingRow>> LoadTrainingRowsAsync(string moduleCode, CancellationToken ct)
    {
        var rawRows = await _db.Submissions
            .AsNoTracking()
            .Include(x => x.ReturnPeriod)
            .Include(x => x.Institution)
            .Where(x => x.ParsedDataJson != null
                        && AnomalySupport.AcceptedStatuses.Contains(x.Status)
                        && x.ReturnPeriod != null
                        && x.ReturnPeriod.Module != null
                        && x.ReturnPeriod.Module.ModuleCode == moduleCode)
            .Select(x => new TrainingRow
            {
                SubmissionId = x.Id,
                TenantId = x.TenantId,
                InstitutionId = x.InstitutionId,
                LicenceType = x.Institution != null && !string.IsNullOrWhiteSpace(x.Institution.LicenseType)
                    ? x.Institution.LicenseType!
                    : string.Empty,
                PeriodCode = string.Empty,
                Metrics = new Dictionary<string, AnomalySupport.MetricPoint>(StringComparer.OrdinalIgnoreCase),
                RawJson = x.ParsedDataJson!,
                Year = x.ReturnPeriod != null ? x.ReturnPeriod.Year : 0,
                Month = x.ReturnPeriod != null ? x.ReturnPeriod.Month : 0,
                Quarter = x.ReturnPeriod != null ? x.ReturnPeriod.Quarter : null
            })
            .ToListAsync(ct);

        foreach (var row in rawRows)
        {
            row.PeriodCode = row.Quarter is >= 1 and <= 4
                ? $"{row.Year}-Q{row.Quarter.Value}"
                : $"{row.Year}-{Math.Clamp(row.Month, 1, 12):00}";
            row.Metrics = AnomalySupport.ExtractSubmissionMetrics(row.RawJson);
        }

        return rawRows;
    }

    private async Task BuildFieldModelsAsync(
        AnomalyModelVersion version,
        IReadOnlyList<TrainingRow> rows,
        int coldStartMin,
        CancellationToken ct)
    {
        var baselines = await _db.AnomalyRuleBaselines
            .AsNoTracking()
            .Where(x => x.IsActive
                        && x.RegulatorCode == version.RegulatorCode
                        && (x.ModuleCode == null || x.ModuleCode == version.ModuleCode))
            .ToListAsync(ct);

        var fieldGroups = rows
            .SelectMany(x => x.Metrics.Values)
            .GroupBy(x => x.FieldCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.ToList(),
                StringComparer.OrdinalIgnoreCase);

        var allFieldCodes = fieldGroups.Keys
            .Union(baselines.Select(x => x.FieldCode), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var fieldCode in allFieldCodes)
        {
            fieldGroups.TryGetValue(fieldCode, out var samples);
            var values = (samples ?? new List<AnomalySupport.MetricPoint>())
                .Select(x => x.Value)
                .OrderBy(x => x)
                .ToList();

            var sample = samples?.FirstOrDefault();
            var baseline = baselines.FirstOrDefault(x => x.FieldCode.Equals(fieldCode, StringComparison.OrdinalIgnoreCase));

            var model = new AnomalyFieldModel
            {
                ModelVersionId = version.Id,
                ModuleCode = version.ModuleCode,
                FieldCode = fieldCode,
                FieldLabel = sample?.FieldLabel ?? baseline?.FieldLabel ?? AnomalySupport.HumanizeFieldLabel(fieldCode),
                CreatedAt = DateTime.UtcNow,
                Observations = values.Count
            };

            if (values.Count < coldStartMin)
            {
                model.DistributionType = "RULE_BASED";
                model.IsColdStart = true;
                model.RuleBasedMin = baseline?.MinimumValue ?? values.FirstOrDefault();
                model.RuleBasedMax = baseline?.MaximumValue ?? values.LastOrDefault();
                model.MeanValue = values.Count == 0 ? null : values.Average();
                model.StdDev = values.Count <= 1 ? null : AnomalySupport.StandardDeviation(values);
                model.MedianValue = values.Count == 0 ? null : AnomalySupport.Median(values);
            }
            else
            {
                model.DistributionType = "NORMAL";
                model.IsColdStart = false;
                model.MeanValue = values.Average();
                model.StdDev = AnomalySupport.StandardDeviation(values);
                model.MedianValue = AnomalySupport.Median(values);
                model.Q1Value = AnomalySupport.Percentile(values, 25m);
                model.Q3Value = AnomalySupport.Percentile(values, 75m);
                model.MinObserved = values.First();
                model.MaxObserved = values.Last();
                model.Percentile05 = AnomalySupport.Percentile(values, 5m);
                model.Percentile95 = AnomalySupport.Percentile(values, 95m);
                model.RuleBasedMin = baseline?.MinimumValue;
                model.RuleBasedMax = baseline?.MaximumValue;
            }

            _db.AnomalyFieldModels.Add(model);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task BuildCorrelationRulesAsync(
        AnomalyModelVersion version,
        IReadOnlyList<TrainingRow> rows,
        int coldStartMin,
        decimal minRSquared,
        CancellationToken ct)
    {
        var seededRules = await _db.AnomalySeedCorrelationRules
            .AsNoTracking()
            .Where(x => x.IsActive
                        && x.RegulatorCode == version.RegulatorCode
                        && (x.ModuleCode == null || x.ModuleCode == version.ModuleCode))
            .ToListAsync(ct);

        var existingPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seeded in seededRules)
        {
            var pairKey = BuildPairKey(seeded.FieldCodeA, seeded.FieldCodeB);
            existingPairs.Add(pairKey);
            _db.AnomalyCorrelationRules.Add(new AnomalyCorrelationRule
            {
                ModelVersionId = version.Id,
                ModuleCode = version.ModuleCode,
                FieldCodeA = seeded.FieldCodeA,
                FieldLabelA = seeded.FieldLabelA,
                FieldCodeB = seeded.FieldCodeB,
                FieldLabelB = seeded.FieldLabelB,
                CorrelationCoefficient = seeded.CorrelationCoefficient,
                RSquared = seeded.RSquared,
                Slope = seeded.Slope,
                Intercept = seeded.Intercept,
                Observations = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        var rankedFields = rows
            .SelectMany(x => x.Metrics.Values)
            .GroupBy(x => x.FieldCode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                FieldCode = x.Key,
                Count = x.Count(),
                Label = x.First().FieldLabel
            })
            .Where(x => x.Count >= coldStartMin)
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.FieldCode, StringComparer.OrdinalIgnoreCase)
            .Take(40)
            .ToList();

        for (var i = 0; i < rankedFields.Count; i++)
        {
            for (var j = i + 1; j < rankedFields.Count; j++)
            {
                var fieldA = rankedFields[i];
                var fieldB = rankedFields[j];
                var pairKey = BuildPairKey(fieldA.FieldCode, fieldB.FieldCode);
                if (existingPairs.Contains(pairKey))
                {
                    continue;
                }

                var pairs = rows
                    .Where(x => x.Metrics.ContainsKey(fieldA.FieldCode) && x.Metrics.ContainsKey(fieldB.FieldCode))
                    .Select(x => (
                        X: x.Metrics[fieldA.FieldCode].Value,
                        Y: x.Metrics[fieldB.FieldCode].Value))
                    .ToList();

                if (pairs.Count < coldStartMin)
                {
                    continue;
                }

                var regression = LinearRegression(pairs);
                if (regression.RSquared < minRSquared)
                {
                    continue;
                }

                _db.AnomalyCorrelationRules.Add(new AnomalyCorrelationRule
                {
                    ModelVersionId = version.Id,
                    ModuleCode = version.ModuleCode,
                    FieldCodeA = fieldA.FieldCode,
                    FieldLabelA = fieldA.Label,
                    FieldCodeB = fieldB.FieldCode,
                    FieldLabelB = fieldB.Label,
                    CorrelationCoefficient = regression.R,
                    RSquared = regression.RSquared,
                    Slope = regression.Slope,
                    Intercept = regression.Intercept,
                    Observations = pairs.Count,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task BuildPeerStatisticsAsync(
        AnomalyModelVersion version,
        IReadOnlyList<TrainingRow> rows,
        CancellationToken ct)
    {
        var peerGroups = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.LicenceType))
            .SelectMany(
                row => row.Metrics.Values,
                (row, metric) => new
                {
                    row.LicenceType,
                    row.PeriodCode,
                    metric.FieldCode,
                    metric.Value
                })
            .GroupBy(
                x => new { x.FieldCode, x.LicenceType, x.PeriodCode },
                x => x.Value);

        foreach (var group in peerGroups)
        {
            var values = group.OrderBy(x => x).ToList();
            if (values.Count == 0)
            {
                continue;
            }

            _db.AnomalyPeerGroupStatistics.Add(new AnomalyPeerGroupStatistic
            {
                ModelVersionId = version.Id,
                ModuleCode = version.ModuleCode,
                FieldCode = group.Key.FieldCode,
                LicenceCategory = group.Key.LicenceType,
                InstitutionSizeBand = "ALL",
                PeerCount = values.Count,
                PeerMean = values.Average(),
                PeerMedian = AnomalySupport.Median(values),
                PeerStdDev = values.Count <= 1 ? null : AnomalySupport.StandardDeviation(values),
                PeerQ1 = AnomalySupport.Percentile(values, 25m),
                PeerQ3 = AnomalySupport.Percentile(values, 75m),
                PeerMin = values.First(),
                PeerMax = values.Last(),
                PeriodCode = group.Key.PeriodCode,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private static string BuildPairKey(string fieldCodeA, string fieldCodeB)
    {
        return string.Compare(fieldCodeA, fieldCodeB, StringComparison.OrdinalIgnoreCase) <= 0
            ? $"{fieldCodeA}|{fieldCodeB}"
            : $"{fieldCodeB}|{fieldCodeA}";
    }

    private static RegressionResult LinearRegression(IReadOnlyList<(decimal X, decimal Y)> pairs)
    {
        var xMean = pairs.Average(x => x.X);
        var yMean = pairs.Average(x => x.Y);

        decimal ssXX = 0m;
        decimal ssXY = 0m;
        decimal ssYY = 0m;

        foreach (var (x, y) in pairs)
        {
            var dx = x - xMean;
            var dy = y - yMean;
            ssXX += dx * dx;
            ssXY += dx * dy;
            ssYY += dy * dy;
        }

        if (ssXX == 0m || ssYY == 0m)
        {
            return new RegressionResult(0m, 0m, 0m, yMean);
        }

        var slope = ssXY / ssXX;
        var intercept = yMean - (slope * xMean);
        var denominator = (decimal)Math.Sqrt((double)(ssXX * ssYY));
        if (denominator == 0m)
        {
            return new RegressionResult(0m, 0m, slope, intercept);
        }

        var r = ssXY / denominator;
        var rSquared = r * r;
        return new RegressionResult(r, rSquared, slope, intercept);
    }

    private sealed class TrainingRow
    {
        public int SubmissionId { get; set; }
        public Guid TenantId { get; set; }
        public int InstitutionId { get; set; }
        public string LicenceType { get; set; } = string.Empty;
        public string PeriodCode { get; set; } = string.Empty;
        public string RawJson { get; set; } = "{}";
        public int Year { get; set; }
        public int Month { get; set; }
        public int? Quarter { get; set; }
        public Dictionary<string, AnomalySupport.MetricPoint> Metrics { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly record struct RegressionResult(
        decimal R,
        decimal RSquared,
        decimal Slope,
        decimal Intercept);
}
