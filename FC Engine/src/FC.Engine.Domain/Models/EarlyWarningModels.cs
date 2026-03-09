using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Models;

// ── CAMELS scoring weights — CBN methodology ──────────────────────────────────
public static class CamelsWeights
{
    public const double Capital      = 0.20;
    public const double AssetQuality = 0.20;
    public const double Management   = 0.15;
    public const double Earnings     = 0.20;
    public const double Liquidity    = 0.15;
    public const double Sensitivity  = 0.10;
}

// ── Regulatory prudential thresholds per institution type ─────────────────────
public static class PrudentialThresholds
{
    /// <summary>Minimum Capital Adequacy Ratio by institution type (CBN/NDIC mandates).</summary>
    public static double GetMinCAR(string institutionType) => institutionType.ToUpperInvariant() switch
    {
        "DMB"     => 15.0,  // Deposit Money Banks — CBN: 15%
        "MFB"     => 10.0,  // Microfinance Banks — CBN: 10%
        "PFA"     => 0.0,   // Pension Fund Administrators — capital base, not ratio-based
        "INSURER" => 0.0,   // Insurance — NAICOM solvency margin basis
        _         => 10.0
    };

    public const double NPLWarningThreshold     = 5.0;   // %
    public const double NPLRapidRiseThreshold   = 2.0;   // pp in a single quarter
    public const double LCRMinimum              = 100.0; // %
    public const double LCRWarningZone          = 110.0; // %
    public const double NSFRMinimum             = 100.0; // %
    public const double DepositConcentrationCap = 30.0;  // top-20 depositors / total deposits
    public const double RelatedPartyLendingCap  = 5.0;   // % of capital
    public const double FXExposureCap           = 20.0;  // % of shareholders' funds
    public const double SuddenGrowthThreshold   = 30.0;  // QoQ asset growth %
    public const double ProvisioningWarning     = 50.0;  // coverage %
    public const double ROANegativeThreshold    = 0.0;   // %
    public const double CIRCriticalThreshold    = 80.0;  // %
}

// ── Value objects ─────────────────────────────────────────────────────────────

/// <summary>Context for a single EWI trigger event (input to persistence).</summary>
public sealed record EWITriggerContext(
    string EWICode,
    string EWISeverity,
    decimal? TriggerValue,
    decimal? ThresholdValue,
    string? TrendDataJson,
    bool IsSystemic);

/// <summary>Result of CAMELS scoring for a single institution.</summary>
public sealed record CAMELSResult(
    int InstitutionId,
    string PeriodCode,
    int CapitalScore,
    int AssetQualityScore,
    int ManagementScore,
    int EarningsScore,
    int LiquidityScore,
    int SensitivityScore,
    double CompositeScore,
    RiskBand RiskBand,
    decimal? TotalAssets);

/// <summary>Summary of a completed EWI computation cycle.</summary>
public sealed record EWIComputationSummary(
    Guid ComputationRunId,
    string RegulatorCode,
    string PeriodCode,
    int EntitiesEvaluated,
    int EWIsTriggered,
    int EWIsCleared,
    int ActionsGenerated,
    TimeSpan Duration);

/// <summary>Node in the interbank contagion network graph.</summary>
public sealed record ContagionNode(
    int InstitutionId,
    string InstitutionName,
    string InstitutionType,
    decimal TotalOutbound,
    decimal TotalInbound,
    double EigenvectorCentrality,
    double BetweennessCentrality,
    double ContagionRiskScore,
    bool IsSystemicallyImportant);

/// <summary>Directed edge in the interbank network (lender → borrower).</summary>
public sealed record ContagionEdge(
    int LendingInstitutionId,
    int BorrowingInstitutionId,
    decimal ExposureAmount,
    string ExposureType);

/// <summary>Cell in the sector risk heatmap (one cell per supervised entity).</summary>
public sealed record HeatmapCell(
    int InstitutionId,
    string InstitutionName,
    string InstitutionType,
    double CompositeScore,
    RiskBand Band,
    decimal TotalAssets,
    int ActiveEWICount,
    bool HasCriticalEWI);

/// <summary>Single row in the EWI history table for an institution.</summary>
public sealed record EWITriggerRow(
    long TriggerId,
    string EWICode,
    string EWIName,
    string CAMELSComponent,
    string Severity,
    decimal? TriggerValue,
    decimal? ThresholdValue,
    string? TrendDataJson,
    bool IsActive,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? ClearedAt);

/// <summary>Aggregated systemic risk indicator snapshot for a sector/period.</summary>
public class SystemicRiskIndicators
{
    public string RegulatorCode { get; set; } = string.Empty;
    public string InstitutionType { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = string.Empty;
    public int EntityCount { get; set; }
    public decimal? SectorAvgCAR { get; set; }
    public decimal? SectorAvgNPL { get; set; }
    public decimal? SectorAvgLCR { get; set; }
    public decimal? SectorAvgROA { get; set; }
    public int EntitiesBreachingCAR { get; set; }
    public int EntitiesBreachingNPL { get; set; }
    public int EntitiesBreachingLCR { get; set; }
    public int HighRiskEntityCount { get; set; }
    public decimal SystemicRiskScore { get; set; }
    public string SystemicRiskBand { get; set; } = "LOW";
    public decimal? AggregateInterbankExposure { get; set; }
    public Guid ComputationRunId { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>Options for the EWI computation engine (bound from appsettings).</summary>
public sealed class EWIEngineOptions
{
    public int CycleIntervalMinutes { get; set; } = 60;
    public string DefaultRegulatorCode { get; set; } = "CBN";
    public bool AutoGenerateActions { get; set; } = true;
    public bool RunContagionAnalysis { get; set; } = true;
}
