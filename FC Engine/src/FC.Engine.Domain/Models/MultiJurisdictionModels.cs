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
}

public class JurisdictionConsolidationItem
{
    public int JurisdictionId { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal FxRateToReportingCurrency { get; set; }
    public int InstitutionCount { get; set; }
    public int SubmissionCount { get; set; }
    public int OverdueSubmissionCount { get; set; }
    public decimal GrossAmountLocal { get; set; }
    public decimal GrossAmountReportingCurrency { get; set; }
}
