using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Early Warning Indicator Engine — evaluates prudential thresholds for institutions
/// and persists trigger events to the immutable EWI audit trail.
/// </summary>
public interface IEWIEngine
{
    /// <summary>
    /// Evaluates all active EWI rules for a single institution for the given period.
    /// Returns triggered indicators (new or persisting) and clears resolved ones.
    /// </summary>
    Task<IReadOnlyList<EWITriggerContext>> EvaluateInstitutionAsync(
        int institutionId,
        string periodCode,
        Guid computationRunId,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a full EWI evaluation cycle across all institutions in a regulator's jurisdiction.
    /// Also evaluates systemic indicators, generates supervisory actions, and clears
    /// resolved triggers. Returns a summary of the computation cycle.
    /// </summary>
    Task<EWIComputationSummary> RunFullCycleAsync(
        string regulatorCode,
        string periodCode,
        CancellationToken ct = default);
}
