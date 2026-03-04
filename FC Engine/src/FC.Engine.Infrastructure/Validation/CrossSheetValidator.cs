using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Validation;

public class CrossSheetValidator : ICrossSheetValidator
{
    private readonly IFormulaRepository _formulaRepo;
    private readonly IGenericDataRepository _dataRepo;
    private readonly ITemplateMetadataCache _cache;
    private readonly ExpressionParser _expressionParser = new();

    public CrossSheetValidator(
        IFormulaRepository formulaRepo,
        IGenericDataRepository dataRepo,
        ITemplateMetadataCache cache)
    {
        _formulaRepo = formulaRepo;
        _dataRepo = dataRepo;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ValidationError>> Validate(
        ReturnDataRecord currentRecord,
        int institutionId,
        int returnPeriodId,
        CancellationToken ct = default)
    {
        var rules = await _formulaRepo.GetCrossSheetRulesForTemplate(currentRecord.ReturnCode, ct);
        if (!rules.Any())
            return Array.Empty<ValidationError>();

        var errors = new List<ValidationError>();

        // Cache loaded records to avoid redundant queries
        var recordCache = new Dictionary<string, ReturnDataRecord?>(StringComparer.OrdinalIgnoreCase);
        recordCache[currentRecord.ReturnCode] = currentRecord;

        foreach (var rule in rules)
        {
            if (rule.Expression == null) continue;

            try
            {
                var error = await EvaluateRule(rule, currentRecord, institutionId, returnPeriodId, recordCache, ct);
                if (error != null) errors.Add(error);
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationError
                {
                    RuleId = rule.RuleCode,
                    Field = "CrossSheet",
                    Message = $"Error evaluating cross-sheet rule {rule.RuleCode}: {ex.Message}",
                    Severity = ValidationSeverity.Error,
                    Category = ValidationCategory.CrossSheet
                });
            }
        }

        return errors;
    }

    private async Task<ValidationError?> EvaluateRule(
        Domain.Validation.CrossSheetRule rule,
        ReturnDataRecord currentRecord,
        int institutionId,
        int returnPeriodId,
        Dictionary<string, ReturnDataRecord?> recordCache,
        CancellationToken ct)
    {
        var variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var operand in rule.Operands.OrderBy(o => o.SortOrder))
        {
            // Load the record for this operand's template
            if (!recordCache.ContainsKey(operand.TemplateReturnCode))
            {
                var record = await _dataRepo.GetByInstitutionAndPeriod(
                    operand.TemplateReturnCode, institutionId, returnPeriodId, ct);
                recordCache[operand.TemplateReturnCode] = record;
            }

            var sourceRecord = recordCache[operand.TemplateReturnCode];
            if (sourceRecord == null)
            {
                // Source record not yet submitted — skip this rule
                return null;
            }

            var value = ResolveOperandValue(operand, sourceRecord);
            variables[operand.OperandAlias] = value;
        }

        // Evaluate the expression
        if (rule.Expression is null)
            return null;

        var result = _expressionParser.Evaluate(rule.Expression.Expression, variables);

        if (!result.Passes)
        {
            // Check tolerance
            if (result.RightValue.HasValue)
            {
                var diff = Math.Abs(result.LeftValue - result.RightValue.Value);
                if (diff <= rule.Expression.ToleranceAmount)
                    return null;

                if (rule.Expression.TolerancePercent.HasValue && result.RightValue.Value != 0)
                {
                    var pctDiff = (diff / Math.Abs(result.RightValue.Value)) * 100;
                    if (pctDiff <= rule.Expression.TolerancePercent.Value)
                        return null;
                }
            }

            var operandSummary = string.Join(", ",
                rule.Operands.Select(o => $"{o.OperandAlias}={variables.GetValueOrDefault(o.OperandAlias)}"));

            return new ValidationError
            {
                RuleId = rule.RuleCode,
                Field = "CrossSheet",
                Message = rule.Expression.ErrorMessage
                    ?? $"Cross-sheet rule '{rule.RuleName}' failed: {rule.Expression.Expression} [{operandSummary}]",
                Severity = rule.Severity,
                Category = ValidationCategory.CrossSheet,
                ExpectedValue = result.RightValue?.ToString(),
                ActualValue = result.LeftValue.ToString(),
                ReferencedReturnCode = string.Join(",", rule.Operands.Select(o => o.TemplateReturnCode).Distinct())
            };
        }

        return null;
    }

    private static decimal ResolveOperandValue(Domain.Validation.CrossSheetRuleOperand operand, ReturnDataRecord record)
    {
        if (record.Category == StructuralCategory.FixedRow)
        {
            var row = record.Rows.FirstOrDefault();
            return row?.GetDecimal(operand.FieldName) ?? 0m;
        }

        // For MultiRow/ItemCoded, apply aggregate function or filter by item code
        var rows = record.Rows.AsEnumerable();

        if (!string.IsNullOrEmpty(operand.FilterItemCode))
        {
            rows = rows.Where(r => r.RowKey == operand.FilterItemCode);
        }

        var values = rows.Select(r => r.GetDecimal(operand.FieldName) ?? 0m).ToList();

        if (!values.Any()) return 0m;

        return (operand.AggregateFunction?.ToUpperInvariant()) switch
        {
            "SUM" => values.Sum(),
            "COUNT" => values.Count,
            "MAX" => values.Max(),
            "MIN" => values.Min(),
            "AVG" => values.Average(),
            null or "" => values.First(), // No aggregate: take first value
            _ => values.Sum()
        };
    }
}
