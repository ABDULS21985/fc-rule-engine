using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IComplianceIqService
{
    Task<ComplianceIqQueryResponse> QueryAsync(ComplianceIqQueryRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ComplianceIqQuickQuestionView>> GetQuickQuestionsAsync(
        bool isRegulatorContext,
        CancellationToken ct = default);

    Task<IReadOnlyList<ComplianceIqConversationTurnView>> GetConversationHistoryAsync(
        Guid conversationId,
        Guid tenantId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ComplianceIqHistoryEntry>> GetQueryHistoryAsync(
        Guid tenantId,
        string? userId = null,
        int limit = 50,
        CancellationToken ct = default);

    Task<IReadOnlyList<ComplianceIqTemplateCatalogItem>> GetTemplateCatalogAsync(
        CancellationToken ct = default);

    Task SubmitFeedbackAsync(
        int turnId,
        Guid tenantId,
        string userId,
        short rating,
        string? feedbackText,
        CancellationToken ct = default);

    Task<byte[]> ExportConversationPdfAsync(
        Guid conversationId,
        Guid tenantId,
        CancellationToken ct = default);
}
