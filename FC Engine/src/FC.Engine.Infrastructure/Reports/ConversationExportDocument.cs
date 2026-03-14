using System.Text.Json;
using FC.Engine.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Reports;

public sealed class ConversationExportDocument : IDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ComplianceIqConversation _conversation;
    private readonly IReadOnlyList<ComplianceIqTurn> _turns;
    private readonly string _tenantName;

    private const string Primary = "#0A2F5C";
    private const string Accent = "#C8A951";
    private const string UserBubble = "#E8F0FE";
    private const string AssistantBubble = "#F4F4F0";

    public ConversationExportDocument(
        ComplianceIqConversation conversation,
        IReadOnlyList<ComplianceIqTurn> turns,
        string tenantName)
    {
        _conversation = conversation;
        _turns = turns;
        _tenantName = tenantName;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("RegulatorIQ Conversation Export").FontSize(16).Bold().FontColor(Primary);
                    row.ConstantItem(180).AlignRight().Text(DateTime.UtcNow.ToString("dd MMM yyyy HH:mm")).FontSize(8).FontColor(Colors.Grey.Medium);
                });
                column.Item().Text($"{_tenantName} | {_conversation.UserId}").FontSize(9).FontColor(Colors.Grey.Darken1);
                column.Item().Text($"Conversation: {_conversation.Id:D} | Scope: {_conversation.Scope ?? "SECTOR"} | Examination: {(_conversation.IsExaminationSession ? "Active" : "No")}").FontSize(8).FontColor(Colors.Grey.Darken1);
                column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Accent);
            });

            page.Content().PaddingTop(12).Column(column =>
            {
                foreach (var turn in _turns.OrderBy(x => x.TurnNumber))
                {
                    column.Item().PaddingBottom(8).Row(row =>
                    {
                        row.ConstantItem(28).Text("Q").Bold().FontColor(Primary);
                        row.RelativeItem().Background(UserBubble).Padding(8).Column(inner =>
                        {
                            inner.Item().Text(turn.QueryText);
                            inner.Item().PaddingTop(2).Text(turn.CreatedAt.ToString("dd MMM yyyy HH:mm")).FontSize(7).FontColor(Colors.Grey.Medium);
                        });
                    });

                    column.Item().PaddingBottom(10).Row(row =>
                    {
                        row.ConstantItem(28).Text("A").Bold().FontColor(Accent);
                        row.RelativeItem().Background(AssistantBubble).Padding(8).Column(inner =>
                        {
                            inner.Item().Row(meta =>
                            {
                                meta.RelativeItem().Text(turn.ResponseText);
                                meta.ConstantItem(96).AlignRight().Background(ResolveClassificationColor(turn.ClassificationLevel)).PaddingHorizontal(6).PaddingVertical(2).Text(turn.ClassificationLevel).FontSize(7).FontColor(Colors.White).SemiBold();
                            });

                            inner.Item().PaddingTop(4).Text(
                                    $"Intent: {turn.IntentCode} | Confidence: {turn.ConfidenceLevel} | Format: {turn.VisualizationType} | {turn.TotalTimeMs} ms")
                                .FontSize(7)
                                .FontColor(Colors.Grey.Medium);

                            if (!string.IsNullOrWhiteSpace(turn.DataSourcesUsed))
                            {
                                inner.Item().PaddingTop(3).Text($"Sources: {turn.DataSourcesUsed}").FontSize(7).FontColor(Colors.Grey.Medium);
                            }
                        });
                    });
                }

                if (_turns.Count > 0)
                {
                    column.Item().PaddingTop(8).Text("Entity Access Appendix").FontSize(12).Bold().FontColor(Primary);
                    column.Item().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(42);
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Turn");
                            header.Cell().Element(HeaderCell).Text("Entities Accessed");
                            header.Cell().Element(HeaderCell).Text("Data Sources");
                            header.Cell().Element(HeaderCell).Text("Classification");
                        });

                        foreach (var turn in _turns.OrderBy(x => x.TurnNumber))
                        {
                            table.Cell().Element(BodyCell).Text(turn.TurnNumber.ToString());
                            table.Cell().Element(BodyCell).Text(string.Join(", ", ParseTextArray(turn.EntitiesAccessedJson)));
                            table.Cell().Element(BodyCell).Text(string.IsNullOrWhiteSpace(turn.DataSourcesUsed) ? "N/A" : turn.DataSourcesUsed);
                            table.Cell().Element(BodyCell).Text(turn.ClassificationLevel);
                        }
                    });
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("CONFIDENTIAL - RegulatorIQ Intelligence Product - Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Primary).Padding(6).DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(6);

    private static string ResolveClassificationColor(string? level)
    {
        return level?.ToUpperInvariant() switch
        {
            "CONFIDENTIAL" => Colors.Red.Darken2,
            "RESTRICTED" => Colors.Orange.Darken2,
            _ => Colors.Grey.Darken2
        };
    }

    private static IReadOnlyList<string> ParseTextArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var strings = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            if (strings is { Count: > 0 })
            {
                return strings;
            }

            var guids = JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions);
            if (guids is { Count: > 0 })
            {
                return guids.Select(x => x.ToString("D")).ToList();
            }
        }
        catch
        {
        }

        return [json];
    }
}
