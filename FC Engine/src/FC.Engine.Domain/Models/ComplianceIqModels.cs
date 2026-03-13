namespace FC.Engine.Domain.Models;

public sealed class ComplianceIqQueryRequest
{
    public string Query { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public bool IsRegulatorContext { get; set; }
    public string? RegulatorCode { get; set; }
    public Guid? ConversationId { get; set; }
}

public sealed class ComplianceIqQueryResponse
{
    public Guid ConversationId { get; set; }
    public int? TurnId { get; set; }
    public string Answer { get; set; } = string.Empty;
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public string VisualizationType { get; set; } = "text";
    public string ConfidenceLevel { get; set; } = "HIGH";
    public string IntentCode { get; set; } = string.Empty;
    public List<ComplianceIqCitation> Citations { get; set; } = new();
    public List<string> FollowUpSuggestions { get; set; } = new();
    public int TotalTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ConfidenceCssClass => ConfidenceLevel switch
    {
        "HIGH" => "badge bg-success",
        "MEDIUM" => "badge bg-warning text-dark",
        "LOW" => "badge bg-danger",
        _ => "badge bg-secondary"
    };
}

public sealed class ComplianceIqCitation
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceModule { get; set; } = string.Empty;
    public string SourceField { get; set; } = string.Empty;
    public string SourcePeriod { get; set; } = string.Empty;
    public string? InstitutionName { get; set; }
}

public sealed class ComplianceIqIntentClassification
{
    public string IntentCode { get; set; } = "UNCLEAR";
    public decimal Confidence { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}

public sealed class ComplianceIqExtractedEntities
{
    public List<string> FieldCodes { get; set; } = new();
    public List<string> FieldNames { get; set; } = new();
    public string? ModuleCode { get; set; }
    public string? RegulatorCode { get; set; }
    public string? PeriodCode { get; set; }
    public string? ComparisonPeriodCode { get; set; }
    public int PeriodCount { get; set; } = 8;
    public string? LicenceCategory { get; set; }
    public string? SearchKeyword { get; set; }
    public string? CircularReference { get; set; }
    public decimal? ScenarioMultiplier { get; set; }
    public List<string> EntityNames { get; set; } = new();
    public int RequestedTopCount { get; set; } = 10;
    public bool WantsOverdueItems { get; set; }
}

public sealed class ComplianceIqQueryPlan
{
    public string IntentCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string VisualizationType { get; set; } = "text";
    public string ResultFormat { get; set; } = "TABLE";
    public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Explanation { get; set; } = string.Empty;
}

public sealed class ComplianceIqConversationTurnView
{
    public int TurnId { get; set; }
    public Guid ConversationId { get; set; }
    public int TurnNumber { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string ResponseText { get; set; } = string.Empty;
    public string IntentCode { get; set; } = string.Empty;
    public string ConfidenceLevel { get; set; } = "HIGH";
    public string VisualizationType { get; set; } = "text";
    public DateTime CreatedAt { get; set; }
    public int TotalTimeMs { get; set; }
}

public sealed class ComplianceIqHistoryEntry
{
    public int TurnId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string IntentCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string ConfidenceLevel { get; set; } = "HIGH";
    public DateTime CreatedAt { get; set; }
    public int TotalTimeMs { get; set; }
}

public sealed class ComplianceIqQuickQuestionView
{
    public int Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconClass { get; set; } = "bi-question-circle";
    public bool RequiresRegulatorContext { get; set; }
}

public sealed class ComplianceIqTemplateCatalogItem
{
    public int Id { get; set; }
    public string IntentCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VisualizationType { get; set; } = "text";
    public bool RequiresRegulatorContext { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ComplianceIqRateLimitResult
{
    public string UserId { get; set; } = string.Empty;
    public bool IsExceeded { get; set; }
    public string? ExceededWindow { get; set; }
    public int? RetryAfterSeconds { get; set; }
}
