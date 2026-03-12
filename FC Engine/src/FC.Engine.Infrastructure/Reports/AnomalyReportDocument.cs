using FC.Engine.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Reports;

public sealed class AnomalyReportDocument
{
    private readonly AnomalyReport _report;

    public AnomalyReportDocument(AnomalyReport report)
    {
        _report = report;
    }

    public byte[] GeneratePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("RegOS anomaly report · Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    text.Span(" / ").FontSize(8).FontColor(Colors.Grey.Medium);
                    text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                });
            });
        }).GeneratePdf();
    }

    private void ComposeHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("AI Anomaly Detection Report").Bold().FontSize(16).FontColor("#0A2F5C");
                    left.Item().Text($"{_report.InstitutionName} · {_report.ModuleCode} · {_report.PeriodCode}")
                        .FontSize(10)
                        .FontColor(Colors.Grey.Darken2);
                });

                row.ConstantItem(120).AlignRight().Column(right =>
                {
                    right.Item().Text(_report.TrafficLight).Bold().FontSize(18).FontColor(TrafficColor(_report.TrafficLight));
                    right.Item().Text($"{_report.OverallQualityScore:F1}/100").FontSize(10).FontColor("#0A2F5C");
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor("#C8A951");
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().PaddingTop(8).Text("Executive Summary").Bold().FontSize(12).FontColor("#0A2F5C");
            column.Item().PaddingTop(4).Text(_report.NarrativeSummary).FontSize(9).LineHeight(1.4f);

            column.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                SummaryCell(table.Cell(), "Fields Analysed", _report.TotalFieldsAnalysed.ToString("N0"));
                SummaryCell(table.Cell(), "Total Findings", _report.TotalFindings.ToString("N0"));
                SummaryCell(table.Cell(), "Alerts", _report.AlertCount.ToString("N0"));
                SummaryCell(table.Cell(), "Warnings", _report.WarningCount.ToString("N0"));
            });

            if (_report.Findings.Count == 0)
            {
                column.Item().PaddingTop(24).AlignCenter().Text("No anomaly findings were raised for this submission.")
                    .FontSize(13)
                    .Bold()
                    .FontColor("#2E7D32");
                return;
            }

            var grouped = _report.Findings
                .GroupBy(x => x.FindingType)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in grouped)
            {
                column.Item().PaddingTop(16).Text($"{group.Key} Findings").Bold().FontSize(11).FontColor("#0A2F5C");
                column.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2.4f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1.4f);
                        columns.RelativeColumn(1f);
                        columns.RelativeColumn(3f);
                    });

                    HeaderCell(table.Cell(), "Field");
                    HeaderCell(table.Cell(), "Reported");
                    HeaderCell(table.Cell(), "Expected");
                    HeaderCell(table.Cell(), "Severity");
                    HeaderCell(table.Cell(), "Explanation");

                    foreach (var finding in group.OrderByDescending(x => SeverityRank(x.Severity)).ThenBy(x => x.FieldLabel))
                    {
                        DataCell(table.Cell(), finding.FieldLabel);
                        DataCell(table.Cell(), finding.ReportedValue?.ToString("N2") ?? "N/A");
                        DataCell(table.Cell(), ResolveExpectedValue(finding));
                        DataCell(table.Cell(), finding.Severity, TrafficColor(finding.Severity));
                        DataCell(table.Cell(), finding.Explanation);
                    }
                });
            }

            column.Item().PaddingTop(14).Text($"Analyzed at {_report.AnalysedAt:dd MMM yyyy HH:mm} UTC using model version #{_report.ModelVersionId}.")
                .FontSize(8)
                .FontColor(Colors.Grey.Darken1);
        });
    }

    private static void SummaryCell(IContainer container, string label, string value)
    {
        container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Column(column =>
            {
                column.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                column.Item().PaddingTop(2).Text(value).Bold().FontSize(15).FontColor("#0A2F5C");
            });
    }

    private static void HeaderCell(IContainer container, string text)
    {
        container
            .Background("#0A2F5C")
            .Padding(5)
            .Text(text)
            .FontSize(8)
            .Bold()
            .FontColor(Colors.White);
    }

    private static void DataCell(IContainer container, string text, string? color = null)
    {
        var cell = container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(5);

        var textDescriptor = cell.Text(text).FontSize(7.5f);
        if (!string.IsNullOrWhiteSpace(color))
        {
            textDescriptor.FontColor(color);
        }
    }

    private static string ResolveExpectedValue(AnomalyFinding finding)
    {
        if (finding.ExpectedRangeLow.HasValue || finding.ExpectedRangeHigh.HasValue)
        {
            return $"{finding.ExpectedRangeLow?.ToString("N2") ?? "N/A"} - {finding.ExpectedRangeHigh?.ToString("N2") ?? "N/A"}";
        }

        if (finding.ExpectedValue.HasValue)
        {
            return finding.ExpectedValue.Value.ToString("N2");
        }

        if (finding.BaselineValue.HasValue)
        {
            return finding.BaselineValue.Value.ToString("N2");
        }

        return "N/A";
    }

    private static string TrafficColor(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "GREEN" => "#2E7D32",
            "AMBER" => "#F57C00",
            "RED" => "#C62828",
            "ALERT" => "#C62828",
            "WARNING" => "#F57C00",
            "INFO" => "#1565C0",
            _ => Colors.Grey.Darken1
        };
    }

    private static int SeverityRank(string severity)
    {
        return severity.ToUpperInvariant() switch
        {
            "ALERT" => 3,
            "WARNING" => 2,
            "INFO" => 1,
            _ => 0
        };
    }
}
