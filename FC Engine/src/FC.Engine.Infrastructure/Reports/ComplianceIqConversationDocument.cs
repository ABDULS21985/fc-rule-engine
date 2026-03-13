using FC.Engine.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Reports;

public sealed class ComplianceIqConversationDocument : IDocument
{
    private readonly IReadOnlyList<ComplianceIqConversationTurnView> _turns;
    private readonly string _tenantName;
    private readonly string _userId;

    private const string Primary = "#0A2F5C";
    private const string Accent = "#C8A951";
    private const string UserBubble = "#E8F0FE";
    private const string AssistantBubble = "#F4F4F0";

    public ComplianceIqConversationDocument(
        IReadOnlyList<ComplianceIqConversationTurnView> turns,
        string tenantName,
        string userId)
    {
        _turns = turns;
        _tenantName = tenantName;
        _userId = userId;
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Text("ComplianceIQ Conversation Export").FontSize(14).Bold().FontColor(Primary);
                    row.ConstantItem(180).AlignRight().Text(DateTime.UtcNow.ToString("dd MMM yyyy HH:mm")).FontSize(8).FontColor(Colors.Grey.Medium);
                });
                column.Item().Text($"{_tenantName} | {_userId}").FontSize(9).FontColor(Colors.Grey.Darken1);
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

                    column.Item().PaddingBottom(12).Row(row =>
                    {
                        row.ConstantItem(28).Text("A").Bold().FontColor(Accent);
                        row.RelativeItem().Background(AssistantBubble).Padding(8).Column(inner =>
                        {
                            inner.Item().Text(turn.ResponseText);
                            inner.Item().PaddingTop(3).Text(
                                $"Intent: {turn.IntentCode} | Confidence: {turn.ConfidenceLevel} | {turn.TotalTimeMs} ms")
                                .FontSize(7)
                                .FontColor(Colors.Grey.Medium);
                        });
                    });
                }
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("ComplianceIQ | RegOS | Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                text.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }
}
