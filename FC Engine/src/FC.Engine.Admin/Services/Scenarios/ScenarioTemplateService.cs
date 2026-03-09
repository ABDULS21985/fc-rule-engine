namespace FC.Engine.Admin.Services.Scenarios;

public class ScenarioTemplateService : IScenarioTemplateService
{
    private readonly List<ScenarioTemplate> _templates;

    public ScenarioTemplateService()
    {
        _templates = BuildTemplates();
    }

    public List<ScenarioTemplate> GetAllTemplates() => _templates;

    public ScenarioTemplate? GetTemplate(string id)
        => _templates.FirstOrDefault(t => t.Id == id);

    public ScenarioDefinition CreateFromTemplate(string templateId)
    {
        var template = GetTemplate(templateId)
            ?? throw new InvalidOperationException($"Template '{templateId}' not found");

        return new ScenarioDefinition
        {
            Name = template.Name,
            Description = template.Description,
            TemplateId = template.Id,
            AffectedModules = [.. template.AffectedModules],
            Overrides = [.. template.DefaultOverrides],
            MacroShocks = [.. template.DefaultShocks],
        };
    }

    private static List<ScenarioTemplate> BuildTemplates() =>
    [
        // 1. Interest Rate Shock (+200bps)
        new()
        {
            Id = "interest_rate_shock",
            Name = "Interest Rate Shock (+200bps)",
            Description = "Simulates a 200 basis point increase in interest rates. Assesses impact on net interest income, bond portfolio valuations, and liquidity coverage ratio across deposit-taking institutions.",
            Category = "Stress Test",
            IconSvg = """<path d="M3 17l6-6 4 4 8-8" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/><path d="M17 3h4v4" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""",
            AffectedModules = ["DMB", "MFB", "PMB"],
            DefaultOverrides =
            [
                new("DMB_NII", "net_interest_income", "Income Statement", ShockType.Relative, -12m, "NII declines as funding costs rise"),
                new("DMB_BOND", "bond_portfolio_value", "Treasury", ShockType.Relative, -8m, "Mark-to-market losses on bond holdings"),
                new("DMB_LCR", "liquidity_coverage_ratio", "Liquidity", ShockType.Relative, -15m, "HQLA outflows from rate-sensitive deposits"),
            ],
            DefaultShocks =
            [
                new("Interest Rate +200bps", "Parallel shift in yield curve of +200 basis points", [
                    new("DMB_NII", "net_interest_income", "Income Statement", ShockType.Relative, -12m),
                    new("DMB_BOND", "bond_portfolio_value", "Treasury", ShockType.Relative, -8m),
                    new("DMB_LCR", "liquidity_coverage_ratio", "Liquidity", ShockType.Relative, -15m),
                    new("DMB_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -5m),
                ]),
            ],
        },

        // 2. FX Depreciation (20%)
        new()
        {
            Id = "fx_depreciation",
            Name = "FX Depreciation (20%)",
            Description = "Models a 20% depreciation in the Naira against major currencies. Evaluates FX position risk, capital adequacy impact, and BDC volume effects.",
            Category = "Stress Test",
            IconSvg = """<circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="2" fill="none"/><path d="M9 9h3l3 6M9 15h6" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round"/>""",
            AffectedModules = ["DMB", "BDC", "IMTO"],
            DefaultOverrides =
            [
                new("DMB_FX", "fx_open_position", "Treasury", ShockType.Relative, 20m, "FX position widens with Naira depreciation"),
                new("DMB_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -8m, "Risk-weighted assets increase for FX exposures"),
                new("DMB_T1", "tier1_capital_ratio", "Capital", ShockType.Relative, -6m, "Tier 1 eroded by FX translation losses"),
            ],
            DefaultShocks =
            [
                new("Naira Depreciation 20%", "Exchange rate moves from ₦1600/$ to ₦1920/$", [
                    new("DMB_FX", "fx_open_position", "Treasury", ShockType.Relative, 20m),
                    new("DMB_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -8m),
                    new("DMB_NII", "net_interest_income", "Income Statement", ShockType.Relative, -3m),
                    new("DMB_T1", "tier1_capital_ratio", "Capital", ShockType.Relative, -6m),
                ]),
            ],
        },

        // 3. NPL Spike (+50%)
        new()
        {
            Id = "npl_spike",
            Name = "NPL Spike (+50%)",
            Description = "Simulates a 50% increase in non-performing loans across the banking sector. Measures cascading effects on capital adequacy, provisioning requirements, and NDIC premium obligations.",
            Category = "Stress Test",
            IconSvg = """<path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" stroke="currentColor" stroke-width="2" fill="none"/><line x1="12" y1="9" x2="12" y2="13" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><line x1="12" y1="17" x2="12.01" y2="17" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>""",
            AffectedModules = ["DMB", "MFB", "PMB", "DFI"],
            DefaultOverrides =
            [
                new("DMB_NPL", "npl_ratio", "Asset Quality", ShockType.Relative, 50m, "NPL ratio increases by 50%"),
                new("DMB_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -12m, "Additional provisions erode capital base"),
                new("DMB_PCR", "provision_coverage_ratio", "Asset Quality", ShockType.Relative, -18m, "Coverage declines as NPLs outpace provisions"),
                new("DMB_PROV", "provisioning_expense", "Income Statement", ShockType.Relative, 45m, "Increased IFRS 9 ECL provisions"),
                new("DMB_NDIC", "ndic_premium", "Regulatory", ShockType.Relative, 25m, "Higher premium from elevated risk profile"),
            ],
            DefaultShocks =
            [
                new("NPL Increase +50%", "Non-performing loans increase by 50% due to economic downturn", [
                    new("DMB_NPL", "npl_ratio", "Asset Quality", ShockType.Relative, 50m),
                    new("DMB_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -12m),
                    new("DMB_PCR", "provision_coverage_ratio", "Asset Quality", ShockType.Relative, -18m),
                    new("DMB_PROV", "provisioning_expense", "Income Statement", ShockType.Relative, 45m),
                    new("DMB_NDIC", "ndic_premium", "Regulatory", ShockType.Relative, 25m),
                    new("DMB_ROE", "return_on_equity", "Profitability", ShockType.Relative, -20m),
                    new("DMB_T1", "tier1_capital_ratio", "Capital", ShockType.Relative, -10m),
                ]),
            ],
        },

        // 4. Deposit Run (30% Outflow)
        new()
        {
            Id = "deposit_run",
            Name = "Deposit Run (30% Outflow)",
            Description = "Models a sudden 30% deposit outflow scenario, testing institutional liquidity resilience. Evaluates LCR, NSFR, and the ability to meet obligations without fire sales.",
            Category = "Stress Test",
            IconSvg = """<path d="M12 2v6l3-3M12 8l-3-3" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/><rect x="3" y="11" width="18" height="10" rx="2" stroke="currentColor" stroke-width="2" fill="none"/><path d="M7 15h10M7 19h6" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round"/>""",
            AffectedModules = ["DMB", "MFB"],
            DefaultOverrides =
            [
                new("DMB_DEP", "total_deposits", "Balance Sheet", ShockType.Relative, -30m, "30% of deposits withdrawn"),
                new("DMB_LCR", "liquidity_coverage_ratio", "Liquidity", ShockType.Relative, -35m, "Severe liquidity stress"),
                new("DMB_NSFR", "net_stable_funding_ratio", "Liquidity", ShockType.Relative, -22m, "Stable funding base eroded"),
                new("DMB_LDR", "loan_to_deposit_ratio", "Balance Sheet", ShockType.Relative, 25m, "Ratio spikes as deposit base shrinks"),
            ],
            DefaultShocks =
            [
                new("Deposit Outflow 30%", "Sudden loss of 30% of total deposits over 30-day period", [
                    new("DMB_DEP", "total_deposits", "Balance Sheet", ShockType.Relative, -30m),
                    new("DMB_LCR", "liquidity_coverage_ratio", "Liquidity", ShockType.Relative, -35m),
                    new("DMB_NSFR", "net_stable_funding_ratio", "Liquidity", ShockType.Relative, -22m),
                    new("DMB_LDR", "loan_to_deposit_ratio", "Balance Sheet", ShockType.Relative, 25m),
                    new("DMB_NII", "net_interest_income", "Income Statement", ShockType.Relative, -8m),
                ]),
            ],
        },

        // 5. Regulatory Capital Increase
        new()
        {
            Id = "regulatory_capital_increase",
            Name = "Regulatory Capital Increase (CAR → 15%)",
            Description = "What-if analysis: CBN raises minimum Capital Adequacy Ratio from current 10% to 15%. Identifies which institutions would fall below the new threshold.",
            Category = "Regulatory Change",
            IconSvg = """<path d="M12 2L3 7v6c0 5.25 3.82 10.13 9 11.25 5.18-1.12 9-6 9-11.25V7l-9-5z" stroke="currentColor" stroke-width="2" fill="none"/><path d="M9 12l2 2 4-4" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""",
            AffectedModules = ["DMB"],
            DefaultOverrides =
            [
                new("DMB_CAR_MIN", "minimum_car_threshold", "Regulatory", ShockType.Override, 15.0m, "New minimum CAR set to 15%"),
            ],
            DefaultShocks =
            [
                new("CAR Threshold Increase", "Minimum CAR moves from 10% to 15%", [
                    new("DMB_CAR_MIN", "minimum_car_threshold", "Regulatory", ShockType.Override, 15.0m),
                    new("DMB_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -2m),
                    new("DMB_T1", "tier1_capital_ratio", "Capital", ShockType.Relative, -2m),
                    new("DMB_ROE", "return_on_equity", "Profitability", ShockType.Relative, -5m),
                ]),
            ],
        },

        // 6. Climate Transition
        new()
        {
            Id = "climate_transition",
            Name = "Climate Transition (Stranded Assets)",
            Description = "Evaluates the impact of stranded fossil fuel assets on bank portfolios. Measures exposure to carbon-intensive sectors and potential write-downs under accelerated green transition.",
            Category = "Climate & ESG",
            IconSvg = """<path d="M12 2a7 7 0 00-7 7c0 5 7 11 7 11s7-6 7-11a7 7 0 00-7-7z" stroke="currentColor" stroke-width="2" fill="none"/><circle cx="12" cy="9" r="2.5" stroke="currentColor" stroke-width="2" fill="none"/>""",
            AffectedModules = ["ESG", "Climate"],
            DefaultOverrides =
            [
                new("ESG_SA", "stranded_assets_exposure", "Climate Risk", ShockType.Relative, 60m, "Stranded assets write-down increases 60%"),
                new("ESG_SC", "sector_concentration", "Climate Risk", ShockType.Relative, 15m, "Fossil fuel sector concentration increases"),
                new("ESG_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -3m, "Climate-related losses reduce capital"),
            ],
            DefaultShocks =
            [
                new("Green Transition Shock", "Accelerated carbon transition strands fossil fuel assets", [
                    new("ESG_SA", "stranded_assets_exposure", "Climate Risk", ShockType.Relative, 60m),
                    new("ESG_SC", "sector_concentration", "Climate Risk", ShockType.Relative, 15m),
                    new("ESG_CAR", "capital_adequacy_ratio", "Capital", ShockType.Relative, -3m),
                    new("ESG_PCR", "provision_coverage_ratio", "Asset Quality", ShockType.Relative, -5m),
                ]),
            ],
        },

        // 7. FATF Grey List Exit
        new()
        {
            Id = "fatf_grey_list_exit",
            Name = "FATF Grey List Exit",
            Description = "Models the positive scenario of Nigeria exiting the FATF grey list. Assesses improved correspondent banking relationships, reduced compliance costs, and enhanced FX flows.",
            Category = "Regulatory Change",
            IconSvg = """<circle cx="12" cy="12" r="10" stroke="currentColor" stroke-width="2" fill="none"/><path d="M8 12l3 3 5-5" stroke="currentColor" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""",
            AffectedModules = ["FATF", "AML"],
            DefaultOverrides =
            [
                new("FATF_FX", "fx_open_position", "Treasury", ShockType.Relative, -10m, "Improved FX flows reduce open positions"),
                new("FATF_NII", "net_interest_income", "Income Statement", ShockType.Relative, 5m, "Better correspondent banking terms"),
                new("FATF_ROE", "return_on_equity", "Profitability", ShockType.Relative, 3m, "Reduced compliance cost burden"),
            ],
            DefaultShocks =
            [
                new("FATF Grey List Exit", "Nigeria exits FATF grey list with improved AML/CFT ratings", [
                    new("FATF_FX", "fx_open_position", "Treasury", ShockType.Relative, -10m),
                    new("FATF_NII", "net_interest_income", "Income Statement", ShockType.Relative, 5m),
                    new("FATF_ROE", "return_on_equity", "Profitability", ShockType.Relative, 3m),
                ]),
            ],
        },
    ];
}
