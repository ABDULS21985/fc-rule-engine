using System.Text.Json;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Application.Services;

/// <summary>
/// Seeds intra-sheet formulas by analyzing table column patterns from schema.sql.
/// Derives Sum, Difference, and Equals formulas from naming conventions:
/// - total_* columns are SUM formulas over preceding component columns
/// - net_* columns are DIFFERENCE formulas (gross - deduction)
/// - carrying_amount_* = book_value_* - impairment_*
/// - *_ytd formulas mirror their non-YTD counterparts
/// </summary>
public class FormulaSeedService
{
    private readonly ITemplateRepository _templateRepo;

    public FormulaSeedService(ITemplateRepository templateRepo)
    {
        _templateRepo = templateRepo;
    }

    public async Task<FormulaSeedResult> SeedFormulasFromSchema(
        string schemaFilePath, string performedBy, CancellationToken ct = default)
    {
        var sql = await File.ReadAllTextAsync(schemaFilePath, ct);
        var tables = ParseCreateTables(sql);
        var result = new FormulaSeedResult();

        foreach (var table in tables)
        {
            if (SeedService.SkipTables.Contains(table.TableName))
                continue;

            var returnCode = SchemaTemplateConventions.DeriveReturnCode(table.TableName);
            if (string.IsNullOrEmpty(returnCode)) continue;

            try
            {
                var template = await _templateRepo.GetByReturnCode(returnCode, ct);
                if (template == null) continue;

                var publishedVersion = template.CurrentPublishedVersion;
                if (publishedVersion == null) continue;

                // Skip if formulas already seeded
                if (publishedVersion.IntraSheetFormulas.Any()) continue;

                var columns = table.Columns.Select(c => c.Name).ToList();
                var formulas = DeriveFormulas(table, returnCode);

                var sortOrder = 0;
                foreach (var f in formulas)
                {
                    f.TemplateVersionId = publishedVersion.Id;
                    f.SortOrder = ++sortOrder;
                    f.CreatedAt = DateTime.UtcNow;
                    f.CreatedBy = performedBy;
                    publishedVersion.AddFormula(f);
                }

                if (formulas.Any())
                {
                    await _templateRepo.Update(template, ct);
                    result.TemplatesWithFormulas.Add(returnCode);
                    result.TotalFormulasCreated += formulas.Count;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{returnCode}: {ex.Message}");
            }
        }

        return result;
    }

    private static List<IntraSheetFormula> DeriveFormulas(ParsedTable table, string returnCode)
    {
        var formulas = new List<IntraSheetFormula>();
        var columns = table.Columns;
        var colNames = columns.Select(c => c.Name.ToLowerInvariant()).ToHashSet();
        var ruleIndex = 0;

        // Build sections: group consecutive non-total columns, then the total
        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var name = col.Name.ToLowerInvariant();

            // Skip system columns
            if (name is "id" or "submission_id" or "created_at" or "updated_at"
                or "serial_no" or "item_code")
                continue;

            // Pattern 1: total_* = SUM of preceding component columns in same section
            if (name.StartsWith("total_"))
            {
                var operands = FindSumOperands(columns, i, name);
                if (operands.Count >= 2)
                {
                    formulas.Add(CreateFormula(
                        $"{returnCode.Replace(" ", "-")}-SUM-{++ruleIndex:D3}",
                        $"{col.Name} = SUM of components",
                        FormulaType.Sum,
                        col.Name, col.LineCode,
                        operands));
                }
            }

            // Pattern 2: net_* = gross - deduction
            if (name.StartsWith("net_"))
            {
                var diff = FindDifferenceOperands(columns, colNames, name, i);
                if (diff != null)
                {
                    formulas.Add(CreateFormula(
                        $"{returnCode.Replace(" ", "-")}-DIFF-{++ruleIndex:D3}",
                        $"{col.Name} = {diff.Value.Minuend} - {diff.Value.Subtrahend}",
                        FormulaType.Difference,
                        col.Name, col.LineCode,
                        new List<string> { diff.Value.Minuend, diff.Value.Subtrahend }));
                }
            }

            // Pattern 3: carrying_amount_* = book_value_* - impairment_*
            if (name.StartsWith("carrying_amount_"))
            {
                var suffix = name["carrying_amount_".Length..];
                var bookValue = $"book_value_{suffix}";
                var impairment = $"impairment_{suffix}";

                if (colNames.Contains(bookValue) && colNames.Contains(impairment))
                {
                    formulas.Add(CreateFormula(
                        $"{returnCode.Replace(" ", "-")}-DIFF-{++ruleIndex:D3}",
                        $"{col.Name} = {bookValue} - {impairment}",
                        FormulaType.Difference,
                        col.Name, col.LineCode,
                        new List<string> { bookValue, impairment }));
                }
            }

            // Pattern 4: profit_after_tax = profit_before_tax - tax_expense
            if (name == "profit_after_tax" &&
                colNames.Contains("profit_before_tax") && colNames.Contains("tax_expense"))
            {
                formulas.Add(CreateFormula(
                    $"{returnCode.Replace(" ", "-")}-DIFF-{++ruleIndex:D3}",
                    "profit_after_tax = profit_before_tax - tax_expense",
                    FormulaType.Difference,
                    "profit_after_tax", col.LineCode,
                    new List<string> { "profit_before_tax", "tax_expense" }));
            }

            // Pattern 5: total_comprehensive_income = profit_after_tax + other_comprehensive_income
            if (name == "total_comprehensive_income" &&
                colNames.Contains("profit_after_tax") && colNames.Contains("other_comprehensive_income"))
            {
                formulas.Add(CreateFormula(
                    $"{returnCode.Replace(" ", "-")}-SUM-{++ruleIndex:D3}",
                    "total_comprehensive_income = profit_after_tax + other_comprehensive_income",
                    FormulaType.Sum,
                    "total_comprehensive_income", col.LineCode,
                    new List<string> { "profit_after_tax", "other_comprehensive_income" }));
            }
        }

        // Pattern 6: YTD mirrors — derive _ytd versions of existing formulas
        var ytdFormulas = new List<IntraSheetFormula>();
        foreach (var formula in formulas)
        {
            var targetYtd = formula.TargetFieldName + "_ytd";
            if (colNames.Contains(targetYtd))
            {
                var operands = JsonSerializer.Deserialize<List<string>>(formula.OperandFields) ?? new();
                var ytdOperands = operands
                    .Select(o => colNames.Contains(o + "_ytd") ? o + "_ytd" : o)
                    .ToList();

                // Only create if all YTD operands exist
                if (ytdOperands.All(colNames.Contains))
                {
                    ytdFormulas.Add(CreateFormula(
                        $"{returnCode.Replace(" ", "-")}-{(formula.FormulaType == FormulaType.Sum ? "SUM" : "DIFF")}-{++ruleIndex:D3}",
                        $"{targetYtd} (YTD mirror)",
                        formula.FormulaType,
                        targetYtd, null,
                        ytdOperands));
                }
            }
        }
        formulas.AddRange(ytdFormulas);

        return formulas;
    }

    private static List<string> FindSumOperands(List<ParsedColumn> columns, int totalIdx, string totalName)
    {
        var operands = new List<string>();
        var baseName = totalName;

        // Special cases for known total patterns
        var knownPatterns = GetKnownSumPatterns();
        if (knownPatterns.TryGetValue(totalName, out var knownOperands))
            return knownOperands;

        // Heuristic: Look backwards from the total column to find consecutive
        // non-total, non-system columns that belong to the same section
        for (int j = totalIdx - 1; j >= 0; j--)
        {
            var name = columns[j].Name.ToLowerInvariant();

            // Stop at system columns or another total
            if (name is "id" or "submission_id" or "created_at" or "updated_at"
                or "serial_no" or "item_code")
                break;

            // Stop at another total (different section)
            if (name.StartsWith("total_") && name != totalName)
                break;

            // Stop at net_ columns (these are computed themselves)
            if (name.StartsWith("net_") || name.StartsWith("carrying_"))
                break;

            // Skip _ytd counterparts in the section scan
            if (name.EndsWith("_ytd"))
                continue;

            operands.Insert(0, columns[j].Name);
        }

        return operands;
    }

    private static (string Minuend, string Subtrahend)? FindDifferenceOperands(
        List<ParsedColumn> columns, HashSet<string> colNames, string netName, int colIdx)
    {
        // net_interest_income = total_interest_income - total_interest_expense
        // net_fees_commission_income = total_fees_commission_income - fees_commission_expenses
        // net_amount_naira = amount_naira - impairment_naira
        // net_amount_foreign = amount_foreign - impairment_foreign

        var suffix = netName["net_".Length..];

        // Pattern: net_X = total_X_income - total_X_expense (or X - X_expense)
        if (colNames.Contains($"total_{suffix}") && colNames.Contains($"total_{suffix.Replace("income", "expense")}"))
            return ($"total_{suffix}", $"total_{suffix.Replace("income", "expense")}");

        // Pattern: net_X = total_fees_commission_income - fees_commission_expenses
        if (suffix == "fees_commission_income" &&
            colNames.Contains("total_fees_commission_income") && colNames.Contains("fees_commission_expenses"))
            return ("total_fees_commission_income", "fees_commission_expenses");

        // Pattern: net_amount_naira = amount_naira - impairment_naira
        if (suffix.StartsWith("amount_"))
        {
            var currencySuffix = suffix["amount_".Length..]; // "naira" or "foreign"
            var amount = $"amount_{currencySuffix}";
            var impairment = $"impairment_{currencySuffix}";
            if (colNames.Contains(amount) && colNames.Contains(impairment))
                return (amount, impairment);
        }

        // Pattern: net_carrying_value = book_value - impairment
        if (suffix == "carrying_value" && colNames.Contains("book_value") && colNames.Contains("impairment"))
            return ("book_value", "impairment");

        // Look at two preceding columns as minuend and subtrahend
        if (colIdx >= 2)
        {
            var prev1 = columns[colIdx - 1].Name.ToLowerInvariant();
            var prev2 = columns[colIdx - 2].Name.ToLowerInvariant();
            if (!prev1.StartsWith("total_") && !prev1.StartsWith("net_") &&
                !prev2.StartsWith("total_") && !prev2.StartsWith("net_") &&
                colNames.Contains(prev2) && colNames.Contains(prev1))
            {
                // Assume: net = prev2 - prev1 (gross first, then deduction)
                return (columns[colIdx - 2].Name, columns[colIdx - 1].Name);
            }
        }

        return null;
    }

    private static Dictionary<string, List<string>> GetKnownSumPatterns()
    {
        return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["total_cash"] = new() { "cash_notes", "cash_coins" },
            ["total_due_from_banks_nigeria"] = new() { "due_from_banks_nigeria", "uncleared_effects", "due_from_other_fi" },
            ["total_due_from_banks_outside"] = new() { "due_from_banks_oecd", "due_from_banks_non_oecd" },
            ["total_money_at_call"] = new() { "money_at_call_secured", "money_at_call_unsecured" },
            ["total_bank_placements"] = new() { "placements_secured_banks", "placements_unsecured_banks", "placements_discount_houses" },
            ["total_securities"] = new() { "treasury_bills", "fgn_bonds", "state_govt_bonds", "local_govt_bonds",
                "corporate_bonds", "other_bonds", "treasury_certificates", "cbn_registered_certificates",
                "certificates_of_deposit", "commercial_papers" },
            ["total_gross_loans"] = new() { "loans_to_fi_nigeria", "loans_to_subsidiary_nigeria",
                "loans_to_subsidiary_outside", "loans_to_associate_nigeria", "loans_to_associate_outside",
                "loans_to_other_entities_outside", "loans_to_government", "loans_to_other_customers" },
            ["total_net_loans"] = new() { "total_gross_loans", "impairment_on_loans" }, // will be overridden as DIFF
            ["total_borrowings"] = new() { "borrowings_from_banks", "borrowings_from_other_fc",
                "borrowings_from_other_fi", "borrowings_from_individuals" },
            ["total_equity"] = new() { "paid_up_capital", "share_premium", "retained_earnings",
                "statutory_reserve", "other_reserves", "revaluation_reserve", "minority_interest" },
            ["total_liabilities_and_equity"] = new() { "total_liabilities", "total_equity" },
            ["total_interest_income"] = new() { "interest_income_loans", "interest_income_leases",
                "interest_income_govt_securities", "interest_income_bank_placements",
                "discount_income", "interest_income_others" },
            ["total_interest_expense"] = new() { "interest_on_borrowings", "interest_expense_others" },
            ["total_fees_commission_income"] = new() { "commissions", "credit_related_fee_income", "other_fees" },
            ["total_impairment_charge"] = new() { "impairment_charge_loans", "impairment_charge_others" },
            ["total_operating_expenses"] = new() { "staff_costs", "depreciation_amortization", "other_operating_expenses" },
            ["total_credit_number"] = new() { "credit_female_number", "credit_male_number", "credit_company_number" },
            ["total_credit_value"] = new() { "credit_female_value", "credit_male_value", "credit_company_value" },
            ["total_borrowings_number"] = new() { "borrowings_female_number", "borrowings_male_number", "borrowings_company_number" },
            ["total_borrowings_value"] = new() { "borrowings_female_value", "borrowings_male_value", "borrowings_company_value" },
            ["total_staff_male"] = new() { "senior_staff_male", "junior_staff_male" },
            ["total_staff_female"] = new() { "senior_staff_female", "junior_staff_female" },
        };
    }

    private static IntraSheetFormula CreateFormula(string ruleCode, string ruleName,
        FormulaType type, string target, string? lineCode, List<string> operands)
    {
        return new IntraSheetFormula
        {
            RuleCode = ruleCode,
            RuleName = ruleName,
            FormulaType = type,
            TargetFieldName = target,
            TargetLineCode = lineCode,
            OperandFields = JsonSerializer.Serialize(operands),
            ToleranceAmount = 0.01m, // Allow 1 cent rounding
            Severity = ValidationSeverity.Error,
            IsActive = true
        };
    }

    // Override special total columns that are actually DIFFERENCE formulas
    private static bool IsDifferenceTotal(string totalName) =>
        totalName is "total_net_loans" or "total_net_loans_ytd";

    private static List<ParsedTable> ParseCreateTables(string sql)
    {
        var tables = new List<ParsedTable>();
        var tableRegex = new Regex(
            @"CREATE\s+TABLE\s+(\w+)\s*\((.*?)\);",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        foreach (Match match in tableRegex.Matches(sql))
        {
            var tableName = match.Groups[1].Value;
            var body = match.Groups[2].Value;
            var columns = ParseColumns(body);
            tables.Add(new ParsedTable(tableName, columns));
        }

        return tables;
    }

    private static List<ParsedColumn> ParseColumns(string body)
    {
        var columns = new List<ParsedColumn>();
        var lines = body.Split('\n')
            .Select(l => l.Trim().TrimEnd(','))
            .Where(l => !string.IsNullOrWhiteSpace(l));

        foreach (var line in lines)
        {
            if (line.StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("FOREIGN", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("CONSTRAINT", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                line.StartsWith(")", StringComparison.Ordinal))
                continue;

            var colMatch = Regex.Match(line,
                @"^(\w+)\s+(SERIAL|INT|INTEGER|BIGINT|NUMERIC\(\d+,\d+\)|VARCHAR\(\d+\)|TEXT|DATE|TIMESTAMP|BOOLEAN)",
                RegexOptions.IgnoreCase);

            if (!colMatch.Success) continue;

            var colName = colMatch.Groups[1].Value;
            var sqlType = colMatch.Groups[2].Value.ToUpperInvariant();
            if (sqlType == "SERIAL") sqlType = "INT";

            string? lineCode = null;
            var commentMatch = Regex.Match(line, @"--\s*(\d{4,5})");
            if (commentMatch.Success) lineCode = commentMatch.Groups[1].Value;

            columns.Add(new ParsedColumn(colName, sqlType, lineCode));
        }

        return columns;
    }

    // Reuse from SeedService
    internal static readonly HashSet<string> SkipTables = SeedService.SkipTables;
}

public class FormulaSeedResult
{
    public List<string> TemplatesWithFormulas { get; set; } = new();
    public int TotalFormulasCreated { get; set; }
    public List<string> Errors { get; set; } = new();
}
