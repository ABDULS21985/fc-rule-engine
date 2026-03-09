namespace FC.Engine.Domain.Entities;

public class SecurityAlert
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AffectedAssetIdsJson { get; set; } = "[]";
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? SourceIp { get; set; }
    public string? MitreTechnique { get; set; }
    public string Status { get; set; } = "open";
    public string? EvidenceJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SecurityEvent
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string EventSource { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public Guid? AlertId { get; set; }
    public Guid? AssetId { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? SourceIp { get; set; }
    public string? MitreTechnique { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }
    public string? EvidenceJson { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class RootCauseAnalysisRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string IncidentType { get; set; } = string.Empty;
    public Guid IncidentId { get; set; }
    public string RootCauseType { get; set; } = string.Empty;
    public string RootCauseSummary { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public string TimelineJson { get; set; } = "[]";
    public string CausalChainJson { get; set; } = "[]";
    public string ImpactJson { get; set; } = "{}";
    public string RecommendationsJson { get; set; } = "[]";
    public string ModelName { get; set; } = "platform-rca-engine";
    public string ModelType { get; set; } = "rule_based";
    public string ExplainabilityMode { get; set; } = "rule_trace";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
