using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class RegulatoryJurisdiction
{
    public int Id { get; set; }
    public string JurisdictionCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string RegulatorName { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencySymbol { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public string RegulatoryFramework { get; set; } = string.Empty;
    public bool EcowasRegion { get; set; }
    public bool AfcftaMember { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class FinancialGroup
{
    public int Id { get; set; }
    public string GroupCode { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string HeadquarterJurisdiction { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public RegulatoryJurisdiction? HeadquarterJurisdictionNav { get; set; }
    public List<GroupSubsidiary> Subsidiaries { get; set; } = new();
}

public class GroupSubsidiary
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int InstitutionId { get; set; }
    public string JurisdictionCode { get; set; } = string.Empty;
    public string SubsidiaryCode { get; set; } = string.Empty;
    public string SubsidiaryName { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string LocalCurrency { get; set; } = string.Empty;
    public decimal OwnershipPercentage { get; set; }
    public ConsolidationMethod ConsolidationMethod { get; set; } = ConsolidationMethod.Full;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public FinancialGroup? Group { get; set; }
    public RegulatoryJurisdiction? JurisdictionNav { get; set; }
}

public class RegulatoryEquivalenceMapping
{
    public long Id { get; set; }
    public string MappingCode { get; set; } = string.Empty;
    public string MappingName { get; set; } = string.Empty;
    public string ConceptDomain { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<EquivalenceMappingEntry> Entries { get; set; } = new();
}

public class EquivalenceMappingEntry
{
    public long Id { get; set; }
    public long MappingId { get; set; }
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
    public int DisplayOrder { get; set; }

    public RegulatoryEquivalenceMapping? Mapping { get; set; }
    public RegulatoryJurisdiction? JurisdictionNav { get; set; }
}

public class CrossBorderFxRate
{
    public long Id { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public DateOnly RateDate { get; set; }
    public decimal Rate { get; set; }
    public decimal InverseRate { get; set; }
    public string RateSource { get; set; } = string.Empty;
    public FxRateType RateType { get; set; } = FxRateType.PeriodEnd;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ConsolidationRun
{
    public long Id { get; set; }
    public int GroupId { get; set; }
    public int RunNumber { get; set; }
    public string ReportingPeriod { get; set; } = string.Empty;
    public DateOnly SnapshotDate { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public ConsolidationRunStatus Status { get; set; } = ConsolidationRunStatus.Pending;
    public int TotalSubsidiaries { get; set; }
    public int SubsidiariesCollected { get; set; }
    public int TotalAdjustments { get; set; }
    public decimal? ConsolidatedTotalAssets { get; set; }
    public decimal? ConsolidatedTotalCapital { get; set; }
    public decimal? ConsolidatedCAR { get; set; }
    public Guid CorrelationId { get; set; }
    public long? ExecutionTimeMs { get; set; }
    public string? ErrorMessage { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public FinancialGroup? Group { get; set; }
    public List<ConsolidationSubsidiarySnapshot> Snapshots { get; set; } = new();
    public List<GroupConsolidationAdjustment> Adjustments { get; set; } = new();
}

public class ConsolidationSubsidiarySnapshot
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public int SubsidiaryId { get; set; }
    public int GroupId { get; set; }
    public string JurisdictionCode { get; set; } = string.Empty;
    public string LocalCurrency { get; set; } = string.Empty;
    public decimal LocalTotalAssets { get; set; }
    public decimal LocalTotalLiabilities { get; set; }
    public decimal LocalTotalCapital { get; set; }
    public decimal LocalRWA { get; set; }
    public decimal LocalCAR { get; set; }
    public decimal? LocalLCR { get; set; }
    public decimal? LocalNSFR { get; set; }
    public decimal FxRateUsed { get; set; }
    public DateOnly FxRateDate { get; set; }
    public string FxRateSource { get; set; } = string.Empty;
    public decimal ConvertedTotalAssets { get; set; }
    public decimal ConvertedTotalLiabilities { get; set; }
    public decimal ConvertedTotalCapital { get; set; }
    public decimal ConvertedRWA { get; set; }
    public decimal OwnershipPercentage { get; set; }
    public string ConsolidationMethodUsed { get; set; } = string.Empty;
    public decimal AdjustedTotalAssets { get; set; }
    public decimal AdjustedTotalCapital { get; set; }
    public decimal AdjustedRWA { get; set; }
    public long? SourceReturnInstanceId { get; set; }
    public DateTime DataCollectedAt { get; set; } = DateTime.UtcNow;

    public ConsolidationRun? Run { get; set; }
    public GroupSubsidiary? Subsidiary { get; set; }
}

public class GroupConsolidationAdjustment
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public int GroupId { get; set; }
    public string AdjustmentType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? AffectedSubsidiaryId { get; set; }
    public string DebitAccount { get; set; } = string.Empty;
    public string CreditAccount { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public bool IsAutomatic { get; set; } = true;
    public int? AppliedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ConsolidationRun? Run { get; set; }
    public GroupSubsidiary? AffectedSubsidiary { get; set; }
}

public class CrossBorderDataFlow
{
    public long Id { get; set; }
    public int GroupId { get; set; }
    public string FlowCode { get; set; } = string.Empty;
    public string FlowName { get; set; } = string.Empty;
    public string SourceJurisdiction { get; set; } = string.Empty;
    public string SourceReturnCode { get; set; } = string.Empty;
    public string SourceLineCode { get; set; } = string.Empty;
    public string TargetJurisdiction { get; set; } = string.Empty;
    public string TargetReturnCode { get; set; } = string.Empty;
    public string TargetLineCode { get; set; } = string.Empty;
    public DataFlowTransformation TransformationType { get; set; } = DataFlowTransformation.Direct;
    public string? TransformationFormula { get; set; }
    public bool RequiresCurrencyConversion { get; set; }
    public bool IsActive { get; set; } = true;
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public FinancialGroup? Group { get; set; }
    public List<DataFlowExecution> Executions { get; set; } = new();
}

public class DataFlowExecution
{
    public long Id { get; set; }
    public long FlowId { get; set; }
    public int GroupId { get; set; }
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
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    public CrossBorderDataFlow? Flow { get; set; }
}

public class RegulatoryDivergence
{
    public long Id { get; set; }
    public long MappingId { get; set; }
    public string ConceptDomain { get; set; } = string.Empty;
    public DivergenceType DivergenceType { get; set; }
    public string SourceJurisdiction { get; set; } = string.Empty;
    public string AffectedJurisdictions { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public DivergenceSeverity Severity { get; set; }
    public DivergenceStatus Status { get; set; } = DivergenceStatus.Open;
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public bool DetectedBySystem { get; set; } = true;
    public int? AcknowledgedByUserId { get; set; }

    public RegulatoryEquivalenceMapping? Mapping { get; set; }
    public List<DivergenceNotification> Notifications { get; set; } = new();
}

public class DivergenceNotification
{
    public long Id { get; set; }
    public long DivergenceId { get; set; }
    public int GroupId { get; set; }
    public int NotifiedUserId { get; set; }
    public string NotificationChannel { get; set; } = "IN_APP";
    public string Status { get; set; } = "SENT";
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    public RegulatoryDivergence? Divergence { get; set; }
}

public class AfcftaProtocolTracking
{
    public int Id { get; set; }
    public string ProtocolCode { get; set; } = string.Empty;
    public string ProtocolName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public AfcftaProtocolStatus Status { get; set; } = AfcftaProtocolStatus.Proposed;
    public string ParticipatingJurisdictions { get; set; } = string.Empty;
    public DateOnly? TargetEffectiveDate { get; set; }
    public DateOnly? ActualEffectiveDate { get; set; }
    public string? Description { get; set; }
    public string? ImpactOnRegOS { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class RegulatoryDeadline
{
    public long Id { get; set; }
    public string JurisdictionCode { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string ReturnName { get; set; } = string.Empty;
    public string ReportingPeriod { get; set; } = string.Empty;
    public DateTimeOffset DeadlineUtc { get; set; }
    public string LocalTimeZone { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int? GroupId { get; set; }
    public DeadlineStatus Status { get; set; } = DeadlineStatus.Upcoming;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public RegulatoryJurisdiction? JurisdictionNav { get; set; }
}

public class HarmonisationAuditLog
{
    public long Id { get; set; }
    public int? GroupId { get; set; }
    public string? JurisdictionCode { get; set; }
    public Guid CorrelationId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public int? PerformedByUserId { get; set; }
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
}
