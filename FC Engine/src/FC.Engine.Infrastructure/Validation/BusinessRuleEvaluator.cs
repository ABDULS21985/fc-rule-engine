using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Infrastructure.Validation;

public class BusinessRuleEvaluator : IBusinessRuleEvaluator
{
    private readonly IFormulaRepository _formulaRepo;
    private readonly ITemplateMetadataCache _cache;
    private readonly ExpressionParser _expressionParser = new();

    public BusinessRuleEvaluator(
        IFormulaRepository formulaRepo,
        ITemplateMetadataCache cache)
    {
        _formulaRepo = formulaRepo;
        _cache = cache;
    }

    public async Task<IReadOnlyList<ValidationError>> Evaluate(
        ReturnDataRecord record,
        Submission submission,
        CancellationToken ct = default)
    {
        var rules = await _formulaRepo.GetBusinessRulesForTemplate(record.ReturnCode, ct);
        if (!rules.Any())
            return Array.Empty<ValidationError>();

        var errors = new List<ValidationError>();

        foreach (var rule in rules)
        {
            try
            {
                var ruleErrors = rule.RuleType switch
                {
                    "Completeness" => EvaluateCompleteness(rule, record),
                    "DateCheck" => EvaluateDateCheck(rule, record, submission),
                    "ThresholdCheck" => EvaluateThresholdCheck(rule, record),
                    "Custom" => EvaluateCustom(rule, record),
                    _ => Enumerable.Empty<ValidationError>()
                };

                errors.AddRange(ruleErrors);
            }
            catch (Exception ex)
            {
                errors.Add(new ValidationError
                {
                    RuleId = rule.RuleCode,
                    Field = "Business",
                    Message = $"Error evaluating business rule {rule.RuleCode}: {ex.Message}",
                    Severity = ValidationSeverity.Error,
                    Category = ValidationCategory.Business
                });
            }
        }

        return errors;
    }

    private static IEnumerable<ValidationError> EvaluateCompleteness(
        Domain.Validation.BusinessRule rule, ReturnDataRecord record)
    {
        // Check that specific fields are present and non-null across all rows
        var targetFields = !string.IsNullOrEmpty(rule.AppliesToFields)
            ? JsonSerializer.Deserialize<List<string>>(rule.AppliesToFields) ?? new()
            : new List<string>();

        if (!targetFields.Any()) yield break;

        // Only check fields that exist in the template schema (present in at least one row)
        var knownFields = record.Rows
            .SelectMany(r => r.AllFields.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in record.Rows)
        {
            foreach (var fieldName in targetFields)
            {
                if (!knownFields.Contains(fieldName)) continue;

                var value = row.GetValue(fieldName);
                if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                {
                    yield return new ValidationError
                    {
                        RuleId = rule.RuleCode,
                        Field = fieldName,
                        Message = $"Business rule '{rule.RuleName}': Field '{fieldName}' must be provided (row: {row.RowKey ?? "1"})",
                        Severity = rule.Severity,
                        Category = ValidationCategory.Business
                    };
                }
            }
        }
    }

    private static IEnumerable<ValidationError> EvaluateDateCheck(
        Domain.Validation.BusinessRule rule, ReturnDataRecord record, Submission submission)
    {
        var targetFields = !string.IsNullOrEmpty(rule.AppliesToFields)
            ? JsonSerializer.Deserialize<List<string>>(rule.AppliesToFields) ?? new()
            : new List<string>();

        foreach (var row in record.Rows)
        {
            foreach (var fieldName in targetFields)
            {
                var dateValue = row.GetDateTime(fieldName);
                if (dateValue == null) continue;

                // Check that date is not in the future
                if (dateValue > DateTime.UtcNow)
                {
                    yield return new ValidationError
                    {
                        RuleId = rule.RuleCode,
                        Field = fieldName,
                        Message = $"Business rule '{rule.RuleName}': Date '{fieldName}' cannot be in the future",
                        Severity = rule.Severity,
                        Category = ValidationCategory.Business,
                        ActualValue = dateValue.Value.ToString("yyyy-MM-dd")
                    };
                }
            }
        }
    }

    private IEnumerable<ValidationError> EvaluateThresholdCheck(
        Domain.Validation.BusinessRule rule, ReturnDataRecord record)
    {
        if (string.IsNullOrWhiteSpace(rule.Expression)) yield break;

        // Build variables from first row (FixedRow) or each row
        foreach (var row in record.Rows)
        {
            var variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in row.AllFields)
            {
                var dec = row.GetDecimal(kvp.Key);
                if (dec.HasValue)
                    variables[kvp.Key] = dec.Value;
            }

            var result = _expressionParser.Evaluate(rule.Expression, variables);
            if (!result.Passes)
            {
                yield return new ValidationError
                {
                    RuleId = rule.RuleCode,
                    Field = "Business",
                    Message = $"Business rule '{rule.RuleName}': Threshold check failed — {rule.Expression} (row: {row.RowKey ?? "1"})",
                    Severity = rule.Severity,
                    Category = ValidationCategory.Business,
                    ExpectedValue = result.RightValue?.ToString(),
                    ActualValue = result.LeftValue.ToString()
                };
            }
        }
    }

    private IEnumerable<ValidationError> EvaluateCustom(
        Domain.Validation.BusinessRule rule, ReturnDataRecord record)
    {
        if (string.IsNullOrWhiteSpace(rule.Expression)) yield break;

        foreach (var row in record.Rows)
        {
            var variables = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in row.AllFields)
            {
                var dec = row.GetDecimal(kvp.Key);
                if (dec.HasValue)
                    variables[kvp.Key] = dec.Value;
            }

            var result = _expressionParser.Evaluate(rule.Expression, variables);
            if (!result.Passes)
            {
                yield return new ValidationError
                {
                    RuleId = rule.RuleCode,
                    Field = "Business",
                    Message = $"Business rule '{rule.RuleName}' failed: {rule.Expression}",
                    Severity = rule.Severity,
                    Category = ValidationCategory.Business,
                    ExpectedValue = result.RightValue?.ToString(),
                    ActualValue = result.LeftValue.ToString()
                };
            }
        }
    }
}
