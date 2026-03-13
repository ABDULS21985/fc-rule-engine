using FC.Engine.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Reports;

public sealed class ForeSightFilingRiskReportDocument
{
    private readonly string _institutionName;
    private readonly IReadOnlyList<FilingRiskForecast> _forecasts;

    public ForeSightFilingRiskReportDocument(string institutionName, IReadOnlyList<FilingRiskForecast> forecasts)
    {
        _institutionName = institutionName;
        _forecasts = forecasts;
    }

    public byte[] GeneratePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(text => text.FontFamily("Arial").FontSize(9));
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("ForeSight advisory report · Page ").FontSize(8).FontColor(Colors.Grey.Medium);
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
            column.Item().Text("ForeSight Predictive Filing Risk Advisory")
                .Bold()
                .FontSize(16)
                .FontColor("#0A2F5C");
            column.Item().Text($"{_institutionName} · Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                .FontColor(Colors.Grey.Darken2);
            column.Item().PaddingTop(8).LineHorizontal(1).LineColor("#C8A951");
        });
    }

    private void ComposeContent(IContainer container)
    {
        var highRisk = _forecasts.Count(x => x.RiskBand is "HIGH" or "CRITICAL");
        var mediumRisk = _forecasts.Count(x => x.RiskBand == "MEDIUM");
        var lowRisk = _forecasts.Count(x => x.RiskBand == "LOW");

        container.Column(column =>
        {
            column.Item().PaddingTop(12).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                SummaryCell(table.Cell(), "High Risk", highRisk.ToString(), "#C62828");
                SummaryCell(table.Cell(), "Medium Risk", mediumRisk.ToString(), "#F9A825");
                SummaryCell(table.Cell(), "Low Risk", lowRisk.ToString(), "#2E7D32");
            });

            column.Item().PaddingTop(16).Text("Advisory note: ForeSight predictions are advisory-only. They must not be used to automatically reject, enforce, or escalate filings.")
                .FontColor("#8A6D3B");
            column.Item().PaddingTop(16).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2f);
                    columns.RelativeColumn(1.5f);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(1f);
                    columns.RelativeColumn(3f);
                });

                HeaderCell(table.Cell(), "Module");
                HeaderCell(table.Cell(), "Period");
                HeaderCell(table.Cell(), "P(Late)");
                HeaderCell(table.Cell(), "Risk");
                HeaderCell(table.Cell(), "Recommendation");

                foreach (var forecast in _forecasts.OrderByDescending(x => x.ProbabilityLate))
                {
                    DataCell(table.Cell(), forecast.ModuleName);
                    DataCell(table.Cell(), forecast.PeriodCode);
                    DataCell(table.Cell(), $"{forecast.ProbabilityLate:P0}");
                    DataCell(table.Cell(), forecast.RiskBand, RiskColor(forecast.RiskBand));
                    DataCell(table.Cell(), string.IsNullOrWhiteSpace(forecast.Recommendation) ? "Continue monitoring." : forecast.Recommendation);
                }
            });

            if (_forecasts.Count > 0)
            {
                column.Item().PaddingTop(20).Text("Key Drivers").Bold().FontSize(12).FontColor("#0A2F5C");
                foreach (var forecast in _forecasts.Where(x => x.TopFactors.Count > 0).Take(5))
                {
                    column.Item().PaddingTop(6).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(8).Column(inner =>
                    {
                        inner.Item().Text($"{forecast.ModuleCode} · {forecast.PeriodCode}").Bold().FontSize(10);
                        inner.Item().Text(forecast.RootCauseNarrative).FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                }
            }
        });
    }

    private static void SummaryCell(IContainer container, string label, string value, string color)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(column =>
        {
            column.Item().Text(label).FontColor(Colors.Grey.Darken1);
            column.Item().Text(value).Bold().FontSize(18).FontColor(color);
        });
    }

    private static void HeaderCell(IContainer container, string text)
    {
        container.Background("#0A2F5C").Padding(5).Text(text).FontColor(Colors.White).Bold().FontSize(8);
    }

    private static void DataCell(IContainer container, string text, string? color = null)
    {
        var textDescriptor = container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(text).FontSize(8);
        if (!string.IsNullOrWhiteSpace(color))
        {
            textDescriptor.FontColor(color);
        }
    }

    private static string RiskColor(string riskBand) => riskBand switch
    {
        "CRITICAL" => "#B71C1C",
        "HIGH" => "#C62828",
        "MEDIUM" => "#F9A825",
        "LOW" => "#2E7D32",
        _ => Colors.Grey.Darken1
    };
}
