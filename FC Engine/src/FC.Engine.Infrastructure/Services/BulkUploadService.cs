using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class BulkUploadService : IBulkUploadService
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IGenericDataRepository _dataRepository;
    private readonly ISubmissionRepository _submissionRepository;
    private readonly ValidationOrchestrator _validationOrchestrator;
    private readonly IAnomalyDetectionService? _anomalyDetectionService;
    private readonly ILogger<BulkUploadService> _logger;

    public BulkUploadService(
        ITemplateMetadataCache templateCache,
        IGenericDataRepository dataRepository,
        ISubmissionRepository submissionRepository,
        ValidationOrchestrator validationOrchestrator,
        ILogger<BulkUploadService> logger,
        IAnomalyDetectionService? anomalyDetectionService = null)
    {
        _templateCache = templateCache;
        _dataRepository = dataRepository;
        _submissionRepository = submissionRepository;
        _validationOrchestrator = validationOrchestrator;
        _anomalyDetectionService = anomalyDetectionService;
        _logger = logger;
    }

    public async Task<BulkUploadResult> ProcessExcelUpload(
        Stream fileStream,
        Guid tenantId,
        string returnCode,
        int institutionId,
        int returnPeriodId,
        int? requestedByUserId = null,
        CancellationToken ct = default)
    {
        var template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();

        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheet(1);

        var result = new BulkUploadResult();
        var mapping = BuildColumnMapping(worksheet, fields, result.UnmappedColumns);
        var parsed = ParseRowsFromWorksheet(worksheet, fields, mapping);
        result.Errors.AddRange(parsed.Errors);

        var record = BuildRecord(returnCode, template.CurrentVersion.Id, template.StructuralCategory, parsed.Rows);
        result.RowsImported = parsed.Rows.Count;

        var submission = Submission.Create(institutionId, returnPeriodId, returnCode, tenantId);
        submission.MarkSubmitted();
        submission.SubmittedByUserId = requestedByUserId;
        submission.SetTemplateVersion(template.CurrentVersion.Id);
        submission.StoreParsedDataJson(SubmissionPayloadSerializer.Serialize(record));
        await _submissionRepository.Add(submission, ct);

        var report = await _validationOrchestrator.Validate(record, submission, institutionId, returnPeriodId, ct);
        foreach (var parseError in parsed.Errors)
        {
            report.AddError(new ValidationError
            {
                RuleId = "BULK_PARSE",
                Field = parseError.FieldCode,
                Message = parseError.Message,
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.TypeRange
            });
        }

        result.Errors.AddRange(MapValidationErrors(report.Errors));
        result.SubmissionId = submission.Id;

        if (report.HasErrors || result.Errors.Count > 0)
        {
            report.FinalizeAt(DateTime.UtcNow);
            submission.AttachValidationReport(report);
            submission.MarkRejected();
            submission.ProcessingDurationMs = null;
            await _submissionRepository.Update(submission, ct);

            result.Success = false;
            result.Status = submission.Status.ToString();
            result.Message = "Upload validation failed.";
            result.ErrorFile = GenerateErrorExcel(workbook, result.Errors, mapping);
            result.ErrorFileName = $"{returnCode}_upload_errors.xlsx";
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
        submission.AttachValidationReport(report);
        await _submissionRepository.Update(submission, ct);
        await TryAnalyzeSubmissionAsync(submission, requestedByUserId, ct);

        result.Success = true;
        result.Status = submission.Status.ToString();
        result.Message = $"Imported {result.RowsImported} row(s).";
        return result;
    }

    public async Task<BulkUploadResult> ProcessCsvUpload(
        Stream fileStream,
        Guid tenantId,
        string returnCode,
        int institutionId,
        int returnPeriodId,
        int? requestedByUserId = null,
        CancellationToken ct = default)
    {
        var template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();
        var result = new BulkUploadResult();

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null
        });

        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            result.Success = false;
            result.Message = "CSV file is empty or missing headers.";
            result.Errors.Add(new BulkUploadError
            {
                RowNumber = 0,
                FieldCode = "CSV",
                Message = "CSV file is empty or missing headers.",
                Category = BulkUploadErrorCategories.Format,
                ExpectedValue = "CSV header row with template column names"
            });
            return result;
        }

        var headers = csv.HeaderRecord ?? Array.Empty<string>();
        var mapping = BuildHeaderMapping(headers, fields, result.UnmappedColumns);

        var parsedRows = new List<ParsedRow>();
        var rowNumber = 1;
        while (await csv.ReadAsync())
        {
            rowNumber++;
            var parsedRow = new ParsedRow { RowNumber = rowNumber };
            var hasData = false;

            foreach (var column in mapping)
            {
                var raw = csv.GetField(column.Key) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    hasData = true;
                }

                try
                {
                    var converted = ConvertRawValue(raw, column.Value.DataType);
                    parsedRow.Row.SetValue(column.Value.FieldName, converted);
                    if (column.Value.IsKeyField && converted is not null)
                    {
                        parsedRow.Row.RowKey = converted.ToString();
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new BulkUploadError
                    {
                        RowNumber = rowNumber,
                        FieldCode = column.Value.FieldName,
                        Message = ex.Message,
                        Category = ResolveConversionCategory(column.Value.DataType),
                        ExpectedValue = DescribeExpectedValue(column.Value.DataType)
                    });
                }
            }

            if (hasData)
            {
                ApplyRequiredChecks(parsedRow, fields, result.Errors);
                parsedRows.Add(parsedRow);
            }
        }

        result.RowsImported = parsedRows.Count;
        var record = BuildRecord(returnCode, template.CurrentVersion.Id, template.StructuralCategory, parsedRows);

        var submission = Submission.Create(institutionId, returnPeriodId, returnCode, tenantId);
        submission.MarkSubmitted();
        submission.SubmittedByUserId = requestedByUserId;
        submission.SetTemplateVersion(template.CurrentVersion.Id);
        submission.StoreParsedDataJson(SubmissionPayloadSerializer.Serialize(record));
        await _submissionRepository.Add(submission, ct);

        var report = await _validationOrchestrator.Validate(record, submission, institutionId, returnPeriodId, ct);
        foreach (var parseError in result.Errors)
        {
            report.AddError(new ValidationError
            {
                RuleId = "BULK_PARSE",
                Field = parseError.FieldCode,
                Message = parseError.Message,
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.TypeRange,
                ExpectedValue = parseError.ExpectedValue
            });
        }

        result.Errors.AddRange(MapValidationErrors(report.Errors));
        result.SubmissionId = submission.Id;

        if (report.HasErrors || result.Errors.Count > 0)
        {
            report.FinalizeAt(DateTime.UtcNow);
            submission.AttachValidationReport(report);
            submission.MarkRejected();
            await _submissionRepository.Update(submission, ct);

            result.Success = false;
            result.Status = submission.Status.ToString();
            result.Message = "Upload validation failed.";
            result.ErrorFile = GenerateErrorExcelFromCsv(headers, parsedRows, result.Errors, mapping);
            result.ErrorFileName = $"{returnCode}_upload_errors.xlsx";
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
        submission.AttachValidationReport(report);
        await _submissionRepository.Update(submission, ct);
        await TryAnalyzeSubmissionAsync(submission, requestedByUserId, ct);

        result.Success = true;
        result.Status = submission.Status.ToString();
        result.Message = $"Imported {result.RowsImported} row(s).";
        return result;
    }

    private async Task TryAnalyzeSubmissionAsync(Submission submission, int? requestedByUserId, CancellationToken ct)
    {
        if (_anomalyDetectionService is null
            || submission.TenantId == Guid.Empty
            || submission.Status is not (SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings))
        {
            return;
        }

        var performedBy = requestedByUserId.HasValue
            ? $"institution-user:{requestedByUserId.Value}"
            : "bulk-upload";

        try
        {
            await _anomalyDetectionService.AnalyzeSubmissionAsync(
                submission.Id,
                submission.TenantId,
                performedBy,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate anomaly report for bulk-upload submission {SubmissionId}", submission.Id);
        }
    }

    private static Dictionary<int, Domain.Metadata.TemplateField> BuildColumnMapping(
        IXLWorksheet worksheet,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        List<string> unmappedColumns)
    {
        var headerRow = worksheet.Row(1);
        var mapping = new Dictionary<int, Domain.Metadata.TemplateField>();
        var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var col = 1; col <= lastCol; col++)
        {
            var header = NormalizeHeader(headerRow.Cell(col).GetString());
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var field = fields.FirstOrDefault(f =>
                string.Equals(NormalizeHeader(f.DisplayName), header, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeHeader(f.FieldName), header, StringComparison.OrdinalIgnoreCase));

            if (field is null)
            {
                unmappedColumns.Add(headerRow.Cell(col).GetString().Trim());
                continue;
            }

            mapping[col] = field;
        }

        return mapping;
    }

    private static Dictionary<string, Domain.Metadata.TemplateField> BuildHeaderMapping(
        IReadOnlyList<string> headers,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        List<string> unmappedColumns)
    {
        var mapping = new Dictionary<string, Domain.Metadata.TemplateField>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            var normalized = NormalizeHeader(header);
            var field = fields.FirstOrDefault(f =>
                string.Equals(NormalizeHeader(f.DisplayName), normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(NormalizeHeader(f.FieldName), normalized, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                unmappedColumns.Add(header);
                continue;
            }

            mapping[header] = field;
        }

        return mapping;
    }

    private ParsedRows ParseRowsFromWorksheet(
        IXLWorksheet worksheet,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        IReadOnlyDictionary<int, Domain.Metadata.TemplateField> mapping)
    {
        var result = new ParsedRows();
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;

        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = new ParsedRow { RowNumber = rowNumber };
            var hasData = false;

            foreach (var column in mapping)
            {
                var cell = worksheet.Cell(rowNumber, column.Key);
                var rawValue = cell.Value.ToString();
                if (!string.IsNullOrWhiteSpace(rawValue))
                {
                    hasData = true;
                }

                try
                {
                    var converted = ConvertCellValue(cell, column.Value.DataType);
                    row.Row.SetValue(column.Value.FieldName, converted);
                    if (column.Value.IsKeyField && converted is not null)
                    {
                        row.Row.RowKey = converted.ToString();
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add(new BulkUploadError
                    {
                        RowNumber = rowNumber,
                        FieldCode = column.Value.FieldName,
                        Message = ex.Message,
                        Category = ResolveConversionCategory(column.Value.DataType),
                        ExpectedValue = DescribeExpectedValue(column.Value.DataType)
                    });
                }
            }

            if (hasData)
            {
                ApplyRequiredChecks(row, fields, result.Errors);
                result.Rows.Add(row);
            }
        }

        return result;
    }

    private static void ApplyRequiredChecks(
        ParsedRow row,
        IReadOnlyList<Domain.Metadata.TemplateField> fields,
        List<BulkUploadError> errors)
    {
        foreach (var field in fields.Where(x => x.IsRequired))
        {
            var value = row.Row.GetValue(field.FieldName);
            if (value is null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                errors.Add(new BulkUploadError
                {
                    RowNumber = row.RowNumber,
                    FieldCode = field.FieldName,
                    Message = $"Required field '{field.DisplayName}' is missing.",
                    Category = BulkUploadErrorCategories.Required,
                    ExpectedValue = "Non-empty value"
                });
            }
        }
    }

    private static ReturnDataRecord BuildRecord(
        string returnCode,
        int templateVersionId,
        string structuralCategory,
        IReadOnlyList<ParsedRow> rows)
    {
        var category = Enum.Parse<StructuralCategory>(structuralCategory);
        var record = new ReturnDataRecord(returnCode, templateVersionId, category);

        if (rows.Count == 0)
        {
            record.AddRow(new ReturnDataRow());
            return record;
        }

        foreach (var parsed in rows)
        {
            record.AddRow(parsed.Row);
        }

        return record;
    }

    private static object? ConvertCellValue(IXLCell cell, FieldDataType dataType)
    {
        if (dataType == FieldDataType.Date && cell.TryGetValue<DateTime>(out var dtValue))
        {
            return dtValue.Date;
        }

        if ((dataType == FieldDataType.Money || dataType == FieldDataType.Decimal || dataType == FieldDataType.Percentage)
            && cell.TryGetValue<decimal>(out var decValue))
        {
            return decValue;
        }

        if (dataType == FieldDataType.Integer && cell.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (dataType == FieldDataType.Boolean && cell.TryGetValue<bool>(out var boolValue))
        {
            return boolValue;
        }

        return dataType switch
        {
            FieldDataType.Integer => ConvertInteger(cell.GetString()),
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => ConvertDecimal(cell.GetString()),
            FieldDataType.Date => ConvertDate(cell.GetString()),
            FieldDataType.Boolean => ConvertBool(cell.GetString()),
            _ => string.IsNullOrWhiteSpace(cell.GetString()) ? null : cell.GetString().Trim()
        };
    }

    private static object? ConvertRawValue(string rawValue, FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Integer => ConvertInteger(rawValue),
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => ConvertDecimal(rawValue),
            FieldDataType.Date => ConvertDate(rawValue),
            FieldDataType.Boolean => ConvertBool(rawValue),
            _ => string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim()
        };
    }

    private static int? ConvertInteger(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            return intValue;
        }

        throw new InvalidOperationException($"Invalid integer value '{rawValue}'.");
    }

    private static decimal? ConvertDecimal(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (decimal.TryParse(rawValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
        {
            return dec;
        }

        throw new InvalidOperationException($"Invalid numeric value '{rawValue}'.");
    }

    private static DateTime? ConvertDate(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            return dt.Date;
        }

        throw new InvalidOperationException($"Invalid date value '{rawValue}'.");
    }

    private static bool? ConvertBool(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (bool.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Invalid boolean value '{rawValue}'.");
    }

    private static List<BulkUploadError> MapValidationErrors(IReadOnlyList<ValidationError> errors)
    {
        var mapped = new List<BulkUploadError>();
        foreach (var error in errors)
        {
            var (field, row) = TryExtractFieldAndRow(error.Field);
            mapped.Add(new BulkUploadError
            {
                RowNumber = row,
                FieldCode = string.IsNullOrWhiteSpace(field) ? error.Field : field,
                Message = error.Message,
                Severity = error.Severity.ToString(),
                Category = error.Category.ToString(),
                ExpectedValue = error.ExpectedValue
            });
        }

        return mapped;
    }

    private static (string FieldCode, int RowNumber) TryExtractFieldAndRow(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return (string.Empty, 0);
        }

        var marker = "(row:";
        var markerIndex = field.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return (field, 0);
        }

        var fieldCode = field[..markerIndex].Trim();
        var end = field.IndexOf(')', markerIndex);
        if (end <= markerIndex)
        {
            return (fieldCode, 0);
        }

        var rawRow = field.Substring(markerIndex + marker.Length, end - markerIndex - marker.Length).Trim();
        return int.TryParse(rawRow, out var parsedRow) ? (fieldCode, parsedRow + 1) : (fieldCode, 0);
    }

    private byte[] GenerateErrorExcel(
        XLWorkbook originalWorkbook,
        IReadOnlyList<BulkUploadError> errors,
        IReadOnlyDictionary<int, Domain.Metadata.TemplateField> mapping)
    {
        var workbook = new XLWorkbook();
        originalWorkbook.Worksheets.First().CopyTo(workbook, "Upload");
        var worksheet = workbook.Worksheet("Upload");
        var errorColumn = (worksheet.LastColumnUsed()?.ColumnNumber() ?? 1) + 2;
        worksheet.Cell(1, errorColumn).Value = "VALIDATION ERRORS";
        worksheet.Cell(1, errorColumn).Style.Font.Bold = true;
        worksheet.Cell(1, errorColumn).Style.Font.FontColor = XLColor.Red;

        foreach (var error in errors)
        {
            var row = error.RowNumber > 0 ? error.RowNumber : 2;
            var col = mapping.FirstOrDefault(x => string.Equals(x.Value.FieldName, error.FieldCode, StringComparison.OrdinalIgnoreCase)).Key;
            if (col > 0 && row > 1)
            {
                worksheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFE0E0");
                worksheet.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thick;
                worksheet.Cell(row, col).Style.Border.OutsideBorderColor = XLColor.Red;
            }

            var existing = worksheet.Cell(row, errorColumn).GetString();
            worksheet.Cell(row, errorColumn).Value = string.IsNullOrWhiteSpace(existing)
                ? error.Message
                : $"{existing}; {error.Message}";
            worksheet.Cell(row, errorColumn).Style.Font.FontColor = XLColor.Red;
        }

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private byte[] GenerateErrorExcelFromCsv(
        IReadOnlyList<string> headers,
        IReadOnlyList<ParsedRow> rows,
        IReadOnlyList<BulkUploadError> errors,
        IReadOnlyDictionary<string, Domain.Metadata.TemplateField> mapping)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Upload");

        var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 0; col < headers.Count; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
            headerIndex[headers[col]] = col;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var header in headers)
            {
                if (!mapping.TryGetValue(header, out var field))
                {
                    continue;
                }

                var value = rows[i].Row.GetValue(field.FieldName);
                worksheet.Cell(i + 2, headerIndex[header] + 1).Value = value?.ToString() ?? string.Empty;
            }
        }

        var byColumn = mapping
            .Where(kv => headerIndex.ContainsKey(kv.Key))
            .ToDictionary(
                kv => headerIndex[kv.Key] + 1,
                kv => kv.Value);

        return GenerateErrorExcel(workbook, errors, byColumn);
    }

    private static string NormalizeHeader(string value)
    {
        return value
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
    }

    private static string ResolveConversionCategory(FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Date or FieldDataType.Boolean => BulkUploadErrorCategories.Format,
            FieldDataType.Integer or FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => BulkUploadErrorCategories.TypeRange,
            _ => BulkUploadErrorCategories.Format
        };
    }

    private static string DescribeExpectedValue(FieldDataType dataType)
    {
        return dataType switch
        {
            FieldDataType.Integer => "Whole number",
            FieldDataType.Money or FieldDataType.Decimal => "Numeric value",
            FieldDataType.Percentage => "Percentage value",
            FieldDataType.Date => "Valid date",
            FieldDataType.Boolean => "true or false",
            _ => "Valid value"
        };
    }

    private sealed class ParsedRows
    {
        public List<ParsedRow> Rows { get; } = [];
        public List<BulkUploadError> Errors { get; } = [];
    }

    private sealed class ParsedRow
    {
        public int RowNumber { get; set; }
        public ReturnDataRow Row { get; } = new();
    }
}
