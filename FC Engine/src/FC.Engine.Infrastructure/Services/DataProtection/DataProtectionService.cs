using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Events;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Services.DataProtection;

public sealed class DataProtectionService : IDataProtectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MetadataDbContext _db;
    private readonly PiiClassifier _piiClassifier;
    private readonly ComplianceTagger _complianceTagger;
    private readonly ShadowCopyDetector _shadowCopyDetector;
    private readonly IDomainEventPublisher? _eventPublisher;
    private readonly ContinuousDspmOptions _options;
    private readonly ILogger<DataProtectionService> _logger;

    public DataProtectionService(
        MetadataDbContext db,
        PiiClassifier piiClassifier,
        ComplianceTagger complianceTagger,
        ShadowCopyDetector shadowCopyDetector,
        IOptions<ContinuousDspmOptions> options,
        ILogger<DataProtectionService> logger,
        IDomainEventPublisher? eventPublisher = null)
    {
        _db = db;
        _piiClassifier = piiClassifier;
        _complianceTagger = complianceTagger;
        _shadowCopyDetector = shadowCopyDetector;
        _options = options.Value;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    public async Task<DataSourceSummary> UpsertDataSourceAsync(Guid tenantId, DataSourceRegistrationRequest request, CancellationToken ct = default)
    {
        if (request.Schema.Tables.Count == 0)
        {
            throw new InvalidOperationException("Data source schema must contain at least one table.");
        }

        var entity = request.SourceId.HasValue
            ? await _db.DataSourceRegistrations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SourceId.Value, ct)
            : await _db.DataSourceRegistrations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.SourceName == request.SourceName.Trim(), ct);

        if (entity is null)
        {
            entity = new DataSourceRegistration
            {
                Id = request.SourceId ?? Guid.NewGuid(),
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow
            };
            _db.DataSourceRegistrations.Add(entity);
        }

        entity.SourceName = request.SourceName.Trim();
        entity.SourceType = request.SourceType.Trim();
        entity.ConnectionIdentifier = request.ConnectionIdentifier?.Trim();
        entity.EncryptionAtRestEnabled = request.EncryptionAtRestEnabled;
        entity.TlsRequired = request.TlsRequired;
        entity.FilesystemRootPath = request.FilesystemRootPath?.Trim();
        entity.SchemaJson = JsonSerializer.Serialize(request.Schema, JsonOptions);
        entity.MetadataJson = JsonSerializer.Serialize(request.Metadata, JsonOptions);
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapSource(entity);
    }

    public async Task<DataPipelineSummary> UpsertPipelineAsync(Guid tenantId, DataPipelineDefinitionRequest request, CancellationToken ct = default)
    {
        await EnsureSourceExists(tenantId, request.SourceDataSourceId, ct);
        await EnsureSourceExists(tenantId, request.TargetDataSourceId, ct);

        var entity = request.PipelineId.HasValue
            ? await _db.DataPipelineDefinitions.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.PipelineId.Value, ct)
            : await _db.DataPipelineDefinitions.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PipelineName == request.PipelineName.Trim(), ct);

        if (entity is null)
        {
            entity = new DataPipelineDefinition
            {
                Id = request.PipelineId ?? Guid.NewGuid(),
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow
            };
            _db.DataPipelineDefinitions.Add(entity);
        }

        entity.PipelineName = request.PipelineName.Trim();
        entity.SourceDataSourceId = request.SourceDataSourceId;
        entity.TargetDataSourceId = request.TargetDataSourceId;
        entity.SourceTlsEnabled = request.SourceTlsEnabled;
        entity.TargetTlsEnabled = request.TargetTlsEnabled;
        entity.IsApproved = request.IsApproved;
        entity.MemoryLimitRows = request.MemoryLimitRows;
        entity.UpstreamPipelineIdsJson = JsonSerializer.Serialize(request.UpstreamPipelineIds.Distinct().ToList(), JsonOptions);
        entity.MetadataJson = JsonSerializer.Serialize(request.Metadata, JsonOptions);
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapPipeline(entity);
    }

    public async Task<CyberAssetSummary> UpsertAssetAsync(Guid tenantId, CyberAssetRegistrationRequest request, CancellationToken ct = default)
    {
        if (request.LinkedDataSourceId.HasValue)
        {
            await EnsureSourceExists(tenantId, request.LinkedDataSourceId.Value, ct);
        }

        var entity = request.AssetId.HasValue
            ? await _db.CyberAssets.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.AssetId.Value, ct)
            : await _db.CyberAssets.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AssetKey == request.AssetKey.Trim(), ct);

        if (entity is null)
        {
            entity = new CyberAsset
            {
                Id = request.AssetId ?? Guid.NewGuid(),
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow
            };
            _db.CyberAssets.Add(entity);
        }

        entity.AssetKey = request.AssetKey.Trim();
        entity.DisplayName = request.DisplayName.Trim();
        entity.AssetType = request.AssetType.Trim();
        entity.Criticality = request.Criticality.Trim().ToLowerInvariant();
        entity.LinkedDataSourceId = request.LinkedDataSourceId;
        entity.DataClassificationsJson = JsonSerializer.Serialize(request.DataClassifications.Distinct().ToList(), JsonOptions);
        entity.MetadataJson = JsonSerializer.Serialize(request.Metadata, JsonOptions);
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return MapAsset(entity);
    }

    public async Task AddAssetDependencyAsync(Guid tenantId, Guid assetId, Guid dependsOnAssetId, CancellationToken ct = default)
    {
        if (assetId == dependsOnAssetId)
        {
            throw new InvalidOperationException("An asset cannot depend on itself.");
        }

        var dependency = await _db.CyberAssetDependencies
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AssetId == assetId && x.DependsOnAssetId == dependsOnAssetId, ct);
        if (dependency is not null)
        {
            return;
        }

        await EnsureAssetExists(tenantId, assetId, ct);
        await EnsureAssetExists(tenantId, dependsOnAssetId, ct);

        _db.CyberAssetDependencies.Add(new CyberAssetDependency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AssetId = assetId,
            DependsOnAssetId = dependsOnAssetId,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<DspmAlertSummary> ReportSecurityAlertAsync(Guid tenantId, SecurityAlertReport report, CancellationToken ct = default)
    {
        var alert = new SecurityAlert
        {
            Id = report.AlertId ?? Guid.NewGuid(),
            TenantId = tenantId,
            AlertType = report.AlertType.Trim(),
            Severity = report.Severity.Trim().ToLowerInvariant(),
            Title = report.Title.Trim(),
            Description = report.Description.Trim(),
            AffectedAssetIdsJson = JsonSerializer.Serialize(report.AffectedAssetIds.Distinct().ToList(), JsonOptions),
            UserId = report.UserId?.Trim(),
            Username = report.Username?.Trim(),
            SourceIp = report.SourceIp?.Trim(),
            MitreTechnique = report.MitreTechnique?.Trim(),
            EvidenceJson = JsonSerializer.Serialize(report.Evidence, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        _db.SecurityAlerts.Add(alert);
        _db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AlertId = alert.Id,
            EventSource = "security_alert",
            EventType = alert.AlertType,
            UserId = alert.UserId,
            Username = alert.Username,
            SourceIp = alert.SourceIp,
            MitreTechnique = alert.MitreTechnique,
            Description = alert.Description,
            EvidenceJson = alert.EvidenceJson,
            OccurredAt = alert.CreatedAt
        });

        await _db.SaveChangesAsync(ct);
        return MapAlert(alert);
    }

    public async Task RecordSecurityEventAsync(Guid tenantId, SecurityEventReport report, CancellationToken ct = default)
    {
        _db.SecurityEvents.Add(new SecurityEvent
        {
            Id = report.EventId ?? Guid.NewGuid(),
            TenantId = tenantId,
            AlertId = report.AlertId,
            AssetId = report.AssetId,
            EventSource = report.EventSource.Trim(),
            EventType = report.EventType.Trim(),
            UserId = report.UserId?.Trim(),
            Username = report.Username?.Trim(),
            SourceIp = report.SourceIp?.Trim(),
            MitreTechnique = report.MitreTechnique?.Trim(),
            Description = report.Description.Trim(),
            RelatedEntityType = report.RelatedEntityType?.Trim(),
            RelatedEntityId = report.RelatedEntityId?.Trim(),
            EvidenceJson = JsonSerializer.Serialize(report.Evidence, JsonOptions),
            OccurredAt = report.OccurredAt ?? DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<DataPipelineExecutionSummary> RecordPipelineEventAsync(Guid tenantId, PipelineEventReport report, CancellationToken ct = default)
    {
        var pipeline = await _db.DataPipelineDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == report.PipelineId, ct)
            ?? throw new InvalidOperationException($"Pipeline {report.PipelineId} was not found for tenant.");

        var execution = new DataPipelineExecution
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PipelineId = pipeline.Id,
            SourceDataSourceId = pipeline.SourceDataSourceId,
            TargetDataSourceId = pipeline.TargetDataSourceId,
            SourceTlsEnabled = pipeline.SourceTlsEnabled,
            TargetTlsEnabled = pipeline.TargetTlsEnabled,
            IsApproved = pipeline.IsApproved,
            Status = report.Status.Trim().ToLowerInvariant(),
            Phase = report.Phase?.Trim(),
            SourceTablesJson = JsonSerializer.Serialize(report.SourceTables.Distinct().ToList(), JsonOptions),
            TargetTablesJson = JsonSerializer.Serialize(report.TargetTables.Distinct().ToList(), JsonOptions),
            ProcessedRows = report.ProcessedRows,
            ErrorMessage = report.ErrorMessage,
            MetadataJson = JsonSerializer.Serialize(report.Metadata, JsonOptions),
            StartedAt = report.StartedAt ?? DateTime.UtcNow,
            CompletedAt = report.CompletedAt ?? (report.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
                || report.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                    ? DateTime.UtcNow
                    : null)
        };

        _db.DataPipelineExecutions.Add(execution);
        _db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventSource = "pipeline",
            EventType = $"pipeline.{execution.Status}",
            Description = execution.ErrorMessage ?? $"Pipeline '{pipeline.PipelineName}' reported status '{execution.Status}'.",
            RelatedEntityType = nameof(DataPipelineExecution),
            RelatedEntityId = execution.Id.ToString(),
            OccurredAt = execution.CompletedAt ?? execution.StartedAt
        });

        await _db.SaveChangesAsync(ct);

        if (_eventPublisher is not null)
        {
            await _eventPublisher.PublishAsync(new DataPipelineLifecycleEvent(
                tenantId,
                pipeline.Id,
                execution.Id,
                pipeline.PipelineName,
                execution.Status,
                pipeline.SourceDataSourceId,
                pipeline.TargetDataSourceId,
                pipeline.SourceTlsEnabled,
                pipeline.TargetTlsEnabled,
                pipeline.IsApproved,
                DeserializeList(execution.SourceTablesJson),
                DeserializeList(execution.TargetTablesJson),
                execution.ProcessedRows,
                execution.ErrorMessage,
                execution.CompletedAt ?? execution.StartedAt,
                Guid.NewGuid()), ct);
        }

        return MapExecution(execution, pipeline.PipelineName);
    }

    public async Task HandlePipelineLifecycleEventAsync(DataPipelineLifecycleEvent pipelineEvent, CancellationToken ct = default)
    {
        var status = pipelineEvent.Status.Trim().ToLowerInvariant();
        if (status == "running")
        {
            await EvaluateTransitRiskAsync(pipelineEvent, ct);
            return;
        }

        if (status == "completed")
        {
            var source = await _db.DataSourceRegistrations
                .FirstOrDefaultAsync(x => x.TenantId == pipelineEvent.TenantId && x.Id == pipelineEvent.TargetDataSourceId, ct);
            if (source is null)
            {
                _logger.LogWarning("Pipeline completion event {ExecutionId} referenced unknown target data source {SourceId}",
                    pipelineEvent.ExecutionId, pipelineEvent.TargetDataSourceId);
                return;
            }

            await RunScanForSourceAsync(
                source,
                "pipeline_completed",
                pipelineEvent.PipelineId,
                pipelineEvent.ExecutionId,
                pipelineEvent.TargetTables,
                ct);
        }
    }

    public async Task<IReadOnlyList<DspmScanSummary>> GetScanHistoryAsync(Guid tenantId, Guid? sourceId = null, CancellationToken ct = default)
    {
        var query = _db.DspmScanRecords.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (sourceId.HasValue)
        {
            query = query.Where(x => x.SourceDataSourceId == sourceId.Value);
        }

        var scans = await query.OrderByDescending(x => x.StartedAt).ToListAsync(ct);
        if (scans.Count == 0)
        {
            return [];
        }

        var sourceNames = await _db.DataSourceRegistrations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.SourceName, ct);

        var scanIds = scans.Select(x => x.Id).ToList();
        var findings = await _db.DspmColumnFindings
            .AsNoTracking()
            .Where(x => scanIds.Contains(x.ScanId))
            .ToListAsync(ct);

        return scans.Select(scan => MapScan(scan, sourceNames.GetValueOrDefault(scan.SourceDataSourceId, "unknown"), findings.Where(f => f.ScanId == scan.Id).ToList())).ToList();
    }

    public async Task<IReadOnlyList<ShadowCopyMatch>> GetShadowCopiesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var sourceNames = await _db.DataSourceRegistrations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.SourceName, ct);

        var records = await _db.ShadowCopyRecords
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(ct);

        return records.Select(r => new ShadowCopyMatch
        {
            ShadowCopyId = r.Id,
            SourceDataSourceId = r.SourceDataSourceId,
            TargetDataSourceId = r.TargetDataSourceId,
            SourceName = sourceNames.GetValueOrDefault(r.SourceDataSourceId, "unknown"),
            TargetName = sourceNames.GetValueOrDefault(r.TargetDataSourceId, "unknown"),
            SourceTable = r.SourceTable,
            TargetTable = r.TargetTable,
            DetectionType = r.DetectionType,
            Fingerprint = r.Fingerprint,
            SimilarityScore = r.SimilarityScore,
            IsLegitimate = r.IsLegitimate,
            RequiresReview = r.RequiresReview,
            DetectedAt = r.DetectedAt
        }).ToList();
    }

    public async Task<IReadOnlyList<DspmAlertSummary>> GetSecurityAlertsAsync(Guid tenantId, string? alertType = null, CancellationToken ct = default)
    {
        var query = _db.SecurityAlerts.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(alertType))
        {
            var normalized = alertType.Trim();
            query = query.Where(x => x.AlertType == normalized);
        }

        return (await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct))
            .Select(MapAlert)
            .ToList();
    }

    public async Task RunAtRestScanAsync(Guid? tenantId = null, CancellationToken ct = default)
    {
        var query = _db.DataSourceRegistrations.AsQueryable();
        if (tenantId.HasValue)
        {
            query = query.Where(x => x.TenantId == tenantId.Value);
        }

        var sources = await query.ToListAsync(ct);
        foreach (var source in sources)
        {
            try
            {
                await RunScanForSourceAsync(source, "at_rest", null, null, null, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "At-rest DSPM scan failed for source {SourceId}", source.Id);
            }
        }
    }

    public async Task RunShadowCopyDetectionAsync(Guid? tenantId = null, CancellationToken ct = default)
    {
        var sourcesQuery = _db.DataSourceRegistrations.AsNoTracking().AsQueryable();
        var pipelinesQuery = _db.DataPipelineDefinitions.AsNoTracking().AsQueryable();
        if (tenantId.HasValue)
        {
            sourcesQuery = sourcesQuery.Where(x => x.TenantId == tenantId.Value);
            pipelinesQuery = pipelinesQuery.Where(x => x.TenantId == tenantId.Value);
        }

        var sources = await sourcesQuery.ToListAsync(ct);
        var pipelines = await pipelinesQuery.ToListAsync(ct);
        var matches = _shadowCopyDetector.Detect(sources, pipelines, _options);
        var sourceLookup = sources.ToDictionary(x => x.Id, x => x, EqualityComparer<Guid>.Default);

        foreach (var match in matches)
        {
            var owner = sourceLookup[match.SourceDataSourceId];
            var existing = await _db.ShadowCopyRecords.FirstOrDefaultAsync(x =>
                x.TenantId == owner.TenantId &&
                x.SourceDataSourceId == match.SourceDataSourceId &&
                x.TargetDataSourceId == match.TargetDataSourceId &&
                x.SourceTable == match.SourceTable &&
                x.TargetTable == match.TargetTable &&
                x.DetectionType == match.DetectionType &&
                x.Fingerprint == match.Fingerprint, ct);

            if (existing is null)
            {
                existing = new ShadowCopyRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = owner.TenantId
                };
                _db.ShadowCopyRecords.Add(existing);
            }

            existing.SourceDataSourceId = match.SourceDataSourceId;
            existing.TargetDataSourceId = match.TargetDataSourceId;
            existing.SourceTable = match.SourceTable;
            existing.TargetTable = match.TargetTable;
            existing.DetectionType = match.DetectionType;
            existing.Fingerprint = match.Fingerprint;
            existing.SimilarityScore = match.SimilarityScore;
            existing.IsLegitimate = match.IsLegitimate;
            existing.RequiresReview = match.RequiresReview;
            existing.EvidenceJson = match.EvidenceJson;
            existing.DetectedAt = DateTime.UtcNow;

            if (!match.IsLegitimate || match.RequiresReview)
            {
                var severity = match.RequiresReview ? "medium" : "high";
                await CreateAlertAsync(
                    owner.TenantId,
                    "shadow_copy",
                    severity,
                    "Possible shadow copy detected.",
                    $"{match.SourceName}:{match.SourceTable} matches {match.TargetName}:{match.TargetTable} via {match.DetectionType}.",
                    null,
                    null,
                    null,
                    new Dictionary<string, string>
                    {
                        ["sourceDataSourceId"] = match.SourceDataSourceId.ToString(),
                        ["targetDataSourceId"] = match.TargetDataSourceId.ToString(),
                        ["sourceTable"] = match.SourceTable,
                        ["targetTable"] = match.TargetTable,
                        ["detectionType"] = match.DetectionType
                    },
                    ct);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task EvaluateTransitRiskAsync(DataPipelineLifecycleEvent pipelineEvent, CancellationToken ct)
    {
        var evidence = new Dictionary<string, string>
        {
            ["pipelineId"] = pipelineEvent.PipelineId.ToString(),
            ["executionId"] = pipelineEvent.ExecutionId.ToString(),
            ["sourceDataSourceId"] = pipelineEvent.SourceDataSourceId.ToString(),
            ["targetDataSourceId"] = pipelineEvent.TargetDataSourceId.ToString(),
            ["processedRows"] = pipelineEvent.ProcessedRows.ToString()
        };

        if (!pipelineEvent.SourceTlsEnabled || !pipelineEvent.TargetTlsEnabled)
        {
            await CreateAlertAsync(
                pipelineEvent.TenantId,
                "transit_encryption",
                "high",
                "Data in transit without encryption.",
                $"Pipeline '{pipelineEvent.PipelineName}' is moving data without TLS on one or more connectors.",
                null,
                null,
                null,
                evidence,
                ct);
        }

        if (!pipelineEvent.IsApproved)
        {
            await CreateAlertAsync(
                pipelineEvent.TenantId,
                "unapproved_transfer",
                "medium",
                "Unapproved data transfer detected.",
                $"Pipeline '{pipelineEvent.PipelineName}' is running without workflow approval.",
                null,
                null,
                null,
                evidence,
                ct);
        }
    }

    private async Task<DspmScanSummary> RunScanForSourceAsync(
        DataSourceRegistration source,
        string trigger,
        Guid? pipelineId,
        Guid? executionId,
        IReadOnlyCollection<string>? scopedTables,
        CancellationToken ct)
    {
        var schema = DeserializeSchema(source.SchemaJson);
        var now = DateTime.UtcNow;
        var scan = new DspmScanRecord
        {
            Id = Guid.NewGuid(),
            TenantId = source.TenantId,
            SourceDataSourceId = source.Id,
            PipelineId = pipelineId,
            PipelineExecutionId = executionId,
            Trigger = trigger,
            Status = "running",
            EncryptionAtRestEnabled = source.EncryptionAtRestEnabled,
            ScopeTablesJson = JsonSerializer.Serialize(scopedTables?.ToList() ?? [], JsonOptions),
            StartedAt = now
        };

        var previousScan = await _db.DspmScanRecords
            .AsNoTracking()
            .Where(x => x.TenantId == source.TenantId
                        && x.SourceDataSourceId == source.Id
                        && x.Status == "completed")
            .OrderByDescending(x => x.CompletedAt)
            .ThenByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(ct);

        Dictionary<string, DspmColumnFinding> previousLookup = new(StringComparer.OrdinalIgnoreCase);
        if (previousScan is not null)
        {
            previousLookup = await _db.DspmColumnFindings
                .AsNoTracking()
                .Where(x => x.ScanId == previousScan.Id)
                .ToDictionaryAsync(
                    x => $"{x.TableName}.{x.ColumnName}",
                    x => x,
                    StringComparer.OrdinalIgnoreCase,
                    ct);
        }

        var tableScope = (scopedTables ?? Array.Empty<string>())
            .Select(NormalizeTable)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var findings = new List<DspmColumnFinding>();
        var piiFindingCount = 0;
        var newPiiCount = 0;
        var driftCount = 0;
        var restrictedCount = 0;
        var confidentialCount = 0;

        foreach (var table in schema.Tables)
        {
            if (tableScope.Count > 0 && !tableScope.Contains(NormalizeTable(table.TableName)))
            {
                continue;
            }

            foreach (var column in table.Columns)
            {
                var piiTypes = _piiClassifier.Classify(table.TableName, column);
                var sensitivity = _piiClassifier.ClassifySensitivity(piiTypes);
                var complianceTags = piiTypes.SelectMany(_complianceTagger.Tag).ToList();
                var key = $"{table.TableName}.{column.ColumnName}";
                previousLookup.TryGetValue(key, out var previous);
                var previousPiiTypes = previous is null ? [] : DeserializeList(previous.DetectedPiiTypesJson);
                var isNewPii = piiTypes.Count > 0
                               && (previous is null || piiTypes.Any(type => !previousPiiTypes.Contains(type, StringComparer.OrdinalIgnoreCase)));
                var isDrift = previous is not null
                              && Enum.TryParse<DataSensitivityLevel>(previous.Sensitivity, true, out var priorSensitivity)
                              && priorSensitivity < sensitivity;

                if (piiTypes.Count > 0)
                {
                    piiFindingCount++;
                }

                if (isNewPii)
                {
                    newPiiCount++;
                }

                if (isDrift)
                {
                    driftCount++;
                }

                if (sensitivity == DataSensitivityLevel.Restricted)
                {
                    restrictedCount++;
                }
                else if (sensitivity == DataSensitivityLevel.Confidential)
                {
                    confidentialCount++;
                }

                findings.Add(new DspmColumnFinding
                {
                    Id = Guid.NewGuid(),
                    ScanId = scan.Id,
                    TableName = table.TableName,
                    ColumnName = column.ColumnName,
                    DataType = column.DataType,
                    DetectedPiiTypesJson = JsonSerializer.Serialize(piiTypes, JsonOptions),
                    PrimaryPiiType = piiTypes.FirstOrDefault(),
                    Sensitivity = sensitivity.ToString(),
                    ComplianceTagsJson = JsonSerializer.Serialize(complianceTags, JsonOptions),
                    IsNewPii = isNewPii,
                    IsDrift = isDrift,
                    PreviousSensitivity = previous?.Sensitivity
                });
            }
        }

        scan.FindingsCount = piiFindingCount;
        scan.NewPiiCount = newPiiCount;
        scan.DriftCount = driftCount;
        scan.PostureScore = CalculatePostureScore(source.EncryptionAtRestEnabled, restrictedCount, confidentialCount, driftCount);
        scan.Status = "completed";
        scan.CompletedAt = DateTime.UtcNow;

        source.PostureScore = scan.PostureScore;
        source.LastScannedAt = scan.CompletedAt;
        source.UpdatedAt = DateTime.UtcNow;

        _db.DspmScanRecords.Add(scan);
        _db.DspmColumnFindings.AddRange(findings);

        if (newPiiCount > 0)
        {
            await CreateAlertAsync(
                source.TenantId,
                "dspm_new_pii",
                "high",
                "New regulated PII detected in scanned data.",
                $"Scan on source '{source.SourceName}' detected {newPiiCount} newly classified PII column(s).",
                null,
                null,
                null,
                new Dictionary<string, string>
                {
                    ["scanId"] = scan.Id.ToString(),
                    ["sourceId"] = source.Id.ToString(),
                    ["trigger"] = trigger
                },
                ct);
        }

        if (trigger == "at_rest" && driftCount > 0)
        {
            await CreateAlertAsync(
                source.TenantId,
                "dspm_classification_drift",
                "high",
                "Sensitive data classification drift detected.",
                $"At-rest scan on '{source.SourceName}' detected {driftCount} column(s) with higher sensitivity than the previous baseline.",
                null,
                null,
                null,
                new Dictionary<string, string>
                {
                    ["scanId"] = scan.Id.ToString(),
                    ["sourceId"] = source.Id.ToString()
                },
                ct);
        }

        if (trigger == "at_rest" && !source.EncryptionAtRestEnabled)
        {
            await CreateAlertAsync(
                source.TenantId,
                "at_rest_encryption",
                "high",
                "Data at rest without encryption detected.",
                $"Source '{source.SourceName}' is not configured with encryption at rest.",
                null,
                null,
                null,
                new Dictionary<string, string>
                {
                    ["scanId"] = scan.Id.ToString(),
                    ["sourceId"] = source.Id.ToString()
                },
                ct);
        }

        await _db.SaveChangesAsync(ct);

        if (_eventPublisher is not null && trigger == "pipeline_completed")
        {
            await _eventPublisher.PublishAsync(new DspmScanCompletedEvent(
                source.TenantId,
                scan.Id,
                source.Id,
                pipelineId,
                trigger,
                scan.FindingsCount,
                scan.NewPiiCount,
                scan.PostureScore,
                scan.CompletedAt ?? DateTime.UtcNow,
                Guid.NewGuid()), ct);
        }

        return MapScan(scan, source.SourceName, findings);
    }

    private async Task CreateAlertAsync(
        Guid tenantId,
        string alertType,
        string severity,
        string title,
        string description,
        string? userId,
        string? username,
        string? sourceIp,
        IReadOnlyDictionary<string, string> evidence,
        CancellationToken ct)
    {
        var alert = new SecurityAlert
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AlertType = alertType,
            Severity = severity,
            Title = title,
            Description = description,
            AffectedAssetIdsJson = "[]",
            UserId = userId,
            Username = username,
            SourceIp = sourceIp,
            EvidenceJson = JsonSerializer.Serialize(evidence, JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        _db.SecurityAlerts.Add(alert);
        _db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AlertId = alert.Id,
            EventSource = "security_alert",
            EventType = alertType,
            UserId = userId,
            Username = username,
            SourceIp = sourceIp,
            Description = description,
            EvidenceJson = alert.EvidenceJson,
            OccurredAt = alert.CreatedAt
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureSourceExists(Guid tenantId, Guid sourceId, CancellationToken ct)
    {
        var exists = await _db.DataSourceRegistrations.AnyAsync(x => x.TenantId == tenantId && x.Id == sourceId, ct);
        if (!exists)
        {
            throw new InvalidOperationException($"Data source {sourceId} was not found for tenant.");
        }
    }

    private async Task EnsureAssetExists(Guid tenantId, Guid assetId, CancellationToken ct)
    {
        var exists = await _db.CyberAssets.AnyAsync(x => x.TenantId == tenantId && x.Id == assetId, ct);
        if (!exists)
        {
            throw new InvalidOperationException($"Cyber asset {assetId} was not found for tenant.");
        }
    }

    private static DataSourceSchema DeserializeSchema(string payload)
        => JsonSerializer.Deserialize<DataSourceSchema>(payload, JsonOptions) ?? new DataSourceSchema();

    private static List<string> DeserializeList(string payload)
        => JsonSerializer.Deserialize<List<string>>(payload, JsonOptions) ?? [];

    private static decimal CalculatePostureScore(bool encryptionAtRest, int restrictedCount, int confidentialCount, int driftCount)
    {
        var score = 100m;
        if (!encryptionAtRest) score -= 20m;
        score -= restrictedCount * 6m;
        score -= confidentialCount * 3m;
        score -= driftCount * 4m;
        return Math.Max(0m, decimal.Round(score, 2));
    }

    private static string NormalizeTable(string tableName)
        => tableName.Trim().ToLowerInvariant();

    private static DataSourceSummary MapSource(DataSourceRegistration entity)
        => new()
        {
            SourceId = entity.Id,
            SourceName = entity.SourceName,
            SourceType = entity.SourceType,
            EncryptionAtRestEnabled = entity.EncryptionAtRestEnabled,
            TlsRequired = entity.TlsRequired,
            PostureScore = entity.PostureScore,
            LastScannedAt = entity.LastScannedAt
        };

    private static DataPipelineSummary MapPipeline(DataPipelineDefinition entity)
        => new()
        {
            PipelineId = entity.Id,
            PipelineName = entity.PipelineName,
            SourceDataSourceId = entity.SourceDataSourceId,
            TargetDataSourceId = entity.TargetDataSourceId,
            SourceTlsEnabled = entity.SourceTlsEnabled,
            TargetTlsEnabled = entity.TargetTlsEnabled,
            IsApproved = entity.IsApproved,
            MemoryLimitRows = entity.MemoryLimitRows,
            UpstreamPipelineIds = JsonSerializer.Deserialize<List<Guid>>(entity.UpstreamPipelineIdsJson, JsonOptions) ?? []
        };

    private static CyberAssetSummary MapAsset(CyberAsset entity)
        => new()
        {
            AssetId = entity.Id,
            AssetKey = entity.AssetKey,
            DisplayName = entity.DisplayName,
            AssetType = entity.AssetType,
            Criticality = entity.Criticality,
            LinkedDataSourceId = entity.LinkedDataSourceId,
            DataClassifications = JsonSerializer.Deserialize<List<string>>(entity.DataClassificationsJson, JsonOptions) ?? []
        };

    private static DspmAlertSummary MapAlert(SecurityAlert entity)
        => new()
        {
            AlertId = entity.Id,
            AlertType = entity.AlertType,
            Severity = entity.Severity,
            Title = entity.Title,
            Description = entity.Description,
            SourceIp = entity.SourceIp,
            UserId = entity.UserId,
            MitreTechnique = entity.MitreTechnique,
            AffectedAssetIds = JsonSerializer.Deserialize<List<Guid>>(entity.AffectedAssetIdsJson, JsonOptions) ?? [],
            CreatedAt = entity.CreatedAt
        };

    private static DataPipelineExecutionSummary MapExecution(DataPipelineExecution entity, string pipelineName)
        => new()
        {
            ExecutionId = entity.Id,
            PipelineId = entity.PipelineId,
            PipelineName = pipelineName,
            Status = entity.Status,
            Phase = entity.Phase,
            ErrorMessage = entity.ErrorMessage,
            ProcessedRows = entity.ProcessedRows,
            StartedAt = entity.StartedAt,
            CompletedAt = entity.CompletedAt
        };

    private static DspmScanSummary MapScan(DspmScanRecord scan, string sourceName, IReadOnlyCollection<DspmColumnFinding> findings)
        => new()
        {
            ScanId = scan.Id,
            SourceId = scan.SourceDataSourceId,
            SourceName = sourceName,
            Trigger = scan.Trigger,
            Status = scan.Status,
            FindingsCount = scan.FindingsCount,
            NewPiiCount = scan.NewPiiCount,
            DriftCount = scan.DriftCount,
            PostureScore = scan.PostureScore,
            EncryptionAtRestEnabled = scan.EncryptionAtRestEnabled,
            StartedAt = scan.StartedAt,
            CompletedAt = scan.CompletedAt,
            Columns = findings.Select(f => new DspmColumnClassification
            {
                TableName = f.TableName,
                ColumnName = f.ColumnName,
                DataType = f.DataType,
                DetectedPiiTypes = DeserializeList(f.DetectedPiiTypesJson),
                Sensitivity = Enum.TryParse<DataSensitivityLevel>(f.Sensitivity, true, out var sensitivity)
                    ? sensitivity
                    : DataSensitivityLevel.Internal,
                ComplianceTags = JsonSerializer.Deserialize<List<ComplianceTag>>(f.ComplianceTagsJson, JsonOptions) ?? [],
                IsNewPii = f.IsNewPii
            }).ToList()
        };
}
