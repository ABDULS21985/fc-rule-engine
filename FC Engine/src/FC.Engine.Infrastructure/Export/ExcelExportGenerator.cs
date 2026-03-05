using ClosedXML.Excel;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Export;

public class ExcelExportGenerator : IExportGenerator
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IGenericDataRepository _dataRepository;
    private readonly IFileStorageService _fileStorage;
    private readonly ISubmissionApprovalRepository _approvalRepository;
    private readonly ILogger<ExcelExportGenerator> _logger;

    public ExcelExportGenerator(
        ITemplateMetadataCache templateCache,
        IGenericDataRepository dataRepository,
        IFileStorageService fileStorage,
        ISubmissionApprovalRepository approvalRepository,
        ILogger<ExcelExportGenerator> logger)
    {
        _templateCache = templateCache;
        _dataRepository = dataRepository;
        _fileStorage = fileStorage;
        _approvalRepository = approvalRepository;
        _logger = logger;
    }

    public ExportFormat Format => ExportFormat.Excel;
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public string FileExtension => "xlsx";

    public async Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default)
    {
        var submission = context.Submission;
        var branding = BrandingConfig.WithDefaults(context.Branding);
        var primaryColorHex = string.IsNullOrWhiteSpace(branding.PrimaryColor) ? "#006B3F" : branding.PrimaryColor!;
        var headerColor = XLColor.FromHtml(primaryColorHex);
        var logoBytes = await TryLoadLogo(branding, ct);

        var baseTemplate = await _templateCache.GetPublishedTemplate(context.TenantId, submission.ReturnCode, ct);
        var templates = await ResolveTemplates(context.TenantId, baseTemplate.ModuleId, submission.ReturnCode, ct);

        using var workbook = new XLWorkbook();
        var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var cover = workbook.Worksheets.Add(ExportUtility.SanitizeWorksheetName("Cover", sheetNames));
        PopulateCoverSheet(cover, submission, branding, primaryColorHex, logoBytes);

        foreach (var template in templates)
        {
            var sheetName = ExportUtility.SanitizeWorksheetName(template.ReturnCode, sheetNames);
            var worksheet = workbook.Worksheets.Add(sheetName);
            var fields = template.CurrentVersion.Fields.OrderBy(f => f.FieldOrder).ToList();

            for (var i = 0; i < fields.Count; i++)
            {
                var cell = worksheet.Cell(1, i + 1);
                cell.Value = fields[i].DisplayName;
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = headerColor;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            worksheet.Row(1).Height = 28;
            worksheet.SheetView.FreezeRows(1);

            var record = await _dataRepository.GetBySubmission(template.ReturnCode, submission.Id, ct);
            if (record is not null)
            {
                var rowIndex = 2;
                foreach (var row in record.Rows)
                {
                    for (var col = 0; col < fields.Count; col++)
                    {
                        var field = fields[col];
                        var rawValue = row.GetValue(field.FieldName);
                        var cell = worksheet.Cell(rowIndex, col + 1);
                        var value = ExportUtility.FormatExcelValue(rawValue, field.DataType);
                        cell.Value = value switch
                        {
                            null => string.Empty,
                            int i => i,
                            decimal d => d,
                            DateTime dt => dt,
                            bool b => b,
                            _ => value.ToString()
                        };

                        ApplyNumberFormat(cell, field.DataType);
                    }

                    rowIndex++;
                }
            }

            worksheet.Columns().AdjustToContents(1, 70);
        }

        var validationSheet = workbook.Worksheets.Add(ExportUtility.SanitizeWorksheetName("Validation Summary", sheetNames));
        PopulateValidationSummary(validationSheet, submission, headerColor);

        var approvalSheet = workbook.Worksheets.Add(ExportUtility.SanitizeWorksheetName("Attestation", sheetNames));
        await PopulateApprovalSummary(approvalSheet, submission.Id, headerColor, ct);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static void PopulateCoverSheet(
        IXLWorksheet cover,
        Domain.Entities.Submission submission,
        BrandingConfig branding,
        string primaryColorHex,
        byte[]? logoBytes)
    {
        cover.Cell("A1").Value = branding.CompanyName ?? "RegOS";
        cover.Cell("A1").Style.Font.Bold = true;
        cover.Cell("A1").Style.Font.FontSize = 18;
        cover.Cell("A1").Style.Font.FontColor = XLColor.FromHtml(primaryColorHex);

        cover.Cell("A3").Value = $"Return: {submission.ReturnCode}";
        cover.Cell("A4").Value = $"Period: {ExportUtility.FormatPeriod(submission.ReturnPeriod)}";
        cover.Cell("A5").Value = $"Institution: {submission.Institution?.InstitutionName ?? "N/A"}";
        cover.Cell("A6").Value = $"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC";
        cover.Cell("A7").Value = $"Status: {submission.Status}";
        cover.Cell("A8").Value = $"Submission ID: {submission.Id}";

        if (logoBytes is { Length: > 0 })
        {
            cover.AddPicture(new MemoryStream(logoBytes))
                .MoveTo(cover.Cell("E1"))
                .Scale(0.5);
        }

        cover.Columns("A:F").AdjustToContents(1, 40);
    }

    private static void PopulateValidationSummary(IXLWorksheet sheet, Domain.Entities.Submission submission, XLColor headerColor)
    {
        sheet.Cell("A1").Value = "Validation Summary";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 16;

        var report = submission.ValidationReport;
        sheet.Cell("A3").Value = "Total Errors";
        sheet.Cell("B3").Value = report?.ErrorCount ?? 0;
        sheet.Cell("A4").Value = "Total Warnings";
        sheet.Cell("B4").Value = report?.WarningCount ?? 0;

        sheet.Cell("A6").Value = "Severity";
        sheet.Cell("B6").Value = "Category";
        sheet.Cell("C6").Value = "Rule";
        sheet.Cell("D6").Value = "Field";
        sheet.Cell("E6").Value = "Message";

        var headerRange = sheet.Range("A6:E6");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = headerColor;
        headerRange.Style.Font.FontColor = XLColor.White;

        var row = 7;
        if (report is not null)
        {
            foreach (var error in report.Errors.OrderByDescending(x => x.Severity).ThenBy(x => x.Category.ToString()))
            {
                sheet.Cell(row, 1).Value = error.Severity.ToString();
                sheet.Cell(row, 2).Value = error.Category.ToString();
                sheet.Cell(row, 3).Value = error.RuleId;
                sheet.Cell(row, 4).Value = error.Field;
                sheet.Cell(row, 5).Value = error.Message;
                row++;
            }
        }

        sheet.Columns("A:E").AdjustToContents(1, 70);
    }

    private async Task PopulateApprovalSummary(IXLWorksheet sheet, int submissionId, XLColor headerColor, CancellationToken ct)
    {
        sheet.Cell("A1").Value = "Digital Attestation";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("A1").Style.Font.FontSize = 16;

        sheet.Cell("A3").Value = "Role";
        sheet.Cell("B3").Value = "User";
        sheet.Cell("C3").Value = "Action";
        sheet.Cell("D3").Value = "Timestamp (UTC)";
        var headerRange = sheet.Range("A3:D3");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = headerColor;
        headerRange.Style.Font.FontColor = XLColor.White;

        var approval = await _approvalRepository.GetBySubmission(submissionId, ct);
        if (approval is null)
        {
            sheet.Cell("A4").Value = "No approval workflow recorded.";
            sheet.Range("A4:D4").Merge();
            sheet.Columns("A:D").AdjustToContents();
            return;
        }

        var row = 4;
        sheet.Cell(row, 1).Value = "Maker";
        sheet.Cell(row, 2).Value = approval.RequestedBy?.DisplayName ?? approval.RequestedBy?.Username ?? "N/A";
        sheet.Cell(row, 3).Value = "Submitted";
        sheet.Cell(row, 4).Value = approval.RequestedAt.ToString("yyyy-MM-dd HH:mm");
        row++;

        if (approval.ReviewedAt.HasValue)
        {
            sheet.Cell(row, 1).Value = "Checker";
            sheet.Cell(row, 2).Value = approval.ReviewedBy?.DisplayName ?? approval.ReviewedBy?.Username ?? "N/A";
            sheet.Cell(row, 3).Value = approval.Status.ToString();
            sheet.Cell(row, 4).Value = approval.ReviewedAt.Value.ToString("yyyy-MM-dd HH:mm");
        }
        else
        {
            sheet.Cell(row, 1).Value = "Checker";
            sheet.Cell(row, 2).Value = "Pending";
            sheet.Cell(row, 3).Value = "Pending";
            sheet.Cell(row, 4).Value = "Pending";
        }

        sheet.Columns("A:D").AdjustToContents(1, 60);
    }

    private async Task<byte[]?> TryLoadLogo(BrandingConfig branding, CancellationToken ct)
    {
        var storagePath = ExportUtility.ResolveStoragePath(branding.LogoUrl);
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return null;
        }

        try
        {
            await using var stream = await _fileStorage.DownloadAsync(storagePath, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to load branding logo from {Path}", storagePath);
            return null;
        }
    }

    private async Task<List<CachedTemplate>> ResolveTemplates(
        Guid tenantId,
        int? moduleId,
        string returnCode,
        CancellationToken ct)
    {
        var templates = await _templateCache.GetAllPublishedTemplates(tenantId, ct);
        if (!moduleId.HasValue)
        {
            return templates
                .Where(t => string.Equals(t.ReturnCode, returnCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.ReturnCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var inModule = templates
            .Where(t => t.ModuleId == moduleId)
            .OrderBy(t => t.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (inModule.Count > 0)
        {
            return inModule;
        }

        return templates
            .Where(t => string.Equals(t.ReturnCode, returnCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ApplyNumberFormat(IXLCell cell, FieldDataType dataType)
    {
        switch (dataType)
        {
            case FieldDataType.Money:
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;
            case FieldDataType.Percentage:
                cell.Style.NumberFormat.Format = "0.00%";
                break;
            case FieldDataType.Decimal:
                cell.Style.NumberFormat.Format = "#,##0.0000";
                break;
            case FieldDataType.Integer:
                cell.Style.NumberFormat.Format = "#,##0";
                break;
            case FieldDataType.Date:
                cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                break;
        }
    }
}
