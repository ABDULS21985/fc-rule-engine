using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class CostBenefitAnalyser : ICostBenefitAnalyser
{
    private readonly MetadataDbContext _db;
    private readonly IPolicyAuditLogger _audit;
    private readonly ILogger<CostBenefitAnalyser> _log;

    public CostBenefitAnalyser(MetadataDbContext db, IPolicyAuditLogger audit, ILogger<CostBenefitAnalyser> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task<CostBenefitResult> GenerateAnalysisAsync(
        long runId, int regulatorId, int userId, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();

        var run = await _db.ImpactAssessmentRuns
            .Include(r => r.Scenario)
            .FirstOrDefaultAsync(r => r.Id == runId && r.RegulatorId == regulatorId && r.Status == ImpactRunStatus.Completed, ct)
            ?? throw new InvalidOperationException($"Completed impact run {runId} not found.");

        var entityResults = await _db.EntityImpactResults
            .Where(e => e.RunId == runId)
            .ToListAsync(ct);

        // Compute costs by entity tier
        var costSmall = entityResults
            .Where(e => e.EntityType is "MFB" or "PMB")
            .Sum(e => e.EstimatedComplianceCost ?? 0m);
        var costMedium = entityResults
            .Where(e => e.EntityType is "MERC")
            .Sum(e => e.EstimatedComplianceCost ?? 0m);
        var costLarge = entityResults
            .Where(e => e.EntityType is "DMB")
            .Sum(e => e.EstimatedComplianceCost ?? 0m);
        var totalCost = costSmall + costMedium + costLarge;

        // Load parameters for this scenario
        var parameters = await _db.PolicyParameters
            .Where(p => p.ScenarioId == run.ScenarioId)
            .ToListAsync(ct);

        // Compute sector improvement metrics
        decimal? carImprovement = null;
        decimal? lcrImprovement = null;
        foreach (var param in parameters)
        {
            var improvement = param.ProposedValue - param.CurrentValue;
            if (param.ParameterCode.StartsWith("MIN_CAR"))
                carImprovement = (carImprovement ?? 0) + improvement;
            else if (param.ParameterCode.StartsWith("MIN_LCR") || param.ParameterCode.StartsWith("MIN_LIQUIDITY"))
                lcrImprovement = (lcrImprovement ?? 0) + improvement;
        }

        // Estimate risk reduction: fewer entities in breach → lower systemic risk
        var totalEntities = entityResults.Count;
        var breachingEntities = entityResults.Count(e =>
            e.ImpactCategory is ImpactCategory.WouldBreach or ImpactCategory.AlreadyBreaching);
        var riskReduction = totalEntities > 0
            ? Math.Round((decimal)breachingEntities / totalEntities * 100m * 0.3m, 4)
            : 0m;

        // Phase-in scenarios
        var phaseInScenarios = new List<PhaseInScenario>();
        var immediateBreachCount = entityResults.Count(e => e.ImpactCategory == ImpactCategory.WouldBreach);

        // Immediate
        phaseInScenarios.Add(new PhaseInScenario(
            "Immediate", 0, immediateBreachCount,
            run.AggregateCapitalShortfall ?? 0m,
            totalCost,
            parameters.FirstOrDefault()?.ProposedValue ?? 0m));

        // 12-month: use midpoint threshold
        var midpointResults12 = ComputePhaseInBreaches(entityResults, parameters, 0.5m);
        phaseInScenarios.Add(new PhaseInScenario(
            "12-Month Phase-In", 12,
            midpointResults12.breachCount,
            midpointResults12.shortfall,
            midpointResults12.cost,
            midpointResults12.interimThreshold));

        // 24-month: use quarter-point threshold
        var midpointResults24 = ComputePhaseInBreaches(entityResults, parameters, 0.25m);
        phaseInScenarios.Add(new PhaseInScenario(
            "24-Month Phase-In", 24,
            midpointResults24.breachCount,
            midpointResults24.shortfall,
            midpointResults24.cost,
            midpointResults24.interimThreshold));

        var netBenefit = riskReduction - (totalCost > 0 ? Math.Min(totalCost / 10000m, 50m) : 0m);

        var recommendation = netBenefit > 0
            ? "The proposed regulatory changes show a net positive benefit-to-cost ratio. " +
              $"With an estimated risk reduction of {riskReduction:F2}% and total industry compliance cost of NGN {totalCost:N2}M, " +
              "the policy is recommended for enactment with the suggested phase-in period."
            : "The proposed regulatory changes show a marginal or negative benefit-to-cost ratio. " +
              "Consider adjusting parameter thresholds or extending the phase-in period to reduce industry impact.";

        // Persist CBA
        var cba = new CostBenefitAnalysis
        {
            ScenarioId = run.ScenarioId,
            RunId = runId,
            TotalIndustryComplianceCost = totalCost,
            CostToSmallEntities = costSmall,
            CostToMediumEntities = costMedium,
            CostToLargeEntities = costLarge,
            SectorCARImprovement = carImprovement,
            SectorLCRImprovement = lcrImprovement,
            EstimatedRiskReduction = riskReduction,
            EstimatedDepositProtection = costLarge * 0.15m, // estimated additional protection
            ImmediateImpactSummary = JsonSerializer.Serialize(phaseInScenarios[0]),
            PhaseIn12MonthSummary = JsonSerializer.Serialize(phaseInScenarios[1]),
            PhaseIn24MonthSummary = JsonSerializer.Serialize(phaseInScenarios[2]),
            NetBenefitScore = netBenefit,
            Recommendation = recommendation
        };

        _db.CostBenefitAnalyses.Add(cba);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(run.ScenarioId, regulatorId, correlationId,
            "CBA_GENERATED", new { analysisId = cba.Id, runId, netBenefit, totalCost }, userId, ct);

        return new CostBenefitResult(
            cba.Id, run.ScenarioId, runId,
            totalCost, costSmall, costMedium, costLarge,
            carImprovement, lcrImprovement, riskReduction,
            cba.EstimatedDepositProtection,
            phaseInScenarios, netBenefit, recommendation);
    }

    public async Task<CostBenefitResult> GetAnalysisAsync(
        long scenarioId, int regulatorId, CancellationToken ct = default)
    {
        var cba = await _db.CostBenefitAnalyses
            .Include(c => c.Run)
            .Where(c => c.ScenarioId == scenarioId && c.Run!.RegulatorId == regulatorId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"No CBA found for scenario {scenarioId}.");

        var phaseInScenarios = new List<PhaseInScenario>
        {
            JsonSerializer.Deserialize<PhaseInScenario>(cba.ImmediateImpactSummary)!,
            JsonSerializer.Deserialize<PhaseInScenario>(cba.PhaseIn12MonthSummary)!,
            JsonSerializer.Deserialize<PhaseInScenario>(cba.PhaseIn24MonthSummary)!
        };

        return new CostBenefitResult(
            cba.Id, cba.ScenarioId, cba.RunId,
            cba.TotalIndustryComplianceCost, cba.CostToSmallEntities,
            cba.CostToMediumEntities, cba.CostToLargeEntities,
            cba.SectorCARImprovement, cba.SectorLCRImprovement,
            cba.EstimatedRiskReduction, cba.EstimatedDepositProtection,
            phaseInScenarios, cba.NetBenefitScore, cba.Recommendation);
    }

    private static (int breachCount, decimal shortfall, decimal cost, decimal interimThreshold)
        ComputePhaseInBreaches(
            List<EntityImpactResult> entityResults,
            List<PolicyParameter> parameters,
            decimal progressFraction)
    {
        // Compute interim threshold: current + (proposed - current) * fraction
        var primaryParam = parameters.FirstOrDefault();
        if (primaryParam is null)
            return (0, 0m, 0m, 0m);

        var interimThreshold = primaryParam.CurrentValue +
            (primaryParam.ProposedValue - primaryParam.CurrentValue) * progressFraction;

        var breachCount = 0;
        var shortfall = 0m;
        var cost = 0m;

        foreach (var entity in entityResults)
        {
            if (entity.ImpactCategory == ImpactCategory.NotAffected) continue;
            if (entity.CurrentMetricValue is null) continue;

            var gap = entity.CurrentMetricValue.Value - interimThreshold;
            if (gap < 0 && entity.CurrentMetricValue.Value >= primaryParam.CurrentValue)
            {
                breachCount++;
                shortfall += Math.Abs(gap);
                cost += entity.EstimatedComplianceCost ?? 0m;
            }
        }

        return (breachCount, shortfall, cost, interimThreshold);
    }
}
