using System.Globalization;
using FC.Engine.Infrastructure.Services;

namespace FC.Engine.Admin.Services;

public sealed class DashboardBriefingPackBuilder
{
    public IReadOnlyList<DashboardBriefingPackSectionInput> Build(
        PlatformIntelligenceWorkspace workspace,
        string lens,
        int? institutionId,
        SanctionsScreeningSessionState? screeningSession,
        SanctionsWorkflowState? workflowState,
        SanctionsStrDraftCatalogState? strDraftCatalog)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var normalizedLens = string.IsNullOrWhiteSpace(lens) ? "director" : lens.Trim().ToLowerInvariant();
        var tfsPreview = BuildTfsPreview(screeningSession?.LatestRun, workflowState);
        var criticalDrafts = strDraftCatalog?.Drafts.Count(x => x.Priority == "Critical") ?? 0;
        var filteredInterventions = FilterInterventions(workspace, normalizedLens, institutionId);
        var concentrationRows = BuildPortfolioConcentrationRows(workspace);

        if (normalizedLens == "executive")
        {
            if (!institutionId.HasValue)
            {
                return [];
            }

            var institution = workspace.InstitutionDetails.FirstOrDefault(x => x.InstitutionId == institutionId.Value);
            if (institution is null)
            {
                return [];
            }

            var topObligation = institution.TopObligations.FirstOrDefault();
            var rejectedCount = institution.RecentSubmissions.Count(x => x.Status.Contains("Reject", StringComparison.OrdinalIgnoreCase));
            var highSeverityActivity = institution.RecentActivity.Count(x => x.Severity is "Critical" or "High");
            var executivePeerBenchmarks = workspace.InstitutionScorecards
                .Where(x => x.LicenceType == institution.LicenceType)
                .OrderByDescending(x => DashboardPriorityRank(x.Priority))
                .ThenByDescending(x => x.OverdueObligations + x.DueSoonObligations + x.OpenResilienceIncidents + x.ModelReviewItems)
                .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();

            return
            [
                Create("EXE-01", "Filing Posture",
                    $"{institution.OverdueObligations} overdue | {institution.DueSoonObligations} due soon",
                    institution.OverdueObligations > 0 ? "Critical" : institution.DueSoonObligations > 0 ? "Watch" : "Current",
                    topObligation is null ? "No immediate filing deadline currently leads the institution queue." : $"{topObligation.ReturnCode} is {topObligation.Status.ToLowerInvariant()} for {topObligation.PeriodLabel}.",
                    topObligation is null ? "Maintain filing readiness and monitor upcoming obligations." : $"Resolve {topObligation.ReturnCode} before {topObligation.NextDeadline:dd MMM yyyy}."),
                Create("EXE-02", "Submission Outcomes",
                    $"{institution.RecentSubmissions.Count} recent | {rejectedCount} rejected",
                    rejectedCount > 0 ? "Critical" : institution.RecentSubmissions.Count > 0 ? "Current" : "Watch",
                    rejectedCount > 0 ? "Recent submissions include rejected returns that need corrective action." : "Recent filing outcomes are not currently showing rejection pressure.",
                    rejectedCount > 0 ? "Close rejection findings and resubmit with corrected evidence." : "Maintain current submission quality controls."),
                Create("EXE-03", "Prudential & Capital",
                    institution.CapitalScore?.ToString("0.0", CultureInfo.InvariantCulture) ?? "N/A",
                    institution.CapitalScore is null ? "Watch" : institution.CapitalScore < 45m ? "Critical" : institution.CapitalScore < 60m ? "Watch" : "Current",
                    string.IsNullOrWhiteSpace(institution.CapitalAlert) ? "No live capital alert is currently attached to this institution." : institution.CapitalAlert,
                    institution.CapitalScore is null ? "Confirm prudential data freshness and rerun capital analytics." : institution.CapitalScore < 60m ? "Open the capital workspace and test buffer-preserving actions." : "Retain current prudential posture and refresh the forecast each quarter."),
                Create("EXE-04", "Supervisory Follow-Up",
                    $"{highSeverityActivity} high-severity activity item(s)",
                    highSeverityActivity > 0 || institution.OpenResilienceIncidents > 0 || institution.ModelReviewItems > 0 ? "Watch" : "Current",
                    highSeverityActivity > 0 ? "Recent high-severity supervisory events remain visible in the institution timeline." : "No high-severity supervisory follow-up currently dominates the timeline.",
                    highSeverityActivity > 0 ? "Assign accountable owners to open supervisory issues and update the response trail." : "Continue monitoring recent supervisory activity for new escalations."),
                Create("EXE-05", "Peer Benchmark",
                    $"{executivePeerBenchmarks.Count} peer institution(s)",
                    executivePeerBenchmarks.Any(x => x.Priority is "Critical" or "High") ? "Watch" : "Current",
                    "Peer posture compares this institution against the same licence segment using the cross-track pressure model.",
                    "Benchmark against peer pressure and use deviations to set management actions.")
            ];
        }

        var topConcentration = concentrationRows.FirstOrDefault();
        var criticalInterventions = workspace.Interventions.Count(x => x.Priority == "Critical");
        var pressureInstitutionCount = workspace.InstitutionScorecards.Count(x => x.Priority is "Critical" or "High");
        var escalatedObligationCount = workspace.KnowledgeGraph.InstitutionObligations.Count(x => x.Status is "Overdue" or "Attention Required");
        var attentionRequiredFilingCount = workspace.KnowledgeGraph.InstitutionObligations.Count(x => x.Status == "Attention Required");
        var openResilienceActionCount = workspace.Resilience.ActionTracker.Count(x => x.Status != "Complete");

        if (normalizedLens == "governor")
        {
            return
            [
                Create("GOV-01", "Systemic Posture",
                    $"{criticalInterventions} critical intervention(s)",
                    criticalInterventions > 0 ? "Critical" : "Current",
                    $"{pressureInstitutionCount} institution(s) currently carry material cross-track pressure.",
                    "Prioritize strategic interventions and confirm owner accountability across the highest-pressure institutions."),
                Create("GOV-02", "Compliance & Filing",
                    $"{escalatedObligationCount} escalated obligation(s)",
                    workspace.KnowledgeGraph.InstitutionObligations.Any(x => x.Status is "Overdue" or "Attention Required") ? "Critical" : "Current",
                    "Population-wide filing pressure remains the primary source of supervisory friction when overdue records persist.",
                    "Drive escalation on blocked returns and require recovery plans from affected institutions."),
                Create("GOV-03", "Capital & Prudential",
                    $"{workspace.Capital.CapitalWatchlistCount} watchlist institution(s)",
                    workspace.Capital.CapitalWatchlistCount > 0 ? "Watch" : "Current",
                    BestCapitalActionHeadline(workspace),
                    "Use the capital workspace to validate intervention scenarios for the watchlist population."),
                Create("GOV-04", "Threat & Resilience",
                    $"{tfsPreview.MatchesFound} screening flag(s) | {workspace.Resilience.BoardSummary.CriticalIssues} board-critical issue(s)",
                    tfsPreview.ConfirmedMatches > 0 || workspace.Resilience.BoardSummary.CriticalIssues > 0 ? "Critical" : tfsPreview.PotentialMatches > 0 || workspace.Resilience.OpenIncidentCount > 0 ? "Watch" : "Current",
                    $"{tfsPreview.Narrative} {workspace.Resilience.BoardSummary.Narrative}",
                    "Escalate confirmed screening hits and board-critical resilience issues through the strategic agenda."),
                Create("GOV-05", "Sector Concentration",
                    topConcentration is null ? "No segment view available" : $"{topConcentration.LicenceType} leads current concentration pressure",
                    topConcentration?.Signal ?? "Current",
                    topConcentration?.Commentary ?? "No licence-segment concentration currently leads the supervisory view.",
                    topConcentration is null ? "Maintain current population segmentation." : $"Review concentration in the {topConcentration.LicenceType} segment and rebalance leadership attention.")
            ];
        }

        if (normalizedLens == "deputy")
        {
            return
            [
                Create("DPY-01", "Operating Queue",
                    $"{filteredInterventions.Count} queued action(s)",
                    filteredInterventions.Any(x => x.Priority == "Critical") ? "Critical" : filteredInterventions.Any(x => x.Priority == "High") ? "Watch" : "Current",
                    "The deputy operating queue combines filing escalations, prudential follow-up, and resilience remediation.",
                    "Sequence the queue by due date and confirm ownership for the highest-pressure actions."),
                Create("DPY-02", "Portfolio Concentration",
                    topConcentration is null ? "No segment view available" : $"{topConcentration.PriorityInstitutionCount} pressure institution(s) in {topConcentration.LicenceType}",
                    topConcentration?.Signal ?? "Current",
                    topConcentration?.Commentary ?? "No licence segment currently dominates the supervisory portfolio.",
                    topConcentration is null ? "Maintain the existing portfolio cadence." : $"Assign deeper review to the {topConcentration.LicenceType} segment."),
                Create("DPY-03", "Prudential Follow-Up",
                    $"{workspace.Capital.CapitalWatchlistCount} capital watchlist | {workspace.ModelRisk.DueValidationCount} due validations",
                    workspace.Capital.CapitalWatchlistCount > 0 || workspace.ModelRisk.DueValidationCount > 0 ? "Watch" : "Current",
                    "Capital watchlist cases and due model validations define the prudential oversight lane.",
                    "Pair capital follow-up with overdue validation challenge and evidence refresh."),
                Create("DPY-04", "Resilience & ICT",
                    $"{workspace.Resilience.OpenIncidentCount} incident(s) | {workspace.Resilience.BoardSummary.OverdueActions} overdue action(s)",
                    workspace.Resilience.BoardSummary.CriticalIssues > 0 ? "Critical" : workspace.Resilience.OpenIncidentCount > 0 ? "Watch" : "Current",
                    workspace.Resilience.BoardSummary.Narrative,
                    "Push overdue resilience actions to closure and confirm testing evidence for weak services."),
                Create("DPY-05", "Sanctions & STR",
                    $"{tfsPreview.PotentialMatches} pending | {criticalDrafts} critical STR draft(s)",
                    criticalDrafts > 0 ? "Critical" : tfsPreview.PotentialMatches > 0 ? "Watch" : "Current",
                    tfsPreview.Narrative,
                    "Close analyst reviews and escalate critical STR drafts through compliance governance.")
            ];
        }

        return
        [
            Create("DIR-01", "Execution Queue",
                $"{filteredInterventions.Count} queue item(s)",
                filteredInterventions.Any(x => x.Priority == "Critical") ? "Critical" : filteredInterventions.Any(x => x.Priority == "High") ? "Watch" : "Current",
                "The director / examiner queue emphasizes direct execution, remediation validation, and review closures.",
                "Start with critical queue items and validate evidence before downgrading priority."),
            Create("DIR-02", "Filing Blockers",
                $"{attentionRequiredFilingCount} attention-required filing(s)",
                workspace.KnowledgeGraph.InstitutionObligations.Any(x => x.Status == "Attention Required") ? "Critical" : workspace.KnowledgeGraph.InstitutionObligations.Any(x => x.Status == "Due Soon") ? "Watch" : "Current",
                "Blocked or attention-required returns are the main filing workload for examiner follow-up.",
                "Resolve validation blockers, confirm resubmission plans, and document examiner decisions."),
            Create("DIR-03", "Sanctions Reviews",
                $"{tfsPreview.PotentialMatches} pending review(s)",
                tfsPreview.ConfirmedMatches > 0 ? "Critical" : tfsPreview.PotentialMatches > 0 ? "Watch" : "Current",
                tfsPreview.Narrative,
                "Finalize analyst decisions and move confirmed hits into the STR queue."),
            Create("DIR-04", "Resilience Remediation",
                $"{openResilienceActionCount} open action(s)",
                workspace.Resilience.ActionTracker.Any(x => x.Status == "Overdue") ? "Critical" : workspace.Resilience.ActionTracker.Any(x => x.Status != "Complete") ? "Watch" : "Current",
                "Open resilience actions and recent incidents define the remediation lane for day-to-day supervisory execution.",
                "Demand evidence for overdue resilience actions and verify recovery-test outcomes."),
            Create("DIR-05", "Model Change Reviews",
                $"{workspace.ModelRisk.ChangeReviewCount} review item(s)",
                workspace.ModelRisk.ChangeReviewCount > 0 ? "Watch" : "Current",
                "Recent model-affecting changes require governance challenge before closure.",
                "Complete change review, backtesting challenge, and workflow updates for open model items.")
        ];
    }

    private static DashboardBriefingPackSectionInput Create(
        string sectionCode,
        string sectionName,
        string coverage,
        string signal,
        string commentary,
        string recommendedAction) =>
        new()
        {
            SectionCode = sectionCode,
            SectionName = sectionName,
            Coverage = coverage,
            Signal = signal,
            Commentary = commentary,
            RecommendedAction = recommendedAction
        };

    private static IReadOnlyList<InterventionQueueRow> FilterInterventions(
        PlatformIntelligenceWorkspace workspace,
        string lens,
        int? institutionId)
    {
        return lens switch
        {
            "governor" => workspace.Interventions.Take(12).ToList(),
            "deputy" => workspace.Interventions
                .Where(x => x.Domain is "Filing" or "Capital" or "Resilience")
                .Take(12)
                .ToList(),
            _ => workspace.Interventions
                .Where(x => x.Priority is "Critical" or "High" || x.Domain == "Model Risk")
                .Take(12)
                .ToList()
        };
    }

    private static IReadOnlyList<DashboardPortfolioConcentrationProjection> BuildPortfolioConcentrationRows(PlatformIntelligenceWorkspace workspace)
    {
        return workspace.InstitutionScorecards
            .GroupBy(x => x.LicenceType, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var priorityInstitutions = group.Count(x => x.Priority is "Critical" or "High");
                var overdue = group.Sum(x => x.OverdueObligations);
                var dueSoon = group.Sum(x => x.DueSoonObligations);
                var capitalWatchlist = group.Count(x => x.CapitalScore is not null && x.CapitalScore < 60m);
                var incidents = group.Sum(x => x.OpenResilienceIncidents);
                var modelReviews = group.Sum(x => x.ModelReviewItems);

                var signal = priorityInstitutions > 0 || overdue > 0
                    ? "Critical"
                    : dueSoon > 0 || capitalWatchlist > 0 || incidents > 0 || modelReviews > 0
                        ? "Watch"
                        : "Current";

                return new DashboardPortfolioConcentrationProjection(
                    group.Key,
                    priorityInstitutions,
                    signal,
                    $"{group.Key} carries {priorityInstitutions} high-pressure institution(s), {capitalWatchlist} capital watchlist case(s), and {modelReviews} model review item(s).");
            })
            .OrderByDescending(x => DashboardPriorityRank(x.Signal))
            .ThenByDescending(x => x.PriorityInstitutionCount)
            .ThenBy(x => x.LicenceType, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }

    private static DashboardTfsPreview BuildTfsPreview(
        SanctionsStoredScreeningRun? run,
        SanctionsWorkflowState? workflowState)
    {
        if (run is null)
        {
            return new DashboardTfsPreview();
        }

        var latestDecisionByKey = workflowState?.LatestDecisions.ToDictionary(
            x => x.MatchKey,
            x => x.Decision,
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var confirmedRows = run.Results
            .Where(x => ResolveDecision(x, latestDecisionByKey) == "Confirm Match")
            .ToList();
        var falsePositives = run.Results.Count(x => ResolveDecision(x, latestDecisionByKey) == "False Positive");
        var escalations = run.Results.Count(x => ResolveDecision(x, latestDecisionByKey) is "Review" or "Escalate");
        var reviewedPopulation = confirmedRows.Count + falsePositives;

        return new DashboardTfsPreview
        {
            MatchesFound = run.Results.Count(x => x.Disposition != "Clear"),
            PotentialMatches = escalations,
            ConfirmedMatches = confirmedRows.Count,
            FalsePositiveCount = falsePositives,
            Narrative = confirmedRows.Count > 0
                ? $"Confirmed hits remain across {confirmedRows.Count} screened subject(s); compliance escalation and TFS reporting should proceed."
                : escalations > 0
                    ? $"{escalations} screened subject(s) still require analyst review before the TFS return is finalized."
                    : "The current screening run does not contain confirmed sanctions hits."
        };
    }

    private static string ResolveDecision(
        SanctionsStoredScreeningResult row,
        IReadOnlyDictionary<string, string> latestDecisionByKey)
    {
        var matchKey = BuildMatchKey(row.Subject, row.SourceCode, row.MatchedName);
        if (latestDecisionByKey.TryGetValue(matchKey, out var decision))
        {
            return decision;
        }

        return row.Disposition switch
        {
            "True Match" => "Confirm Match",
            "Potential Match" => "Review",
            _ => "Clear"
        };
    }

    private static string BuildMatchKey(string subject, string sourceCode, string matchedName) =>
        $"{NormalizeKeyPart(subject)}|{NormalizeKeyPart(sourceCode)}|{NormalizeKeyPart(matchedName)}";

    private static string NormalizeKeyPart(string value) =>
        new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string BestCapitalActionHeadline(PlatformIntelligenceWorkspace workspace)
    {
        var topCapitalPackAttention = workspace.Capital.ReturnPack.FirstOrDefault(x => x.Signal is "Critical" or "Watch");
        if (topCapitalPackAttention is not null)
        {
            return $"{topCapitalPackAttention.SectionName} is currently {topCapitalPackAttention.Signal.ToLowerInvariant()}; {topCapitalPackAttention.RecommendedAction}";
        }

        return workspace.Capital.CapitalWatchlistCount == 0
            ? "No institution currently requires capital action escalation."
            : $"{workspace.Capital.CapitalWatchlistCount} institution(s) remain on the capital watchlist and should be scenario-tested for buffer protection.";
    }

    private static int DashboardPriorityRank(string value) => value.ToLowerInvariant() switch
    {
        "critical" => 3,
        "high" => 2,
        "watch" => 2,
        "medium" => 1,
        "priority" => 1,
        _ => 0
    };

    private sealed record DashboardPortfolioConcentrationProjection(
        string LicenceType,
        int PriorityInstitutionCount,
        string Signal,
        string Commentary);

    private sealed class DashboardTfsPreview
    {
        public int MatchesFound { get; set; }
        public int PotentialMatches { get; set; }
        public int ConfirmedMatches { get; set; }
        public int FalsePositiveCount { get; set; }
        public string Narrative { get; set; } = "The current screening run does not contain confirmed sanctions hits.";
    }
}
