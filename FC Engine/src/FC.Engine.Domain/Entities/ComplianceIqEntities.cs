namespace FC.Engine.Domain.Entities;

public class ComplianceIqConfig
{
    public int Id { get; set; }
    public string ConfigKey { get; set; } = string.Empty;
    public string ConfigValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveTo { get; set; }
    public string CreatedBy { get; set; } = "SYSTEM";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ComplianceIqIntent
{
    public int Id { get; set; }
    public string IntentCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresRegulatorContext { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ComplianceIqTemplate
{
    public int Id { get; set; }
    public string IntentCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TemplateBody { get; set; } = string.Empty;
    public string ParameterSchema { get; set; } = "{}";
    public string ResultFormat { get; set; } = "TABLE";
    public string VisualizationType { get; set; } = "text";
    public bool RequiresRegulatorContext { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ComplianceIqFieldSynonym
{
    public int Id { get; set; }
    public string Synonym { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public string? ModuleCode { get; set; }
    public string? RegulatorCode { get; set; }
    public decimal ConfidenceBoost { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ComplianceIqConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public bool IsRegulatorContext { get; set; }
    public string Title { get; set; } = "ComplianceIQ conversation";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public int TurnCount { get; set; }
    public bool IsActive { get; set; } = true;

    public List<ComplianceIqTurn> Turns { get; set; } = new();
}

public class ComplianceIqTurn
{
    public int Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid TenantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public int TurnNumber { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string IntentCode { get; set; } = "UNCLEAR";
    public decimal IntentConfidence { get; set; }
    public string ExtractedEntitiesJson { get; set; } = "{}";
    public string TemplateCode { get; set; } = string.Empty;
    public string ResolvedParametersJson { get; set; } = "{}";
    public string ExecutedPlan { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int ExecutionTimeMs { get; set; }
    public string ResponseText { get; set; } = string.Empty;
    public string ResponseDataJson { get; set; } = "[]";
    public string VisualizationType { get; set; } = "text";
    public string ConfidenceLevel { get; set; } = "HIGH";
    public string CitationsJson { get; set; } = "[]";
    public string FollowUpSuggestionsJson { get; set; } = "[]";
    public int TotalTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ComplianceIqConversation? Conversation { get; set; }
    public List<ComplianceIqFeedback> Feedback { get; set; } = new();
}

public class ComplianceIqQuickQuestion
{
    public int Id { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconClass { get; set; } = "bi-question-circle";
    public bool RequiresRegulatorContext { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class ComplianceIqFeedback
{
    public int Id { get; set; }
    public int TurnId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public short Rating { get; set; }
    public string? FeedbackText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ComplianceIqTurn? Turn { get; set; }
}
