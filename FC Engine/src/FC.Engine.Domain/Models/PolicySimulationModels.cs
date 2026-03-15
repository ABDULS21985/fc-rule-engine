using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Models;

// ── Value Objects ──────────────────────────────────────────────────────

public sealed record PolicyParameterChange(
    string ParameterCode,
    string ParameterName,
    decimal CurrentValue,
    decimal ProposedValue,
    ParameterUnit Unit,
    string? ReturnLineReference
);

public sealed record EntityImpactDetail(
    string ParameterCode,
    decimal CurrentEntityValue,
    decimal CurrentThreshold,
    decimal ProposedThreshold,
    decimal Gap,
    string Status
);

public sealed record PhaseInScenario(
    string Label,
    int TransitionMonths,
    int EntitiesInBreach,
    decimal CapitalShortfall,
    decimal EstimatedComplianceCost,
    decimal InterimThreshold
);

public sealed record FeedbackAggregateByProvision(
    long ProvisionId,
    int ProvisionNumber,
    string ProvisionTitle,
    int TotalResponses,
    int SupportCount,
    int OpposeCount,
    int NeutralCount,
    int AmendCount,
    decimal SupportPercentage,
    decimal OpposePercentage,
    Dictionary<string, EntityTypeBreakdown> ByEntityType,
    IReadOnlyList<string> TopConcerns,
    IReadOnlyList<string> TopSuggestedAmendments
);

public sealed record EntityTypeBreakdown(
    int Support, int Oppose, int Neutral, int Amend, int Total
);

public sealed record PredictedVsActual(
    DateOnly TrackingDate,
    int MonthsSinceEnactment,
    int PredictedBreachCount,
    int ActualBreachCount,
    decimal? PredictedShortfall,
    decimal? ActualShortfall,
    decimal? AccuracyScore
);

// ── Service Result Types ───────────────────────────────────────────────

public sealed record ImpactAssessmentResult(
    long RunId,
    long ScenarioId,
    int RunNumber,
    ImpactRunStatus Status,
    DateOnly SnapshotDate,
    int TotalEntitiesEvaluated,
    int EntitiesCurrentlyCompliant,
    int EntitiesWouldBreach,
    int EntitiesAlreadyBreaching,
    int EntitiesNotAffected,
    decimal? AggregateCapitalShortfall,
    decimal? AggregateComplianceCost,
    long ExecutionTimeMs,
    Guid CorrelationId
);

public sealed record EntityImpactSummary(
    int InstitutionId,
    string InstitutionCode,
    string InstitutionName,
    string EntityType,
    ImpactCategory Category,
    decimal? CurrentMetricValue,
    decimal? ProposedThreshold,
    decimal? GapToCompliance,
    decimal? EstimatedComplianceCost,
    IReadOnlyList<EntityImpactDetail> ParameterDetails
);

public sealed record ScenarioComparisonResult(
    IReadOnlyList<ScenarioComparisonColumn> Scenarios,
    IReadOnlyList<ComparisonRow> EntityRows,
    bool IsTruncated = false
);

public sealed record ScenarioComparisonColumn(
    long RunId,
    long ScenarioId,
    string ScenarioTitle,
    int EntitiesWouldBreach,
    decimal? AggregateShortfall
);

public sealed record ComparisonRow(
    int InstitutionId,
    string InstitutionCode,
    string EntityType,
    IReadOnlyDictionary<long, ImpactCategory> CategoryByRunId
);

public sealed record CostBenefitResult(
    long AnalysisId,
    long ScenarioId,
    long RunId,
    decimal TotalIndustryComplianceCost,
    decimal CostToSmallEntities,
    decimal CostToMediumEntities,
    decimal CostToLargeEntities,
    decimal? SectorCARImprovement,
    decimal? SectorLCRImprovement,
    decimal? EstimatedRiskReduction,
    decimal? EstimatedDepositProtection,
    IReadOnlyList<PhaseInScenario> PhaseInScenarios,
    decimal? NetBenefitScore,
    string? Recommendation
);

public sealed record PolicyScenarioDetail(
    long Id,
    int RegulatorId,
    string Title,
    string? Description,
    PolicyDomain Domain,
    string TargetEntityTypes,
    DateOnly BaselineDate,
    PolicyStatus Status,
    int Version,
    IReadOnlyList<PolicyParameterChange> Parameters,
    IReadOnlyList<PolicyScenarioRunSummary> Runs,
    long? DecisionId
);

public sealed record PolicyScenarioRunSummary(
    long RunId,
    int RunNumber,
    ImpactRunStatus Status,
    int TotalEntities,
    int WouldBreach,
    int AlreadyBreaching,
    decimal? AggregateShortfall,
    DateTime? CompletedAt
);

public sealed record ScenarioStatusCounts(
    int Total,
    int Draft,
    int ParametersSet,
    int Simulated,
    int InConsultation,
    int Enacted,
    int Withdrawn
);

public sealed record PolicyScenarioSummary(
    long Id,
    string Title,
    PolicyDomain Domain,
    PolicyStatus Status,
    string TargetEntityTypes,
    DateOnly BaselineDate,
    int ParameterCount,
    int RunCount,
    DateTime CreatedAt
);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
}

// ── Consultation Types ─────────────────────────────────────────────────

public sealed record ConsultationProvisionInput(
    int ProvisionNumber,
    string ProvisionTitle,
    string ProvisionText,
    string? RelatedParameterCode
);

public sealed record ProvisionFeedbackInput(
    long ProvisionId,
    ProvisionPosition Position,
    string? Reasoning,
    string? SuggestedAmendment,
    string? ImpactAssessment
);

public sealed record FeedbackAggregationResult(
    long ConsultationId,
    int TotalFeedbackReceived,
    IReadOnlyList<FeedbackAggregateByProvision> ByProvision,
    Dictionary<string, int> OverallPositionCounts
);

public sealed record ConsultationDetail(
    long Id,
    string Title,
    string? CoverNote,
    ConsultationStatus Status,
    DateOnly Deadline,
    int TotalFeedbackReceived,
    IReadOnlyList<ConsultationProvisionDetail> Provisions
);

public sealed record ConsultationProvisionDetail(
    long Id,
    int ProvisionNumber,
    string Title,
    string Text,
    FeedbackAggregateByProvision? Aggregation
);

public sealed record ConsultationSummary(
    long Id,
    string Title,
    DateOnly Deadline,
    ConsultationStatus Status,
    bool HasSubmittedFeedback
);

// ── API Request DTOs ───────────────────────────────────────────────────

public sealed record CreateScenarioRequest(
    string Title, string? Description, PolicyDomain Domain,
    string TargetEntityTypes, DateOnly BaselineDate);

public sealed record AddParameterRequest(
    string ParameterCode, decimal ProposedValue, string? ApplicableEntityTypes);

public sealed record CompareRunsRequest(IReadOnlyList<long> RunIds);

public sealed record CreateConsultationRequest(
    long ScenarioId, string Title, string? CoverNote, DateOnly Deadline,
    IReadOnlyList<ConsultationProvisionInput> Provisions);

public sealed record RecordDecisionRequest(
    long ScenarioId, DecisionType Decision, string Summary,
    DateOnly? EffectiveDate, int? PhaseInMonths, string? CircularReference);

public sealed record SubmitFeedbackRequest(
    FeedbackPosition OverallPosition, string? GeneralComments,
    IReadOnlyList<ProvisionFeedbackInput> ProvisionFeedback);

public sealed record EntityResultsQuery(
    ImpactCategory? Category, string? EntityType, int Page = 1, int PageSize = 50);

// ── Configuration Options ──────────────────────────────────────────────

public sealed class PolicySimulationOptions
{
    public const string SectionName = "PolicySimulation";
    public int MaxEntitiesPerRun { get; set; } = 10000;
    public int TrackingCycleIntervalDays { get; set; } = 30;
    public int ConsultationDeadlineCheckIntervalHours { get; set; } = 6;
}
