namespace FC.Engine.Admin.Services.Scenarios;

// ── Enums ──────────────────────────────────────────────────────

public enum ShockType { Absolute, Relative, Override }
public enum BreachSeverity { None, Warning, Breach, Critical }
public enum ScenarioStatus { Draft, Running, Completed, Failed }
public enum ScenarioScope { Single, Comparison, MacroPrudential }

// ── Input Models ───────────────────────────────────────────────

public record FieldOverride(
    string ReturnCode,
    string FieldName,
    string SectionName,
    ShockType ShockType,
    decimal ShockValue,
    string? Description = null
);

public record MacroShock(
    string Name,
    string Description,
    List<FieldOverride> Overrides
);

public class ScenarioDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? TemplateId { get; set; }
    public ScenarioStatus Status { get; set; } = ScenarioStatus.Draft;
    public ScenarioScope Scope { get; set; } = ScenarioScope.Single;
    public List<FieldOverride> Overrides { get; set; } = [];
    public List<MacroShock> MacroShocks { get; set; } = [];
    public List<string> AffectedModules { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

// ── Result Models ──────────────────────────────────────────────

public record MetricResult(
    string MetricName,
    string ReturnCode,
    string FieldName,
    decimal BaselineValue,
    decimal ScenarioValue,
    decimal Delta,
    decimal DeltaPct,
    decimal? Threshold,
    BreachSeverity Breach
);

public record BreachAlert(
    string MetricName,
    string ReturnCode,
    string FieldName,
    decimal BaselineValue,
    decimal ScenarioValue,
    decimal Threshold,
    BreachSeverity Severity,
    string Message
);

public record ProFormaField(
    string FieldName,
    string DisplayName,
    string SectionName,
    string DataType,
    object? BaselineValue,
    object? ScenarioValue,
    bool IsOverridden,
    bool IsComputed,
    BreachSeverity Breach
);

public class ScenarioResult
{
    public int ScenarioId { get; set; }
    public string ScenarioName { get; set; } = "";
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public long DurationMs { get; set; }
    public List<MetricResult> KeyMetrics { get; set; } = [];
    public List<BreachAlert> Breaches { get; set; } = [];
    public List<ProFormaField> ProFormaFields { get; set; } = [];
    public int TotalFieldsAffected { get; set; }
    public int FormulasRecomputed { get; set; }
    public int ValidationErrors { get; set; }
    public int ValidationWarnings { get; set; }
}

public class ComparisonReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<ScenarioResult> Scenarios { get; set; } = [];
    public List<string> SharedMetricNames { get; set; } = [];
}

// ── Macro-Prudential ───────────────────────────────────────────

public record InstitutionImpact(
    int InstitutionId,
    string InstitutionName,
    string InstitutionType,
    bool IsSystemicallyImportant,
    List<MetricResult> Metrics,
    List<BreachAlert> Breaches,
    BreachSeverity WorstBreach
);

public class MacroPrudentialResult
{
    public int ScenarioId { get; set; }
    public string ShockName { get; set; } = "";
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public int TotalInstitutions { get; set; }
    public int InstitutionsBreaching { get; set; }
    public int CriticalBreaches { get; set; }
    public List<InstitutionImpact> Impacts { get; set; } = [];
    public Dictionary<string, SectorAggregate> SectorAggregates { get; set; } = new();
}

public record SectorAggregate(
    string MetricName,
    decimal SectorAvgBaseline,
    decimal SectorAvgScenario,
    decimal WorstCase,
    string WorstCaseInstitution,
    int CountBreaching
);

// ── Pre-Built Template ─────────────────────────────────────────

public class ScenarioTemplate
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string IconSvg { get; set; } = "";
    public List<string> AffectedModules { get; set; } = [];
    public List<FieldOverride> DefaultOverrides { get; set; } = [];
    public List<MacroShock> DefaultShocks { get; set; } = [];
}
