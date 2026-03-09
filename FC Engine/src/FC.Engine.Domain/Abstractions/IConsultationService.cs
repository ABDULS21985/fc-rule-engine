using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Manages the entire consultation lifecycle: publish → collect → aggregate.
/// </summary>
public interface IConsultationService
{
    // ── Regulator-facing ────────────────────────────────────────────

    Task<long> CreateConsultationAsync(
        long scenarioId,
        int regulatorId,
        string title,
        string? coverNote,
        DateOnly deadline,
        IReadOnlyList<ConsultationProvisionInput> provisions,
        int userId,
        CancellationToken ct = default);

    Task PublishConsultationAsync(
        long consultationId,
        int regulatorId,
        int userId,
        CancellationToken ct = default);

    Task CloseConsultationAsync(
        long consultationId,
        int regulatorId,
        int userId,
        CancellationToken ct = default);

    Task<FeedbackAggregationResult> AggregateFeedbackAsync(
        long consultationId,
        int regulatorId,
        int userId,
        CancellationToken ct = default);

    Task<ConsultationDetail> GetConsultationAsync(
        long consultationId,
        int regulatorId,
        CancellationToken ct = default);

    // ── Institution-facing ──────────────────────────────────────────

    Task<IReadOnlyList<ConsultationSummary>> GetOpenConsultationsAsync(
        int institutionId,
        CancellationToken ct = default);

    Task<long> SubmitFeedbackAsync(
        long consultationId,
        int institutionId,
        FeedbackPosition overallPosition,
        string? generalComments,
        IReadOnlyList<ProvisionFeedbackInput> provisionFeedback,
        int submittedByUserId,
        CancellationToken ct = default);
}
