using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.ValueObjects;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Export;

public class BoardPackGenerator : IBoardPackGenerator
{
    public Task<byte[]> Generate(
        List<BoardPackSection> sections,
        BrandingConfig branding,
        string title,
        CancellationToken ct = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var effectiveBranding = BrandingConfig.WithDefaults(branding);
        var primaryColor = string.IsNullOrWhiteSpace(effectiveBranding.PrimaryColor)
            ? "#006B3F"
            : effectiveBranding.PrimaryColor!;

        var pdfBytes = Document.Create(document =>
        {
            // Cover page
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);

                page.Content().AlignCenter().AlignMiddle().Column(col =>
                {
                    col.Spacing(15);
                    col.Item().Text(effectiveBranding.CompanyName ?? "RegOS")
                        .Bold().FontSize(28).FontColor(primaryColor);
                    col.Item().LineHorizontal(2).LineColor(primaryColor);
                    col.Item().Text(title).FontSize(20).Bold();
                    col.Item().Text($"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                        .FontSize(11).FontColor(Colors.Grey.Medium);
                    col.Item().Text($"Contains {sections.Count} report(s)")
                        .FontSize(11).FontColor(Colors.Grey.Medium);
                });
            });

            // Table of contents
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);

                page.Header().Column(col =>
                {
                    col.Item().Text("Table of Contents")
                        .Bold().FontSize(16).FontColor(primaryColor);
                    col.Item().PaddingBottom(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    for (var i = 0; i < sections.Count; i++)
                    {
                        var sectionNumber = i + 1;
                        var name = sections[i].ReportName;
                        col.Item().Row(row =>
                        {
                            row.ConstantItem(30).Text($"{sectionNumber}.").FontSize(11);
                            row.RelativeItem().Text(name).FontSize(11);
                            row.ConstantItem(60).AlignRight()
                                .Text($"{sections[i].Rows.Count} rows")
                                .FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                    }
                });
            });

            // Each report section
            for (var i = 0; i < sections.Count; i++)
            {
                var section = sections[i];
                var sectionNumber = i + 1;

                document.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"{sectionNumber}. {section.ReportName}")
                                .Bold().FontSize(13).FontColor(primaryColor);
                            row.ConstantItem(120).AlignRight()
                                .Text($"{section.Rows.Count} rows")
                                .FontSize(9).FontColor(Colors.Grey.Medium);
                        });
                        col.Item().PaddingBottom(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span($"{effectiveBranding.CompanyName ?? "RegOS"} — Board Pack — ")
                            .FontSize(7).FontColor(Colors.Grey.Medium);
                        text.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Medium);
                        text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Medium);
                        text.TotalPages().FontSize(7).FontColor(Colors.Grey.Medium);
                    });

                    page.Content().Column(col =>
                    {
                        if (section.ColumnNames.Count == 0 || section.Rows.Count == 0)
                        {
                            col.Item().PaddingTop(20).AlignCenter()
                                .Text("No data available for this report.")
                                .FontSize(10).FontColor(Colors.Grey.Medium);
                            return;
                        }

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                foreach (var _ in section.ColumnNames)
                                    columns.RelativeColumn();
                            });

                            // Header row
                            table.Header(header =>
                            {
                                foreach (var colName in section.ColumnNames)
                                {
                                    header.Cell()
                                        .Background(primaryColor)
                                        .Padding(4)
                                        .Text(colName)
                                        .FontSize(8)
                                        .Bold()
                                        .FontColor(Colors.White);
                                }
                            });

                            // Data rows
                            var rowIndex = 0;
                            foreach (var row in section.Rows)
                            {
                                var isAlt = rowIndex % 2 == 1;
                                foreach (var colName in section.ColumnNames)
                                {
                                    row.TryGetValue(colName, out var value);
                                    var cellContainer = table.Cell();
                                    IContainer cell = isAlt
                                        ? cellContainer.Background(Colors.Grey.Lighten4).Padding(3)
                                        : cellContainer.Padding(3);
                                    cell.Text(FormatCellValue(value)).FontSize(8);
                                }
                                rowIndex++;
                            }
                        });
                    });
                });
            }
        }).GeneratePdf();

        return Task.FromResult(pdfBytes);
    }

    private static string FormatCellValue(object? value) => value switch
    {
        null => "",
        DateTime dt => dt.ToString("dd MMM yyyy"),
        decimal d => d.ToString("N2"),
        double dbl => dbl.ToString("N2"),
        float f => f.ToString("N2"),
        _ => value.ToString() ?? ""
    };
}
