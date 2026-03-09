using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed class RootCauseAnalysisService : IRootCauseAnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyDictionary<string, (int Rank, string Phase)> MitreKillChain = new Dictionary<string, (int, string)>(StringComparer.OrdinalIgnoreCase)
    {
        ["T1595"] = (0, "Reconnaissance"),
        ["T1598"] = (0, "Reconnaissance"),
        ["T1190"] = (1, "Initial Access"),
        ["T1133"] = (1, "Initial Access"),
        ["T1566"] = (1, "Initial Access"),
        ["T1059"] = (2, "Execution"),
        ["T1204"] = (2, "Execution"),
        ["T1547"] = (3, "Persistence"),
        ["T1505"] = (3, "Persistence"),
        ["T1068"] = (4, "Privilege Escalation"),
        ["T1548"] = (4, "Privilege Escalation"),
        ["T1070"] = (5, "Defense Evasion"),
        ["T1562"] = (5, "Defense Evasion"),
        ["T1003"] = (6, "Credential Access"),
        ["T1110"] = (6, "Credential Access"),
        ["T1087"] = (7, "Discovery"),
        ["T1018"] = (7, "Discovery"),
        ["T1021"] = (8, "Lateral Movement"),
        ["T1210"] = (8, "Lateral Movement"),
        ["T1005"] = (9, "Collection"),
        ["T1114"] = (9, "Collection"),
        ["T1041"] = (10, "Exfiltration"),
        ["T1020"] = (10, "Exfiltration"),
        ["T1486"] = (11, "Impact"),
        ["T1499"] = (11, "Impact")
    };

    private readonly MetadataDbContext _db;
    private readonly ILogger<RootCauseAnalysisService> _logger;

    public RootCauseAnalysisService(MetadataDbContext db, ILogger<RootCauseAnalysisService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<RootCauseAnalysis> AnalyzeAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh)
        {
            var cached = await GetCachedAsync(tenantId, type, incidentId, ct);
            if (cached is not null)
            {
                return cached;
            }
        }

        var analysis = type switch
        {
            RcaIncidentType.SecurityAlert => await AnalyzeSecurityAlertAsync(tenantId, incidentId, ct),
            RcaIncidentType.PipelineFailure => await AnalyzePipelineFailureAsync(tenantId, incidentId, ct),
            RcaIncidentType.QualityIssue => await AnalyzeQualityIssueAsync(tenantId, incidentId, ct),
            _ => throw new InvalidOperationException($"Unsupported RCA incident type '{type}'.")
        };

        await UpsertCacheAsync(tenantId, type, incidentId, analysis, ct);
        return analysis;
    }

    public async Task<RootCauseAnalysis?> GetCachedAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, CancellationToken ct = default)
    {
        var record = await _db.RootCauseAnalysisRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                      && x.IncidentType == ToIncidentType(type)
                                      && x.IncidentId == incidentId, ct);

        return record is null ? null : MapRecord(record);
    }

    public async Task<IReadOnlyList<RcaTimelineEntry>> GetTimelineAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, CancellationToken ct = default)
    {
        var cached = await GetCachedAsync(tenantId, type, incidentId, ct)
                     ?? await AnalyzeAsync(tenantId, type, incidentId, false, ct);
        return cached.Timeline;
    }

    private async Task<RootCauseAnalysis> AnalyzeSecurityAlertAsync(Guid tenantId, Guid alertId, CancellationToken ct)
    {
        var alert = await _db.SecurityAlerts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == alertId, ct)
            ?? throw new InvalidOperationException($"Security alert {alertId} was not found for tenant.");

        var directAssetIds = DeserializeGuidList(alert.AffectedAssetIdsJson);
        var windowStart = alert.CreatedAt.AddHours(-2);
        var windowEnd = alert.CreatedAt.AddHours(1);

        var securityEvents = await _db.SecurityEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.OccurredAt >= windowStart && x.OccurredAt <= windowEnd)
            .ToListAsync(ct);

        var correlatedSecurityEvents = securityEvents
            .Where(x =>
                x.AlertId == alert.Id
                || (!string.IsNullOrWhiteSpace(alert.SourceIp) && x.SourceIp == alert.SourceIp)
                || (!string.IsNullOrWhiteSpace(alert.UserId) && x.UserId == alert.UserId)
                || (!string.IsNullOrWhiteSpace(alert.MitreTechnique) && x.MitreTechnique == alert.MitreTechnique)
                || (x.AssetId.HasValue && directAssetIds.Contains(x.AssetId.Value)))
            .ToList();

        var loginAttempts = await _db.LoginAttempts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.AttemptedAt >= windowStart && x.AttemptedAt <= windowEnd)
            .ToListAsync(ct);

        var correlatedLogins = loginAttempts
            .Where(x =>
                (!string.IsNullOrWhiteSpace(alert.SourceIp) && x.IpAddress == alert.SourceIp)
                || (!string.IsNullOrWhiteSpace(alert.UserId) && x.UserId.HasValue && x.UserId.Value.ToString() == alert.UserId)
                || (!string.IsNullOrWhiteSpace(alert.Username) && x.Username.Equals(alert.Username, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var auditLogs = await _db.AuditLog
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.PerformedAt >= windowStart && x.PerformedAt <= windowEnd)
            .ToListAsync(ct);

        var correlatedAudit = auditLogs
            .Where(x =>
                (!string.IsNullOrWhiteSpace(alert.Username) && x.PerformedBy.Equals(alert.Username, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(alert.UserId) && x.PerformedBy.Equals(alert.UserId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var timeline = new List<RcaTimelineEntry>
        {
            new()
            {
                Source = "security_alert",
                SourceId = alert.Id.ToString(),
                EventType = alert.AlertType,
                Description = alert.Description,
                UserId = alert.UserId,
                Username = alert.Username,
                SourceIp = alert.SourceIp,
                MitreTechnique = alert.MitreTechnique,
                AssetId = directAssetIds.FirstOrDefault(),
                OccurredAt = alert.CreatedAt
            }
        };

        timeline.AddRange(correlatedSecurityEvents.Select(x => new RcaTimelineEntry
        {
            Source = x.EventSource,
            SourceId = x.Id.ToString(),
            EventType = x.EventType,
            Description = x.Description,
            UserId = x.UserId,
            Username = x.Username,
            SourceIp = x.SourceIp,
            MitreTechnique = x.MitreTechnique,
            AssetId = x.AssetId,
            OccurredAt = x.OccurredAt
        }));

        timeline.AddRange(correlatedLogins.Select(x => new RcaTimelineEntry
        {
            Source = "iam",
            SourceId = x.Id.ToString(),
            EventType = x.Succeeded ? "login.success" : "login.failure",
            Description = x.Succeeded
                ? $"Successful login for '{x.Username}'."
                : $"Failed login for '{x.Username}' ({x.FailureReason ?? "unknown reason"}).",
            UserId = x.UserId?.ToString(),
            Username = x.Username,
            SourceIp = x.IpAddress,
            MitreTechnique = x.Succeeded ? "T1078" : "T1110",
            OccurredAt = x.AttemptedAt
        }));

        timeline.AddRange(correlatedAudit.Select(x => new RcaTimelineEntry
        {
            Source = "audit",
            SourceId = x.Id.ToString(),
            EventType = $"{x.EntityType}.{x.Action}",
            Description = $"{x.Action} on {x.EntityType}#{x.EntityId}.",
            UserId = x.PerformedBy,
            Username = x.PerformedBy,
            SourceIp = x.IpAddress,
            OccurredAt = x.PerformedAt
        }));

        foreach (var item in timeline)
        {
            item.KillChainPhase = ResolveKillChainPhase(item);
        }

        timeline = timeline
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rootEvent = timeline
            .OrderBy(x => KillChainRank(x.KillChainPhase))
            .ThenBy(x => x.OccurredAt)
            .FirstOrDefault() ?? timeline.First();

        var rootCauseType = InferSecurityRootCauseType(rootEvent, alert, timeline);
        var causalChain = BuildCausalChain(timeline);
        var impact = await AssessImpactAsync(tenantId, directAssetIds, timeline, ct);
        var recommendations = BuildRecommendations(rootCauseType);

        return new RootCauseAnalysis
        {
            IncidentType = ToIncidentType(RcaIncidentType.SecurityAlert),
            IncidentId = alert.Id,
            RootCauseType = rootCauseType,
            RootCauseSummary = InferSecurityRootCauseSummary(rootCauseType, rootEvent),
            Confidence = CalculateSecurityConfidence(alert, timeline),
            Timeline = timeline,
            CausalChain = causalChain,
            Impact = impact,
            Recommendations = recommendations,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<RootCauseAnalysis> AnalyzePipelineFailureAsync(Guid tenantId, Guid executionId, CancellationToken ct)
    {
        var execution = await _db.DataPipelineExecutions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == executionId, ct)
            ?? throw new InvalidOperationException($"Pipeline execution {executionId} was not found for tenant.");

        var pipeline = await _db.DataPipelineDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == execution.PipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline definition {execution.PipelineId} was not found for tenant.");

        var source = await _db.DataSourceRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == execution.SourceDataSourceId, ct);
        var target = await _db.DataSourceRegistrations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == execution.TargetDataSourceId, ct);

        var upstreamRoot = await FindUpstreamRootAsync(tenantId, pipeline, execution.StartedAt, new HashSet<Guid>(), ct);
        var rootCauseType = upstreamRoot is not null ? "upstream_failure" : InferPipelineFailureType(execution.ErrorMessage);
        var rootCauseSummary = upstreamRoot is not null
            ? $"Upstream failure: pipeline {upstreamRoot.Value.PipelineName} failed at {upstreamRoot.Value.Execution.StartedAt:yyyy-MM-dd HH:mm:ss} UTC, and this pipeline depends on its output."
            : await BuildPipelineFailureSummaryAsync(rootCauseType, execution, pipeline, source, ct);

        var timeline = new List<RcaTimelineEntry>
        {
            new()
            {
                Source = "pipeline",
                SourceId = execution.Id.ToString(),
                EventType = $"pipeline.{execution.Status}",
                Description = execution.ErrorMessage ?? $"Pipeline '{pipeline.PipelineName}' failed.",
                OccurredAt = execution.CompletedAt ?? execution.StartedAt
            }
        };

        var recentExecutions = await _db.DataPipelineExecutions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && x.PipelineId == pipeline.Id
                        && x.StartedAt >= execution.StartedAt.AddHours(-4)
                        && x.StartedAt <= execution.StartedAt)
            .OrderBy(x => x.StartedAt)
            .ToListAsync(ct);

        timeline.AddRange(recentExecutions
            .Where(x => x.Id != execution.Id)
            .Select(x => new RcaTimelineEntry
            {
                Source = "pipeline",
                SourceId = x.Id.ToString(),
                EventType = $"pipeline.{x.Status}",
                Description = x.ErrorMessage ?? $"Pipeline '{pipeline.PipelineName}' reported {x.Status}.",
                OccurredAt = x.CompletedAt ?? x.StartedAt
            }));

        if (upstreamRoot is not null)
        {
            timeline.Add(new RcaTimelineEntry
            {
                Source = "pipeline",
                SourceId = upstreamRoot.Value.Execution.Id.ToString(),
                EventType = $"pipeline.{upstreamRoot.Value.Execution.Status}",
                Description = upstreamRoot.Value.Execution.ErrorMessage ?? $"Upstream pipeline '{upstreamRoot.Value.PipelineName}' failed.",
                OccurredAt = upstreamRoot.Value.Execution.CompletedAt ?? upstreamRoot.Value.Execution.StartedAt
            });
        }

        var relatedEvents = await _db.SecurityEvents
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && x.OccurredAt >= execution.StartedAt.AddHours(-4)
                        && x.OccurredAt <= (execution.CompletedAt ?? execution.StartedAt).AddHours(1))
            .Where(x => (x.RelatedEntityType == nameof(DataPipelineExecution) && x.RelatedEntityId == execution.Id.ToString())
                        || (x.RelatedEntityType == nameof(DataPipelineDefinition) && x.RelatedEntityId == pipeline.Id.ToString()))
            .ToListAsync(ct);

        timeline.AddRange(relatedEvents.Select(x => new RcaTimelineEntry
        {
            Source = x.EventSource,
            SourceId = x.Id.ToString(),
            EventType = x.EventType,
            Description = x.Description,
            OccurredAt = x.OccurredAt
        }));

        var recentScans = await _db.DspmScanRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && x.SourceDataSourceId == execution.SourceDataSourceId
                        && x.CompletedAt.HasValue
                        && x.CompletedAt.Value <= execution.StartedAt)
            .OrderByDescending(x => x.CompletedAt)
            .Take(2)
            .ToListAsync(ct);

        timeline.AddRange(recentScans.Select(x => new RcaTimelineEntry
        {
            Source = "dspm_scan",
            SourceId = x.Id.ToString(),
            EventType = $"scan.{x.Trigger}",
            Description = $"DSPM scan completed with posture score {x.PostureScore}.",
            OccurredAt = x.CompletedAt ?? x.StartedAt
        }));

        timeline = timeline.OrderBy(x => x.OccurredAt).ToList();
        var assetIds = await GetAssetsForSourcesAsync(tenantId, execution.SourceDataSourceId, execution.TargetDataSourceId, ct);
        var impact = await AssessImpactAsync(tenantId, assetIds, timeline, ct);

        return new RootCauseAnalysis
        {
            IncidentType = ToIncidentType(RcaIncidentType.PipelineFailure),
            IncidentId = execution.Id,
            RootCauseType = rootCauseType,
            RootCauseSummary = rootCauseSummary,
            Confidence = CalculatePipelineConfidence(execution, upstreamRoot, recentScans),
            Timeline = timeline,
            CausalChain = BuildCausalChain(timeline),
            Impact = impact,
            Recommendations = BuildRecommendations(rootCauseType),
            GeneratedAt = DateTime.UtcNow
        };
    }

    private async Task<RootCauseAnalysis> AnalyzeQualityIssueAsync(Guid tenantId, Guid incidentId, CancellationToken ct)
    {
        var pipelineAnalysis = await AnalyzePipelineFailureAsync(tenantId, incidentId, ct);
        pipelineAnalysis.IncidentType = ToIncidentType(RcaIncidentType.QualityIssue);
        if (pipelineAnalysis.RootCauseType == "pipeline_failure")
        {
            pipelineAnalysis.RootCauseType = "upstream_data_quality";
            pipelineAnalysis.RootCauseSummary = "Quality issue traced through lineage; no lower-level execution failure evidence overrode the upstream data-quality signal.";
            pipelineAnalysis.Recommendations = BuildRecommendations(pipelineAnalysis.RootCauseType);
        }

        return pipelineAnalysis;
    }

    private async Task<UpstreamFailure?> FindUpstreamRootAsync(Guid tenantId, DataPipelineDefinition pipeline, DateTime cutoff, HashSet<Guid> visited, CancellationToken ct)
    {
        if (!visited.Add(pipeline.Id))
        {
            return null;
        }

        var upstreamIds = DeserializeGuidList(pipeline.UpstreamPipelineIdsJson);
        UpstreamFailure? earliest = null;

        foreach (var upstreamId in upstreamIds)
        {
            var upstreamPipeline = await _db.DataPipelineDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == upstreamId, ct);
            if (upstreamPipeline is null)
            {
                continue;
            }

            var failedExecution = await _db.DataPipelineExecutions
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId
                            && x.PipelineId == upstreamId
                            && x.Status == "failed"
                            && x.StartedAt <= cutoff)
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefaultAsync(ct);
            if (failedExecution is null)
            {
                continue;
            }

            var deeper = await FindUpstreamRootAsync(tenantId, upstreamPipeline, failedExecution.StartedAt, visited, ct);
            var candidate = deeper ?? new UpstreamFailure(upstreamPipeline.PipelineName, failedExecution);

            if (earliest is null || candidate.Execution.StartedAt < earliest.Value.Execution.StartedAt)
            {
                earliest = candidate;
            }
        }

        return earliest;
    }

    private async Task<string> BuildPipelineFailureSummaryAsync(
        string rootCauseType,
        DataPipelineExecution execution,
        DataPipelineDefinition pipeline,
        DataSourceRegistration? source,
        CancellationToken ct)
    {
        return rootCauseType switch
        {
            "connectivity" => $"Source connection timeout: {source?.SourceName ?? pipeline.SourceDataSourceId.ToString()} unreachable since {(execution.CompletedAt ?? execution.StartedAt):yyyy-MM-dd HH:mm:ss} UTC.",
            "credential_expiry" => $"Credential expiry or authorization failure: {execution.ErrorMessage ?? "Pipeline access was denied."}",
            "schema_drift" => await BuildSchemaDriftSummaryAsync(execution, source, ct),
            "resource_exhaustion" => $"Resource exhaustion: pipeline processed {execution.ProcessedRows:N0} rows, exceeding {(pipeline.MemoryLimitRows?.ToString("N0") ?? "the configured")} memory limit.",
            "quality_gate_failure" => $"Quality gate failed: {execution.ErrorMessage ?? "The pipeline reported a quality rule failure."}",
            _ => execution.ErrorMessage ?? "Pipeline failure occurred without a more specific classifier match."
        };
    }

    private async Task<string> BuildSchemaDriftSummaryAsync(DataPipelineExecution execution, DataSourceRegistration? source, CancellationToken ct)
    {
        var scans = await _db.DspmScanRecords
            .AsNoTracking()
            .Where(x => x.TenantId == execution.TenantId
                        && x.SourceDataSourceId == execution.SourceDataSourceId
                        && x.CompletedAt.HasValue
                        && x.CompletedAt.Value <= execution.StartedAt)
            .OrderByDescending(x => x.CompletedAt)
            .Take(2)
            .ToListAsync(ct);

        if (scans.Count < 2)
        {
            return $"Schema drift suspected on source {source?.SourceName ?? execution.SourceDataSourceId.ToString()}, but no prior scan baseline was available.";
        }

        var latest = await _db.DspmColumnFindings
            .AsNoTracking()
            .Where(x => x.ScanId == scans[0].Id)
            .ToListAsync(ct);
        var previous = await _db.DspmColumnFindings
            .AsNoTracking()
            .Where(x => x.ScanId == scans[1].Id)
            .ToListAsync(ct);

        var previousLookup = previous.ToDictionary(x => $"{x.TableName}.{x.ColumnName}", x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var current in latest)
        {
            if (previousLookup.TryGetValue($"{current.TableName}.{current.ColumnName}", out var prior)
                && !string.Equals(current.DataType, prior.DataType, StringComparison.OrdinalIgnoreCase))
            {
                var at = scans[0].CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown time";
                return $"Schema drift: column {current.TableName}.{current.ColumnName} changed from {prior.DataType} to {current.DataType} on {at} UTC.";
            }
        }

        return $"Schema drift suspected on source {source?.SourceName ?? execution.SourceDataSourceId.ToString()}, but the changed column could not be isolated from scan history.";
    }

    private async Task<ImpactAssessment> AssessImpactAsync(Guid tenantId, IReadOnlyCollection<Guid> directAssetIds, IReadOnlyCollection<RcaTimelineEntry> timeline, CancellationToken ct)
    {
        var directSet = directAssetIds.ToHashSet();
        var visited = new HashSet<Guid>(directSet);
        var queue = new Queue<Guid>(directSet);

        while (queue.TryDequeue(out var current))
        {
            var dependents = await _db.CyberAssetDependencies
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.DependsOnAssetId == current)
                .Select(x => x.AssetId)
                .ToListAsync(ct);

            foreach (var dependent in dependents)
            {
                if (visited.Add(dependent))
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        var assets = await _db.CyberAssets
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && visited.Contains(x.Id))
            .ToListAsync(ct);

        var dataAtRisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            foreach (var item in JsonSerializer.Deserialize<List<string>>(asset.DataClassificationsJson, JsonOptions) ?? [])
            {
                dataAtRisk.Add(item);
            }

            if (asset.LinkedDataSourceId.HasValue)
            {
                var latestScanId = await _db.DspmScanRecords
                    .AsNoTracking()
                    .Where(x => x.TenantId == tenantId
                                && x.SourceDataSourceId == asset.LinkedDataSourceId.Value
                                && x.Status == "completed")
                    .OrderByDescending(x => x.CompletedAt)
                    .Select(x => (Guid?)x.Id)
                    .FirstOrDefaultAsync(ct);

                if (latestScanId.HasValue)
                {
                    var piiTypes = await _db.DspmColumnFindings
                        .AsNoTracking()
                        .Where(x => x.ScanId == latestScanId.Value && x.PrimaryPiiType != null)
                        .Select(x => x.PrimaryPiiType!)
                        .Distinct()
                        .ToListAsync(ct);
                    foreach (var piiType in piiTypes)
                    {
                        dataAtRisk.Add(piiType);
                    }
                }
            }
        }

        var usersAtRisk = timeline
            .SelectMany(x => new[] { x.UserId, x.Username })
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var highestCriticality = assets
            .Select(a => a.Criticality.ToLowerInvariant())
            .OrderByDescending(CriticalityScore)
            .FirstOrDefault() ?? "low";

        return new ImpactAssessment
        {
            DirectAssetCount = directSet.Count,
            DependentAssetCount = Math.Max(0, visited.Count - directSet.Count),
            TotalAffectedAssets = visited.Count,
            AffectedAssets = assets.Select(a => a.DisplayName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            DataAtRisk = dataAtRisk.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
            UsersAtRisk = usersAtRisk,
            BusinessImpact = BuildBusinessImpact(highestCriticality, visited.Count)
        };
    }

    private async Task<List<Guid>> GetAssetsForSourcesAsync(Guid tenantId, Guid sourceDataSourceId, Guid targetDataSourceId, CancellationToken ct)
    {
        return await _db.CyberAssets
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && x.LinkedDataSourceId.HasValue
                        && (x.LinkedDataSourceId == sourceDataSourceId || x.LinkedDataSourceId == targetDataSourceId))
            .Select(x => x.Id)
            .ToListAsync(ct);
    }

    private async Task UpsertCacheAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, RootCauseAnalysis analysis, CancellationToken ct)
    {
        var record = await _db.RootCauseAnalysisRecords
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                      && x.IncidentType == ToIncidentType(type)
                                      && x.IncidentId == incidentId, ct);

        if (record is null)
        {
            record = new RootCauseAnalysisRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                IncidentType = ToIncidentType(type),
                IncidentId = incidentId
            };
            _db.RootCauseAnalysisRecords.Add(record);
        }

        record.RootCauseType = analysis.RootCauseType;
        record.RootCauseSummary = analysis.RootCauseSummary;
        record.Confidence = analysis.Confidence;
        record.TimelineJson = JsonSerializer.Serialize(analysis.Timeline, JsonOptions);
        record.CausalChainJson = JsonSerializer.Serialize(analysis.CausalChain, JsonOptions);
        record.ImpactJson = JsonSerializer.Serialize(analysis.Impact, JsonOptions);
        record.RecommendationsJson = JsonSerializer.Serialize(analysis.Recommendations, JsonOptions);
        record.ModelName = analysis.ModelName;
        record.ModelType = analysis.ModelType;
        record.ExplainabilityMode = analysis.ExplainabilityMode;
        record.GeneratedAt = analysis.GeneratedAt;

        await _db.SaveChangesAsync(ct);
    }

    private static RootCauseAnalysis MapRecord(RootCauseAnalysisRecord record)
        => new()
        {
            IncidentType = record.IncidentType,
            IncidentId = record.IncidentId,
            RootCauseType = record.RootCauseType,
            RootCauseSummary = record.RootCauseSummary,
            Confidence = record.Confidence,
            ModelName = record.ModelName,
            ModelType = record.ModelType,
            ExplainabilityMode = record.ExplainabilityMode,
            GeneratedAt = record.GeneratedAt,
            Timeline = JsonSerializer.Deserialize<List<RcaTimelineEntry>>(record.TimelineJson, JsonOptions) ?? [],
            CausalChain = JsonSerializer.Deserialize<List<CausalStep>>(record.CausalChainJson, JsonOptions) ?? [],
            Impact = JsonSerializer.Deserialize<ImpactAssessment>(record.ImpactJson, JsonOptions) ?? new ImpactAssessment(),
            Recommendations = JsonSerializer.Deserialize<List<RecommendationAction>>(record.RecommendationsJson, JsonOptions) ?? []
        };

    private static List<CausalStep> BuildCausalChain(IReadOnlyList<RcaTimelineEntry> timeline)
    {
        var chain = new List<CausalStep>(timeline.Count);
        RcaTimelineEntry? previous = null;

        for (var index = 0; index < timeline.Count; index++)
        {
            var current = timeline[index];
            chain.Add(new CausalStep
            {
                StepNumber = index + 1,
                Title = $"{current.Source}:{current.EventType}",
                Description = current.Description,
                EvidenceSource = current.Source,
                EvidenceId = current.SourceId,
                Correlation = BuildCorrelation(previous, current),
                MitreTechnique = current.MitreTechnique,
                KillChainPhase = current.KillChainPhase,
                AssetId = current.AssetId,
                UserId = current.UserId,
                SourceIp = current.SourceIp,
                OccurredAt = current.OccurredAt
            });
            previous = current;
        }

        return chain;
    }

    private static string BuildCorrelation(RcaTimelineEntry? previous, RcaTimelineEntry current)
    {
        if (previous is null)
        {
            return "initial_observation";
        }

        if (!string.IsNullOrWhiteSpace(current.SourceIp) && current.SourceIp == previous.SourceIp)
        {
            return "same_source_ip";
        }

        if (!string.IsNullOrWhiteSpace(current.UserId) && current.UserId == previous.UserId)
        {
            return "same_user";
        }

        if (!string.IsNullOrWhiteSpace(current.KillChainPhase)
            && !string.IsNullOrWhiteSpace(previous.KillChainPhase)
            && KillChainRank(current.KillChainPhase) >= KillChainRank(previous.KillChainPhase))
        {
            return "sequential_kill_chain";
        }

        if (current.AssetId.HasValue && current.AssetId == previous.AssetId)
        {
            return "same_asset";
        }

        return "time_proximity";
    }

    private static string InferSecurityRootCauseType(RcaTimelineEntry rootEvent, SecurityAlert alert, IReadOnlyList<RcaTimelineEntry> timeline)
    {
        if (string.Equals(rootEvent.MitreTechnique, "T1190", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rootEvent.KillChainPhase, "Initial Access", StringComparison.OrdinalIgnoreCase))
        {
            return "exposed_service";
        }

        if (string.Equals(rootEvent.MitreTechnique, "T1078", StringComparison.OrdinalIgnoreCase)
            || timeline.Count(x => !string.IsNullOrWhiteSpace(alert.UserId) && x.UserId == alert.UserId) >= 2)
        {
            return "credential_compromise";
        }

        if (string.Equals(rootEvent.KillChainPhase, "Impact", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(alert.SourceIp)
            && !string.IsNullOrWhiteSpace(alert.UserId))
        {
            return "insider_threat";
        }

        if (string.Equals(rootEvent.MitreTechnique, "T1068", StringComparison.OrdinalIgnoreCase)
            || rootEvent.Description.Contains("patch", StringComparison.OrdinalIgnoreCase)
            || rootEvent.Description.Contains("vulnerability", StringComparison.OrdinalIgnoreCase))
        {
            return "unpatched_vulnerability";
        }

        return "attack_progression";
    }

    private static string InferSecurityRootCauseSummary(string rootCauseType, RcaTimelineEntry rootEvent)
    {
        return rootCauseType switch
        {
            "exposed_service" => $"Initial compromise via exposed service or API path evidenced by {rootEvent.Source}:{rootEvent.SourceId}.",
            "credential_compromise" => $"Credential compromise likely originated from activity tied to user '{rootEvent.UserId ?? rootEvent.Username ?? "unknown"}'.",
            "insider_threat" => $"Insider activity is the earliest supported origin, anchored to event {rootEvent.Source}:{rootEvent.SourceId}.",
            "unpatched_vulnerability" => $"Unpatched vulnerability exploitation is the earliest supported origin, anchored to event {rootEvent.Source}:{rootEvent.SourceId}.",
            _ => $"Earliest correlated attack activity originates at {rootEvent.Source}:{rootEvent.SourceId}."
        };
    }

    private static decimal CalculateSecurityConfidence(SecurityAlert alert, IReadOnlyList<RcaTimelineEntry> timeline)
    {
        var confidence = 0.45m;
        if (timeline.Count >= 3) confidence += 0.15m;
        if (!string.IsNullOrWhiteSpace(alert.SourceIp) && timeline.Count(x => x.SourceIp == alert.SourceIp) >= 2) confidence += 0.15m;
        if (!string.IsNullOrWhiteSpace(alert.UserId) && timeline.Count(x => x.UserId == alert.UserId) >= 2) confidence += 0.1m;
        if (timeline.Any(x => !string.IsNullOrWhiteSpace(x.KillChainPhase))) confidence += 0.1m;
        if (timeline.Any(x => !string.IsNullOrWhiteSpace(x.MitreTechnique))) confidence += 0.1m;
        return Math.Min(0.99m, decimal.Round(confidence, 2));
    }

    private static decimal CalculatePipelineConfidence(DataPipelineExecution execution, UpstreamFailure? upstreamRoot, IReadOnlyCollection<DspmScanRecord> scans)
    {
        var confidence = 0.5m;
        if (upstreamRoot is not null) confidence += 0.2m;
        if (!string.IsNullOrWhiteSpace(execution.ErrorMessage)) confidence += 0.1m;
        if (scans.Count >= 2) confidence += 0.1m;
        if (execution.Status == "failed") confidence += 0.05m;
        return Math.Min(0.95m, decimal.Round(confidence, 2));
    }

    private static string InferPipelineFailureType(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return "pipeline_failure";
        }

        if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
        {
            return "connectivity";
        }

        if (errorMessage.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            return "credential_expiry";
        }

        if (errorMessage.Contains("schema", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("mismatch", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("column", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("type", StringComparison.OrdinalIgnoreCase))
        {
            return "schema_drift";
        }

        if (errorMessage.Contains("oom", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("memory", StringComparison.OrdinalIgnoreCase))
        {
            return "resource_exhaustion";
        }

        if (errorMessage.Contains("quality gate", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("quality", StringComparison.OrdinalIgnoreCase))
        {
            return "quality_gate_failure";
        }

        return "pipeline_failure";
    }

    private static List<RecommendationAction> BuildRecommendations(string rootCauseType)
    {
        return rootCauseType switch
        {
            "exposed_service" => [Recommend("high", "Restrict network access and apply patches.", "The earliest evidence indicates exposed service exploitation.")],
            "credential_compromise" => [Recommend("high", "Force password reset and enable MFA.", "The evidence points to compromised credentials."), Recommend("medium", "Invalidate active sessions and rotate secrets.", "Session reuse risk remains until credentials are rotated.")],
            "insider_threat" => [Recommend("high", "Review user access and initiate HR/compliance investigation.", "The evidence suggests internally initiated impact activity.")],
            "unpatched_vulnerability" => [Recommend("high", "Apply the relevant patch and scan for exploitation indicators.", "The earliest event maps to vulnerability exploitation.")],
            "upstream_failure" => [Recommend("high", "Remediate the upstream pipeline before retrying downstream jobs.", "Lineage walk traced the failure to an upstream dependency.")],
            "connectivity" => [Recommend("high", "Restore source connectivity and validate connector health.", "The failure signature matches network or endpoint unavailability.")],
            "credential_expiry" => [Recommend("high", "Rotate credentials and revalidate access scope.", "The pipeline failed with an authorization signature.")],
            "schema_drift" => [Recommend("high", "Update schema mappings and validate downstream contracts.", "Scan evidence shows upstream schema changed.")],
            "resource_exhaustion" => [Recommend("high", "Increase capacity or partition the workload.", "Processed volume exceeded the execution envelope.")],
            "quality_gate_failure" => [Recommend("medium", "Review failing quality rules and remediate upstream data quality.", "The execution failed at the quality gate.")],
            "upstream_data_quality" => [Recommend("medium", "Trace upstream lineage and correct the originating dataset.", "No lower-level infra fault displaced the quality signal.")],
            _ => [Recommend("medium", "Review correlated evidence and contain the affected assets.", "The analysis found a causal chain but no narrower rule match.")]
        };
    }

    private static RecommendationAction Recommend(string priority, string action, string reason)
        => new() { Priority = priority, Action = action, Reason = reason };

    private static string ResolveKillChainPhase(RcaTimelineEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.MitreTechnique)
            && MitreKillChain.TryGetValue(entry.MitreTechnique, out var mapped))
        {
            return mapped.Phase;
        }

        if (entry.EventType.Contains("login", StringComparison.OrdinalIgnoreCase))
        {
            return entry.EventType.Contains("failure", StringComparison.OrdinalIgnoreCase)
                ? "Credential Access"
                : "Initial Access";
        }

        if (entry.EventType.Contains("lateral", StringComparison.OrdinalIgnoreCase))
        {
            return "Lateral Movement";
        }

        if (entry.EventType.Contains("exfil", StringComparison.OrdinalIgnoreCase))
        {
            return "Exfiltration";
        }

        if (entry.EventType.Contains("impact", StringComparison.OrdinalIgnoreCase)
            || entry.Description.Contains("ransom", StringComparison.OrdinalIgnoreCase))
        {
            return "Impact";
        }

        return string.Empty;
    }

    private static int KillChainRank(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return int.MaxValue;
        }

        return phase switch
        {
            "Reconnaissance" => 0,
            "Initial Access" => 1,
            "Execution" => 2,
            "Persistence" => 3,
            "Privilege Escalation" => 4,
            "Defense Evasion" => 5,
            "Credential Access" => 6,
            "Discovery" => 7,
            "Lateral Movement" => 8,
            "Collection" => 9,
            "Exfiltration" => 10,
            "Impact" => 11,
            _ => int.MaxValue
        };
    }

    private static string BuildBusinessImpact(string highestCriticality, int assetCount)
    {
        var scope = assetCount >= 5 ? "broad" : assetCount >= 2 ? "moderate" : "limited";
        return highestCriticality switch
        {
            "critical" => $"Critical business impact with {scope} blast radius across {assetCount} asset(s).",
            "high" => $"High business impact with {scope} blast radius across {assetCount} asset(s).",
            "medium" => $"Moderate business impact across {assetCount} asset(s).",
            _ => $"Contained business impact across {assetCount} asset(s)."
        };
    }

    private static int CriticalityScore(string criticality)
        => criticality switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            _ => 1
        };

    private static string ToIncidentType(RcaIncidentType type)
        => type switch
        {
            RcaIncidentType.SecurityAlert => "security_alert",
            RcaIncidentType.PipelineFailure => "pipeline_failure",
            RcaIncidentType.QualityIssue => "quality_issue",
            _ => type.ToString().ToLowerInvariant()
        };

    private static List<Guid> DeserializeGuidList(string payload)
        => JsonSerializer.Deserialize<List<Guid>>(payload, JsonOptions) ?? [];

    private readonly record struct UpstreamFailure(string PipelineName, DataPipelineExecution Execution);
}
