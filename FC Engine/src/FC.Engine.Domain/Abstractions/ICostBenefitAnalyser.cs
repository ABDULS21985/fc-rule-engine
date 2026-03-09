using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Generates structured cost-benefit analysis from impact assessment results,
/// including phase-in scenario modelling.
/// </summary>
public interface ICostBenefitAnalyser
{
    Task<CostBenefitResult> GenerateAnalysisAsync(
        long runId,
        int regulatorId,
        int userId,
        CancellationToken ct = default);

    Task<CostBenefitResult> GetAnalysisAsync(
        long scenarioId,
        int regulatorId,
        CancellationToken ct = default);
}
