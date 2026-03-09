using System.Text.Json;
using ClosedXML.Excel;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public class ScheduledReportJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledReportJob> _logger;

    public ScheduledReportJob(
        IServiceProvider serviceProvider,
        ILogger<ScheduledReportJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueReports(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Scheduled report processing cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task ProcessDueReports(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var reportRepo = scope.ServiceProvider.GetRequiredService<ISavedReportRepository>();
        var queryEngine = scope.ServiceProvider.GetRequiredService<IReportQueryEngine>();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var notificationOrchestrator = scope.ServiceProvider.GetRequiredService<INotificationOrchestrator>();

        var scheduledReports = await reportRepo.GetScheduledDue(ct);

        foreach (var report in scheduledReports)
        {
            if (!IsDue(report.ScheduleCron, report.LastRunAt))
                continue;

            try
            {
                var definition = JsonSerializer.Deserialize<ReportDefinition>(report.Definition);
                if (definition is null) continue;

                var entitlement = await entitlementService.ResolveEntitlements(report.TenantId, ct);
                var entitledModules = entitlement.ActiveModules
                    .Select(m => m.ModuleCode)
                    .ToList();

                var result = await queryEngine.Execute(definition, report.TenantId, entitledModules, ct);

                byte[] fileBytes;

                if (string.Equals(report.ScheduleFormat, "PDF", StringComparison.OrdinalIgnoreCase))
                {
                    fileBytes = GeneratePdfFromResult(result, report.Name);
                }
                else
                {
                    fileBytes = GenerateExcelFromResult(result, report.Name);
                }

                // Notify recipients
                var recipients = !string.IsNullOrWhiteSpace(report.ScheduleRecipients)
                    ? JsonSerializer.Deserialize<List<int>>(report.ScheduleRecipients) ?? new()
                    : new List<int> { report.CreatedByUserId };

                await notificationOrchestrator.Notify(new NotificationRequest
                {
                    TenantId = report.TenantId,
                    EventType = NotificationEvents.ScheduledReportReady,
                    Title = $"Scheduled Report: {report.Name}",
                    Message = "Your scheduled report has been generated and is ready.",
                    Priority = NotificationPriority.Normal,
                    RecipientUserIds = recipients,
                    ActionUrl = "/reports/saved"
                }, ct);

                report.LastRunAt = DateTime.UtcNow;
                await reportRepo.Update(report, ct);

                _logger.LogInformation(
                    "Scheduled report {ReportId} executed for tenant {TenantId}: {RowCount} rows",
                    report.Id, report.TenantId, result.TotalRowCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to execute scheduled report {ReportId} for tenant {TenantId}",
                    report.Id, report.TenantId);
            }
        }
    }

    internal static bool IsDue(string? cronExpression, DateTime? lastRunAt)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return false;

        // Simple cron evaluation: support daily, weekly, monthly patterns
        // Format: "0 8 * * *" (daily at 8am), "0 8 * * 1" (weekly Monday), "0 8 1 * *" (monthly 1st)
        var now = DateTime.UtcNow;
        var parts = cronExpression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) return false;

        var minute = parts[0];
        var hour = parts[1];
        var dayOfMonth = parts[2];
        var month = parts[3];
        var dayOfWeek = parts[4];

        // Check if current time matches the cron pattern
        if (minute != "*" && int.TryParse(minute, out var m) && now.Minute != m) return false;
        if (hour != "*" && int.TryParse(hour, out var h) && now.Hour != h) return false;
        if (dayOfMonth != "*" && int.TryParse(dayOfMonth, out var dom) && now.Day != dom) return false;
        if (month != "*" && int.TryParse(month, out var mon) && now.Month != mon) return false;
        if (dayOfWeek != "*" && int.TryParse(dayOfWeek, out var dow) && (int)now.DayOfWeek != dow) return false;

        // Don't run if already ran in this period
        if (lastRunAt.HasValue)
        {
            var minInterval = TimeSpan.FromMinutes(55); // At least 55 minutes between runs
            if (now - lastRunAt.Value < minInterval) return false;
        }

        return true;
    }

    private static byte[] GenerateExcelFromResult(ReportQueryResult result, string reportName)
    {
        using var wb = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ws = wb.AddWorksheet(ExportUtility.SanitizeWorksheetName(reportName, usedNames));

        // Headers
        for (var col = 0; col < result.Columns.Count; col++)
        {
            ws.Cell(1, col + 1).Value = result.Columns[col];
            ws.Cell(1, col + 1).Style.Font.Bold = true;
            ws.Cell(1, col + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#006B3F");
            ws.Cell(1, col + 1).Style.Font.FontColor = XLColor.White;
        }

        // Data rows
        for (var row = 0; row < result.Rows.Count; row++)
        {
            for (var col = 0; col < result.Columns.Count; col++)
            {
                var colName = result.Columns[col];
                result.Rows[row].TryGetValue(colName, out var value);
                var cell = ws.Cell(row + 2, col + 1);
                if (value is decimal d) cell.Value = (double)d;
                else if (value is int i) cell.Value = i;
                else if (value is long l) cell.Value = l;
                else if (value is double dbl) cell.Value = dbl;
                else if (value is DateTime dt) cell.Value = dt;
                else cell.Value = value?.ToString() ?? "";
            }
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(result.Rows.Count + 1, 100), 5, 50);

        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] GeneratePdfFromResult(ReportQueryResult result, string reportName)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

        return QuestPDF.Fluent.Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(QuestPDF.Helpers.PageSizes.A4);
                page.Margin(1, QuestPDF.Infrastructure.Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Header().Column(col =>
                {
                    col.Item().Text(reportName).Bold().FontSize(14).FontColor("#006B3F");
                    col.Item().Text($"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                        .FontSize(9).FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
                    col.Item().PaddingBottom(8).LineHorizontal(1)
                        .LineColor(QuestPDF.Helpers.Colors.Grey.Lighten2);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in result.Columns)
                            columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var colName in result.Columns)
                        {
                            header.Cell().Background("#006B3F").Padding(3)
                                .Text(colName).FontSize(7).Bold()
                                .FontColor(QuestPDF.Helpers.Colors.White);
                        }
                    });

                    var rowIdx = 0;
                    foreach (var row in result.Rows)
                    {
                        var isAlt = rowIdx % 2 == 1;
                        foreach (var colName in result.Columns)
                        {
                            row.TryGetValue(colName, out var value);
                            var cell = table.Cell();
                            if (isAlt)
                            {
                                cell.Background(QuestPDF.Helpers.Colors.Grey.Lighten4);
                            }

                            cell.Padding(2).Text(value?.ToString() ?? "").FontSize(7);
                        }
                        rowIdx++;
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ").FontSize(7).FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
                    text.CurrentPageNumber().FontSize(7);
                    text.Span(" / ").FontSize(7).FontColor(QuestPDF.Helpers.Colors.Grey.Medium);
                    text.TotalPages().FontSize(7);
                });
            });
        }).GeneratePdf();
    }
}
