using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class PolicyDecisionService : IPolicyDecisionService
{
    private readonly MetadataDbContext _db;
    private readonly IPolicyAuditLogger _audit;
    private readonly ILogger<PolicyDecisionService> _log;

    public PolicyDecisionService(MetadataDbContext db, IPolicyAuditLogger audit, ILogger<PolicyDecisionService> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task<long> RecordDecisionAsync(
        long scenarioId, int regulatorId, DecisionType decision, string summary,
        DateOnly? effectiveDate, int? phaseInMonths, string? circularReference,
        int userId, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();

        var scenario = await _db.PolicyScenarios
            .Include(s => s.Parameters)
            .FirstOrDefaultAsync(s => s.Id == scenarioId && s.RegulatorId == regulatorId, ct)
            ?? throw new InvalidOperationException($"Policy scenario {scenarioId} not found.");

        // Build final parameters JSON from current scenario parameters
        var finalParams = scenario.Parameters.Select(p => new
        {
            p.ParameterCode,
            p.ParameterName,
            p.CurrentValue,
            FinalValue = decision == DecisionType.Withdraw ? p.CurrentValue : p.ProposedValue,
            p.Unit,
            p.ApplicableEntityTypes
        }).ToList();

        var policyDecision = new PolicyDecision
        {
            ScenarioId = scenarioId,
            RegulatorId = regulatorId,
            DecisionType = decision,
            DecisionSummary = summary,
            FinalParametersJson = JsonSerializer.Serialize(finalParams),
            EffectiveDate = effectiveDate,
            PhaseInMonths = phaseInMonths,
            CircularReference = circularReference,
            DecidedByUserId = userId
        };

        _db.PolicyDecisions.Add(policyDecision);

        // Transition scenario status
        scenario.Status = decision switch
        {
            DecisionType.Enact or DecisionType.EnactAmended => PolicyStatus.Enacted,
            DecisionType.Withdraw => PolicyStatus.Withdrawn,
            DecisionType.Defer => PolicyStatus.DecisionPending,
            _ => PolicyStatus.DecisionPending
        };
        scenario.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(scenarioId, regulatorId, correlationId,
            "DECISION_MADE", new
            {
                decisionId = policyDecision.Id,
                decision = decision.ToString(),
                effectiveDate,
                phaseInMonths,
                circularReference
            }, userId, ct);

        if (decision is DecisionType.Enact or DecisionType.EnactAmended)
        {
            await _audit.LogAsync(scenarioId, regulatorId, correlationId,
                "POLICY_ENACTED", new
                {
                    decisionId = policyDecision.Id,
                    circularReference,
                    effectiveDate,
                    phaseInMonths
                }, userId, ct);
        }

        _log.LogInformation(
            "Policy decision recorded: DecisionId={DecisionId}, Scenario={ScenarioId}, Type={Type}, Effective={Date}",
            policyDecision.Id, scenarioId, decision, effectiveDate);

        return policyDecision.Id;
    }

    public async Task<byte[]> GeneratePolicyDocumentAsync(
        long decisionId, int regulatorId, CancellationToken ct = default)
    {
        var decision = await _db.PolicyDecisions
            .Include(d => d.Scenario)
                .ThenInclude(s => s!.Parameters)
            .FirstOrDefaultAsync(d => d.Id == decisionId && d.RegulatorId == regulatorId, ct)
            ?? throw new InvalidOperationException($"Policy decision {decisionId} not found.");

        var scenario = decision.Scenario!;

        // Get latest impact run
        var latestRun = await _db.ImpactAssessmentRuns
            .Where(r => r.ScenarioId == scenario.Id && r.Status == ImpactRunStatus.Completed)
            .OrderByDescending(r => r.RunNumber)
            .FirstOrDefaultAsync(ct);

        // Get CBA if available
        var cba = latestRun is not null
            ? await _db.CostBenefitAnalyses
                .FirstOrDefaultAsync(c => c.RunId == latestRun.Id, ct)
            : null;

        // Get consultation feedback summary
        var consultation = await _db.ConsultationRounds
            .Where(c => c.ScenarioId == scenario.Id)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Generate a text-based policy document
        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("                    REGULATORY POLICY DOCUMENT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Title:               {scenario.Title}");
        sb.AppendLine($"Decision:            {decision.DecisionType}");
        sb.AppendLine($"Circular Reference:  {decision.CircularReference ?? "N/A"}");
        sb.AppendLine($"Effective Date:      {decision.EffectiveDate?.ToString("dd MMMM yyyy") ?? "TBD"}");
        sb.AppendLine($"Phase-In Period:     {(decision.PhaseInMonths.HasValue ? $"{decision.PhaseInMonths} months" : "Immediate")}");
        sb.AppendLine($"Policy Domain:       {scenario.PolicyDomain}");
        sb.AppendLine($"Target Entities:     {scenario.TargetEntityTypes}");
        sb.AppendLine($"Decided:             {decision.DecidedAt:dd MMMM yyyy HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("1. DECISION SUMMARY");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine(decision.DecisionSummary);
        sb.AppendLine();

        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine("2. PARAMETER CHANGES");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        foreach (var param in scenario.Parameters)
        {
            sb.AppendLine($"  {param.ParameterName} ({param.ParameterCode})");
            sb.AppendLine($"    Current:  {param.CurrentValue} {param.Unit}");
            sb.AppendLine($"    Proposed: {param.ProposedValue} {param.Unit}");
            sb.AppendLine($"    Applies:  {param.ApplicableEntityTypes}");
            sb.AppendLine();
        }

        if (latestRun is not null)
        {
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("3. IMPACT ASSESSMENT SUMMARY");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine($"  Total Entities Evaluated:     {latestRun.TotalEntitiesEvaluated}");
            sb.AppendLine($"  Currently Compliant:          {latestRun.EntitiesCurrentlyCompliant}");
            sb.AppendLine($"  Would Breach:                 {latestRun.EntitiesWouldBreach}");
            sb.AppendLine($"  Already Breaching:            {latestRun.EntitiesAlreadyBreaching}");
            sb.AppendLine($"  Not Affected:                 {latestRun.EntitiesNotAffected}");
            sb.AppendLine($"  Aggregate Capital Shortfall:  NGN {latestRun.AggregateCapitalShortfall:N2}M");
            sb.AppendLine($"  Aggregate Compliance Cost:    NGN {latestRun.AggregateComplianceCost:N2}M");
            sb.AppendLine();
        }

        if (cba is not null)
        {
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("4. COST-BENEFIT ANALYSIS");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine($"  Total Industry Cost:   NGN {cba.TotalIndustryComplianceCost:N2}M");
            sb.AppendLine($"  Cost (Small):          NGN {cba.CostToSmallEntities:N2}M");
            sb.AppendLine($"  Cost (Medium):         NGN {cba.CostToMediumEntities:N2}M");
            sb.AppendLine($"  Cost (Large):          NGN {cba.CostToLargeEntities:N2}M");
            sb.AppendLine($"  Net Benefit Score:     {cba.NetBenefitScore:F4}");
            sb.AppendLine($"  Recommendation:        {cba.Recommendation}");
            sb.AppendLine();
        }

        if (consultation is not null)
        {
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("5. CONSULTATION SUMMARY");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine($"  Title:              {consultation.Title}");
            sb.AppendLine($"  Feedback Received:  {consultation.TotalFeedbackReceived}");
            sb.AppendLine($"  Status:             {consultation.Status}");
            sb.AppendLine();
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("                         END OF DOCUMENT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public async Task<long?> GetDecisionIdForScenarioAsync(
        long scenarioId, int regulatorId, CancellationToken ct = default)
    {
        return await _db.PolicyDecisions
            .Where(d => d.ScenarioId == scenarioId && d.RegulatorId == regulatorId)
            .Select(d => (long?)d.Id)
            .FirstOrDefaultAsync(ct);
    }
}
