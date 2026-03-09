using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Records the final policy decision and generates the enactment document.
/// </summary>
public interface IPolicyDecisionService
{
    Task<long> RecordDecisionAsync(
        long scenarioId,
        int regulatorId,
        DecisionType decision,
        string summary,
        DateOnly? effectiveDate,
        int? phaseInMonths,
        string? circularReference,
        int userId,
        CancellationToken ct = default);

    Task<byte[]> GeneratePolicyDocumentAsync(
        long decisionId,
        int regulatorId,
        CancellationToken ct = default);
}
