using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class DataFeedService : IDataFeedService
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IGenericDataRepository _dataRepository;
    private readonly ISubmissionRepository _submissionRepository;
    private readonly ValidationOrchestrator _validationOrchestrator;
    private readonly IAnomalyDetectionService? _anomalyDetectionService;
    private readonly MetadataDbContext _db;
    private readonly ILogger<DataFeedService> _logger;

    public DataFeedService(
        ITemplateMetadataCache templateCache,
        IGenericDataRepository dataRepository,
        ISubmissionRepository submissionRepository,
        ValidationOrchestrator validationOrchestrator,
        MetadataDbContext db,
        ILogger<DataFeedService> logger,
        IAnomalyDetectionService? anomalyDetectionService = null)
    {
        _templateCache = templateCache;
        _dataRepository = dataRepository;
        _submissionRepository = submissionRepository;
        _validationOrchestrator = validationOrchestrator;
        _anomalyDetectionService = anomalyDetectionService;
        _db = db;
        _logger = logger;
    }

    public async Task<DataFeedResult?> GetByIdempotencyKey(Guid tenantId, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var existing = await _db.DataFeedRequestLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<DataFeedResult>(existing.ResultJson);
    }

    public async Task<DataFeedResult> ProcessFeed(
        Guid tenantId,
        string returnCode,
        DataFeedRequest request,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        var result = new DataFeedResult
        {
            ReturnCode = returnCode,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim()
        };

        if (request is null)
        {
            result.Success = false;
            result.Message = "Request payload is required.";
            result.Errors.Add("Request payload is required.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(request.PeriodCode))
        {
            result.Success = false;
            result.Message = "PeriodCode is required.";
            result.Errors.Add("PeriodCode is required.");
            return result;
        }

        if (request.Fields is null || request.Fields.Count == 0)
        {
            result.Success = false;
            result.Message = "No field values were provided.";
            result.Errors.Add("No field values were provided.");
            return result;
        }

        var template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        var period = await ResolvePeriod(tenantId, request.PeriodCode, ct);
        if (period is null)
        {
            result.Success = false;
            result.Message = "Unable to resolve return period from PeriodCode.";
            result.Errors.Add($"PeriodCode '{request.PeriodCode}' could not be resolved.");
            return result;
        }

        var institution = await ResolveInstitution(tenantId, request.InstitutionCode, ct);
        if (institution is null)
        {
            result.Success = false;
            result.Message = "Unable to resolve institution for tenant.";
            result.Errors.Add("No active institution found for tenant.");
            return result;
        }

        var normalizedMappings = await GetFieldMappingDictionary(
            tenantId,
            request.IntegrationName ?? "default",
            returnCode,
            ct);

        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();
        var fieldByName = fields.ToDictionary(x => x.FieldName, StringComparer.OrdinalIgnoreCase);
        var row = new ReturnDataRow();

        foreach (var input in request.Fields)
        {
            if (string.IsNullOrWhiteSpace(input.FieldCode))
            {
                continue;
            }

            var incoming = input.FieldCode.Trim();
            var mappedFieldName = incoming;
            if (!fieldByName.ContainsKey(mappedFieldName)
                && normalizedMappings.TryGetValue(incoming, out var mapped))
            {
                mappedFieldName = mapped;
            }

            if (!fieldByName.TryGetValue(mappedFieldName, out var templateField))
            {
                result.Errors.Add($"Unknown field '{incoming}' for return {returnCode}.");
                continue;
            }

            try
            {
                var converted = ConvertIncomingValue(input.Value, templateField.DataType);
                row.SetValue(templateField.FieldName, converted);
                if (templateField.IsKeyField && converted is not null)
                {
                    row.RowKey = converted.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Data feed field conversion failed for {FieldCode}", incoming);
                result.Errors.Add($"Field '{incoming}' conversion failed: {ex.Message}");
            }
        }

        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        var record = new ReturnDataRecord(returnCode, template.CurrentVersion.Id, category);
        record.AddRow(row);

        var submission = Submission.Create(institution.Id, period.Id, returnCode, tenantId);
        submission.MarkSubmitted();
        submission.SetTemplateVersion(template.CurrentVersion.Id);
        submission.StoreParsedDataJson(SubmissionPayloadSerializer.Serialize(record));
        await _submissionRepository.Add(submission, ct);

        var report = await _validationOrchestrator.Validate(record, submission, institution.Id, period.Id, ct);
        foreach (var error in result.Errors)
        {
            report.AddError(new ValidationError
            {
                RuleId = "DATA_FEED",
                Field = "N/A",
                Message = error,
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.TypeRange
            });
        }

        submission.AttachValidationReport(report);

        if (report.HasErrors)
        {
            submission.MarkRejected();
            report.FinalizeAt(DateTime.UtcNow);
            await _submissionRepository.Update(submission, ct);

            result.Success = false;
            result.SubmissionId = submission.Id;
            result.Status = submission.Status.ToString();
            result.Message = "Data feed validation failed.";
            result.Errors = report.Errors.Select(x => x.Message).Distinct().ToList();
            await PersistIdempotency(tenantId, returnCode, idempotencyKey, request, result, ct);
            return result;
        }

        await _dataRepository.DeleteBySubmission(returnCode, submission.Id, ct);
        await _dataRepository.Save(record, submission.Id, ct);

        if (report.HasWarnings)
        {
            submission.MarkAcceptedWithWarnings();
        }
        else
        {
            submission.MarkAccepted();
        }

        report.FinalizeAt(DateTime.UtcNow);
        await _submissionRepository.Update(submission, ct);
        await TryAnalyzeSubmissionAsync(submission, ct);

        result.Success = true;
        result.SubmissionId = submission.Id;
        result.Status = submission.Status.ToString();
        result.RowsPersisted = 1;
        result.Message = "Data feed processed successfully.";
        await PersistIdempotency(tenantId, returnCode, idempotencyKey, request, result, ct);
        return result;
    }

    private async Task TryAnalyzeSubmissionAsync(Submission submission, CancellationToken ct)
    {
        if (_anomalyDetectionService is null
            || submission.TenantId == Guid.Empty
            || submission.Status is not (SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings))
        {
            return;
        }

        try
        {
            await _anomalyDetectionService.AnalyzeSubmissionAsync(
                submission.Id,
                submission.TenantId,
                "data-feed",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate anomaly report for data-feed submission {SubmissionId}", submission.Id);
        }
    }

    public async Task UpsertFieldMapping(
        Guid tenantId,
        string integrationName,
        string returnCode,
        string externalFieldName,
        string templateFieldName,
        CancellationToken ct = default)
    {
        var normalizedIntegration = NormalizeIntegrationName(integrationName);
        var normalizedExternal = externalFieldName.Trim();
        var normalizedTemplateField = templateFieldName.Trim();

        var existing = await _db.TenantFieldMappings.FirstOrDefaultAsync(
            x => x.TenantId == tenantId
                 && x.IntegrationName == normalizedIntegration
                 && x.ReturnCode == returnCode
                 && x.ExternalFieldName == normalizedExternal,
            ct);

        if (existing is null)
        {
            _db.TenantFieldMappings.Add(new TenantFieldMapping
            {
                TenantId = tenantId,
                IntegrationName = normalizedIntegration,
                ReturnCode = returnCode,
                ExternalFieldName = normalizedExternal,
                TemplateFieldName = normalizedTemplateField,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.TemplateFieldName = normalizedTemplateField;
            existing.IsActive = true;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TenantFieldMappingEntry>> GetFieldMappings(
        Guid tenantId,
        string integrationName,
        string returnCode,
        CancellationToken ct = default)
    {
        var normalizedIntegration = NormalizeIntegrationName(integrationName);
        var entries = await _db.TenantFieldMappings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && x.IntegrationName == normalizedIntegration
                        && x.ReturnCode == returnCode
                        && x.IsActive)
            .OrderBy(x => x.ExternalFieldName)
            .ToListAsync(ct);

        return entries.Select(x => new TenantFieldMappingEntry
        {
            IntegrationName = x.IntegrationName,
            ReturnCode = x.ReturnCode,
            ExternalFieldName = x.ExternalFieldName,
            TemplateFieldName = x.TemplateFieldName,
            IsActive = x.IsActive
        }).ToList();
    }

    private async Task PersistIdempotency(
        Guid tenantId,
        string returnCode,
        string? idempotencyKey,
        DataFeedRequest request,
        DataFeedResult result,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return;
        }

        var key = idempotencyKey.Trim();
        var requestJson = JsonSerializer.Serialize(request);
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson))).ToLowerInvariant();
        var resultJson = JsonSerializer.Serialize(result);

        var existing = await _db.DataFeedRequestLogs
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == key, ct);

        if (existing is null)
        {
            _db.DataFeedRequestLogs.Add(new DataFeedRequestLog
            {
                TenantId = tenantId,
                ReturnCode = returnCode,
                IdempotencyKey = key,
                RequestHash = requestHash,
                ResultJson = resultJson,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.ReturnCode = returnCode;
            existing.RequestHash = requestHash;
            existing.ResultJson = resultJson;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<ReturnPeriod?> ResolvePeriod(Guid tenantId, string periodCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(periodCode))
        {
            return null;
        }

        var normalized = periodCode.Trim();
        if (TryParseYearMonth(normalized, out var year, out var month))
        {
            return await _db.ReturnPeriods
                .Where(x => x.TenantId == tenantId && x.Year == year && x.Month == month && x.IsOpen)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (TryParseYearQuarter(normalized, out year, out var quarter))
        {
            return await _db.ReturnPeriods
                .Where(x => x.TenantId == tenantId && x.Year == year && x.Quarter == quarter && x.IsOpen)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(ct);
        }

        return null;
    }

    private async Task<Institution?> ResolveInstitution(Guid tenantId, string? institutionCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(institutionCode))
        {
            var code = institutionCode.Trim();
            return await _db.Institutions
                .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                          && x.InstitutionCode == code
                                          && x.IsActive, ct);
        }

        return await _db.Institutions
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Dictionary<string, string>> GetFieldMappingDictionary(
        Guid tenantId,
        string integrationName,
        string returnCode,
        CancellationToken ct)
    {
        var normalizedIntegration = NormalizeIntegrationName(integrationName);
        var mappings = await _db.TenantFieldMappings
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId
                        && x.IntegrationName == normalizedIntegration
                        && x.ReturnCode == returnCode
                        && x.IsActive)
            .ToListAsync(ct);

        return mappings.ToDictionary(
            x => x.ExternalFieldName,
            x => x.TemplateFieldName,
            StringComparer.OrdinalIgnoreCase);
    }

    private static object? ConvertIncomingValue(object? value, FieldDataType dataType)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement element)
        {
            return ConvertJsonElement(element, dataType);
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return dataType switch
        {
            FieldDataType.Integer => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => decimal.Parse(text, NumberStyles.Any, CultureInfo.InvariantCulture),
            FieldDataType.Date => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).Date,
            FieldDataType.Boolean => bool.Parse(text),
            _ => text.Trim()
        };
    }

    private static object? ConvertJsonElement(JsonElement value, FieldDataType dataType)
    {
        if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return dataType switch
        {
            FieldDataType.Integer => value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : int.Parse(value.GetString() ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture),
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : decimal.Parse(value.GetString() ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture),
            FieldDataType.Date => value.ValueKind == JsonValueKind.String
                ? DateTime.Parse(value.GetString() ?? string.Empty, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).Date
                : value.GetDateTime().Date,
            FieldDataType.Boolean => value.ValueKind == JsonValueKind.True
                ? true
                : value.ValueKind == JsonValueKind.False
                    ? false
                    : bool.Parse(value.GetString() ?? "false"),
            _ => value.ToString()
        };
    }

    private static bool TryParseYearMonth(string periodCode, out int year, out int month)
    {
        year = 0;
        month = 0;

        var parts = periodCode.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[1].StartsWith("Q", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return int.TryParse(parts[0], out year)
               && int.TryParse(parts[1], out month)
               && month is >= 1 and <= 12;
    }

    private static bool TryParseYearQuarter(string periodCode, out int year, out int quarter)
    {
        year = 0;
        quarter = 0;

        var parts = periodCode.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[1].StartsWith("Q", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var qPart = parts[1][1..];
        return int.TryParse(parts[0], out year)
               && int.TryParse(qPart, out quarter)
               && quarter is >= 1 and <= 4;
    }

    private static string NormalizeIntegrationName(string? integrationName)
    {
        return string.IsNullOrWhiteSpace(integrationName) ? "default" : integrationName.Trim();
    }
}
