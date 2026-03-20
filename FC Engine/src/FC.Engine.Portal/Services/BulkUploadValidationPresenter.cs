using System.Text;
using FC.Engine.Application.DTOs;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Portal.Services;

public static class BulkUploadValidationPresenter
{
    public static SubmissionResultDto BuildSubmissionResult(string returnCode, BulkUploadResult bulkResult)
    {
        return new SubmissionResultDto
        {
            SubmissionId = bulkResult.SubmissionId,
            ReturnCode = returnCode,
            Status = bulkResult.Status,
            ValidationReport = new ValidationReportDto
            {
                IsValid = bulkResult.Success,
                ErrorCount = bulkResult.Errors.Count(x => string.Equals(x.Severity, "Error", StringComparison.OrdinalIgnoreCase)),
                WarningCount = bulkResult.Errors.Count(x => string.Equals(x.Severity, "Warning", StringComparison.OrdinalIgnoreCase)),
                Errors = bulkResult.Errors.Select(x => new ValidationErrorDto
                {
                    RuleId = "BULK",
                    Field = x.FieldCode,
                    Message = x.Message,
                    Severity = x.Severity,
                    Category = x.Category,
                    ExpectedValue = x.ExpectedValue
                }).ToList()
            }
        };
    }

    public static string BuildErrorReportCsv(IEnumerable<BulkUploadValidationExportFile> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("File,ReturnCode,Row,Severity,Category,Rule,Field,Message,ExpectedFormat");

        foreach (var file in files.Where(x => x.Errors.Count > 0))
        {
            var rowNum = 1;
            foreach (var err in file.Errors)
            {
                var message = EscapeCsv(err.Message);
                var field = EscapeCsv(err.Field);
                var category = EscapeCsv(err.Category);
                var expected = EscapeCsv(ResolveExpectedFormat(err));
                var severity = EscapeCsv(err.Severity);
                var ruleId = EscapeCsv(err.RuleId);

                sb.AppendLine($"\"{EscapeCsv(file.FileName)}\",\"{EscapeCsv(file.ReturnCode)}\",{rowNum},\"{severity}\",\"{category}\",\"{ruleId}\",\"{field}\",\"{message}\",\"{expected}\"");
                rowNum++;
            }
        }

        return sb.ToString();
    }

    public static string ResolveExpectedFormat(ValidationErrorDto error)
    {
        if (!string.IsNullOrWhiteSpace(error.ExpectedValue))
        {
            return error.ExpectedValue;
        }

        return error.Category switch
        {
            BulkUploadErrorCategories.TypeRange => "Numeric value within allowed range",
            BulkUploadErrorCategories.Required => "Non-empty value required",
            BulkUploadErrorCategories.Format => "Expected date/code format per schema",
            _ => error.Category ?? string.Empty
        };
    }

    private static string EscapeCsv(string? value)
    {
        return (value ?? string.Empty).Replace("\"", "\"\"");
    }
}

public sealed class BulkUploadValidationExportFile
{
    public string FileName { get; init; } = string.Empty;
    public string ReturnCode { get; init; } = string.Empty;
    public IReadOnlyList<ValidationErrorDto> Errors { get; init; } = Array.Empty<ValidationErrorDto>();
}
