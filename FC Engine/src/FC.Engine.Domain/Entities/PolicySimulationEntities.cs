using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class PolicyScenario
{
    public long Id { get; set; }
    public int RegulatorId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PolicyDomain PolicyDomain { get; set; }
    public string TargetEntityTypes { get; set; } = "ALL";
    public DateOnly BaselineDate { get; set; }
    public PolicyStatus Status { get; set; } = PolicyStatus.Draft;
    public int Version { get; set; } = 1;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<PolicyParameter> Parameters { get; set; } = new();
    public List<ImpactAssessmentRun> ImpactRuns { get; set; } = new();
    public List<ConsultationRound> Consultations { get; set; } = new();
    public PolicyDecision? Decision { get; set; }
}

public class PolicyParameter
{
    public long Id { get; set; }
    public long ScenarioId { get; set; }
    public string ParameterCode { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal ProposedValue { get; set; }
    public ParameterUnit Unit { get; set; }
    public string ApplicableEntityTypes { get; set; } = "ALL";
    public string? ReturnLineReference { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PolicyScenario? Scenario { get; set; }
}

public class PolicyParameterPreset
{
    public int Id { get; set; }
    public string ParameterCode { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public PolicyDomain PolicyDomain { get; set; }
    public decimal CurrentBaseline { get; set; }
    public ParameterUnit Unit { get; set; }
    public string? ReturnLineReference { get; set; }
    public string? Description { get; set; }
    public string RegulatorCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class ImpactAssessmentRun
{
    public long Id { get; set; }
    public long ScenarioId { get; set; }
    public int RegulatorId { get; set; }
    public int RunNumber { get; set; }
    public ImpactRunStatus Status { get; set; } = ImpactRunStatus.Pending;
    public DateOnly SnapshotDate { get; set; }
    public int TotalEntitiesEvaluated { get; set; }
    public int EntitiesCurrentlyCompliant { get; set; }
    public int EntitiesWouldBreach { get; set; }
    public int EntitiesAlreadyBreaching { get; set; }
    public int EntitiesNotAffected { get; set; }
    public decimal? AggregateCapitalShortfall { get; set; }
    public decimal? AggregateComplianceCost { get; set; }
    public Guid CorrelationId { get; set; }
    public long? ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PolicyScenario? Scenario { get; set; }
    public List<EntityImpactResult> EntityResults { get; set; } = new();
    public CostBenefitAnalysis? CostBenefitAnalysis { get; set; }
}

public class EntityImpactResult
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public ImpactCategory ImpactCategory { get; set; }
    public string ParameterResults { get; set; } = "[]";
    public decimal? CurrentMetricValue { get; set; }
    public decimal? ProposedThreshold { get; set; }
    public decimal? GapToCompliance { get; set; }
    public decimal? EstimatedComplianceCost { get; set; }
    public decimal? RiskScore { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ImpactAssessmentRun? Run { get; set; }
}

public class CostBenefitAnalysis
{
    public long Id { get; set; }
    public long ScenarioId { get; set; }
    public long RunId { get; set; }
    public decimal TotalIndustryComplianceCost { get; set; }
    public decimal CostToSmallEntities { get; set; }
    public decimal CostToMediumEntities { get; set; }
    public decimal CostToLargeEntities { get; set; }
    public decimal? SectorCARImprovement { get; set; }
    public decimal? SectorLCRImprovement { get; set; }
    public decimal? EstimatedRiskReduction { get; set; }
    public decimal? EstimatedDepositProtection { get; set; }
    public string ImmediateImpactSummary { get; set; } = "{}";
    public string PhaseIn12MonthSummary { get; set; } = "{}";
    public string PhaseIn24MonthSummary { get; set; } = "{}";
    public decimal? NetBenefitScore { get; set; }
    public string? Recommendation { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PolicyScenario? Scenario { get; set; }
    public ImpactAssessmentRun? Run { get; set; }
}

public class ConsultationRound
{
    public long Id { get; set; }
    public long ScenarioId { get; set; }
    public int RegulatorId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CoverNote { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateOnly DeadlineDate { get; set; }
    public ConsultationStatus Status { get; set; } = ConsultationStatus.Draft;
    public string TargetEntityTypes { get; set; } = "ALL";
    public int TotalFeedbackReceived { get; set; }
    public DateTime? AggregationCompletedAt { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public PolicyScenario? Scenario { get; set; }
    public List<ConsultationProvision> Provisions { get; set; } = new();
    public List<ConsultationFeedback> Feedback { get; set; } = new();
}

public class ConsultationProvision
{
    public long Id { get; set; }
    public long ConsultationId { get; set; }
    public int ProvisionNumber { get; set; }
    public string ProvisionTitle { get; set; } = string.Empty;
    public string ProvisionText { get; set; } = string.Empty;
    public string? RelatedParameterCode { get; set; }
    public int DisplayOrder { get; set; }

    public ConsultationRound? Consultation { get; set; }
    public List<ProvisionFeedbackEntry> ProvisionFeedback { get; set; } = new();
    public FeedbackAggregation? Aggregation { get; set; }
}

public class ConsultationFeedback
{
    public long Id { get; set; }
    public long ConsultationId { get; set; }
    public int InstitutionId { get; set; }
    public string InstitutionCode { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int SubmittedByUserId { get; set; }
    public FeedbackPosition OverallPosition { get; set; }
    public string? GeneralComments { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public bool IsAnonymised { get; set; }

    public ConsultationRound? Consultation { get; set; }
    public List<ProvisionFeedbackEntry> ProvisionFeedback { get; set; } = new();
}

public class ProvisionFeedbackEntry
{
    public long Id { get; set; }
    public long FeedbackId { get; set; }
    public long ProvisionId { get; set; }
    public ProvisionPosition Position { get; set; }
    public string? Reasoning { get; set; }
    public string? SuggestedAmendment { get; set; }
    public string? ImpactAssessment { get; set; }

    public ConsultationFeedback? Feedback { get; set; }
    public ConsultationProvision? Provision { get; set; }
}

public class FeedbackAggregation
{
    public long Id { get; set; }
    public long ConsultationId { get; set; }
    public long ProvisionId { get; set; }
    public int TotalResponses { get; set; }
    public int SupportCount { get; set; }
    public int OpposeCount { get; set; }
    public int NeutralCount { get; set; }
    public int AmendCount { get; set; }
    public decimal SupportPercentage { get; set; }
    public decimal OpposePercentage { get; set; }
    public string ByEntityType { get; set; } = "{}";
    public string? TopConcerns { get; set; }
    public string? TopSuggestedAmendments { get; set; }
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;

    public ConsultationRound? Consultation { get; set; }
    public ConsultationProvision? Provision { get; set; }
}

public class PolicyDecision
{
    public long Id { get; set; }
    public long ScenarioId { get; set; }
    public int RegulatorId { get; set; }
    public DecisionType DecisionType { get; set; }
    public string DecisionSummary { get; set; } = string.Empty;
    public string FinalParametersJson { get; set; } = "{}";
    public DateOnly? EffectiveDate { get; set; }
    public int? PhaseInMonths { get; set; }
    public string? CircularReference { get; set; }
    public string? DocumentBlobPath { get; set; }
    public int DecidedByUserId { get; set; }
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;

    public PolicyScenario? Scenario { get; set; }
    public List<HistoricalImpactTracking> TrackingEntries { get; set; } = new();
}

public class HistoricalImpactTracking
{
    public long Id { get; set; }
    public long DecisionId { get; set; }
    public long ScenarioId { get; set; }
    public DateOnly TrackingDate { get; set; }
    public int MonthsSinceEnactment { get; set; }
    public int PredictedBreachCount { get; set; }
    public decimal? PredictedCapitalShortfall { get; set; }
    public decimal? PredictedComplianceCost { get; set; }
    public int ActualBreachCount { get; set; }
    public decimal? ActualCapitalShortfall { get; set; }
    public decimal? ActualComplianceCost { get; set; }
    public decimal? BreachCountVariance { get; set; }
    public decimal? ShortfallVariance { get; set; }
    public decimal? AccuracyScore { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PolicyDecision? Decision { get; set; }
}

public class PolicyAuditLog
{
    public long Id { get; set; }
    public long? ScenarioId { get; set; }
    public int RegulatorId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int PerformedByUserId { get; set; }
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
}
