namespace FC.Engine.Domain.Models;

public enum RcaIncidentType
{
    SecurityAlert = 0,
    PipelineFailure = 1,
    QualityIssue = 2
}

public sealed class AnalyzeRootCauseRequest
{
    public string Type { get; set; } = string.Empty;
    public Guid IncidentId { get; set; }
    public bool ForceRefresh { get; set; }
}

public sealed class RootCauseAnalysis
{
    public string IncidentType { get; set; } = string.Empty;
    public Guid IncidentId { get; set; }
    public string RootCauseType { get; set; } = string.Empty;
    public string RootCauseSummary { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string ModelName { get; set; } = "platform-rca-engine";
    public string ModelType { get; set; } = "rule_based";
    public string ExplainabilityMode { get; set; } = "rule_trace";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<RcaTimelineEntry> Timeline { get; set; } = [];
    public List<CausalStep> CausalChain { get; set; } = [];
    public ImpactAssessment Impact { get; set; } = new();
    public List<RecommendationAction> Recommendations { get; set; } = [];
}

public sealed class RcaTimelineEntry
{
    public string Source { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? SourceIp { get; set; }
    public string? MitreTechnique { get; set; }
    public string? KillChainPhase { get; set; }
    public Guid? AssetId { get; set; }
    public DateTime OccurredAt { get; set; }
}

public sealed class CausalStep
{
    public int StepNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EvidenceSource { get; set; } = string.Empty;
    public string EvidenceId { get; set; } = string.Empty;
    public string? Correlation { get; set; }
    public string? MitreTechnique { get; set; }
    public string? KillChainPhase { get; set; }
    public Guid? AssetId { get; set; }
    public string? UserId { get; set; }
    public string? SourceIp { get; set; }
    public DateTime OccurredAt { get; set; }
}

public sealed class ImpactAssessment
{
    public int DirectAssetCount { get; set; }
    public int DependentAssetCount { get; set; }
    public int TotalAffectedAssets { get; set; }
    public List<string> AffectedAssets { get; set; } = [];
    public List<string> DataAtRisk { get; set; } = [];
    public List<string> UsersAtRisk { get; set; } = [];
    public string BusinessImpact { get; set; } = string.Empty;
}

public sealed class RecommendationAction
{
    public string Priority { get; set; } = "medium";
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
