using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Admin.Services;

/// <summary>Column definition used for export operations.</summary>
public record ExportColumnDef<TItem>(string Title, Func<TItem, string> ValueFunc);

/// <summary>
/// Provides CSV, Excel (ClosedXML), and PDF (QuestPDF) export for DataTable.
/// Registered as a scoped service in Program.cs.
/// </summary>
public sealed class DataTableExportService
{
    static DataTableExportService()
    {
        // QuestPDF community license — free for open-source projects
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ── CSV ──────────────────────────────────────────────────────────────────────

    /// <summary>Generates UTF-8 CSV bytes (with BOM for Excel compatibility).</summary>
    public byte[] ExportCsv<TItem>(IEnumerable<TItem> items, IReadOnlyList<ExportColumnDef<TItem>> columns)
    {
        var sb = new StringBuilder();

        // Header row
        sb.AppendLine(string.Join(",", columns.Select(c => EscapeCsv(c.Title))));

        // Data rows
        foreach (var item in items)
            sb.AppendLine(string.Join(",", columns.Select(c => EscapeCsv(c.ValueFunc(item)))));

        // UTF-8 with BOM so Excel opens it correctly
        var utf8 = Encoding.UTF8;
        return [.. utf8.GetPreamble(), .. utf8.GetBytes(sb.ToString())];
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    // ── Excel (ClosedXML) ────────────────────────────────────────────────────────

    /// <summary>Generates an xlsx file using ClosedXML.</summary>
    public byte[] ExportExcel<TItem>(
        IEnumerable<TItem> items,
        IReadOnlyList<ExportColumnDef<TItem>> columns,
        string sheetName = "Export")
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(sheetName.Length > 31 ? sheetName[..31] : sheetName);

        // Header row — bold, light gold background
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = columns[c].Title;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FEF9E7");
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#C8A415");
        }

        // Data rows
        var rowNum = 2;
        foreach (var item in items)
        {
            for (var c = 0; c < columns.Count; c++)
                ws.Cell(rowNum, c + 1).Value = columns[c].ValueFunc(item);
            rowNum++;
        }

        // Auto-fit columns (cap at 60 chars wide)
        ws.Columns().AdjustToContents(1, 60);

        // Freeze header row
        ws.SheetView.FreezeRows(1);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    // ── PDF (QuestPDF) ───────────────────────────────────────────────────────────

    /// <summary>Generates a PDF file using QuestPDF.</summary>
    public byte[] ExportPdf<TItem>(
        IEnumerable<TItem> items,
        IReadOnlyList<ExportColumnDef<TItem>> columns,
        string title = "Export")
    {
        var rows = items.ToList();
        var colCount = columns.Count;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Helvetica"));

                // Header
                page.Header().PaddingBottom(4, Unit.Point).Row(row =>
                {
                    row.RelativeItem().Text(title).Bold().FontSize(14).FontColor("#1A2B23");
                    row.ConstantItem(120).AlignRight()
                       .Text($"Exported {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                       .FontSize(8).FontColor("#64748B");
                });

                // Table
                page.Content().PaddingTop(8, Unit.Point).Table(table =>
                {
                    // Column widths — equal distribution
                    table.ColumnsDefinition(def =>
                    {
                        for (var i = 0; i < colCount; i++)
                            def.RelativeColumn();
                    });

                    // Header cells
                    table.Header(header =>
                    {
                        foreach (var col in columns)
                        {
                            header.Cell()
                                  .Background("#FEF9E7")
                                  .BorderBottom(1f, Unit.Point).BorderColor("#C8A415")
                                  .PaddingVertical(4, Unit.Point).PaddingHorizontal(6, Unit.Point)
                                  .Text(col.Title).Bold().FontSize(8).FontColor("#374151");
                        }
                    });

                    // Data rows
                    var even = false;
                    foreach (var item in rows)
                    {
                        var bg = even ? "#F0FDF4" : "#FFFFFF";
                        foreach (var col in columns)
                        {
                            table.Cell()
                                 .Background(bg)
                                 .BorderBottom(1f, Unit.Point).BorderColor("#E2E8F0")
                                 .PaddingVertical(3, Unit.Point).PaddingHorizontal(6, Unit.Point)
                                 .Text(col.ValueFunc(item)).FontSize(8).FontColor("#1F2937");
                        }
                        even = !even;
                    }
                });

                // Footer with page numbers
                page.Footer().AlignCenter()
                    .Text(x =>
                    {
                        x.Span("Page ").FontSize(8).FontColor("#94A3B8");
                        x.CurrentPageNumber().FontSize(8).FontColor("#94A3B8");
                        x.Span(" of ").FontSize(8).FontColor("#94A3B8");
                        x.TotalPages().FontSize(8).FontColor("#94A3B8");
                    });
            });
        }).GeneratePdf();
    }
}
