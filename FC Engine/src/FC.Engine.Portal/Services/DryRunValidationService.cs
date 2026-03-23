namespace FC.Engine.Portal.Services;

using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using FC.Engine.Application.DTOs;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

/// <summary>
/// Provides pre-submission validation without persisting any data.
/// Three modes:
///   1. Full dry run — runs entire pipeline, returns errors + parsed data preview
///   2. Schema only — validates XML structure against XSD, fast
///   3. Field validation — validates individual field values against template rules
/// </summary>
public class DryRunValidationService
{
    private readonly ITemplateMetadataCache _cache;
    private readonly IXsdGenerator _xsdGenerator;
    private readonly IGenericXmlParser _xmlParser;
    private readonly IFormulaEvaluator _formulaEvaluator;
    private readonly ICrossSheetValidator _crossSheetValidator;
    private readonly IBusinessRuleEvaluator _businessRuleEvaluator;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ILogger<DryRunValidationService> _logger;

    public DryRunValidationService(
        ITemplateMetadataCache cache,
        IXsdGenerator xsdGenerator,
        IGenericXmlParser xmlParser,
        IFormulaEvaluator formulaEvaluator,
        ICrossSheetValidator crossSheetValidator,
        IBusinessRuleEvaluator businessRuleEvaluator,
        ISubmissionRepository submissionRepo,
        ILogger<DryRunValidationService> logger)
    {
        _cache = cache;
        _xsdGenerator = xsdGenerator;
        _xmlParser = xmlParser;
        _formulaEvaluator = formulaEvaluator;
        _crossSheetValidator = crossSheetValidator;
        _businessRuleEvaluator = businessRuleEvaluator;
        _submissionRepo = submissionRepo;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MODE 1: FULL DRY RUN
    // ═══════════════════════════════════════════════════════════════

    public async Task<DryRunResult> ValidateFullAsync(
        Stream xmlStream,
        string returnCode,
        int institutionId,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new DryRunResult
        {
            ReturnCode = returnCode,
            Mode = "Full Validation",
            StartedAt = DateTime.UtcNow
        };

        try
        {
            // Phase 1: Resolve template
            CachedTemplate template;
            try
            {
                template = await _cache.GetPublishedTemplate(returnCode, ct);
                result.TemplateName = template.Name;
            }
            catch (InvalidOperationException)
            {
                result.Errors.Add(new DryRunError
                {
                    Code = "TPL-001",
                    Severity = "Error",
                    Category = "System",
                    Message = $"Template '{returnCode}' is not published or does not exist."
                });
                result.SimulatedStatus = "Error";
                return result;
            }

            // Phase 2: Buffer stream
            var bufferedStream = new MemoryStream();
            await xmlStream.CopyToAsync(bufferedStream, ct);
            bufferedStream.Position = 0;

            // Phase 3: XSD schema validation
            var schemaErrors = await ValidateXsd(bufferedStream, returnCode, ct);
            result.PhasesCompleted.Add("Schema Validation");

            if (schemaErrors.Count > 0)
            {
                foreach (var err in schemaErrors)
                {
                    result.Errors.Add(new DryRunError
                    {
                        Code = err.RuleId,
                        Severity = err.Severity.ToString(),
                        Category = err.Category.ToString(),
                        Message = err.Message,
                        FieldName = err.Field
                    });
                }
                result.StoppedAtPhase = "Schema Validation";
                FinalizeResult(result, sw);
                return result;
            }

            // Phase 4: Parse XML
            bufferedStream.Position = 0;
            ReturnDataRecord record;
            try
            {
                record = await _xmlParser.Parse(bufferedStream, returnCode, ct);
                result.PhasesCompleted.Add("XML Parsing");
            }
            catch (Exception ex)
            {
                result.Errors.Add(new DryRunError
                {
                    Code = "PARSE-001",
                    Severity = "Error",
                    Category = "Schema",
                    Message = $"Failed to parse XML: {ex.Message}"
                });
                result.StoppedAtPhase = "XML Parsing";
                FinalizeResult(result, sw);
                return result;
            }

            // Build data preview
            result.DataPreview = BuildDataPreview(record, template);

            // Phase 5: Type/Range validation (same logic as ValidationOrchestrator)
            var typeErrors = ValidateTypeRange(record, template);
            foreach (var err in typeErrors)
            {
                result.Errors.Add(MapValidationError(err));
            }
            result.PhasesCompleted.Add("Type & Range Checks");

            // Phase 6: Intra-sheet formula validation
            var formulaErrors = await _formulaEvaluator.Evaluate(record, ct);
            foreach (var err in formulaErrors)
            {
                result.Errors.Add(MapValidationError(err));
            }
            result.PhasesCompleted.Add("Formula Validation");

            // Phase 7: Cross-sheet validation (only if no errors so far)
            var hasErrors = result.Errors.Any(e => e.Severity == "Error");
            if (!hasErrors)
            {
                try
                {
                    // Find a valid return period for cross-sheet lookup
                    var returnPeriodId = await FindCurrentReturnPeriod(institutionId, ct);
                    if (returnPeriodId > 0)
                    {
                        var crossErrors = await _crossSheetValidator.Validate(
                            record, institutionId, returnPeriodId, ct);
                        foreach (var err in crossErrors)
                        {
                            result.Errors.Add(MapValidationError(err));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cross-sheet validation skipped in dry run for {ReturnCode}", returnCode);
                }
            }
            result.PhasesCompleted.Add("Cross-Sheet Validation");

            // Phase 8: Business rules (create a temporary submission for context)
            try
            {
                var tempSubmission = new Submission
                {
                    ReturnCode = returnCode,
                    InstitutionId = institutionId,
                    SubmittedAt = DateTime.UtcNow
                };
                var businessErrors = await _businessRuleEvaluator.Evaluate(record, tempSubmission, ct);
                foreach (var err in businessErrors)
                {
                    result.Errors.Add(MapValidationError(err));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Business rule validation skipped in dry run for {ReturnCode}", returnCode);
            }
            result.PhasesCompleted.Add("Business Rules");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dry run validation failed for {ReturnCode}", returnCode);
            result.Errors.Add(new DryRunError
            {
                Code = "SYS-001",
                Severity = "Error",
                Category = "System",
                Message = "An unexpected error occurred during validation. The file may be corrupted or in an unsupported format."
            });
            result.SimulatedStatus = "Error";
        }
        finally
        {
            FinalizeResult(result, sw);
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MODE 2: SCHEMA ONLY
    // ═══════════════════════════════════════════════════════════════

    public async Task<DryRunResult> ValidateSchemaOnlyAsync(
        Stream xmlStream,
        string returnCode,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new DryRunResult
        {
            ReturnCode = returnCode,
            Mode = "Schema Only",
            StartedAt = DateTime.UtcNow
        };

        try
        {
            // Check template exists
            try
            {
                var template = await _cache.GetPublishedTemplate(returnCode, ct);
                result.TemplateName = template.Name;
            }
            catch (InvalidOperationException)
            {
                result.Errors.Add(new DryRunError
                {
                    Code = "TPL-001",
                    Severity = "Error",
                    Category = "System",
                    Message = $"Template '{returnCode}' is not published or does not exist."
                });
                result.SimulatedStatus = "Error";
                FinalizeResult(result, sw);
                return result;
            }

            var bufferedStream = new MemoryStream();
            await xmlStream.CopyToAsync(bufferedStream, ct);
            bufferedStream.Position = 0;

            var schemaErrors = await ValidateXsd(bufferedStream, returnCode, ct);
            result.PhasesCompleted.Add("Schema Validation");

            foreach (var err in schemaErrors)
            {
                result.Errors.Add(new DryRunError
                {
                    Code = err.RuleId,
                    Severity = err.Severity.ToString(),
                    Category = err.Category.ToString(),
                    Message = err.Message,
                    FieldName = err.Field
                });
            }

            var errorCount = result.Errors.Count(e => e.Severity == "Error");
            result.SimulatedStatus = errorCount > 0 ? "Schema Invalid" : "Schema Valid";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schema validation failed for {ReturnCode}", returnCode);
            result.Errors.Add(new DryRunError
            {
                Code = "SYS-002",
                Severity = "Error",
                Category = "System",
                Message = "Schema validation could not be completed. Ensure the file is valid XML."
            });
            result.SimulatedStatus = "Error";
        }
        finally
        {
            FinalizeResult(result, sw);
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    //  MODE 3: FIELD-LEVEL VALIDATION (for live form)
    // ═══════════════════════════════════════════════════════════════

    public async Task<FieldValidationResult> ValidateFieldsAsync(
        string returnCode,
        Dictionary<string, string?> fieldValues,
        CancellationToken ct = default)
    {
        var result = new FieldValidationResult();

        try
        {
            CachedTemplate template;
            try
            {
                template = await _cache.GetPublishedTemplate(returnCode, ct);
            }
            catch (InvalidOperationException)
            {
                result.Error = "Template not found.";
                return result;
            }

            var fields = template.CurrentVersion.Fields;
            var formulas = template.CurrentVersion.IntraSheetFormulas;

            // Validate each field
            foreach (var (fieldName, value) in fieldValues)
            {
                var fieldDef = fields.FirstOrDefault(f =>
                    f.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                if (fieldDef == null) continue;

                var fieldStatus = new FieldStatus { FieldName = fieldName };

                // Required check
                if (fieldDef.IsRequired && string.IsNullOrWhiteSpace(value))
                {
                    fieldStatus.Status = "Error";
                    fieldStatus.Message = "This field is required";
                    result.Fields.Add(fieldStatus);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    fieldStatus.Status = "Empty";
                    result.Fields.Add(fieldStatus);
                    continue;
                }

                // Data type checks
                var dt = fieldDef.DataType;
                if (dt is FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage or FieldDataType.Integer)
                {
                    if (!decimal.TryParse(value, out var numVal))
                    {
                        fieldStatus.Status = "Error";
                        fieldStatus.Message = dt == FieldDataType.Integer ? "Must be a whole number" : "Must be a number";
                    }
                    else if (dt == FieldDataType.Integer && numVal != Math.Truncate(numVal))
                    {
                        // Integer fields must not have a fractional part
                        fieldStatus.Status = "Error";
                        fieldStatus.Message = "Must be a whole number (no decimals)";
                    }
                    else
                    {
                        // Range checks
                        if (fieldDef.MinValue != null && decimal.TryParse(fieldDef.MinValue, out var min) && numVal < min)
                        {
                            fieldStatus.Status = "Error";
                            fieldStatus.Message = $"Value must be at least {min}";
                        }
                        else if (fieldDef.MaxValue != null && decimal.TryParse(fieldDef.MaxValue, out var max) && numVal > max)
                        {
                            fieldStatus.Status = "Warning";
                            fieldStatus.Message = $"Value exceeds expected maximum ({max})";
                        }
                    }
                }
                else if (dt == FieldDataType.Date)
                {
                    if (!DateTime.TryParse(value, out _))
                    {
                        fieldStatus.Status = "Error";
                        fieldStatus.Message = "Invalid date format";
                    }
                }
                else if (dt == FieldDataType.Text && fieldDef.MaxLength.HasValue)
                {
                    if (value.Length > fieldDef.MaxLength.Value)
                    {
                        fieldStatus.Status = "Warning";
                        fieldStatus.Message = $"Exceeds maximum length of {fieldDef.MaxLength}";
                    }
                }

                if (string.IsNullOrEmpty(fieldStatus.Status))
                {
                    fieldStatus.Status = "Valid";
                }

                result.Fields.Add(fieldStatus);
            }

            // Formula evaluation
            foreach (var formula in formulas.Where(f => f.IsActive && f.FormulaType == FormulaType.Sum))
            {
                var targetValue = fieldValues.GetValueOrDefault(formula.TargetFieldName);

                // Parse operand fields from JSON array
                List<string>? operands = null;
                try
                {
                    operands = System.Text.Json.JsonSerializer.Deserialize<List<string>>(formula.OperandFields);
                }
                catch { continue; }

                if (operands == null || !operands.Any()) continue;

                decimal computedSum = 0;
                foreach (var op in operands)
                {
                    var opValue = fieldValues.GetValueOrDefault(op);
                    if (!string.IsNullOrWhiteSpace(opValue) && decimal.TryParse(opValue, out var opNum))
                    {
                        computedSum += opNum;
                    }
                }

                var matches = false;
                if (!string.IsNullOrWhiteSpace(targetValue) && decimal.TryParse(targetValue, out var targetNum))
                {
                    var diff = Math.Abs(targetNum - computedSum);
                    matches = diff <= formula.ToleranceAmount;
                    if (!matches && formula.TolerancePercent.HasValue && computedSum != 0)
                    {
                        var percentDiff = (diff / Math.Abs(computedSum)) * 100;
                        matches = percentDiff <= formula.TolerancePercent.Value;
                    }
                }

                result.FormulaResults.Add(new FormulaCheckResult
                {
                    FieldName = formula.TargetFieldName,
                    Expression = $"{formula.RuleName}: {string.Join(" + ", operands)}",
                    ComputedValue = computedSum.ToString("F2"),
                    EnteredValue = targetValue ?? "",
                    Matches = matches
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Field validation failed for {ReturnCode}", returnCode);
            result.Error = "Field validation encountered an error.";
        }

        return result;
    }

    /// <summary>
    /// Get the list of available templates for the template selector.
    /// </summary>
    public async Task<List<TemplateOption>> GetAvailableTemplatesAsync(CancellationToken ct = default)
    {
        var templates = await _cache.GetAllPublishedTemplates(ct);
        return templates.Select(t => new TemplateOption
        {
            ReturnCode = t.ReturnCode,
            Name = t.Name,
            Frequency = t.Frequency.ToString(),
            ModuleCode = t.ModuleCode
        }).OrderBy(t => t.ReturnCode).ToList();
    }

    // ── Private Helpers ────────────────────────────────────────────

    private async Task<List<ValidationError>> ValidateXsd(
        Stream xmlStream, string returnCode, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        try
        {
            var schemaSet = await _xsdGenerator.GenerateSchema(returnCode, ct);
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = schemaSet,
                Async = true
            };
            settings.ValidationEventHandler += (_, e) =>
            {
                errors.Add(new ValidationError
                {
                    RuleId = "XSD",
                    Field = "XML",
                    Message = e.Message,
                    Severity = e.Severity == XmlSeverityType.Error
                        ? ValidationSeverity.Error : ValidationSeverity.Warning,
                    Category = ValidationCategory.Schema
                });
            };

            using var reader = XmlReader.Create(xmlStream, settings);
            while (await reader.ReadAsync()) { }
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationError
            {
                RuleId = "XSD",
                Field = "XML",
                Message = $"Schema validation failed: {ex.Message}",
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.Schema
            });
        }

        return errors;
    }

    private static List<ValidationError> ValidateTypeRange(ReturnDataRecord record, CachedTemplate template)
    {
        var errors = new List<ValidationError>();
        var fields = template.CurrentVersion.Fields;

        foreach (var row in record.Rows)
        {
            foreach (var field in fields)
            {
                var value = row.GetValue(field.FieldName);

                if (field.IsRequired && value == null)
                {
                    errors.Add(new ValidationError
                    {
                        RuleId = $"REQ-{field.FieldName}",
                        Field = field.FieldName,
                        Message = $"Required field '{field.DisplayName}' is missing",
                        Severity = ValidationSeverity.Error,
                        Category = ValidationCategory.TypeRange,
                        ExpectedValue = "Non-null value"
                    });
                    continue;
                }

                if (value == null) continue;

                if (field.DataType is FieldDataType.Money or FieldDataType.Decimal
                    or FieldDataType.Integer or FieldDataType.Percentage)
                {
                    var decVal = row.GetDecimal(field.FieldName);
                    if (decVal == null) continue;

                    if (field.MinValue != null && decimal.TryParse(field.MinValue, out var min) && decVal < min)
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = $"RANGE-{field.FieldName}",
                            Field = field.FieldName,
                            Message = $"'{field.DisplayName}' value {decVal} is below minimum {min}",
                            Severity = ValidationSeverity.Error,
                            Category = ValidationCategory.TypeRange,
                            ExpectedValue = $">= {min}",
                            ActualValue = decVal.ToString()
                        });
                    }

                    if (field.MaxValue != null && decimal.TryParse(field.MaxValue, out var max) && decVal > max)
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = $"RANGE-{field.FieldName}",
                            Field = field.FieldName,
                            Message = $"'{field.DisplayName}' value {decVal} exceeds maximum {max}",
                            Severity = ValidationSeverity.Error,
                            Category = ValidationCategory.TypeRange,
                            ExpectedValue = $"<= {max}",
                            ActualValue = decVal.ToString()
                        });
                    }
                }

                if (field.DataType == FieldDataType.Text && field.MaxLength.HasValue)
                {
                    var strVal = value.ToString();
                    if (strVal != null && strVal.Length > field.MaxLength.Value)
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = $"LEN-{field.FieldName}",
                            Field = field.FieldName,
                            Message = $"'{field.DisplayName}' exceeds max length of {field.MaxLength}",
                            Severity = ValidationSeverity.Error,
                            Category = ValidationCategory.TypeRange,
                            ExpectedValue = $"<= {field.MaxLength} chars",
                            ActualValue = $"{strVal.Length} chars"
                        });
                    }
                }

                if (field.AllowedValues != null)
                {
                    var allowed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(field.AllowedValues);
                    if (allowed != null && !allowed.Contains(value.ToString()!, StringComparer.OrdinalIgnoreCase))
                    {
                        errors.Add(new ValidationError
                        {
                            RuleId = $"ENUM-{field.FieldName}",
                            Field = field.FieldName,
                            Message = $"'{field.DisplayName}' value '{value}' is not in the allowed list",
                            Severity = ValidationSeverity.Error,
                            Category = ValidationCategory.TypeRange,
                            ExpectedValue = field.AllowedValues,
                            ActualValue = value.ToString()
                        });
                    }
                }
            }
        }

        return errors;
    }

    private async Task<int> FindCurrentReturnPeriod(int institutionId, CancellationToken ct)
    {
        // Look at recent submissions for this institution to find an active return period
        var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);
        var recent = submissions
            .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
            .OrderByDescending(s => s.SubmittedAt)
            .FirstOrDefault();
        return recent?.ReturnPeriodId ?? 0;
    }

    private static List<DryRunDataPreviewRow> BuildDataPreview(ReturnDataRecord record, CachedTemplate template)
    {
        var preview = new List<DryRunDataPreviewRow>();
        var fields = template.CurrentVersion.Fields;
        var fieldNames = fields.Select(f => f.FieldName).ToList();
        var displayNames = fields.ToDictionary(f => f.FieldName, f => f.DisplayName);

        foreach (var row in record.Rows.Take(20)) // Limit preview rows
        {
            var rowData = new Dictionary<string, string?>();
            foreach (var fieldName in fieldNames)
            {
                var val = row.GetValue(fieldName);
                rowData[displayNames.GetValueOrDefault(fieldName, fieldName)] = val?.ToString();
            }
            preview.Add(new DryRunDataPreviewRow
            {
                RowKey = row.RowKey,
                Values = rowData
            });
        }

        return preview;
    }

    private static DryRunError MapValidationError(ValidationError err)
    {
        return new DryRunError
        {
            Code = err.RuleId,
            Severity = err.Severity.ToString(),
            Category = err.Category.ToString(),
            Message = err.Message,
            FieldName = err.Field,
            ExpectedValue = err.ExpectedValue,
            ActualValue = err.ActualValue
        };
    }

    private static void FinalizeResult(DryRunResult result, Stopwatch sw)
    {
        sw.Stop();
        result.CompletedAt = DateTime.UtcNow;
        result.DurationMs = (int)sw.ElapsedMilliseconds;
        result.ErrorCount = result.Errors.Count(e => e.Severity == "Error");
        result.WarningCount = result.Errors.Count(e => e.Severity == "Warning");

        if (string.IsNullOrEmpty(result.SimulatedStatus))
        {
            result.SimulatedStatus = result.ErrorCount > 0
                ? "Rejected"
                : result.WarningCount > 0
                    ? "AcceptedWithWarnings"
                    : "Accepted";
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  DATA MODELS
// ═══════════════════════════════════════════════════════════════════════

public class DryRunResult
{
    public string ReturnCode { get; set; } = "";
    public string? TemplateName { get; set; }
    public string Mode { get; set; } = "Full Validation";
    public string SimulatedStatus { get; set; } = "";
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<DryRunError> Errors { get; set; } = new();
    public List<DryRunDataPreviewRow> DataPreview { get; set; } = new();
    public List<string> PhasesCompleted { get; set; } = new();
    public string? StoppedAtPhase { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public int DurationMs { get; set; }
}

public class DryRunError
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "Error";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
}

public class DryRunDataPreviewRow
{
    public string? RowKey { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new();
}

public class FieldValidationResult
{
    public List<FieldStatus> Fields { get; set; } = new();
    public List<FormulaCheckResult> FormulaResults { get; set; } = new();
    public string? Error { get; set; }
}

public class FieldStatus
{
    public string FieldName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Message { get; set; }
}

public class FormulaCheckResult
{
    public string FieldName { get; set; } = "";
    public string Expression { get; set; } = "";
    public string? ComputedValue { get; set; }
    public string? EnteredValue { get; set; }
    public bool Matches { get; set; }
}

public class TemplateOption
{
    public string ReturnCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string? ModuleCode { get; set; }
}
