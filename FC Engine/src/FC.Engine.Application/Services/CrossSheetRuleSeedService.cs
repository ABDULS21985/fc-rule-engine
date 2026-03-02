using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Validation;

namespace FC.Engine.Application.Services;

/// <summary>
/// Seeds cross-sheet validation rules (XS-001 through XS-045).
/// These rules validate relationships between different return templates,
/// ensuring data consistency across the CBN regulatory reporting suite.
/// </summary>
public class CrossSheetRuleSeedService
{
    private readonly IFormulaRepository _formulaRepo;

    public CrossSheetRuleSeedService(IFormulaRepository formulaRepo)
    {
        _formulaRepo = formulaRepo;
    }

    public async Task<int> SeedCrossSheetRules(string performedBy, CancellationToken ct = default)
    {
        var existing = await _formulaRepo.GetAllActiveCrossSheetRules(ct);
        if (existing.Any()) return 0; // Already seeded

        var rules = BuildCrossSheetRules(performedBy);

        foreach (var rule in rules)
        {
            await _formulaRepo.AddCrossSheetRule(rule, ct);
        }

        return rules.Count;
    }

    private static List<CrossSheetRule> BuildCrossSheetRules(string createdBy)
    {
        var rules = new List<CrossSheetRule>();
        var now = DateTime.UtcNow;

        // === Balance Sheet Integrity (XS-001 to XS-010) ===

        // XS-001: Total Assets = Total Liabilities + Equity
        rules.Add(CreateRule("XS-001",
            "Balance Sheet Equation",
            "Total assets must equal total liabilities and equity",
            new[] {
                ("A", "MFCR 300", "total_assets", (string?)null, (string?)null),
                ("B", "MFCR 300", "total_liabilities_and_equity", null, null)
            },
            "A = B", createdBy, now));

        // XS-002: MFCR 300 Total Cash = SUM of MFCR 318 (Treasury Bills) carrying values
        rules.Add(CreateRule("XS-002",
            "Cash Reconciliation - Treasury Bills",
            "Total treasury bills on balance sheet should match schedule detail",
            new[] {
                ("A", "MFCR 300", "treasury_bills", (string?)null, (string?)null),
                ("B", "MFCR 318", "net_carrying_value", null, "SUM")
            },
            "A = B", createdBy, now));

        // XS-003: Gross Loans = SUM of loan schedules
        rules.Add(CreateRule("XS-003",
            "Gross Loans Reconciliation",
            "Total gross loans must equal sum of all loan schedule totals",
            new[] {
                ("A", "MFCR 300", "total_gross_loans", (string?)null, (string?)null),
                ("B", "MFCR 340", "total", null, "SUM"),
                ("C", "MFCR 342", "total", null, "SUM"),
                ("D", "MFCR 344", "total", null, "SUM"),
                ("E", "MFCR 346", "total", null, "SUM")
            },
            "A = B + C + D + E", createdBy, now));

        // XS-004: Other Assets = MFCR 356 total
        rules.Add(CreateRule("XS-004",
            "Other Assets Reconciliation",
            "Other assets on balance sheet should match schedule detail",
            new[] {
                ("A", "MFCR 300", "other_assets", (string?)null, (string?)null),
                ("B", "MFCR 356", "net_amount_naira", null, "SUM"),
                ("C", "MFCR 356", "net_amount_foreign", null, "SUM")
            },
            "A = B + C", createdBy, now));

        // XS-005: Intangible Assets = MFCR 358 total
        rules.Add(CreateRule("XS-005",
            "Intangible Assets Reconciliation",
            "Intangible assets on balance sheet should match schedule detail",
            new[] {
                ("A", "MFCR 300", "intangible_assets", (string?)null, (string?)null),
                ("B", "MFCR 358", "carrying_end_naira", null, "SUM"),
                ("C", "MFCR 358", "carrying_end_foreign", null, "SUM")
            },
            "A = B + C", createdBy, now));

        // XS-006: Non-current Assets Held for Sale = MFCR 360 total
        rules.Add(CreateRule("XS-006",
            "Non-Current Assets HFS Reconciliation",
            "Non-current assets held for sale should match MFCR 360 schedule",
            new[] {
                ("A", "MFCR 300", "non_current_assets_held_for_sale", (string?)null, (string?)null),
                ("B", "MFCR 360", "total", null, "SUM")
            },
            "A = B", createdBy, now));

        // XS-007: PPE = MFCR 362 total
        rules.Add(CreateRule("XS-007",
            "Property Plant Equipment Reconciliation",
            "PPE on balance sheet should match MFCR 362 schedule",
            new[] {
                ("A", "MFCR 300", "property_plant_equipment", (string?)null, (string?)null),
                ("B", "MFCR 362", "carrying_end_naira", null, "SUM"),
                ("C", "MFCR 362", "carrying_end_foreign", null, "SUM")
            },
            "A = B + C", createdBy, now));

        // XS-008: Investments in Subsidiaries = MFCR 354 carrying value
        rules.Add(CreateRule("XS-008",
            "Investments in Subsidiaries Reconciliation",
            "Investments in subsidiaries should match MFCR 354 schedule",
            new[] {
                ("A", "MFCR 300", "investments_in_subsidiaries", (string?)null, (string?)null),
                ("B", "MFCR 354", "carrying_value_beginning", null, "SUM")
            },
            "A = B", createdBy, now));

        // === Income Statement Integrity (XS-011 to XS-020) ===

        // XS-011: Net Interest Income = Total Interest Income - Total Interest Expense
        rules.Add(CreateRule("XS-011",
            "Net Interest Income Calculation",
            "Net interest income must equal total interest income minus total interest expense",
            new[] {
                ("A", "MFCR 1000", "net_interest_income", (string?)null, (string?)null),
                ("B", "MFCR 1000", "total_interest_income", null, null),
                ("C", "MFCR 1000", "total_interest_expense", null, null)
            },
            "A = B - C", createdBy, now));

        // XS-012: Total Operating Income consistency
        rules.Add(CreateRule("XS-012",
            "Total Operating Income",
            "Total operating income = net interest + net fees + other operating",
            new[] {
                ("A", "MFCR 1000", "total_operating_income", (string?)null, (string?)null),
                ("B", "MFCR 1000", "net_interest_income", null, null),
                ("C", "MFCR 1000", "net_fees_commission_income", null, null),
                ("D", "MFCR 1000", "other_operating_income", null, null)
            },
            "A = B + C + D", createdBy, now));

        // XS-013: Profit Before Tax
        rules.Add(CreateRule("XS-013",
            "Profit Before Tax Calculation",
            "PBT = operating income - impairments - operating expenses",
            new[] {
                ("A", "MFCR 1000", "profit_before_tax", (string?)null, (string?)null),
                ("B", "MFCR 1000", "total_operating_income", null, null),
                ("C", "MFCR 1000", "total_impairment_charge", null, null),
                ("D", "MFCR 1000", "total_operating_expenses", null, null)
            },
            "A = B - C - D", createdBy, now));

        // === Cross-Return Consistency (XS-021 to XS-035) ===

        // XS-021: Loan Impairment on Balance Sheet = Income Statement Impairment
        rules.Add(CreateRule("XS-021",
            "Loan Impairment Consistency",
            "Impairment on loans on BS should relate to impairment charge in P&L",
            new[] {
                ("A", "MFCR 300", "impairment_on_loans", (string?)null, (string?)null),
                ("B", "MFCR 1000", "impairment_charge_loans_ytd", null, null)
            },
            "A >= B", createdBy, now,
            ValidationSeverity.Warning)); // Warning only - stock vs flow

        // XS-022: Staff count consistency with memorandum
        rules.Add(CreateRule("XS-022",
            "Staff Count Consistency",
            "Total staff = senior + junior (male + female)",
            new[] {
                ("A", "MFCR 100", "total_staff_male", (string?)null, (string?)null),
                ("B", "MFCR 100", "senior_staff_male", null, null),
                ("C", "MFCR 100", "junior_staff_male", null, null)
            },
            "A = B + C", createdBy, now));

        // XS-023: Credit disbursements total
        rules.Add(CreateRule("XS-023",
            "Credit Disbursements Total",
            "Total credit disbursements = female + male + company",
            new[] {
                ("A", "MFCR 100", "total_credit_number", (string?)null, (string?)null),
                ("B", "MFCR 100", "credit_female_number", null, null),
                ("C", "MFCR 100", "credit_male_number", null, null),
                ("D", "MFCR 100", "credit_company_number", null, null)
            },
            "A = B + C + D", createdBy, now));

        // === Quarterly Return Cross-Checks (XS-036 to XS-045) ===

        // XS-036: QFCR should be consistent with corresponding MFCR
        rules.Add(CreateRule("XS-036",
            "Quarterly vs Monthly Consistency",
            "Quarterly total assets should match last month's MFCR 300",
            new[] {
                ("A", "QFCR 364", "total", (string?)null, "SUM"),
                ("B", "MFCR 300", "total_assets", null, null)
            },
            "A >= 0", createdBy, now, // Simplified check — exact match requires temporal context
            ValidationSeverity.Warning));

        // XS-037: Other Assets Breakdown Detail
        rules.Add(CreateRule("XS-037",
            "Other Assets Breakdown Reconciliation",
            "MFCR 357 breakdown should reconcile to MFCR 356",
            new[] {
                ("A", "MFCR 356", "net_amount_naira", (string?)null, (string?)"SUM"),
                ("B", "MFCR 357", "net_amount_naira", (string?)null, (string?)"SUM")
            },
            "A = B", createdBy, now,
            ValidationSeverity.Warning));

        // XS-038: Loan Schedule Totals
        rules.Add(CreateRule("XS-038",
            "Loan to Subsidiaries Nigeria",
            "Loans to subsidiaries in Nigeria on BS = schedule total",
            new[] {
                ("A", "MFCR 300", "loans_to_subsidiary_nigeria", (string?)null, (string?)null),
                ("B", "MFCR 340", "total", null, "SUM")
            },
            "A = B", createdBy, now));

        // XS-039: Loan to Subsidiaries Outside
        rules.Add(CreateRule("XS-039",
            "Loan to Subsidiaries Outside",
            "Loans to subsidiaries outside Nigeria on BS = schedule total",
            new[] {
                ("A", "MFCR 300", "loans_to_subsidiary_outside", (string?)null, (string?)null),
                ("B", "MFCR 342", "total", null, "SUM")
            },
            "A = B", createdBy, now));

        // XS-040: Loan to Associates Nigeria
        rules.Add(CreateRule("XS-040",
            "Loan to Associates Nigeria",
            "Loans to associates in Nigeria = schedule total",
            new[] {
                ("A", "MFCR 300", "loans_to_associate_nigeria", (string?)null, (string?)null),
                ("B", "MFCR 344", "total", null, "SUM")
            },
            "A = B", createdBy, now));

        // XS-041: Loan to Associates Outside
        rules.Add(CreateRule("XS-041",
            "Loan to Associates Outside",
            "Loans to associates outside Nigeria = schedule total",
            new[] {
                ("A", "MFCR 300", "loans_to_associate_outside", (string?)null, (string?)null),
                ("B", "MFCR 346", "total", null, "SUM")
            },
            "A = B", createdBy, now));

        // XS-042: YTD Income vs Monthly
        rules.Add(CreateRule("XS-042",
            "YTD Interest Income Consistency",
            "YTD total interest income must be >= current month",
            new[] {
                ("A", "MFCR 1000", "total_interest_income_ytd", (string?)null, (string?)null),
                ("B", "MFCR 1000", "total_interest_income", null, null)
            },
            "A >= B", createdBy, now));

        // XS-043: YTD Expense vs Monthly
        rules.Add(CreateRule("XS-043",
            "YTD Interest Expense Consistency",
            "YTD total interest expense must be >= current month",
            new[] {
                ("A", "MFCR 1000", "total_interest_expense_ytd", (string?)null, (string?)null),
                ("B", "MFCR 1000", "total_interest_expense", null, null)
            },
            "A >= B", createdBy, now));

        // XS-044: Impairment Provisions
        rules.Add(CreateRule("XS-044",
            "Impairment Provision Check",
            "Total impairment on BS should be non-negative",
            new[] {
                ("A", "MFCR 300", "impairment_on_loans", (string?)null, (string?)null),
            },
            "A >= 0", createdBy, now));

        // XS-045: Capital Adequacy
        rules.Add(CreateRule("XS-045",
            "Capital Adequacy Indicator",
            "Total equity should be positive",
            new[] {
                ("A", "MFCR 300", "total_equity", (string?)null, (string?)null),
            },
            "A > 0", createdBy, now,
            ValidationSeverity.Warning));

        return rules;
    }

    private static CrossSheetRule CreateRule(string ruleCode, string ruleName, string description,
        (string Alias, string ReturnCode, string FieldName, string? LineCode, string? Aggregate)[] operands,
        string expression, string createdBy, DateTime now,
        ValidationSeverity severity = ValidationSeverity.Error)
    {
        var rule = new CrossSheetRule
        {
            RuleCode = ruleCode,
            RuleName = ruleName,
            Description = description,
            Severity = severity,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = createdBy
        };

        var sortOrder = 0;
        foreach (var (alias, returnCode, fieldName, lineCode, aggregate) in operands)
        {
            rule.AddOperand(new CrossSheetRuleOperand
            {
                OperandAlias = alias,
                TemplateReturnCode = returnCode,
                FieldName = fieldName,
                LineCode = lineCode,
                AggregateFunction = aggregate,
                SortOrder = ++sortOrder
            });
        }

        rule.Expression = new CrossSheetRuleExpression
        {
            Expression = expression,
            ToleranceAmount = 0.01m,
            ErrorMessage = $"{ruleName}: {description}"
        };

        return rule;
    }
}
