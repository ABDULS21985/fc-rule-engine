namespace FC.Engine.Domain.Entities;

public class RegIqConfig
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

public class RegIqConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RegulatorTenantId { get; set; }
    public string RegulatorId { get; set; } = string.Empty;
    public string RegulatorRole { get; set; } = string.Empty;
    public string RegulatorAgency { get; set; } = string.Empty;
    public string ClassificationLevel { get; set; } = "RESTRICTED";
    public string Scope { get; set; } = "SECTOR_WIDE";
    public string Title { get; set; } = "RegulatorIQ conversation";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public int TurnCount { get; set; }
    public bool IsActive { get; set; } = true;

    public List<RegIqTurn> Turns { get; set; } = new();
}

public class RegIqTurn
{
    public int Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid RegulatorTenantId { get; set; }
    public string RegulatorId { get; set; } = string.Empty;
    public string RegulatorRole { get; set; } = string.Empty;
    public int TurnNumber { get; set; }
    public string QueryText { get; set; } = string.Empty;
    public string IntentCode { get; set; } = "HELP";
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
    public string EntitiesQueriedJson { get; set; } = "[]";
    public string DataSourcesAccessedJson { get; set; } = "[]";
    public string ClassificationLevel { get; set; } = "RESTRICTED";
    public string? RegulatorAgencyFilterApplied { get; set; }
    public Guid? PrimaryEntityTenantId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public RegIqConversation? Conversation { get; set; }
}

public class RegIqIntent
{
    public int Id { get; set; }
    public string IntentCode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExampleQuery { get; set; } = string.Empty;
    public string PrimaryDataSource { get; set; } = string.Empty;
    public bool RequiresRegulatorContext { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RegIqQueryTemplate
{
    public int Id { get; set; }
    public string IntentCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SqlTemplate { get; set; } = string.Empty;
    public string ParameterSchema { get; set; } = "{}";
    public string ResultFormat { get; set; } = "TABLE";
    public string VisualizationType { get; set; } = "text";
    public string Scope { get; set; } = "SECTOR_WIDE";
    public string ClassificationLevel { get; set; } = "RESTRICTED";
    public string DataSourcesJson { get; set; } = "[]";
    public bool CrossTenantEnabled { get; set; } = true;
    public bool RequiresEntityContext { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RegIqEntityAlias
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string NormalizedAlias { get; set; } = string.Empty;
    public string AliasType { get; set; } = "NAME";
    public string LicenceCategory { get; set; } = string.Empty;
    public string RegulatorAgency { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string? HoldingCompanyName { get; set; }
    public string? GeoTag { get; set; }
    public int MatchPriority { get; set; } = 100;
    public bool IsPrimary { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RegIqAccessLog
{
    public long Id { get; set; }
    public Guid RegulatorTenantId { get; set; }
    public Guid? ConversationId { get; set; }
    public int? TurnId { get; set; }
    public string RegulatorId { get; set; } = string.Empty;
    public string RegulatorAgency { get; set; } = string.Empty;
    public string RegulatorRole { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string ResponseSummary { get; set; } = string.Empty;
    public string ClassificationLevel { get; set; } = "RESTRICTED";
    public string EntitiesAccessedJson { get; set; } = "[]";
    public Guid? PrimaryEntityTenantId { get; set; }
    public string DataSourcesAccessedJson { get; set; } = "[]";
    public string? FilterContextJson { get; set; }
    public string? IpAddress { get; set; }
    public string? SessionId { get; set; }
    public DateTime AccessedAt { get; set; } = DateTime.UtcNow;
    public DateTime RetainUntil { get; set; } = DateTime.UtcNow.AddYears(7);
}
