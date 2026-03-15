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

    /// <summary>
    /// Returns the decision ID for the given scenario, or null if no decision exists.
    /// Used as a fallback when the scenario's Decision navigation property is missing
    /// due to data inconsistency (e.g. status is Enacted but Decision record is absent).
    /// </summary>
    Task<long?> GetDecisionIdForScenarioAsync(
        long scenarioId,
        int regulatorId,
        CancellationToken ct = default);
}
