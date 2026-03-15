using System.Globalization;
using System.Text.Json;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class HistoricalMigrationService : IHistoricalMigrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly MetadataDbContext _db;
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IEnumerable<IFileParser> _parsers;
    private readonly IGenericDataRepository _dataRepository;
    private readonly ValidationOrchestrator _validationOrchestrator;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<HistoricalMigrationService> _logger;

    public HistoricalMigrationService(
        MetadataDbContext db,
        ITemplateMetadataCache templateCache,
        IEnumerable<IFileParser> parsers,
        IGenericDataRepository dataRepository,
        ValidationOrchestrator validationOrchestrator,
        IAuditLogger auditLogger,
        ILogger<HistoricalMigrationService> logger)
    {
        _db = db;
        _templateCache = templateCache;
        _parsers = parsers;
        _dataRepository = dataRepository;
        _validationOrchestrator = validationOrchestrator;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ImportJobDto>> GetJobs(
        Guid tenantId,
        int? institutionId = null,
        CancellationToken ct = default)
    {
        var query = _db.ImportJobs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (institutionId.HasValue)
        {
            query = query.Where(x => x.InstitutionId == institutionId.Value);
        }

        var jobs = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        if (jobs.Count == 0)
        {
            return Array.Empty<ImportJobDto>();
        }

        var templateIds = jobs.Select(x => x.TemplateId).Distinct().ToList();
        var templateCodes = await _db.ReturnTemplates
            .AsNoTracking()
            .Where(x => templateIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ReturnCode, ct);

        return jobs
            .Select(job => ToDto(job, templateCodes.GetValueOrDefault(job.TemplateId, string.Empty)))
            .ToList();
    }

    public async Task<ImportJobDto> UploadAndParse(
        Guid tenantId,
        int institutionId,
        string returnCode,
        int returnPeriodId,
        string fileName,
        Stream fileStream,
        int importedBy,
        CancellationToken ct = default)
    {
        var template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        var parser = ResolveParser(fileName);

        var parseResult = await parser.Parse(fileStream, fileName, template.CurrentVersion.Fields, ct);
        var sourceFormat = ParseSourceFormat(parser.SourceFormat);

        await ApplySavedMappings(tenantId, institutionId, template.TemplateId, sourceFormat, parseResult, ct);
        var recomputedUnmapped = ComputeUnmappedFields(template.CurrentVersion.Fields, parseResult.ColumnMappings);
        parseResult.UnmappedFields = recomputedUnmapped;
        parseResult.UnmappedColumns = parseResult.ColumnMappings
            .Where(x => string.IsNullOrWhiteSpace(x.TargetFieldName) && !x.Ignored)
            .Select(x => x.SourceHeader)
            .ToList();

        var staged = new ImportStagedPayload
        {
            Records = parseResult.Records,
            Mappings = parseResult.ColumnMappings,
            UnmappedColumns = parseResult.UnmappedColumns,
            UnmappedFields = parseResult.UnmappedFields,
            ParserWarnings = parseResult.Warnings
        };

        var status = parseResult.UnmappedColumns.Count > 0 || parseResult.UnmappedFields.Count > 0
            ? ImportJobStatus.MappingReview
            : ImportJobStatus.Parsed;

        var job = new ImportJob
        {
            TenantId = tenantId,
            TemplateId = template.TemplateId,
            InstitutionId = institutionId,
            ReturnPeriodId = returnPeriodId,
            SourceFileName = fileName,
            SourceFormat = sourceFormat,
            Status = status,
            RecordCount = parseResult.RecordCount,
            ErrorCount = 0,
            WarningCount = parseResult.Warnings.Count,
            StagedData = JsonSerializer.Serialize(staged, JsonOptions),
            ImportedBy = importedBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.ImportJobs.Add(job);
        await _db.SaveChangesAsync(ct);

        return ToDto(job, template.ReturnCode);
    }

    public async Task<ImportJobDto?> GetJob(Guid tenantId, int importJobId, CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct);
        if (job is null)
        {
            return null;
        }

        var returnCode = await _db.ReturnTemplates
            .Where(x => x.Id == job.TemplateId)
            .Select(x => x.ReturnCode)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        return ToDto(job, returnCode);
    }

    public async Task<ImportMappingEditorDto> GetMappingEditor(Guid tenantId, int importJobId, CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job {importJobId} was not found for tenant.");

        var template = await _templateCache.GetPublishedTemplate(tenantId,
            await _db.ReturnTemplates.Where(x => x.Id == job.TemplateId).Select(x => x.ReturnCode).FirstAsync(ct),
            ct);

        var staged = DeserializeStaged(job.StagedData);

        return new ImportMappingEditorDto
        {
            ImportJobId = job.Id,
            ReturnCode = template.ReturnCode,
            TemplateName = template.Name,
            Mappings = staged.Mappings,
            UnmappedColumns = staged.UnmappedColumns,
            UnmappedFields = staged.UnmappedFields,
            AvailableFields = template.CurrentVersion.Fields
                .OrderBy(x => x.FieldOrder)
                .Select(x => new ImportFieldOption
                {
                    FieldName = x.FieldName,
                    FieldLabel = x.DisplayName,
                    DataType = x.DataType.ToString()
                }).ToList()
        };
    }

    public async Task SaveMapping(
        Guid tenantId,
        int importJobId,
        IReadOnlyList<ImportMappingUpdate> updates,
        string? sourceIdentifier,
        CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job {importJobId} was not found for tenant.");

        var staged = DeserializeStaged(job.StagedData);

        foreach (var update in updates)
        {
            var target = staged.Mappings.FirstOrDefault(m =>
                m.SourceIndex == update.SourceIndex
                || string.Equals(m.SourceHeader, update.SourceHeader, StringComparison.OrdinalIgnoreCase));

            if (target is null)
            {
                continue;
            }

            target.TargetFieldName = string.IsNullOrWhiteSpace(update.TargetFieldName) ? null : update.TargetFieldName.Trim();
            target.Ignored = update.Ignored;
            if (target.TargetFieldName is not null)
            {
                target.Confidence = Math.Max(target.Confidence, 0.99);
            }
        }

        var template = await _templateCache.GetPublishedTemplate(tenantId,
            await _db.ReturnTemplates.Where(x => x.Id == job.TemplateId).Select(x => x.ReturnCode).FirstAsync(ct),
            ct);

        staged.UnmappedColumns = staged.Mappings
            .Where(x => string.IsNullOrWhiteSpace(x.TargetFieldName) && !x.Ignored)
            .Select(x => x.SourceHeader)
            .ToList();
        staged.UnmappedFields = ComputeUnmappedFields(template.CurrentVersion.Fields, staged.Mappings);
        staged.ReviewedRecords.Clear();

        job.StagedData = JsonSerializer.Serialize(staged, JsonOptions);
        job.Status = staged.UnmappedColumns.Count == 0 && staged.UnmappedFields.Count == 0
            ? ImportJobStatus.Parsed
            : ImportJobStatus.MappingReview;
        job.UpdatedAt = DateTime.UtcNow;

        var mappingConfig = JsonSerializer.Serialize(staged.Mappings.Select(x => new
        {
            sourceIndex = x.SourceIndex,
            sourceColumn = x.SourceHeader,
            targetFieldName = x.TargetFieldName,
            ignored = x.Ignored
        }), JsonOptions);

        var existing = await _db.ImportMappings.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId
            && x.InstitutionId == job.InstitutionId
            && x.TemplateId == job.TemplateId
            && x.SourceFormat == job.SourceFormat, ct);

        if (existing is null)
        {
            _db.ImportMappings.Add(new ImportMapping
            {
                TenantId = tenantId,
                InstitutionId = job.InstitutionId,
                TemplateId = job.TemplateId,
                SourceFormat = job.SourceFormat,
                SourceIdentifier = sourceIdentifier,
                MappingConfig = mappingConfig,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.SourceIdentifier = sourceIdentifier;
            existing.MappingConfig = mappingConfig;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<ImportJobDto> ValidateJob(Guid tenantId, int importJobId, CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job {importJobId} was not found for tenant.");

        var returnCode = await _db.ReturnTemplates
            .Where(x => x.Id == job.TemplateId)
            .Select(x => x.ReturnCode)
            .FirstAsync(ct);
        var template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        var staged = DeserializeStaged(job.StagedData);

        var mappedRecords = GetMappedRecords(staged, template.CurrentVersion.Fields);
        var report = await _validationOrchestrator.ValidateRelaxed(mappedRecords, template, tenantId, ct);

        var serializedErrors = JsonSerializer.Serialize(report.Errors.Select(x => new
        {
            x.RuleId,
            x.Field,
            x.Message,
            Severity = x.Severity.ToString(),
            Category = x.Category.ToString()
        }), JsonOptions);

        job.ValidationReport = serializedErrors;
        job.WarningCount = report.WarningCount;
        job.ErrorCount = report.ErrorCount;
        job.RecordCount = mappedRecords.Count;
        job.Status = ImportJobStatus.Validated;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return ToDto(job, template.ReturnCode);
    }

    public async Task<ImportJobDto> StageJob(Guid tenantId, int importJobId, CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job {importJobId} was not found for tenant.");

        if (job.Status is ImportJobStatus.Uploaded or ImportJobStatus.Parsed or ImportJobStatus.MappingReview)
        {
            await ValidateJob(tenantId, importJobId, ct);
        }

        job.Status = ImportJobStatus.Staged;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var returnCode = await _db.ReturnTemplates.Where(x => x.Id == job.TemplateId).Select(x => x.ReturnCode).FirstAsync(ct);
        return ToDto(job, returnCode);
    }

    public async Task<ImportJobDto> CommitJob(Guid tenantId, int importJobId, CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job {importJobId} was not found for tenant.");

        if (job.ReturnPeriodId is null)
        {
            throw new InvalidOperationException("Import job is missing ReturnPeriodId.");
        }

        var returnCode = await _db.ReturnTemplates
            .Where(x => x.Id == job.TemplateId)
            .Select(x => x.ReturnCode)
            .FirstAsync(ct);

        var template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        var staged = DeserializeStaged(job.StagedData);
        var mappedRecords = GetMappedRecords(staged, template.CurrentVersion.Fields);

        var submission = Submission.Create(job.InstitutionId, job.ReturnPeriodId.Value, returnCode, tenantId);
        submission.MarkSubmitted();
        submission.SetTemplateVersion(template.CurrentVersion.Id);
        submission.Status = SubmissionStatus.Historical;
        submission.SubmittedByUserId = job.ImportedBy;
        submission.ApprovalRequired = false;
        submission.SubmittedAt = DateTime.UtcNow;
        submission.CreatedAt = DateTime.UtcNow;

        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync(ct);

        var record = BuildRecord(template, mappedRecords);
        await _dataRepository.DeleteBySubmission(returnCode, submission.Id, ct);
        await _dataRepository.Save(record, submission.Id, ct);

        job.Status = ImportJobStatus.Committed;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "ImportJob",
            job.Id,
            "Commit",
            new { previousStatus = ImportJobStatus.Staged.ToString() },
            new { submissionId = submission.Id, submissionStatus = SubmissionStatus.Historical.ToString() },
            job.ImportedBy.ToString(CultureInfo.InvariantCulture),
            ct);

        return ToDto(job, returnCode);
    }

    public async Task<ImportStagedReviewDto> GetStagedReview(
        Guid tenantId,
        int importJobId,
        int take = 200,
        CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job {importJobId} was not found for tenant.");

        var returnCode = await _db.ReturnTemplates
            .AsNoTracking()
            .Where(x => x.Id == job.TemplateId)
            .Select(x => x.ReturnCode)
            .FirstAsync(ct);
        var template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        var staged = DeserializeStaged(job.StagedData);
        var allMappedRecords = GetMappedRecords(staged, template.CurrentVersion.Fields);
        var mappedRecords = allMappedRecords
            .Take(Math.Max(1, take))
            .ToList();

        var columns = template.CurrentVersion.Fields
            .OrderBy(x => x.FieldOrder)
            .Select(x => x.FieldName)
            .ToList();

        var response = new ImportStagedReviewDto
        {
            ImportJobId = job.Id,
            ReturnCode = returnCode,
            Columns = columns,
            TotalRecords = allMappedRecords.Count
        };

        for (var i = 0; i < mappedRecords.Count; i++)
        {
            var source = mappedRecords[i];
            var dto = new ImportStagedRecordDto { RowNumber = i + 1 };
            foreach (var column in columns)
            {
                dto.Values[column] = source.TryGetValue(column, out var value)
                    ? value?.ToString()
                    : null;
            }

            response.Records.Add(dto);
        }

        return response;
    }

    public async Task SaveStagedReview(
        Guid tenantId,
        int importJobId,
        IReadOnlyList<ImportStagedRecordDto> records,
        CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .FirstOrDefaultAsync(x => x.Id == importJobId && x.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job {importJobId} was not found for tenant.");

        var staged = DeserializeStaged(job.StagedData);
        var normalized = records
            .OrderBy(x => x.RowNumber)
            .Select(r => r.Values.ToDictionary(
                x => x.Key,
                x => string.IsNullOrWhiteSpace(x.Value) ? null : x.Value?.Trim(),
                StringComparer.OrdinalIgnoreCase))
            .ToList();

        staged.ReviewedRecords = normalized;
        job.StagedData = JsonSerializer.Serialize(staged, JsonOptions);
        job.Status = ImportJobStatus.Staged;
        job.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<MigrationTrackerDto> GetTracker(Guid tenantId, CancellationToken ct = default)
    {
        var templateModules = await _db.ReturnTemplates
            .AsNoTracking()
            .Where(t => t.ModuleId != null)
            .Select(t => new { t.Id, t.ModuleId })
            .ToListAsync(ct);

        var jobs = await _db.ImportJobs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToListAsync(ct);

        var signOffs = await _db.MigrationModuleSignOffs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.ModuleId, x => x, ct);

        var modules = await _db.Modules.AsNoTracking()
            .OrderBy(x => x.ModuleCode)
            .ToListAsync(ct);

        var response = new MigrationTrackerDto
        {
            TotalModules = modules.Count
        };

        foreach (var module in modules)
        {
            var templateIds = templateModules
                .Where(x => x.ModuleId == module.Id)
                .Select(x => x.Id)
                .ToHashSet();

            var moduleJobs = jobs.Where(j => templateIds.Contains(j.TemplateId)).ToList();
            var committed = moduleJobs.Where(j => j.Status == ImportJobStatus.Committed).ToList();

            var totalPeriods = await _db.ReturnPeriods
                .AsNoTracking()
                .CountAsync(rp => rp.TenantId == tenantId && rp.ModuleId == module.Id, ct);

            var progress = new MigrationModuleProgressDto
            {
                ModuleId = module.Id,
                ModuleCode = module.ModuleCode,
                ModuleName = module.ModuleName,
                ImportedPeriods = committed.Select(x => x.ReturnPeriodId).Distinct().Count(),
                TotalPeriods = totalPeriods,
                WarningCount = moduleJobs.Sum(x => x.WarningCount ?? 0),
                ErrorCount = moduleJobs.Sum(x => x.ErrorCount ?? 0)
            };

            progress.AutoSignOffEligible = progress.ImportedPeriods > 0 && progress.ErrorCount == 0;
            if (signOffs.TryGetValue(module.Id, out var signed))
            {
                progress.SignOff = signed.IsSignedOff;
                progress.SignedOffBy = signed.SignedOffBy;
                progress.SignedOffAt = signed.SignedOffAt;
                progress.SignOffNotes = signed.Notes;
            }
            else
            {
                progress.SignOff = false;
            }

            response.Modules.Add(progress);
        }

        response.ModulesMigrated = response.Modules.Count(x => x.ImportedPeriods > 0);
        return response;
    }

    public async Task SetModuleSignOff(
        Guid tenantId,
        int moduleId,
        bool signedOff,
        int signedOffByUserId,
        string? notes,
        CancellationToken ct = default)
    {
        var existing = await _db.MigrationModuleSignOffs
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ModuleId == moduleId, ct);

        if (existing is null)
        {
            _db.MigrationModuleSignOffs.Add(new MigrationModuleSignOff
            {
                TenantId = tenantId,
                ModuleId = moduleId,
                IsSignedOff = signedOff,
                SignedOffBy = signedOffByUserId,
                SignedOffAt = DateTime.UtcNow,
                Notes = notes
            });
        }
        else
        {
            existing.IsSignedOff = signedOff;
            existing.SignedOffBy = signedOffByUserId;
            existing.SignedOffAt = DateTime.UtcNow;
            existing.Notes = notes;
        }

        await _db.SaveChangesAsync(ct);
    }

    private IFileParser ResolveParser(string fileName)
    {
        var parser = _parsers.FirstOrDefault(x => x.CanHandle(fileName));
        if (parser is null)
        {
            throw new InvalidOperationException($"Unsupported source file '{fileName}'. Supported formats are Excel, CSV, and PDF.");
        }

        return parser;
    }

    private static HistoricalSourceFormat ParseSourceFormat(string sourceFormat)
    {
        if (sourceFormat.Equals("excel", StringComparison.OrdinalIgnoreCase)) return HistoricalSourceFormat.Excel;
        if (sourceFormat.Equals("csv", StringComparison.OrdinalIgnoreCase)) return HistoricalSourceFormat.Csv;
        if (sourceFormat.Equals("pdf", StringComparison.OrdinalIgnoreCase)) return HistoricalSourceFormat.Pdf;
        return HistoricalSourceFormat.Excel;
    }

    private async Task ApplySavedMappings(
        Guid tenantId,
        int institutionId,
        int templateId,
        HistoricalSourceFormat sourceFormat,
        ParseResult parseResult,
        CancellationToken ct)
    {
        var existing = await _db.ImportMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                      && x.InstitutionId == institutionId
                                      && x.TemplateId == templateId
                                      && x.SourceFormat == sourceFormat,
                ct);
        if (existing is null)
        {
            return;
        }

        List<SavedMappingRecord>? mappings;
        try
        {
            mappings = JsonSerializer.Deserialize<List<SavedMappingRecord>>(existing.MappingConfig, JsonOptions);
        }
        catch
        {
            _logger.LogWarning("Failed to deserialize saved import mapping for tenant {TenantId}, template {TemplateId}", tenantId, templateId);
            return;
        }

        if (mappings is null || mappings.Count == 0)
        {
            return;
        }

        foreach (var column in parseResult.ColumnMappings)
        {
            var saved = mappings.FirstOrDefault(x =>
                x.SourceIndex == column.SourceIndex
                || string.Equals(x.SourceColumn, column.SourceHeader, StringComparison.OrdinalIgnoreCase));

            if (saved is null)
            {
                continue;
            }

            column.TargetFieldName = saved.TargetFieldName;
            column.Ignored = saved.Ignored;
            if (!string.IsNullOrWhiteSpace(saved.TargetFieldName))
            {
                column.Confidence = Math.Max(column.Confidence, 0.99);
            }
        }
    }

    private static List<string> ComputeUnmappedFields(
        IReadOnlyList<TemplateField> fields,
        IReadOnlyList<ImportColumnMapping> mappings)
    {
        return fields
            .Where(field => mappings.All(x => !string.Equals(x.TargetFieldName, field.FieldName, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.FieldName)
            .ToList();
    }

    private static ImportStagedPayload DeserializeStaged(string? stagedData)
    {
        if (string.IsNullOrWhiteSpace(stagedData))
        {
            return new ImportStagedPayload();
        }

        var payload = JsonSerializer.Deserialize<ImportStagedPayload>(stagedData, JsonOptions)
                      ?? new ImportStagedPayload();
        payload.ReviewedRecords ??= [];
        payload.Mappings ??= [];
        payload.Records ??= [];
        payload.UnmappedColumns ??= [];
        payload.UnmappedFields ??= [];
        payload.ParserWarnings ??= [];
        return payload;
    }

    private static List<Dictionary<string, object?>> GetMappedRecords(
        ImportStagedPayload staged,
        IReadOnlyList<TemplateField> fields)
    {
        if (staged.ReviewedRecords is { Count: > 0 })
        {
            var reviewedRows = new List<Dictionary<string, object?>>();
            foreach (var reviewed in staged.ReviewedRecords)
            {
                var destination = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in fields)
                {
                    if (!reviewed.TryGetValue(field.FieldName, out var raw))
                    {
                        continue;
                    }

                    destination[field.FieldName] = ConvertFromString(raw, field.DataType);
                }

                if (destination.Count > 0)
                {
                    reviewedRows.Add(destination);
                }
            }

            if (reviewedRows.Count > 0)
            {
                return reviewedRows;
            }
        }

        var fieldByName = fields.ToDictionary(x => x.FieldName, StringComparer.OrdinalIgnoreCase);
        var mappings = staged.Mappings
            .Where(x => !x.Ignored && !string.IsNullOrWhiteSpace(x.TargetFieldName))
            .ToList();

        var mapped = new List<Dictionary<string, object?>>();

        foreach (var sourceRow in staged.Records)
        {
            var destination = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var mapping in mappings)
            {
                if (mapping.TargetFieldName is null)
                {
                    continue;
                }

                if (!fieldByName.TryGetValue(mapping.TargetFieldName, out var field))
                {
                    continue;
                }

                if (!sourceRow.TryGetValue(mapping.SourceHeader, out var rawValue))
                {
                    continue;
                }

                destination[field.FieldName] = ConvertFromString(rawValue, field.DataType);
            }

            if (destination.Count > 0)
            {
                mapped.Add(destination);
            }
        }

        return mapped;
    }

    private static object? ConvertFromString(string? raw, FieldDataType dataType)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        return dataType switch
        {
            FieldDataType.Integer => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : value,
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage =>
                decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : value,
            FieldDataType.Date => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.Date
                : value,
            FieldDataType.Boolean => bool.TryParse(value, out var b) ? b : value,
            _ => value
        };
    }

    private static ReturnDataRecord BuildRecord(CachedTemplate template, IReadOnlyList<Dictionary<string, object?>> mappedRecords)
    {
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        var record = new ReturnDataRecord(template.ReturnCode, template.CurrentVersion.Id, category);

        if (mappedRecords.Count == 0)
        {
            record.AddRow(new ReturnDataRow());
            return record;
        }

        foreach (var source in mappedRecords)
        {
            var row = new ReturnDataRow();
            foreach (var pair in source)
            {
                row.SetValue(pair.Key, pair.Value);
            }

            record.AddRow(row);
        }

        return record;
    }

    private static ImportJobDto ToDto(ImportJob job, string returnCode)
    {
        return new ImportJobDto
        {
            Id = job.Id,
            TenantId = job.TenantId,
            TemplateId = job.TemplateId,
            ReturnCode = returnCode,
            InstitutionId = job.InstitutionId,
            ReturnPeriodId = job.ReturnPeriodId,
            SourceFileName = job.SourceFileName,
            SourceFormat = job.SourceFormat,
            Status = job.Status,
            RecordCount = job.RecordCount ?? 0,
            ErrorCount = job.ErrorCount ?? 0,
            WarningCount = job.WarningCount ?? 0,
            ImportedBy = job.ImportedBy,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt
        };
    }

    public async Task<ImportJobDto> RollbackJob(Guid tenantId, int importJobId, int rolledBackByUserId, CancellationToken ct = default)
    {
        var job = await _db.ImportJobs
            .FirstOrDefaultAsync(j => j.Id == importJobId && j.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Import job #{importJobId} not found.");

        if (job.Status != ImportJobStatus.Committed)
            throw new InvalidOperationException("Only committed import jobs can be rolled back.");

        if ((DateTime.UtcNow - job.UpdatedAt).TotalHours >= 24)
            throw new InvalidOperationException("The 24-hour rollback window has expired for this import job.");

        job.Status = ImportJobStatus.Staged;
        job.StagedData = null;
        job.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Import job {JobId} rolled back by user {UserId}", importJobId, rolledBackByUserId);

        return MapToDto(job);
    }

    private static ImportJobDto MapToDto(ImportJob job) => new()
    {
        Id = job.Id,
        TenantId = job.TenantId,
        TemplateId = job.TemplateId,
        ReturnCode = job.Template?.ReturnCode ?? string.Empty,
        InstitutionId = job.InstitutionId,
        ReturnPeriodId = job.ReturnPeriodId,
        SourceFileName = job.SourceFileName,
        SourceFormat = job.SourceFormat,
        Status = job.Status,
        RecordCount = job.RecordCount ?? 0,
        ErrorCount = job.ErrorCount ?? 0,
        WarningCount = job.WarningCount ?? 0,
        ImportedBy = job.ImportedBy,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt
    };

    private sealed class ImportStagedPayload
    {
        public List<Dictionary<string, string?>> Records { get; set; } = [];
        public List<ImportColumnMapping> Mappings { get; set; } = [];
        public List<Dictionary<string, string?>> ReviewedRecords { get; set; } = [];
        public List<string> UnmappedColumns { get; set; } = [];
        public List<string> UnmappedFields { get; set; } = [];
        public List<string> ParserWarnings { get; set; } = [];
    }

    private sealed class SavedMappingRecord
    {
        public int SourceIndex { get; set; }
        public string SourceColumn { get; set; } = string.Empty;
        public string? TargetFieldName { get; set; }
        public bool Ignored { get; set; }
    }
}
