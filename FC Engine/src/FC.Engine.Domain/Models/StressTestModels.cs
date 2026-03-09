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
