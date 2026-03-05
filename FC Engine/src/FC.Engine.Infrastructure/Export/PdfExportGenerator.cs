using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.ValueObjects;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Export;

public class PdfExportGenerator : IExportGenerator
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IGenericDataRepository _dataRepository;
    private readonly ISubmissionApprovalRepository _approvalRepository;
    private readonly IFileStorageService _fileStorage;

    public PdfExportGenerator(
        ITemplateMetadataCache templateCache,
        IGenericDataRepository dataRepository,
        ISubmissionApprovalRepository approvalRepository,
        IFileStorageService fileStorage)
    {
        _templateCache = templateCache;
        _dataRepository = dataRepository;
        _approvalRepository = approvalRepository;
        _fileStorage = fileStorage;
    }

    public ExportFormat Format => ExportFormat.PDF;
    public string ContentType => "application/pdf";
    public string FileExtension => "pdf";

    public async Task<byte[]> Generate(ExportGenerationContext context, CancellationToken ct = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var submission = context.Submission;
        var branding = BrandingConfig.WithDefaults(context.Branding);
        var primaryColor = string.IsNullOrWhiteSpace(branding.PrimaryColor) ? "#006B3F" : branding.PrimaryColor!;
        var logoBytes = await TryLoadLogo(branding, ct);

        var baseTemplate = await _templateCache.GetPublishedTemplate(context.TenantId, submission.ReturnCode, ct);
        var templates = await ResolveTemplates(context.TenantId, baseTemplate.ModuleId, submission.ReturnCode, ct);
        var templateData = await BuildTemplateData(submission.Id, templates, ct);
        var approval = await _approvalRepository.GetBySubmission(submission.Id, ct);
        var validationErrors = submission.ValidationReport?.Errors
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Category.ToString())
            .ToList() ?? new List<ValidationError>();

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9));

                if (!string.IsNullOrWhiteSpace(branding.WatermarkText))
                {
                    page.Background()
                        .AlignCenter()
                        .Rotate(-35)
                        .Text(branding.WatermarkText!)
                        .FontSize(42)
                        .Bold()
                        .FontColor(Colors.Grey.Lighten2);
                }

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(branding.CompanyName ?? "RegOS").Bold().FontSize(14).FontColor(primaryColor);
                        col.Item().Text($"{submission.ReturnCode} - {ExportUtility.FormatPeriod(submission.ReturnPeriod)}")
                            .FontSize(10)
                            .FontColor(Colors.Grey.Medium);
                    });

                    if (logoBytes is { Length: > 0 })
                    {
                        row.ConstantItem(90)
                            .AlignRight()
                            .Height(50)
                            .Image(logoBytes)
                            .FitArea();
                    }
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Column(cover =>
                    {
                        cover.Spacing(4);
                        cover.Item().Text("Regulatory Return Report").FontSize(22).Bold().FontColor(primaryColor);
                        cover.Item().Text($"Institution: {submission.Institution?.InstitutionName ?? "N/A"}");
                        cover.Item().Text($"Period: {ExportUtility.FormatPeriod(submission.ReturnPeriod)}");
                        cover.Item().Text($"Status: {submission.Status}");
                        cover.Item().Text($"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC").FontColor(Colors.Grey.Medium);
                    });

                    col.Item().PaddingTop(10).Text("Approval Chain").FontSize(14).Bold();
                    col.Item().Column(chain =>
                    {
                        chain.Spacing(2);
                        if (approval is null)
                        {
                            chain.Item().Text("No maker-checker attestation record found.");
                        }
                        else
                        {
                            chain.Item().Text($"Maker: {approval.RequestedBy?.DisplayName ?? approval.RequestedBy?.Username ?? "N/A"} - Submitted at {approval.RequestedAt:dd MMM yyyy HH:mm} UTC");
                            if (approval.ReviewedAt.HasValue)
                            {
                                chain.Item().Text($"Checker: {approval.ReviewedBy?.DisplayName ?? approval.ReviewedBy?.Username ?? "N/A"} - {approval.Status} at {approval.ReviewedAt:dd MMM yyyy HH:mm} UTC");
                            }
                            else
                            {
                                chain.Item().Text("Checker: Pending review");
                            }
                        }
                    });

                    col.Item().PageBreak();
                    col.Item().Text("Table of Contents").FontSize(16).Bold().FontColor(primaryColor);
                    col.Item().Column(toc =>
                    {
                        toc.Spacing(2);
                        toc.Item().Text("1. Cover");
                        toc.Item().Text("2. Approval Chain");

                        var index = 3;
                        foreach (var dataset in templateData)
                        {
                            toc.Item().Text($"{index}. Data Sheet - {dataset.ReturnCode}");
                            index++;
                        }

                        toc.Item().Text($"{index}. Validation Summary");
                        toc.Item().Text($"{index + 1}. Digital Attestation");
                    });

                    foreach (var dataset in templateData)
                    {
                        col.Item().PageBreak();
                        col.Item().Text(dataset.ReturnCode).FontSize(16).Bold().FontColor(primaryColor);
                        col.Item().Element(container => ComposeDataTable(container, dataset, primaryColor));
                    }

                    col.Item().PageBreak();
                    col.Item().Text("Validation Summary").FontSize(16).Bold();
                    col.Item().Text($"Errors: {submission.ValidationReport?.ErrorCount ?? 0} | Warnings: {submission.ValidationReport?.WarningCount ?? 0}");
                    col.Item().Element(container => ComposeValidationTable(container, validationErrors, primaryColor));

                    col.Item().PageBreak();
                    col.Item().Text("Digital Attestation").FontSize(16).Bold();
                    col.Item().Text("This return was digitally attested by authorized signatories within the maker-checker workflow.");
                    if (approval is not null)
                    {
                        col.Item().Text($"Requested by: {approval.RequestedBy?.DisplayName ?? approval.RequestedBy?.Username ?? "N/A"}");
                        col.Item().Text($"Review status: {approval.Status}");
                        if (!string.IsNullOrWhiteSpace(approval.ReviewerComments))
                        {
                            col.Item().Text($"Reviewer comments: {approval.ReviewerComments}");
                        }
                    }
                });

                page.Footer().Row(row =>
                {
                    var footerText = branding.CopyrightText ?? "RegOS";
                    if (!string.IsNullOrWhiteSpace(branding.WatermarkText))
                    {
                        footerText = $"{footerText} | Watermark: {branding.WatermarkText}";
                    }

                    row.RelativeItem().Text(footerText)
                        .FontSize(7)
                        .FontColor(Colors.Grey.Medium);

                    row.ConstantItem(100).AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7);
                        txt.CurrentPageNumber().FontSize(7);
                        txt.Span(" of ").FontSize(7);
                        txt.TotalPages().FontSize(7);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeDataTable(IContainer container, PdfTemplateData dataset, string primaryColor)
    {
        var fields = dataset.Fields;
        var rows = dataset.Rows;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                if (fields.Count == 0)
                {
                    columns.RelativeColumn();
                    return;
                }

                for (var i = 0; i < fields.Count; i++)
                {
                    columns.RelativeColumn();
                }
            });

            table.Header(header =>
            {
                if (fields.Count == 0)
                {
                    header.Cell().Background(primaryColor).Padding(4).Text("No fields").FontColor(Colors.White).Bold();
                    return;
                }

                foreach (var field in fields)
                {
                    header.Cell()
                        .Background(primaryColor)
                        .Padding(3)
                        .Text(field.DisplayName)
                        .FontSize(7)
                        .FontColor(Colors.White)
                        .Bold();
                }
            });

            if (rows.Count == 0)
            {
                if (fields.Count == 0)
                {
                    table.Cell().Padding(3).Text("No data available.");
                }
                else
                {
                    table.Cell().ColumnSpan((uint)fields.Count).Padding(3).Text("No data available.");
                }

                return;
            }

            foreach (var row in rows)
            {
                foreach (var value in row)
                {
                    table.Cell()
                        .BorderBottom(1)
                        .BorderColor(Colors.Grey.Lighten3)
                        .Padding(3)
                        .Text(value)
                        .FontSize(7);
                }
            }
        });
    }

    private static void ComposeValidationTable(
        IContainer container,
        IReadOnlyList<ValidationError> validationErrors,
        string primaryColor)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(70);
                columns.ConstantColumn(70);
                columns.ConstantColumn(80);
                columns.RelativeColumn();
            });

            table.Header(header =>
            {
                header.Cell().Background(primaryColor).Padding(3).Text("Severity").FontColor(Colors.White).Bold().FontSize(7);
                header.Cell().Background(primaryColor).Padding(3).Text("Category").FontColor(Colors.White).Bold().FontSize(7);
                header.Cell().Background(primaryColor).Padding(3).Text("Field").FontColor(Colors.White).Bold().FontSize(7);
                header.Cell().Background(primaryColor).Padding(3).Text("Message").FontColor(Colors.White).Bold().FontSize(7);
            });

            if (validationErrors.Count == 0)
            {
                table.Cell().ColumnSpan(4).Padding(3).Text("No validation issues recorded.");
                return;
            }

            foreach (var error in validationErrors)
            {
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(error.Severity.ToString()).FontSize(7);
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(error.Category.ToString()).FontSize(7);
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(error.Field).FontSize(7);
                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(3).Text(error.Message).FontSize(7);
            }
        });
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
        catch
        {
            return null;
        }
    }

    private async Task<List<PdfTemplateData>> BuildTemplateData(
        int submissionId,
        IReadOnlyList<CachedTemplate> templates,
        CancellationToken ct)
    {
        var result = new List<PdfTemplateData>(templates.Count);
        foreach (var template in templates)
        {
            var fields = template.CurrentVersion.Fields
                .OrderBy(f => f.FieldOrder)
                .ToList();

            var rows = new List<IReadOnlyList<string>>();
            var record = await _dataRepository.GetBySubmission(template.ReturnCode, submissionId, ct);
            if (record is not null)
            {
                foreach (var row in record.Rows)
                {
                    rows.Add(fields
                        .Select(field => ExportUtility.FormatPlainValue(row.GetValue(field.FieldName), field.DataType))
                        .ToList()
                        .AsReadOnly());
                }
            }

            result.Add(new PdfTemplateData(template.ReturnCode, fields, rows));
        }

        return result;
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

    private sealed record PdfTemplateData(
        string ReturnCode,
        IReadOnlyList<TemplateField> Fields,
        IReadOnlyList<IReadOnlyList<string>> Rows);
}
