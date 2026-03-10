using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Models;

// ── Value Objects ────────────────────────────────────────────────

public sealed class CurrencyAmount
{
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public sealed class ConvertedAmount
{
    public decimal SourceAmount { get; set; }
    public string SourceCurrency { get; set; } = string.Empty;
    public decimal ConvertedValue { get; set; }
    public string TargetCurrency { get; set; } = string.Empty;
    public decimal FxRate { get; set; }
    public DateOnly RateDate { get; set; }
    public string RateSource { get; set; } = string.Empty;
}

public sealed class JurisdictionThreshold
{
    public string JurisdictionCode { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ParameterCode { get; set; } = string.Empty;
    public decimal Threshold { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string CalculationBasis { get; set; } = string.Empty;
    public string RegulatoryFramework { get; set; } = string.Empty;
}

public sealed class DivergenceAlert
{
    public long DivergenceId { get; set; }
    public string ConceptDomain { get; set; } = string.Empty;
    public DivergenceType Type { get; set; }
    public string SourceJurisdiction { get; set; } = string.Empty;
    public List<string> AffectedJurisdictions { get; set; } = [];
    public string Description { get; set; } = string.Empty;
    public DivergenceSeverity Severity { get; set; }
    public DateTime DetectedAt { get; set; }
}

public sealed class SubsidiaryComplianceSnapshot
{
    public int SubsidiaryId { get; set; }
    public string SubsidiaryCode { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public string LocalCurrency { get; set; } = string.Empty;
    public decimal LocalCAR { get; set; }
    public decimal LocalThreshold { get; set; }
    public bool IsCompliant { get; set; }
    public decimal? Gap { get; set; }
    public ConvertedAmount? ConvertedCapital { get; set; }
}

// ── Equivalence Mapping DTOs ─────────────────────────────────────

public sealed class EquivalenceEntryInput
{
    public string JurisdictionCode { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string LocalParameterCode { get; set; } = string.Empty;
    public string LocalParameterName { get; set; } = string.Empty;
    public decimal LocalThreshold { get; set; }
    public string ThresholdUnit { get; set; } = string.Empty;
    public string CalculationBasis { get; set; } = string.Empty;
    public string? ReturnFormCode { get; set; }
    public string? ReturnLineReference { get; set; }
    public string RegulatoryFramework { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class EquivalenceMappingDetail
{
    public long Id { get; set; }
    public string MappingCode { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public string ConceptDomain { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public List<EquivalenceEntryDetail> Entries { get; set; } = [];
}

public sealed class EquivalenceEntryDetail
{
    public long Id { get; set; }
    public string JurisdictionCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string LocalParameterCode { get; set; } = string.Empty;
    public string LocalParameterName { get; set; } = string.Empty;
    public decimal LocalThreshold { get; set; }
    public string ThresholdUnit { get; set; } = string.Empty;
    public string CalculationBasis { get; set; } = string.Empty;
    public string? ReturnFormCode { get; set; }
    public string? ReturnLineReference { get; set; }
    public string RegulatoryFramework { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class EquivalenceMappingSummary
{
    public long Id { get; set; }
    public string MappingCode { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public string ConceptDomain { get; set; } = string.Empty;
    public int JurisdictionCount { get; set; }
    public int Version { get; set; }
}

public sealed class CreateMappingRequest
{
    public string MappingCode { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public string ConceptDomain { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<EquivalenceEntryInput> Entries { get; set; } = [];
}

// ── Consolidation DTOs ───────────────────────────────────────────

public sealed class ConsolidationResult
{
    public long RunId { get; set; }
    public int GroupId { get; set; }
    public int RunNumber { get; set; }
    public string ReportingPeriod { get; set; } = string.Empty;
    public ConsolidationRunStatus Status { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public int TotalSubsidiaries { get; set; }
    public int SubsidiariesCollected { get; set; }
    public int TotalAdjustments { get; set; }
    public decimal? ConsolidatedTotalAssets { get; set; }
    public decimal? ConsolidatedTotalCapital { get; set; }
    public decimal? ConsolidatedCAR { get; set; }
    public long? ExecutionTimeMs { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed class ConsolidationSubsidiaryResult
{
    public int SubsidiaryId { get; set; }
    public string SubsidiaryCode { get; set; } = string.Empty;
    public string SubsidiaryName { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public string LocalCurrency { get; set; } = string.Empty;
    public decimal LocalTotalAssets { get; set; }
    public decimal LocalTotalCapital { get; set; }
    public decimal LocalCAR { get; set; }
    public decimal FxRateUsed { get; set; }
    public DateOnly FxRateDate { get; set; }
    public string FxRateSource { get; set; } = string.Empty;
    public decimal ConvertedTotalAssets { get; set; }
    public decimal ConvertedTotalCapital { get; set; }
    public decimal OwnershipPercentage { get; set; }
    public string ConsolidationMethod { get; set; } = string.Empty;
    public decimal AdjustedTotalAssets { get; set; }
    public decimal AdjustedTotalCapital { get; set; }
    public decimal AdjustedRWA { get; set; }
}

public sealed class ConsolidationAdjustmentDto
{
    public long Id { get; set; }
    public string AdjustmentType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? AffectedSubsidiaryCode { get; set; }
    public string DebitAccount { get; set; } = string.Empty;
    public string CreditAccount { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool IsAutomatic { get; set; }
}

public sealed class ConsolidationAdjustmentInput
{
    public string AdjustmentType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? AffectedSubsidiaryId { get; set; }
    public string DebitAccount { get; set; } = string.Empty;
    public string CreditAccount { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class ConsolidateRequest
{
    public string ReportingPeriod { get; set; } = string.Empty;
    public DateOnly SnapshotDate { get; set; }
}

// ── Data Flow DTOs ───────────────────────────────────────────────

public sealed class DataFlowDefinition
{
    public string FlowCode { get; set; } = string.Empty;
    public string FlowName { get; set; } = string.Empty;
    public string SourceJurisdiction { get; set; } = string.Empty;
    public string SourceReturnCode { get; set; } = string.Empty;
    public string SourceLineCode { get; set; } = string.Empty;
    public string TargetJurisdiction { get; set; } = string.Empty;
    public string TargetReturnCode { get; set; } = string.Empty;
    public string TargetLineCode { get; set; } = string.Empty;
    public DataFlowTransformation Transformation { get; set; }
    public string? TransformationFormula { get; set; }
    public bool RequiresCurrencyConversion { get; set; }
}

public sealed class DataFlowExecutionResult
{
    public long ExecutionId { get; set; }
    public long FlowId { get; set; }
    public string FlowCode { get; set; } = string.Empty;
    public string ReportingPeriod { get; set; } = string.Empty;
    public decimal SourceValue { get; set; }
    public string SourceCurrency { get; set; } = string.Empty;
    public decimal? FxRateApplied { get; set; }
    public decimal? ConvertedValue { get; set; }
    public decimal TargetValue { get; set; }
    public string TargetCurrency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed class DataFlowSummary
{
    public long Id { get; set; }
    public string FlowCode { get; set; } = string.Empty;
    public string FlowName { get; set; } = string.Empty;
    public string SourceJurisdiction { get; set; } = string.Empty;
    public string SourceReturnCode { get; set; } = string.Empty;
    public string TargetJurisdiction { get; set; } = string.Empty;
    public string TargetReturnCode { get; set; } = string.Empty;
    public string Transformation { get; set; } = string.Empty;
    public bool RequiresCurrencyConversion { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ExecuteFlowsRequest
{
    public string ReportingPeriod { get; set; } = string.Empty;
}

// ── FX Rate DTOs ─────────────────────────────────────────────────

public sealed class FxRateDto
{
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
    public decimal InverseRate { get; set; }
    public string RateSource { get; set; } = string.Empty;
    public string RateType { get; set; } = string.Empty;
}

public sealed class UpsertRateRequest
{
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
    public string RateSource { get; set; } = string.Empty;
    public FxRateType RateType { get; set; }
}

// ── Dashboard DTOs ───────────────────────────────────────────────

public sealed class GroupComplianceOverview
{
    public int GroupId { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = string.Empty;
    public int TotalSubsidiaries { get; set; }
    public int TotalJurisdictions { get; set; }
    public int SubsidiariesCompliant { get; set; }
    public int SubsidiariesInBreach { get; set; }
    public int OpenDivergences { get; set; }
    public int UpcomingDeadlines { get; set; }
    public decimal? ConsolidatedCAR { get; set; }
    public decimal? ConsolidatedLCR { get; set; }
    public List<JurisdictionSummary> ByJurisdiction { get; set; } = [];
}

public sealed class JurisdictionSummary
{
    public string JurisdictionCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public int SubsidiaryCount { get; set; }
    public bool AllCompliant { get; set; }
    public decimal? AggregateCAR { get; set; }
    public int UpcomingDeadlines { get; set; }
    public int OpenDivergences { get; set; }
}

public sealed class RegulatoryDeadlineDto
{
    public long Id { get; set; }
    public string JurisdictionCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string ReturnName { get; set; } = string.Empty;
    public string ReportingPeriod { get; set; } = string.Empty;
    public DateTimeOffset DeadlineUtc { get; set; }
    public string LocalTimeZone { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public DeadlineStatus Status { get; set; }
    public int DaysUntilDeadline { get; set; }
}

public sealed class CrossBorderRiskMetrics
{
    public int GroupId { get; set; }
    public string ReportingPeriod { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = string.Empty;
    public decimal ConsolidatedTotalAssets { get; set; }
    public decimal ConsolidatedTotalCapital { get; set; }
    public decimal ConsolidatedRWA { get; set; }
    public decimal ConsolidatedCAR { get; set; }
    public decimal? ConsolidatedLCR { get; set; }
    public decimal? ConsolidatedNSFR { get; set; }
    public List<SubsidiaryRiskContribution> BySubsidiary { get; set; } = [];
}

public sealed class SubsidiaryRiskContribution
{
    public string SubsidiaryCode { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public decimal ContributionToAssets { get; set; }
    public decimal ContributionToRWA { get; set; }
    public decimal ContributionPercentage { get; set; }
    public decimal LocalCAR { get; set; }
}

// ── AfCFTA DTOs ──────────────────────────────────────────────────

public sealed class AfcftaProtocolDto
{
    public int Id { get; set; }
    public string ProtocolCode { get; set; } = string.Empty;
    public string ProtocolName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public AfcftaProtocolStatus Status { get; set; }
    public List<string> ParticipatingJurisdictions { get; set; } = [];
    public DateOnly? TargetEffectiveDate { get; set; }
    public DateOnly? ActualEffectiveDate { get; set; }
    public string? Description { get; set; }
    public string? ImpactOnRegOS { get; set; }
}
