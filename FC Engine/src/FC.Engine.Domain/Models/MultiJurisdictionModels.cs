namespace FC.Engine.Domain.Models;

public class CrossJurisdictionConsolidation
{
    public Guid TenantId { get; set; }
    public string ReportingCurrency { get; set; } = "NGN";
    public decimal GrossAmount { get; set; }
    public decimal EliminationAdjustments { get; set; }
    public decimal NetAmount { get; set; }
    public int SubsidiaryCount { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<JurisdictionConsolidationItem> Jurisdictions { get; set; } = new();
    public List<EntityNode> EntityHierarchy { get; set; } = new();
    public List<ConsolidationStatusEntry> StatusMatrix { get; set; } = new();
    public List<AggregationField> AggregationFields { get; set; } = new();
    public List<EliminationEntry> Eliminations { get; set; } = new();
    public List<ReconciliationAlert> ReconciliationAlerts { get; set; } = new();
}

public class JurisdictionConsolidationItem
{
    public int JurisdictionId { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal FxRateToReportingCurrency { get; set; }
    public DateTime FxRateDate { get; set; }
    public int InstitutionCount { get; set; }
    public int SubmissionCount { get; set; }
    public int OverdueSubmissionCount { get; set; }
    public decimal GrossAmountLocal { get; set; }
    public decimal GrossAmountReportingCurrency { get; set; }

    public bool IsFxRateStale => (DateTime.UtcNow - FxRateDate).TotalHours > 24;
}

public class EntityNode
{
    public int EntityId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty; // HoldingCompany, Subsidiary, Branch
    public string? Jurisdiction { get; set; }
    public int SubmissionCount { get; set; }
    public int PendingCount { get; set; }
    public List<EntityNode> Children { get; set; } = new();
}

public class ConsolidationStatusEntry
{
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string EntityCode { get; set; } = string.Empty;
    public List<ReturnStatusCell> Returns { get; set; } = new();
}

public class ReturnStatusCell
{
    public string ReturnCode { get; set; } = string.Empty;
    public string ReturnName { get; set; } = string.Empty;
    public ConsolidationSubmissionStatus Status { get; set; }
    public int? SubmissionId { get; set; }
    public DateTime? SubmittedAt { get; set; }
}

public enum ConsolidationSubmissionStatus
{
    Missing,
    Pending,
    Submitted,
    Accepted,
    Rejected
}

public class AggregationField
{
    public string FieldCode { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public decimal GroupTotal { get; set; }
    public decimal ExpectedTotal { get; set; }
    public string Currency { get; set; } = "NGN";
    public List<EntityAggregationValue> EntityValues { get; set; } = new();

    public decimal Variance => GroupTotal - ExpectedTotal;
    public double VariancePercent => ExpectedTotal != 0 ? (double)((GroupTotal - ExpectedTotal) / ExpectedTotal * 100) : 0;
}

public class EntityAggregationValue
{
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public string LocalCurrency { get; set; } = string.Empty;
    public decimal LocalValue { get; set; }
    public decimal FxRate { get; set; }
    public DateTime FxRateDate { get; set; }

    public bool IsFxStale => (DateTime.UtcNow - FxRateDate).TotalHours > 24;
}

public class EliminationEntry
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Intercompany Loan, Revenue, Dividend, etc.
    public int SourceEntityId { get; set; }
    public string SourceEntityName { get; set; } = string.Empty;
    public int CounterpartyEntityId { get; set; }
    public string CounterpartyEntityName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NGN";
    public decimal AmountReportingCurrency { get; set; }
    public string FieldCode { get; set; } = string.Empty;
    public string? CounterpartyFieldCode { get; set; }
    public bool IsMatched { get; set; }
}

public class ReconciliationAlert
{
    public string AlertId { get; set; } = string.Empty;
    public ReconciliationAlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? FieldCode { get; set; }
    public decimal? ExpectedValue { get; set; }
    public decimal? ActualValue { get; set; }
    public decimal? Difference { get; set; }
    public List<ReconciliationDrilldownItem> DrilldownItems { get; set; } = new();
}

public enum ReconciliationAlertSeverity
{
    Info,
    Warning,
    Error
}

public class ReconciliationDrilldownItem
{
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string FieldCode { get; set; } = string.Empty;
    public decimal ReportedValue { get; set; }
    public decimal ExpectedValue { get; set; }
    public decimal Difference { get; set; }
}
