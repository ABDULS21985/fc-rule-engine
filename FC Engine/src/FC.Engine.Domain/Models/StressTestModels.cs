namespace FC.Engine.Domain.Models;

// ── RG-37: Sector-Wide Stress Testing Framework ─────────────────────

public enum StressScenarioType
{
    NgfsOrderly,
    NgfsDisorderly,
    NgfsHotHouse,
    OilPriceCollapse,
    GlobalRecession,
    InterestRateSpike,
    Pandemic,
    CyberIncident,
    Custom
}

public enum SectorResilienceRating { Green, Amber, Red }

public class StressTestRequest
{
    public StressScenarioType ScenarioType { get; set; }
    public StressTestShockParameters? CustomParameters { get; set; }
    public List<string>? TargetLicenceTypes { get; set; }
}

public class StressTestShockParameters
{
    public string ScenarioName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal GdpGrowthDeltaPct { get; set; }
    public decimal FxDepreciationPct { get; set; }
    public decimal InflationDeltaPp { get; set; }
    public decimal InterestRateDeltaBps { get; set; }
    public decimal CreditLossMultiplier { get; set; } = 1m;
    public decimal TradeVolumeDeltaPct { get; set; }
    public decimal RemittanceDeltaPct { get; set; }
    public decimal FdiDeltaPct { get; set; }
    public decimal DepositFlightPct { get; set; }
    public bool MoratoriaFlag { get; set; }
    public List<string> ImpactChannels { get; set; } = new();
    public List<string> AffectedSectors { get; set; } = new();
}

public class StressScenarioInfo
{
    public StressScenarioType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public StressTestShockParameters DefaultParameters { get; set; } = new();
}

public class StressTestEntityResult
{
    public int InstitutionId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public decimal TotalAssets { get; set; }
    public decimal InsurableDeposits { get; set; }

    // Pre-stress metrics
    public decimal PreStressCar { get; set; }
    public decimal PreStressNpl { get; set; }
    public decimal PreStressLcr { get; set; }

    // Post-stress metrics
    public decimal PostStressCar { get; set; }
    public decimal PostStressNpl { get; set; }
    public decimal PostStressLcr { get; set; }

    // Breach flags
    public bool CarBreach { get; set; }
    public bool LcrBreach { get; set; }
    public bool NplBreach { get; set; }
    public bool SolvencyBreach { get; set; }

    // Impact magnitude
    public decimal CarImpact => PostStressCar - PreStressCar;
    public decimal NplImpact => PostStressNpl - PreStressNpl;
    public decimal LcrImpact => PostStressLcr - PreStressLcr;

    // CAMELS composite
    public decimal PreStressCamels { get; set; }
    public decimal PostStressCamels { get; set; }
}

public class StressTestContagionResult
{
    public int FailedEntityId { get; set; }
    public string FailedEntityName { get; set; } = string.Empty;
    public decimal FailedEntityCar { get; set; }
    public List<ContagionExposure> ExposedEntities { get; set; } = new();
    public decimal TotalInterbankExposure { get; set; }
    public decimal EstimatedDepositFlight { get; set; }
}

public class ContagionExposure
{
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public decimal CorrelationStrength { get; set; }
    public decimal EstimatedLoss { get; set; }
    public bool SecondRoundBreach { get; set; }
}

public class StressTestSectorAggregation
{
    public int TotalEntitiesTested { get; set; }
    public int CarBreachCount { get; set; }
    public int LcrBreachCount { get; set; }
    public int NplBreachCount { get; set; }
    public int SolvencyBreachCount { get; set; }
    public decimal NdicInsurableDepositsAtRisk { get; set; }
    public decimal NdicFundCapacity { get; set; } = 500_000_000_000m; // ₦500B default NDIC fund
    public decimal NdicExposureRatio => NdicFundCapacity > 0 ? Math.Round(NdicInsurableDepositsAtRisk / NdicFundCapacity * 100, 2) : 0;

    // CHS distribution: count of entities per rating band
    public PrePostDistribution CarDistribution { get; set; } = new();
    public decimal PreStressAverageCar { get; set; }
    public decimal PostStressAverageCar { get; set; }
    public decimal PreStressAverageNpl { get; set; }
    public decimal PostStressAverageNpl { get; set; }
    public decimal PreStressAverageLcr { get; set; }
    public decimal PostStressAverageLcr { get; set; }
}

public class PrePostDistribution
{
    public List<DistributionBucket> PreStress { get; set; } = new();
    public List<DistributionBucket> PostStress { get; set; } = new();
}

public class DistributionBucket
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class StressTestReport
{
    public string ScenarioName { get; set; } = string.Empty;
    public StressScenarioType ScenarioType { get; set; }
    public StressTestShockParameters Parameters { get; set; } = new();
    public SectorResilienceRating ResilienceRating { get; set; }
    public string ResilienceRationale { get; set; } = string.Empty;
    public List<StressTestEntityResult> EntityResults { get; set; } = new();
    public List<StressTestContagionResult> ContagionResults { get; set; } = new();
    public StressTestSectorAggregation Aggregation { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string RegulatorCode { get; set; } = string.Empty;
}

// ── RG-37 Full Implementation: Enums ─────────────────────────────────────────

public enum ScenarioCategory  { NgfsClimate, Macro, Custom }
public enum ScenarioSeverity  { Mild, Moderate, Severe, Extreme }
public enum StressTestRunStatus { Running, Completed, Failed }
public enum FailureCause { DirectShock, Interbank, DepositFlight }
public enum PhysicalHazard { None, Flood, Drought, HeatStress, FloodDrought }

// ── RG-37 Full Implementation: Records ───────────────────────────────────────

public sealed record EntityShockResult(
    int    InstitutionId,
    string InstitutionType,
    // Pre-stress
    decimal PreCAR,
    decimal PreNPL,
    decimal PreLCR,
    decimal PreNSFR,
    decimal PreROA,
    decimal PreTotalAssets,
    decimal PreTotalDeposits,
    // Post-stress
    decimal PostCAR,
    decimal PostNPL,
    decimal PostLCR,
    decimal PostNSFR,
    decimal PostROA,
    decimal PostCapitalShortfall,
    decimal AdditionalProvisions,
    // Flags
    bool   BreachesCAR,
    bool   BreachesLCR,
    bool   BreachesNSFR,
    bool   IsInsolvent,
    // NDIC
    decimal InsurableDeposits,
    decimal UninsurableDeposits,
    // Contagion (mutable via `with`)
    bool    IsContagionVictim  = false,
    int?    ContagionRound     = null,
    string? FailureCauseCode   = null
);

public sealed record ResolvedShockParameters(
    int    ScenarioId,
    string InstitutionType,
    decimal GDPGrowthShock,
    decimal OilPriceShockPct,
    decimal FXDepreciationPct,
    decimal InflationShockPp,
    int    InterestRateShockBps,
    // External-sector shocks
    decimal TradeVolumeShockPct,
    decimal RemittanceShockPct,
    decimal FDIShockPct,
    // NGFS
    decimal CarbonTaxUSDPerTon,
    decimal StrandedAssetsPct,
    string? PhysicalRiskHazardCode,
    // Transmission coefficients
    decimal CARDeltaPerGDPPp,
    decimal NPLDeltaPerGDPPp,
    decimal LCRDeltaPerRateHike100,
    decimal CARDeltaPerFXPct,
    decimal NPLDeltaPerFXPct,
    decimal CARDeltaPerOilPct,
    decimal NPLDeltaPerOilPct,
    decimal LCRDeltaPerCyber,
    decimal DepositOutflowPctCyber
);

public sealed record SectorStressAggregate(
    string InstitutionType,
    int    EntityCount,
    decimal PreAvgCAR,
    decimal PreAvgNPL,
    decimal PreAvgLCR,
    decimal PostAvgCAR,
    decimal PostAvgNPL,
    decimal PostAvgLCR,
    int    EntitiesBreachingCAR,
    int    EntitiesBreachingLCR,
    int    EntitiesInsolvent,
    int    EntitiesContagionVictims,
    decimal TotalCapitalShortfall,
    decimal TotalAdditionalProvisions,
    decimal TotalInsurableDepositsAtRisk,
    decimal TotalUninsurableDepositsAtRisk
);

public sealed record StressTestRunSummary(
    long   RunId,
    Guid   RunGuid,
    string ScenarioCode,
    string ScenarioName,
    string PeriodCode,
    string TimeHorizon,
    int    EntitiesShocked,
    int    ContagionRounds,
    double SystemicResilienceScore,
    string ResilienceRating,
    IReadOnlyList<SectorStressAggregate> BySector,
    decimal TotalCapitalShortfallNgn,
    decimal TotalNDICExposureAtRisk,
    TimeSpan Duration,
    string? ExecutiveSummary = null
);

public sealed record ContagionEvent(
    int  ContagionRound,
    int  FailingInstitutionId,
    int  AffectedInstitutionId,
    decimal ExposureAmount,
    string ExposureType,
    string TransmissionType
);

public sealed record PrudentialMetricSnapshot(
    int     InstitutionId,
    string  InstitutionType,
    string  RegulatorCode,
    string  PeriodCode,
    decimal CAR,
    decimal NPL,
    decimal LCR,
    decimal NSFR,
    decimal ROA,
    decimal TotalAssets,
    decimal TotalDeposits,
    decimal OilSectorExposurePct,
    decimal AgriExposurePct,
    decimal FXLoansAssetPct,
    decimal BondPortfolioAssetPct,
    decimal TopDepositorConcentration
);
