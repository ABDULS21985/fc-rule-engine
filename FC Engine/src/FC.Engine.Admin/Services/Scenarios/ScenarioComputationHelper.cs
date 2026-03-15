namespace FC.Engine.Admin.Services.Scenarios;

/// <summary>
/// Stateless, thread-safe computation helpers shared by both the in-memory
/// ScenarioEngine and the persisted PersistedScenarioEngine.
/// All methods are static so neither implementation needs to duplicate logic.
/// </summary>
internal static class ScenarioComputationHelper
{
    // ── Baseline data for key regulatory metrics ──────────────────────────
    internal static readonly Dictionary<string, (decimal Value, decimal? Threshold, bool HigherIsBetter)> BaselineMetrics = new()
    {
        ["Capital Adequacy Ratio (CAR)"]     = (15.2m,  10.0m, true),
        ["Tier 1 Capital Ratio"]             = (12.8m,  8.0m,  true),
        ["NPL Ratio"]                        = (4.2m,   10.0m, false),
        ["Liquidity Coverage Ratio (LCR)"]   = (135.0m, 100.0m, true),
        ["Net Stable Funding Ratio (NSFR)"]  = (118.0m, 100.0m, true),
        ["Net Interest Income (₦B)"]         = (245.6m, null,   true),
        ["Loan-to-Deposit Ratio"]            = (62.5m,  80.0m,  false),
        ["Provision Coverage Ratio"]         = (142.0m, 100.0m, true),
        ["Return on Equity (ROE)"]           = (18.4m,  null,   true),
        ["FX Open Position (% Capital)"]     = (8.2m,   20.0m,  false),
        ["Total Deposits (₦B)"]              = (3_420.0m, null, true),
        ["Bond Portfolio Value (₦B)"]        = (890.0m, null,   true),
        ["NDIC Premium (₦M)"]               = (12_500.0m, null, false),
        ["Provisioning Expense (₦B)"]        = (45.8m,  null,   false),
        ["Stranded Assets Exposure (%)"]     = (3.5m,   15.0m,  false),
        ["Sector Concentration (%)"]         = (22.0m,  30.0m,  false),
    };

    // ── Mapping from field overrides to metric impacts ─────────────────────
    internal static readonly Dictionary<string, List<(string Metric, decimal Multiplier)>> ImpactMap = new()
    {
        ["interest_rate_shock"] =
        [
            ("Net Interest Income (₦B)", -0.12m),
            ("Bond Portfolio Value (₦B)", -0.08m),
            ("Liquidity Coverage Ratio (LCR)", -0.15m),
            ("Capital Adequacy Ratio (CAR)", -0.05m),
            ("Return on Equity (ROE)", -0.10m),
        ],
        ["fx_depreciation"] =
        [
            ("FX Open Position (% Capital)", 0.20m),
            ("Capital Adequacy Ratio (CAR)", -0.08m),
            ("Net Interest Income (₦B)", -0.03m),
            ("Tier 1 Capital Ratio", -0.06m),
        ],
        ["npl_spike"] =
        [
            ("NPL Ratio", 0.50m),
            ("Capital Adequacy Ratio (CAR)", -0.12m),
            ("Provision Coverage Ratio", -0.18m),
            ("Provisioning Expense (₦B)", 0.45m),
            ("NDIC Premium (₦M)", 0.25m),
            ("Return on Equity (ROE)", -0.20m),
            ("Tier 1 Capital Ratio", -0.10m),
        ],
        ["deposit_run"] =
        [
            ("Total Deposits (₦B)", -0.30m),
            ("Liquidity Coverage Ratio (LCR)", -0.35m),
            ("Net Stable Funding Ratio (NSFR)", -0.22m),
            ("Loan-to-Deposit Ratio", 0.25m),
            ("Net Interest Income (₦B)", -0.08m),
        ],
        ["regulatory_capital_increase"] =
        [
            ("Capital Adequacy Ratio (CAR)", -0.02m),
            ("Tier 1 Capital Ratio", -0.02m),
            ("Return on Equity (ROE)", -0.05m),
        ],
        ["climate_transition"] =
        [
            ("Stranded Assets Exposure (%)", 0.60m),
            ("Sector Concentration (%)", 0.15m),
            ("Capital Adequacy Ratio (CAR)", -0.03m),
            ("Provision Coverage Ratio", -0.05m),
        ],
        ["fatf_grey_list_exit"] =
        [
            ("FX Open Position (% Capital)", -0.10m),
            ("Net Interest Income (₦B)", 0.05m),
            ("Return on Equity (ROE)", 0.03m),
        ],
    };

    internal static string ResolveImpactKey(ScenarioDefinition scenario)
    {
        if (!string.IsNullOrEmpty(scenario.TemplateId))
            return scenario.TemplateId;

        if (scenario.MacroShocks.Count > 0)
            return scenario.MacroShocks[0].Name.ToLowerInvariant().Replace(" ", "_");

        if (scenario.Overrides.Any(o => o.FieldName.Contains("npl", StringComparison.OrdinalIgnoreCase)))
            return "npl_spike";
        if (scenario.Overrides.Any(o => o.FieldName.Contains("fx", StringComparison.OrdinalIgnoreCase)))
            return "fx_depreciation";
        if (scenario.Overrides.Any(o => o.FieldName.Contains("deposit", StringComparison.OrdinalIgnoreCase)))
            return "deposit_run";

        return "interest_rate_shock";
    }

    internal static List<MetricResult> ComputeMetrics(
        string impactKey,
        ScenarioDefinition scenario,
        decimal jitter = 1.0m,
        decimal baselineVariance = 0m)
    {
        var impacts = ImpactMap.TryGetValue(impactKey, out var map)
            ? map
            : ImpactMap["interest_rate_shock"];

        var results = new List<MetricResult>();
        foreach (var (metricName, multiplier) in impacts)
        {
            if (!BaselineMetrics.TryGetValue(metricName, out var baseline)) continue;

            var baseVal = baseline.Value + (baseline.Value * baselineVariance);
            var adjustedMultiplier = multiplier * jitter;

            var matchingOverride = scenario.Overrides
                .FirstOrDefault(o => metricName.Contains(o.FieldName, StringComparison.OrdinalIgnoreCase));

            decimal scenarioVal;
            if (matchingOverride != null)
            {
                scenarioVal = matchingOverride.ShockType switch
                {
                    ShockType.Absolute => baseVal + matchingOverride.ShockValue,
                    ShockType.Relative => baseVal * (1 + matchingOverride.ShockValue / 100m),
                    ShockType.Override => matchingOverride.ShockValue,
                    _ => baseVal + (baseVal * adjustedMultiplier)
                };
            }
            else
            {
                scenarioVal = baseVal + (baseVal * adjustedMultiplier);
            }

            scenarioVal = Math.Round(scenarioVal, 2);
            var delta = Math.Round(scenarioVal - baseVal, 2);
            var deltaPct = baseVal != 0 ? Math.Round(delta / baseVal * 100, 2) : 0;
            var breach = EvaluateBreach(scenarioVal, baseline.Threshold, baseline.HigherIsBetter);

            results.Add(new MetricResult(
                metricName,
                "DMB_RETURNS",
                metricName.ToLowerInvariant().Replace(" ", "_").Replace("(", "").Replace(")", ""),
                Math.Round(baseVal, 2),
                scenarioVal,
                delta,
                deltaPct,
                baseline.Threshold,
                breach
            ));
        }

        return results;
    }

    internal static BreachSeverity EvaluateBreach(decimal value, decimal? threshold, bool higherIsBetter)
    {
        if (!threshold.HasValue) return BreachSeverity.None;

        if (higherIsBetter)
        {
            if (value < threshold.Value * 0.8m) return BreachSeverity.Critical;
            if (value < threshold.Value)         return BreachSeverity.Breach;
            if (value < threshold.Value * 1.1m)  return BreachSeverity.Warning;
        }
        else
        {
            if (value > threshold.Value * 1.3m) return BreachSeverity.Critical;
            if (value > threshold.Value)         return BreachSeverity.Breach;
            if (value > threshold.Value * 0.9m)  return BreachSeverity.Warning;
        }

        return BreachSeverity.None;
    }

    internal static List<BreachAlert> DetectBreaches(List<MetricResult> metrics)
    {
        return metrics
            .Where(m => m.Breach != BreachSeverity.None)
            .Select(m => new BreachAlert(
                m.MetricName, m.ReturnCode, m.FieldName,
                m.BaselineValue, m.ScenarioValue,
                m.Threshold ?? 0,
                m.Breach,
                m.Breach switch
                {
                    BreachSeverity.Critical => $"{m.MetricName} at {m.ScenarioValue:N1} — CRITICAL: far beyond regulatory threshold of {m.Threshold:N1}",
                    BreachSeverity.Breach   => $"{m.MetricName} at {m.ScenarioValue:N1} — breaches regulatory minimum of {m.Threshold:N1}",
                    BreachSeverity.Warning  => $"{m.MetricName} at {m.ScenarioValue:N1} — approaching regulatory threshold of {m.Threshold:N1}",
                    _                       => ""
                }
            ))
            .ToList();
    }

    internal static List<ProFormaField> BuildProFormaFields(List<MetricResult> metrics, ScenarioDefinition scenario)
    {
        var fields = new List<ProFormaField>();

        foreach (var metric in metrics)
        {
            var isOverridden = scenario.Overrides.Any(o =>
                metric.MetricName.Contains(o.FieldName, StringComparison.OrdinalIgnoreCase));

            fields.Add(new ProFormaField(
                metric.FieldName,
                metric.MetricName,
                "Regulatory Metrics",
                metric.MetricName.Contains("₦") ? "Money" : "Percentage",
                metric.BaselineValue,
                metric.ScenarioValue,
                isOverridden,
                !isOverridden,
                metric.Breach
            ));
        }

        fields.Add(new ProFormaField("institution_name", "Institution Name", "General Information", "Text", "Sample Bank Plc", "Sample Bank Plc", false, false, BreachSeverity.None));
        fields.Add(new ProFormaField("reporting_date", "Reporting Date", "General Information", "Date", DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM-dd"), DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM-dd"), false, false, BreachSeverity.None));
        fields.Add(new ProFormaField("total_assets", "Total Assets (₦B)", "Balance Sheet", "Money", 5_230.0m, 5_230.0m, false, false, BreachSeverity.None));
        fields.Add(new ProFormaField("total_liabilities", "Total Liabilities (₦B)", "Balance Sheet", "Money", 4_180.0m, 4_180.0m, false, false, BreachSeverity.None));

        return fields;
    }

    internal record MockInstitution(int Id, string Name, string Type, bool IsSii, decimal BaselineVariance);

    internal static List<MockInstitution> GenerateMockInstitutions() =>
    [
        new(1,  "First Bank of Nigeria",    "DMB", true,   0.05m),
        new(2,  "Zenith Bank Plc",          "DMB", true,   0.08m),
        new(3,  "Access Bank Plc",          "DMB", true,   0.03m),
        new(4,  "UBA Plc",                  "DMB", true,  -0.02m),
        new(5,  "GTBank Plc",               "DMB", true,   0.10m),
        new(6,  "Stanbic IBTC",             "DMB", false,  0.04m),
        new(7,  "Fidelity Bank Plc",        "DMB", false, -0.05m),
        new(8,  "Sterling Bank Plc",        "DMB", false, -0.08m),
        new(9,  "Wema Bank Plc",            "DMB", false, -0.12m),
        new(10, "Unity Bank Plc",           "DMB", false, -0.15m),
        new(11, "LAPO Microfinance",        "MFB", false, -0.10m),
        new(12, "AB Microfinance Bank",     "MFB", false, -0.06m),
        new(13, "Accion Microfinance",      "MFB", false, -0.03m),
        new(14, "Mutual Benefits MFB",      "MFB", false, -0.08m),
        new(15, "FairMoney MFB",            "MFB", false,  0.02m),
        new(16, "NPF Microfinance Bank",    "PMB", false, -0.04m),
        new(17, "Travelex BDC",             "BDC", false,  0.00m),
        new(18, "BOI (Bank of Industry)",   "DFI", true,   0.06m),
        new(19, "NEXIM Bank",               "DFI", false,  0.01m),
        new(20, "Federal Mortgage Bank",    "DFI", false, -0.07m),
    ];
}
