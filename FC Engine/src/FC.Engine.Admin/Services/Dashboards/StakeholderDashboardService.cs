using FC.Engine.Admin.Services;

namespace FC.Engine.Admin.Services.Dashboards;

/// <summary>
/// Aggregates data from PlatformIntelligenceService into role-specific stakeholder views.
/// All data flows through real DB-backed services.
/// </summary>
public sealed class StakeholderDashboardService
{
    private readonly PlatformIntelligenceService _intelligence;

    public StakeholderDashboardService(PlatformIntelligenceService intelligence)
    {
        _intelligence = intelligence;
    }

    public async Task<GovernorDashboardData> GetGovernorDashboardAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var scorecards = ws.InstitutionScorecards;
        var interventions = ws.Interventions;
        var capital = ws.Capital;

        var totalInstitutions = scorecards.Count;
        var avgCapitalScore = scorecards.Where(s => s.CapitalScore.HasValue).Select(s => s.CapitalScore!.Value).DefaultIfEmpty(0).Average();
        var complianceRate = totalInstitutions > 0
            ? (decimal)(scorecards.Count(s => s.OverdueObligations == 0)) / totalInstitutions * 100
            : 0m;

        var priorityDistribution = scorecards
            .GroupBy(s => s.Priority)
            .Select(g => new LabelCount { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var licenceDistribution = scorecards
            .GroupBy(s => s.LicenceType)
            .Select(g => new LabelCount { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        return new GovernorDashboardData
        {
            TotalInstitutions = totalInstitutions,
            AverageCapitalScore = Math.Round(avgCapitalScore, 1),
            ComplianceRatePercent = Math.Round(complianceRate, 1),
            OpenWarnings = interventions.Count(i => i.Signal is "Alert" or "Critical"),
            CapitalWatchlistCount = capital.CapitalWatchlistCount,
            ActiveInterventions = interventions.Count,
            OpenResilienceIncidents = scorecards.Sum(s => s.OpenResilienceIncidents),
            ModelsUnderReview = scorecards.Sum(s => s.ModelReviewItems),
            PriorityDistribution = priorityDistribution,
            LicenceDistribution = licenceDistribution,
            TopRiskInstitutions = scorecards
                .OrderByDescending(s => s.OverdueObligations + s.OpenResilienceIncidents + s.OpenSecurityAlerts)
                .Take(10)
                .ToList(),
            RecentActivity = ws.ActivityTimeline
                .OrderByDescending(a => a.HappenedAt)
                .Take(20)
                .ToList()
        };
    }

    public async Task<DeputyGovernorDashboardData> GetDeputyGovernorDashboardAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var scorecards = ws.InstitutionScorecards;
        var interventions = ws.Interventions;

        var totalIssues = scorecards.Sum(s => s.OverdueObligations + s.OpenResilienceIncidents + s.OpenSecurityAlerts);
        var escalations = interventions.Count(i => i.Priority is "Critical" or "High");
        var resolved = interventions.Count(i => i.Signal == "Resolved");
        var remediationRate = interventions.Count > 0
            ? Math.Round((decimal)resolved / interventions.Count * 100, 1)
            : 0m;

        var domainDistribution = interventions
            .GroupBy(i => i.Domain)
            .Select(g => new LabelCount { Label = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        return new DeputyGovernorDashboardData
        {
            PortfolioSize = scorecards.Count,
            UnresolvedIssues = totalIssues,
            Escalations = escalations,
            RemediationRatePercent = remediationRate,
            DomainDistribution = domainDistribution,
            InterventionQueue = interventions
                .OrderBy(i => i.DueDate)
                .Take(20)
                .ToList(),
            InstitutionScorecards = scorecards
                .OrderByDescending(s => s.OverdueObligations)
                .Take(15)
                .ToList()
        };
    }

    public async Task<ExaminerDashboardData> GetExaminerDashboardAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var scorecards = ws.InstitutionScorecards;
        var interventions = ws.Interventions;

        var overdueReviews = scorecards.Sum(s => s.OverdueObligations);
        var upcomingDeadlines = interventions.Count(i => i.DueDate <= DateTime.UtcNow.AddDays(30));

        return new ExaminerDashboardData
        {
            AssignedInstitutions = scorecards.Count,
            OverdueReviews = overdueReviews,
            PendingExams = interventions.Count(i => i.NextAction.Contains("Exam", StringComparison.OrdinalIgnoreCase)
                                                   || i.NextAction.Contains("Review", StringComparison.OrdinalIgnoreCase)),
            UpcomingDeadlines = upcomingDeadlines,
            OverdueItems = scorecards
                .Where(s => s.OverdueObligations > 0)
                .OrderByDescending(s => s.OverdueObligations)
                .ToList(),
            ExamQueue = interventions
                .OrderBy(i => i.DueDate)
                .Take(20)
                .ToList(),
            RecentActivity = ws.ActivityTimeline
                .OrderByDescending(a => a.HappenedAt)
                .Take(15)
                .ToList()
        };
    }

    public async Task<ExecutiveDashboardData> GetExecutiveDashboardAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var scorecards = ws.InstitutionScorecards;
        var interventions = ws.Interventions;
        var kg = ws.KnowledgeGraph;

        var totalObligations = kg.InstitutionObligations.Count;
        var filedCount = kg.InstitutionObligations.Count(o => o.Status is "Filed" or "Current");
        var filingComplianceRate = totalObligations > 0
            ? Math.Round((decimal)filedCount / totalObligations * 100, 1)
            : 0m;

        var openFindings = scorecards.Sum(s => s.OverdueObligations + s.OpenResilienceIncidents);

        var domainSummary = new List<LabelCount>();
        domainSummary.Add(new LabelCount { Label = "Compliance", Count = kg.InstitutionObligations.Count(o => o.Status is "Overdue" or "Due Soon") });
        domainSummary.Add(new LabelCount { Label = "Capital", Count = ws.Capital.CapitalWatchlistCount });
        domainSummary.Add(new LabelCount { Label = "Resilience", Count = scorecards.Sum(s => s.OpenResilienceIncidents) });
        domainSummary.Add(new LabelCount { Label = "Model Risk", Count = scorecards.Sum(s => s.ModelReviewItems) });
        domainSummary.Add(new LabelCount { Label = "Sanctions", Count = scorecards.Sum(s => s.OpenSecurityAlerts) });

        return new ExecutiveDashboardData
        {
            FilingCompliancePercent = filingComplianceRate,
            DaysToNextDeadline = interventions
                .Where(i => i.DueDate > DateTime.UtcNow)
                .Select(i => (int)(i.DueDate - DateTime.UtcNow).TotalDays)
                .DefaultIfEmpty(0)
                .Min(),
            OpenFindings = openFindings,
            ActiveInterventions = interventions.Count,
            DomainSummary = domainSummary,
            TopInstitutions = scorecards
                .OrderByDescending(s => s.OverdueObligations + s.DueSoonObligations)
                .Take(10)
                .ToList(),
            RecentActivity = ws.ActivityTimeline
                .OrderByDescending(a => a.HappenedAt)
                .Take(10)
                .ToList()
        };
    }
}

public sealed class LabelCount
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class GovernorDashboardData
{
    public int TotalInstitutions { get; set; }
    public decimal AverageCapitalScore { get; set; }
    public decimal ComplianceRatePercent { get; set; }
    public int OpenWarnings { get; set; }
    public int CapitalWatchlistCount { get; set; }
    public int ActiveInterventions { get; set; }
    public int OpenResilienceIncidents { get; set; }
    public int ModelsUnderReview { get; set; }
    public List<LabelCount> PriorityDistribution { get; set; } = [];
    public List<LabelCount> LicenceDistribution { get; set; } = [];
    public List<InstitutionScorecardRow> TopRiskInstitutions { get; set; } = [];
    public List<ActivityTimelineRow> RecentActivity { get; set; } = [];
}

public sealed class DeputyGovernorDashboardData
{
    public int PortfolioSize { get; set; }
    public int UnresolvedIssues { get; set; }
    public int Escalations { get; set; }
    public decimal RemediationRatePercent { get; set; }
    public List<LabelCount> DomainDistribution { get; set; } = [];
    public List<InterventionQueueRow> InterventionQueue { get; set; } = [];
    public List<InstitutionScorecardRow> InstitutionScorecards { get; set; } = [];
}

public sealed class ExaminerDashboardData
{
    public int AssignedInstitutions { get; set; }
    public int OverdueReviews { get; set; }
    public int PendingExams { get; set; }
    public int UpcomingDeadlines { get; set; }
    public List<InstitutionScorecardRow> OverdueItems { get; set; } = [];
    public List<InterventionQueueRow> ExamQueue { get; set; } = [];
    public List<ActivityTimelineRow> RecentActivity { get; set; } = [];
}

public sealed class ExecutiveDashboardData
{
    public decimal FilingCompliancePercent { get; set; }
    public int DaysToNextDeadline { get; set; }
    public int OpenFindings { get; set; }
    public int ActiveInterventions { get; set; }
    public List<LabelCount> DomainSummary { get; set; } = [];
    public List<InstitutionScorecardRow> TopInstitutions { get; set; } = [];
    public List<ActivityTimelineRow> RecentActivity { get; set; } = [];
}
