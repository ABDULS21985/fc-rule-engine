using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class HistoricalImpactTrackerService : IHistoricalImpactTracker
{
    private readonly MetadataDbContext _db;
    private readonly IPolicyAuditLogger _audit;
    private readonly ILogger<HistoricalImpactTrackerService> _log;

    public HistoricalImpactTrackerService(
        MetadataDbContext db, IPolicyAuditLogger audit, ILogger<HistoricalImpactTrackerService> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task RunTrackingCycleAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var correlationId = Guid.NewGuid();

        // Find all enacted policies with decisions
        var enactedPolicies = await _db.PolicyDecisions
            .Include(pd => pd.Scenario)
            .Where(pd => (pd.DecisionType == DecisionType.Enact || pd.DecisionType == DecisionType.EnactAmended)
                && pd.EffectiveDate != null && pd.EffectiveDate <= today)
            .ToListAsync(ct);

        foreach (var decision in enactedPolicies)
        {
            // Skip if already tracked this month
            var alreadyTracked = await _db.HistoricalImpactTracking
                .AnyAsync(h => h.DecisionId == decision.Id && h.TrackingDate == today, ct);

            if (alreadyTracked) continue;

            // Get the latest completed run for this scenario
            var latestRun = await _db.ImpactAssessmentRuns
                .Where(r => r.ScenarioId == decision.ScenarioId && r.Status == ImpactRunStatus.Completed)
                .OrderByDescending(r => r.RunNumber)
                .FirstOrDefaultAsync(ct);

            if (latestRun is null) continue;

            // Count entities that are currently breaching (from latest run results)
            var actualBreachCount = await _db.EntityImpactResults
                .CountAsync(e => e.RunId == latestRun.Id
                    && e.ImpactCategory == ImpactCategory.WouldBreach, ct);

            var monthsSinceEnactment = ((today.Year - decision.EffectiveDate!.Value.Year) * 12)
                + (today.Month - decision.EffectiveDate.Value.Month);

            var predictedBreachCount = latestRun.EntitiesWouldBreach;
            var breachVariance = predictedBreachCount > 0
                ? Math.Round((decimal)(actualBreachCount - predictedBreachCount) / predictedBreachCount, 4)
                : 0m;

            var accuracyScore = Math.Max(0, Math.Round(100m - Math.Abs(breachVariance * 100m), 2));

            var tracking = new HistoricalImpactTracking
            {
                DecisionId = decision.Id,
                ScenarioId = decision.ScenarioId,
                TrackingDate = today,
                MonthsSinceEnactment = monthsSinceEnactment,
                PredictedBreachCount = predictedBreachCount,
                PredictedCapitalShortfall = latestRun.AggregateCapitalShortfall,
                PredictedComplianceCost = latestRun.AggregateComplianceCost,
                ActualBreachCount = actualBreachCount,
                BreachCountVariance = breachVariance,
                AccuracyScore = accuracyScore
            };

            _db.HistoricalImpactTracking.Add(tracking);
            await _db.SaveChangesAsync(ct);

            await _audit.LogAsync(decision.ScenarioId, decision.RegulatorId, correlationId,
                "IMPACT_TRACKED", new
                {
                    decisionId = decision.Id,
                    trackingDate = today,
                    predictedBreachCount,
                    actualBreachCount,
                    accuracyScore
                }, decision.DecidedByUserId, ct);

            _log.LogInformation(
                "Tracked impact: DecisionId={DecisionId}, Month={Month}, Predicted={Predicted}, " +
                "Actual={Actual}, Accuracy={Accuracy}%",
                decision.Id, monthsSinceEnactment, predictedBreachCount, actualBreachCount, accuracyScore);
        }
    }

    public async Task<IReadOnlyList<PredictedVsActual>> GetTrackingHistoryAsync(
        long decisionId, int regulatorId, CancellationToken ct = default)
    {
        // Verify decision belongs to regulator
        var decisionExists = await _db.PolicyDecisions
            .AnyAsync(d => d.Id == decisionId && d.RegulatorId == regulatorId, ct);
        if (!decisionExists)
            throw new InvalidOperationException($"Policy decision {decisionId} not found.");

        var entries = await _db.HistoricalImpactTracking
            .Where(h => h.DecisionId == decisionId)
            .OrderBy(h => h.TrackingDate)
            .ToListAsync(ct);

        return entries.Select(h => new PredictedVsActual(
            h.TrackingDate,
            h.MonthsSinceEnactment,
            h.PredictedBreachCount,
            h.ActualBreachCount,
            h.PredictedCapitalShortfall,
            h.ActualCapitalShortfall,
            h.AccuracyScore)).ToList();
    }

    public async Task<decimal> GetAccuracyScoreAsync(
        long decisionId, int regulatorId, CancellationToken ct = default)
    {
        var decisionExists = await _db.PolicyDecisions
            .AnyAsync(d => d.Id == decisionId && d.RegulatorId == regulatorId, ct);
        if (!decisionExists)
            throw new InvalidOperationException($"Policy decision {decisionId} not found.");

        var latestTracking = await _db.HistoricalImpactTracking
            .Where(h => h.DecisionId == decisionId && h.AccuracyScore != null)
            .OrderByDescending(h => h.TrackingDate)
            .FirstOrDefaultAsync(ct);

        return latestTracking?.AccuracyScore ?? 0m;
    }
}
