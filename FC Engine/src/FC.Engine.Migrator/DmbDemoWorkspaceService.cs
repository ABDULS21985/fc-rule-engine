using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Dapper;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.DataRecord;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Persistence;
using FC.Engine.Infrastructure.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Migrator;

public sealed class DmbDemoWorkspaceService
{
    private const string ModuleCode = "DMB_BASEL3";
    private const string VerificationReturnCode = "DMB_OPR";
    private const string DemoUsername = "accessdemo";
    private const string DemoEmail = "accessdemo@accessbank.local";
    private const string DemoDisplayName = "Access Demo Admin";

    private static readonly Regex FuncExpressionRegex = new(
        @"^FUNC:(?<name>[A-Z0-9_]+)\((?<args>.*)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> CoordinatedCrossModuleFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DMB_AML:str_count",
        "DMB_CAP:paid_up_capital",
        "DMB_COV:reporting_year",
        "DMB_CRR:sovereign_gross_exposure",
        "DMB_DEP:demand_deposits",
        "DMB_DIC:total_field_count",
        "DMB_FIN:cash_and_balances",
        "DMB_GOV:board_size",
        "DMB_IFR:stage1_gross_carrying_amount",
        "DMB_LCR:hqla_level1_cash",
        "DMB_LND:lending_oil_gas",
        "DMB_MKR:fx_gross_long",
        "DMB_NPL:oil_gas_gross_loans",
        "DMB_NSF:asf_regulatory_capital",
        "DMB_OPR:gross_income_year1"
    };

    private readonly MetadataDbContext _db;
    private readonly ITemplateMetadataCache _templateCache;
    private readonly ValidationOrchestrator _validationOrchestrator;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DynamicSqlBuilder _sqlBuilder;
    private readonly IXsdGenerator _xsdGenerator;
    private readonly IGenericXmlParser _xmlParser;
    private readonly InstitutionAuthService _institutionAuthService;
    private readonly IInstitutionUserRepository _institutionUserRepository;
    private readonly IMfaService _mfaService;
    private readonly ExpressionParser _expressionParser = new();
    private readonly ILogger<DmbDemoWorkspaceService> _logger;

    public DmbDemoWorkspaceService(
        MetadataDbContext db,
        ITemplateMetadataCache templateCache,
        ValidationOrchestrator validationOrchestrator,
        IDbConnectionFactory connectionFactory,
        DynamicSqlBuilder sqlBuilder,
        IXsdGenerator xsdGenerator,
        IGenericXmlParser xmlParser,
        InstitutionAuthService institutionAuthService,
        IInstitutionUserRepository institutionUserRepository,
        IMfaService mfaService,
        ILogger<DmbDemoWorkspaceService> logger)
    {
        _db = db;
        _templateCache = templateCache;
        _validationOrchestrator = validationOrchestrator;
        _connectionFactory = connectionFactory;
        _sqlBuilder = sqlBuilder;
        _xsdGenerator = xsdGenerator;
        _xmlParser = xmlParser;
        _institutionAuthService = institutionAuthService;
        _institutionUserRepository = institutionUserRepository;
        _mfaService = mfaService;
        _logger = logger;
    }

    public async Task<DmbDemoWorkspaceResult> PrepareAsync(string templatesDirectory, string demoPassword, CancellationToken ct = default)
    {
        var institution = await ResolveAccessBankAsync(ct);
        var module = await ResolveDmbModuleAsync(ct);
        var verificationPeriod = await EnsureOpenVerificationPeriodAsync(institution.TenantId, module.Id, ct);
        var demoUser = await EnsureDemoUserAsync(institution, demoPassword, ct);
        var templates = await LoadDmbTemplatesAsync(ct);

        var sampleResult = await GenerateSamplesAsync(
            templates,
            templatesDirectory,
            institution,
            verificationPeriod,
            ct);

        var seedResult = await SeedHistoricalReturnsAsync(
            templates,
            institution,
            module,
            demoUser.Id,
            ct);

        return new DmbDemoWorkspaceResult
        {
            TemplatesDirectory = templatesDirectory,
            SampleFilesWritten = sampleResult.FilesWritten,
            SampleFilesDeleted = sampleResult.FilesDeleted,
            HistoricalPeriodsCreated = seedResult.PeriodsCreated,
            HistoricalSubmissionsCreated = seedResult.SubmissionsCreated,
            DemoUsername = demoUser.Username,
            DemoPassword = demoPassword,
            VerificationReturnCode = VerificationReturnCode,
            VerificationReturnPeriodId = verificationPeriod.Id,
            VerificationSamplePath = Path.Combine(templatesDirectory, $"{VerificationReturnCode}_Valid_Sample.xml")
        };
    }

    public async Task<DmbSampleGenerationResult> GenerateSamplesAsync(
        string templatesDirectory,
        CancellationToken ct = default)
    {
        var institution = await ResolveAccessBankAsync(ct);
        var module = await ResolveDmbModuleAsync(ct);
        var verificationPeriod = await EnsureOpenVerificationPeriodAsync(institution.TenantId, module.Id, ct);
        var templates = await LoadDmbTemplatesAsync(ct);
        return await GenerateSamplesAsync(templates, templatesDirectory, institution, verificationPeriod, ct);
    }

    public async Task<DmbHistoricalSeedResult> SeedHistoricalReturnsAsync(string demoPassword, CancellationToken ct = default)
    {
        var institution = await ResolveAccessBankAsync(ct);
        var module = await ResolveDmbModuleAsync(ct);
        var demoUser = await EnsureDemoUserAsync(institution, demoPassword, ct);
        var templates = await LoadDmbTemplatesAsync(ct);
        return await SeedHistoricalReturnsAsync(templates, institution, module, demoUser.Id, ct);
    }

    private async Task<DmbSampleGenerationResult> GenerateSamplesAsync(
        IReadOnlyList<CachedTemplate> templates,
        string templatesDirectory,
        Institution institution,
        ReturnPeriod verificationPeriod,
        CancellationToken ct)
    {
        Directory.CreateDirectory(templatesDirectory);

        var allowedCodes = templates
            .Select(x => x.ReturnCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingFiles = Directory
            .EnumerateFiles(templatesDirectory, "DMB_*_Valid_Sample.xml", SearchOption.TopDirectoryOnly)
            .ToList();

        var deleted = 0;
        foreach (var file in existingFiles)
        {
            var code = Path.GetFileNameWithoutExtension(file)
                .Replace("_Valid_Sample", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (allowedCodes.Contains(code))
            {
                continue;
            }

            File.Delete(file);
            deleted++;
        }

        var written = 0;
        var reportingDate = verificationPeriod.ReportingDate.Date;
        foreach (var template in templates)
        {
            var sample = await GenerateValidatedSampleAsync(
                template,
                institution.InstitutionCode,
                reportingDate,
                sampleIndex: 0,
                institution.Id,
                verificationPeriod.Id,
                ct);

            var filePath = Path.Combine(templatesDirectory, $"{template.ReturnCode}_Valid_Sample.xml");
            await File.WriteAllTextAsync(filePath, sample.Xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            written++;
        }

        return new DmbSampleGenerationResult
        {
            FilesWritten = written,
            FilesDeleted = deleted
        };
    }

    private async Task<DmbHistoricalSeedResult> SeedHistoricalReturnsAsync(
        IReadOnlyList<CachedTemplate> templates,
        Institution institution,
        Module module,
        int demoUserId,
        CancellationToken ct)
    {
        var verificationPeriod = await EnsureOpenVerificationPeriodAsync(institution.TenantId, module.Id, ct);
        var specs = BuildHistoricalQuarterSpecs(DateTime.UtcNow, count: 8);
        var periods = await EnsureHistoricalPeriodsAsync(institution.TenantId, module.Id, specs, ct);

        var existingAccepted = await _db.Submissions
            .AsNoTracking()
            .Where(x => x.TenantId == institution.TenantId && x.InstitutionId == institution.Id)
            .Where(x =>
                x.Status == SubmissionStatus.Accepted ||
                x.Status == SubmissionStatus.AcceptedWithWarnings ||
                x.Status == SubmissionStatus.Historical ||
                x.Status == SubmissionStatus.RegulatorAcknowledged ||
                x.Status == SubmissionStatus.RegulatorAccepted ||
                x.Status == SubmissionStatus.RegulatorQueriesRaised)
            .Select(x => new { x.ReturnCode, x.ReturnPeriodId })
            .ToListAsync(ct);

        var acceptedLookup = existingAccepted
            .Select(x => $"{x.ReturnCode}:{x.ReturnPeriodId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var submissionsCreated = 0;
        foreach (var spec in specs)
        {
            var period = periods.Single(x => x.Year == spec.Year && x.Month == spec.Month);
            foreach (var template in templates)
            {
                var key = $"{template.ReturnCode}:{period.Id}";
                if (acceptedLookup.Contains(key))
                {
                    continue;
                }

                var sample = await GenerateValidatedSampleAsync(
                    template,
                    institution.InstitutionCode,
                    period.ReportingDate.Date,
                    sampleIndex: spec.Index + 1,
                    institution.Id,
                    period.Id,
                    ct);

                var submission = Submission.Create(institution.Id, period.Id, template.ReturnCode, institution.TenantId);
                submission.SetTemplateVersion(template.CurrentVersion.Id);
                submission.SubmittedByUserId = demoUserId;
                submission.ApprovalRequired = false;
                submission.CreatedAt = spec.SubmittedAt.AddMinutes(-45);
                submission.SubmittedAt = spec.SubmittedAt;
                submission.ProcessingDurationMs = 180 + (spec.Index * 17);
                submission.StoreRawXml(sample.Xml);
                submission.StoreParsedDataJson(SubmissionPayloadSerializer.Serialize(sample.Record));

                _db.Submissions.Add(submission);
                await _db.SaveChangesAsync(ct);

                await PersistRecordAsync(template, sample.Record, submission.Id, institution.TenantId, ct);

                sample.ValidationReport.FinalizeAt(spec.SubmittedAt.AddSeconds(2));
                submission.AttachValidationReport(sample.ValidationReport);
                if (sample.ValidationReport.HasWarnings)
                {
                    submission.MarkAcceptedWithWarnings();
                }
                else
                {
                    submission.MarkAccepted();
                }

                await _db.SaveChangesAsync(ct);
                submissionsCreated++;
                acceptedLookup.Add(key);
            }
        }

        submissionsCreated += await RefreshVerificationBundleAsync(
            templates,
            institution,
            verificationPeriod,
            demoUserId,
            ct);

        return new DmbHistoricalSeedResult
        {
            PeriodsCreated = periods.Count(x => specs.Any(spec => spec.Year == x.Year && spec.Month == x.Month && x.CreatedAt >= DateTime.UtcNow.AddMinutes(-10))),
            SubmissionsCreated = submissionsCreated
        };
    }

    private async Task<GeneratedTemplateSample> GenerateValidatedSampleAsync(
        CachedTemplate template,
        string institutionCode,
        DateTime reportingDate,
        int sampleIndex,
        int institutionId,
        int returnPeriodId,
        CancellationToken ct)
    {
        var row = BuildInitialRow(template, reportingDate, sampleIndex);

        ValidationReport? finalReport = null;
        string? finalXml = null;
        ReturnDataRecord? finalRecord = null;

        for (var iteration = 0; iteration < 12; iteration++)
        {
            ApplyFormulaTargets(template, row);
            CoerceRowValues(template, row, reportingDate);

            var xml = BuildXml(template, row, institutionCode, reportingDate);
            var xsdErrors = await ValidateXsdAsync(template.ReturnCode, xml, ct);
            if (xsdErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Generated XML for {template.ReturnCode} failed XSD validation: {string.Join("; ", xsdErrors.Select(x => x.Message))}");
            }

            var parsedRecord = await ParseXmlAsync(template.ReturnCode, xml, ct);
            var report = await ValidateParsedRecordAsync(parsedRecord, template.ReturnCode, institutionId, returnPeriodId, ct);

            if (!report.HasErrors)
            {
                finalXml = xml;
                finalRecord = parsedRecord;
                finalReport = report;
                break;
            }

            RepairValidationErrors(template, row, report.Errors, reportingDate);
        }

        if (finalXml is null || finalRecord is null || finalReport is null)
        {
            var record = BuildRecord(template, row);
            var report = await ValidateParsedRecordAsync(record, template.ReturnCode, institutionId, returnPeriodId, ct);
            throw new InvalidOperationException(
                $"Unable to generate a valid sample for {template.ReturnCode}: {string.Join("; ", report.Errors.Select(x => $"{x.RuleId}:{x.Message}"))}");
        }

        return new GeneratedTemplateSample(template.ReturnCode, finalXml, finalRecord, finalReport);
    }

    private ReturnDataRow BuildInitialRow(CachedTemplate template, DateTime reportingDate, int sampleIndex)
    {
        var row = new ReturnDataRow();
        foreach (var field in template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder))
        {
            var value = BuildInitialValue(template.ReturnCode, field, reportingDate, sampleIndex);
            if (value is not null)
            {
                row.SetValue(field.FieldName, value);
            }
        }

        return row;
    }

    private object? BuildInitialValue(
        string returnCode,
        TemplateField field,
        DateTime reportingDate,
        int sampleIndex)
    {
        var fieldName = field.FieldName.Trim();
        var normalized = fieldName.ToLowerInvariant();

        var coordinatedCrossModuleValue = TryResolveCoordinatedCrossModuleValue(returnCode, field, reportingDate);
        if (coordinatedCrossModuleValue is not null)
        {
            return coordinatedCrossModuleValue;
        }

        if (string.Equals(returnCode, "DMB_OPR", StringComparison.OrdinalIgnoreCase))
        {
            if (normalized is "gross_income_year2")
            {
                return 1000m;
            }

            if (normalized is "gross_income_year3")
            {
                return 3020m - reportingDate.Year;
            }
        }

        var allowed = ParseAllowedValues(field.AllowedValues);
        if (allowed.Count > 0)
        {
            return ConvertStringValue(field, allowed[0]);
        }

        if (!string.IsNullOrWhiteSpace(field.DefaultValue))
        {
            return ConvertStringValue(field, field.DefaultValue);
        }

        if (normalized is "reporting_year")
        {
            return reportingDate.Year;
        }

        if (normalized is "reporting_month")
        {
            return reportingDate.Month;
        }

        if (normalized is "reporting_quarter")
        {
            return ((reportingDate.Month - 1) / 3) + 1;
        }

        if (normalized is "return_code" or "returncode")
        {
            return returnCode;
        }

        if (normalized is "institution_code" or "institutioncode")
        {
            return "ACCESSBA";
        }

        if (normalized.Contains("alpha_factor", StringComparison.OrdinalIgnoreCase))
        {
            return 0.15m;
        }

        if (normalized.StartsWith("pd_", StringComparison.OrdinalIgnoreCase))
        {
            return 0.02m + (sampleIndex * 0.001m);
        }

        if (normalized.StartsWith("lgd_", StringComparison.OrdinalIgnoreCase))
        {
            return 0.45m;
        }

        return field.DataType switch
        {
            FieldDataType.Integer => BuildInitialIntegerValue(field, reportingDate, sampleIndex),
            FieldDataType.Money => BuildInitialDecimalValue(returnCode, field, sampleIndex, scale: 1_000m),
            FieldDataType.Decimal => BuildInitialDecimalValue(returnCode, field, sampleIndex, scale: 100m),
            FieldDataType.Percentage => BuildInitialPercentageValue(field, sampleIndex),
            FieldDataType.Date => reportingDate.Date,
            FieldDataType.Boolean => true,
            FieldDataType.Text => BuildInitialTextValue(field, returnCode, sampleIndex),
            _ => null
        };
    }

    private static object BuildInitialIntegerValue(TemplateField field, DateTime reportingDate, int sampleIndex)
    {
        var normalized = field.FieldName.ToLowerInvariant();
        if (normalized.Contains("count", StringComparison.OrdinalIgnoreCase))
        {
            return 3 + sampleIndex;
        }

        if (normalized.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return 2 + sampleIndex;
        }

        if (normalized.Contains("critical", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (normalized.Contains("info", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (normalized.Contains("year", StringComparison.OrdinalIgnoreCase))
        {
            return reportingDate.Year;
        }

        if (normalized.Contains("month", StringComparison.OrdinalIgnoreCase))
        {
            return reportingDate.Month;
        }

        if (normalized.Contains("quarter", StringComparison.OrdinalIgnoreCase))
        {
            return ((reportingDate.Month - 1) / 3) + 1;
        }

        return 10 + sampleIndex;
    }

    private static decimal BuildInitialDecimalValue(string returnCode, TemplateField field, int sampleIndex, decimal scale)
    {
        var normalized = field.FieldName.ToLowerInvariant();

        if (string.Equals(returnCode, "DMB_CAP", StringComparison.OrdinalIgnoreCase))
        {
            return normalized switch
            {
                "paid_up_capital" => 40_000m + (sampleIndex * 2_000m),
                "share_premium" => 10_000m + (sampleIndex * 500m),
                "retained_earnings" => 15_000m + (sampleIndex * 800m),
                "other_comprehensive_income" => 5_000m + (sampleIndex * 250m),
                "qualifying_at1_instruments" => 20_000m + (sampleIndex * 900m),
                "qualifying_tier2_instruments" => 15_000m + (sampleIndex * 650m),
                "general_provisions" => 5_000m + (sampleIndex * 250m),
                "total_rwa" => 5_000m + (sampleIndex * 200m),
                _ => BuildCapFallbackMoney(normalized, sampleIndex, scale)
            };
        }

        if (normalized.Contains("minimum", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveThresholdValue(normalized);
        }

        if (normalized.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveThresholdValue(normalized);
        }

        if (normalized.Contains("threshold", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveThresholdValue(normalized);
        }

        if (normalized.Contains("shareholders_funds", StringComparison.OrdinalIgnoreCase))
        {
            return 85_000m + (sampleIndex * 4_000m);
        }

        if (normalized.Contains("rwa", StringComparison.OrdinalIgnoreCase))
        {
            return 600_000m + (sampleIndex * 18_500m);
        }

        if (normalized.Contains("loans", StringComparison.OrdinalIgnoreCase))
        {
            return 420_000m + (sampleIndex * 14_000m);
        }

        if (normalized.Contains("deposits", StringComparison.OrdinalIgnoreCase))
        {
            return 650_000m + (sampleIndex * 16_500m);
        }

        if (normalized.Contains("assets", StringComparison.OrdinalIgnoreCase))
        {
            return 700_000m + (sampleIndex * 20_000m);
        }

        if (normalized.Contains("capital", StringComparison.OrdinalIgnoreCase))
        {
            return 120_000m + (sampleIndex * 6_000m);
        }

        return scale + (sampleIndex * (scale / 5m));
    }

    private static decimal BuildCapFallbackMoney(string normalizedFieldName, int sampleIndex, decimal scale)
    {
        if (normalizedFieldName.Contains("deduction", StringComparison.OrdinalIgnoreCase)
            || normalizedFieldName.StartsWith("less_", StringComparison.OrdinalIgnoreCase))
        {
            return 0m;
        }

        if (normalizedFieldName.Contains("stress_", StringComparison.OrdinalIgnoreCase))
        {
            return 12m + sampleIndex;
        }

        return scale + (sampleIndex * (scale / 5m));
    }

    private static decimal BuildInitialPercentageValue(TemplateField field, int sampleIndex)
    {
        var normalized = field.FieldName.ToLowerInvariant();

        if (normalized.Contains("alpha_factor", StringComparison.OrdinalIgnoreCase))
        {
            return 0.15m;
        }

        if (normalized.StartsWith("pd_", StringComparison.OrdinalIgnoreCase))
        {
            return 0.02m + (sampleIndex * 0.001m);
        }

        if (normalized.StartsWith("lgd_", StringComparison.OrdinalIgnoreCase))
        {
            return 0.45m;
        }

        if (normalized.Contains("cet1_minimum", StringComparison.OrdinalIgnoreCase))
        {
            return 6m;
        }

        if (normalized.Contains("tier1_minimum", StringComparison.OrdinalIgnoreCase))
        {
            return 8m;
        }

        if (normalized.Contains("car_minimum", StringComparison.OrdinalIgnoreCase))
        {
            return 10m;
        }

        if (normalized.Contains("lcr_minimum", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("nsfr_minimum", StringComparison.OrdinalIgnoreCase))
        {
            return 100m;
        }

        if (normalized.Contains("completion_percentage", StringComparison.OrdinalIgnoreCase))
        {
            return 97.5m;
        }

        if (normalized.Contains("migration_threshold", StringComparison.OrdinalIgnoreCase))
        {
            return 25m;
        }

        return 12m + sampleIndex;
    }

    private static string BuildInitialTextValue(TemplateField field, string returnCode, int sampleIndex)
    {
        var normalized = field.FieldName.ToLowerInvariant();
        var raw = normalized switch
        {
            var s when s.Contains("institution", StringComparison.OrdinalIgnoreCase) && s.Contains("code", StringComparison.OrdinalIgnoreCase) => "ACCESSBA",
            var s when s.Contains("name", StringComparison.OrdinalIgnoreCase) => "Access Bank Plc",
            var s when s.Contains("email", StringComparison.OrdinalIgnoreCase) => "demo@accessbank.ng",
            var s when s.Contains("phone", StringComparison.OrdinalIgnoreCase) => "+2348012345678",
            var s when s.Contains("address", StringComparison.OrdinalIgnoreCase) => "1 Marina Lagos",
            var s when s.Contains("currency", StringComparison.OrdinalIgnoreCase) => "NGN",
            var s when s.Contains("country", StringComparison.OrdinalIgnoreCase) => "NG",
            var s when s.Contains("city", StringComparison.OrdinalIgnoreCase) => "Lagos",
            var s when s.Contains("branch", StringComparison.OrdinalIgnoreCase) => "Head Office",
            var s when s.Contains("return", StringComparison.OrdinalIgnoreCase) && s.Contains("type", StringComparison.OrdinalIgnoreCase) => "AML",
            var s when s.Contains("consolidated", StringComparison.OrdinalIgnoreCase) => "Solo",
            var s when s.Contains("licence", StringComparison.OrdinalIgnoreCase) && s.Contains("category", StringComparison.OrdinalIgnoreCase) => "National",
            var s when s.Contains("accounting", StringComparison.OrdinalIgnoreCase) => "IFRS",
            _ => $"{returnCode}_{field.FieldName}_{sampleIndex + 1}"
        };

        var maxLength = field.MaxLength.GetValueOrDefault(40);
        return raw.Length <= maxLength ? raw : raw[..maxLength];
    }

    private void ApplyFormulaTargets(CachedTemplate template, ReturnDataRow row)
    {
        var fields = template.CurrentVersion.Fields.ToDictionary(x => x.FieldName, StringComparer.OrdinalIgnoreCase);
        foreach (var formula in template.CurrentVersion.IntraSheetFormulas.OrderBy(x => x.SortOrder))
        {
            ApplyFormulaTarget(formula, row, fields);
        }
    }

    private void ApplyFormulaTarget(
        IntraSheetFormula formula,
        ReturnDataRow row,
        IReadOnlyDictionary<string, TemplateField> fieldMap)
    {
        if (!fieldMap.TryGetValue(formula.TargetFieldName, out var targetField))
        {
            return;
        }

        var operands = ParseOperandFields(formula.OperandFields);
        var currentValue = row.GetDecimal(formula.TargetFieldName) ?? 0m;

        decimal nextValue = formula.FormulaType switch
        {
            FormulaType.Sum => operands.Sum(x => row.GetDecimal(x) ?? 0m),
            FormulaType.Difference => (row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? 0m)
                - (row.GetDecimal(operands.ElementAtOrDefault(1) ?? string.Empty) ?? 0m),
            FormulaType.Equals => row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue,
            FormulaType.Ratio => ComputeRatioValue(row, operands, currentValue),
            FormulaType.GreaterThan => ComputeGreaterThanValue(row, operands, currentValue, targetField),
            FormulaType.GreaterThanOrEqual => ComputeGreaterThanOrEqualValue(row, operands, currentValue),
            FormulaType.LessThan => ComputeLessThanValue(row, operands, currentValue, targetField),
            FormulaType.LessThanOrEqual => ComputeLessThanOrEqualValue(row, operands, currentValue),
            FormulaType.Between => ComputeBetweenValue(row, operands, currentValue),
            FormulaType.Custom => ComputeCustomValue(formula, row, currentValue),
            FormulaType.Required => currentValue,
            _ => currentValue
        };

        row.SetValue(targetField.FieldName, ConvertNumericValue(targetField, nextValue));
    }

    private decimal ComputeCustomValue(IntraSheetFormula formula, ReturnDataRow row, decimal fallback)
    {
        if (string.IsNullOrWhiteSpace(formula.CustomExpression))
        {
            return fallback;
        }

        var expression = formula.CustomExpression.Trim();
        var funcMatch = FuncExpressionRegex.Match(expression);
        if (funcMatch.Success)
        {
            var functionName = funcMatch.Groups["name"].Value.Trim();
            var argsText = funcMatch.Groups["args"].Value.Trim();
            var args = string.IsNullOrWhiteSpace(argsText)
                ? Array.Empty<string>()
                : argsText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return EvaluateCustomFunction(functionName, args, row);
        }

        var equalsIndex = expression.IndexOf('=');
        if (equalsIndex > 0)
        {
            var left = expression[..equalsIndex].Trim();
            var right = expression[(equalsIndex + 1)..].Trim();
            if (left.Equals(formula.TargetFieldName, StringComparison.OrdinalIgnoreCase))
            {
                var variables = row.AllFields
                    .Select(x => new { x.Key, Value = row.GetDecimal(x.Key) })
                    .Where(x => x.Value.HasValue)
                    .ToDictionary(x => x.Key, x => x.Value!.Value, StringComparer.OrdinalIgnoreCase);
                return _expressionParser.Evaluate(right, variables).LeftValue;
            }
        }

        return fallback;
    }

    private static decimal ComputeRatioValue(ReturnDataRow row, IReadOnlyList<string> operands, decimal fallback)
    {
        if (operands.Count < 2)
        {
            return fallback;
        }

        var numerator = row.GetDecimal(operands[0]) ?? 0m;
        var denominator = row.GetDecimal(operands[1]) ?? 0m;
        if (denominator == 0)
        {
            denominator = 1m;
            row.SetValue(operands[1], denominator);
        }

        return numerator / denominator;
    }

    private static decimal ComputeGreaterThanValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue,
        TemplateField targetField)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        var candidate = Math.Max(currentValue, operand);
        return candidate + ResolveStep(targetField);
    }

    private static decimal ComputeGreaterThanOrEqualValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        return Math.Max(currentValue, operand);
    }

    private static decimal ComputeLessThanValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue,
        TemplateField targetField)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        var candidate = Math.Min(currentValue, operand);
        return candidate - ResolveStep(targetField);
    }

    private static decimal ComputeLessThanOrEqualValue(
        ReturnDataRow row,
        IReadOnlyList<string> operands,
        decimal currentValue)
    {
        var operand = row.GetDecimal(operands.ElementAtOrDefault(0) ?? string.Empty) ?? currentValue;
        return Math.Min(currentValue, operand);
    }

    private static decimal ComputeBetweenValue(ReturnDataRow row, IReadOnlyList<string> operands, decimal fallback)
    {
        if (operands.Count < 2)
        {
            return fallback;
        }

        var lower = row.GetDecimal(operands[0]) ?? fallback;
        var upper = row.GetDecimal(operands[1]) ?? fallback;
        if (upper < lower)
        {
            (lower, upper) = (upper, lower);
        }

        return lower + ((upper - lower) / 2m);
    }

    private static decimal ResolveStep(TemplateField field)
        => field.DataType switch
        {
            FieldDataType.Integer => 1m,
            FieldDataType.Percentage => 0.5m,
            _ => 1m
        };

    private static decimal ResolveThresholdValue(string normalizedFieldName)
    {
        if (normalizedFieldName.Contains("lcr_minimum", StringComparison.OrdinalIgnoreCase)
            || normalizedFieldName.Contains("nsfr_minimum", StringComparison.OrdinalIgnoreCase))
        {
            return 100m;
        }

        if (normalizedFieldName.Contains("cet1", StringComparison.OrdinalIgnoreCase))
        {
            return 6m;
        }

        if (normalizedFieldName.Contains("tier1", StringComparison.OrdinalIgnoreCase))
        {
            return 8m;
        }

        if (normalizedFieldName.Contains("car", StringComparison.OrdinalIgnoreCase))
        {
            return 10m;
        }

        if (normalizedFieldName.Contains("threshold", StringComparison.OrdinalIgnoreCase))
        {
            return 25m;
        }

        if (normalizedFieldName.Contains("limit", StringComparison.OrdinalIgnoreCase))
        {
            return 30m;
        }

        return 10m;
    }

    private void CoerceRowValues(CachedTemplate template, ReturnDataRow row, DateTime reportingDate)
    {
        foreach (var field in template.CurrentVersion.Fields)
        {
            var value = row.GetValue(field.FieldName);
            if (value is null)
            {
                continue;
            }

            if (field.DataType == FieldDataType.Text)
            {
                var str = value.ToString() ?? string.Empty;
                var allowed = ParseAllowedValues(field.AllowedValues);
                if (allowed.Count > 0 && !allowed.Contains(str, StringComparer.OrdinalIgnoreCase))
                {
                    str = allowed[0];
                }

                if (field.MaxLength.HasValue && str.Length > field.MaxLength.Value)
                {
                    str = str[..field.MaxLength.Value];
                }

                row.SetValue(field.FieldName, str);
                continue;
            }

            if (field.DataType == FieldDataType.Date)
            {
                row.SetValue(field.FieldName, reportingDate.Date);
                continue;
            }

            if (field.DataType == FieldDataType.Boolean)
            {
                row.SetValue(field.FieldName, Convert.ToBoolean(value, CultureInfo.InvariantCulture));
                continue;
            }

            var dec = row.GetDecimal(field.FieldName);
            if (!dec.HasValue)
            {
                continue;
            }

            var next = dec.Value;
            if (decimal.TryParse(field.MinValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var min)
                && next < min)
            {
                next = min;
            }

            if (decimal.TryParse(field.MaxValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var max)
                && next > max)
            {
                next = max;
            }

            row.SetValue(field.FieldName, ConvertNumericValue(field, next));
        }
    }

    private void RepairValidationErrors(
        CachedTemplate template,
        ReturnDataRow row,
        IReadOnlyList<ValidationError> errors,
        DateTime reportingDate)
    {
        var fields = template.CurrentVersion.Fields.ToDictionary(x => x.FieldName, StringComparer.OrdinalIgnoreCase);
        var formulas = template.CurrentVersion.IntraSheetFormulas.ToDictionary(x => x.RuleCode, StringComparer.OrdinalIgnoreCase);

        foreach (var error in errors.Where(x => x.Severity == ValidationSeverity.Error))
        {
            if (error.Category == ValidationCategory.TypeRange && fields.TryGetValue(error.Field, out var field))
            {
                if (error.RuleId.StartsWith("REQ-", StringComparison.OrdinalIgnoreCase))
                {
                    row.SetValue(field.FieldName, BuildInitialValue(template.ReturnCode, field, reportingDate, sampleIndex: 0));
                    continue;
                }

                if (error.RuleId.StartsWith("ENUM-", StringComparison.OrdinalIgnoreCase))
                {
                    var allowed = ParseAllowedValues(field.AllowedValues);
                    if (allowed.Count > 0)
                    {
                        row.SetValue(field.FieldName, ConvertStringValue(field, allowed[0]));
                    }
                    continue;
                }

                if (error.RuleId.StartsWith("LEN-", StringComparison.OrdinalIgnoreCase))
                {
                    var value = row.GetString(field.FieldName) ?? string.Empty;
                    var maxLength = field.MaxLength.GetValueOrDefault(40);
                    row.SetValue(field.FieldName, value[..Math.Min(value.Length, maxLength)]);
                    continue;
                }

                if (error.RuleId.StartsWith("RANGE-", StringComparison.OrdinalIgnoreCase))
                {
                    if (decimal.TryParse(field.MinValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var min))
                    {
                        row.SetValue(field.FieldName, ConvertNumericValue(field, min));
                        continue;
                    }

                    if (decimal.TryParse(field.MaxValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var max))
                    {
                        row.SetValue(field.FieldName, ConvertNumericValue(field, max));
                    }
                    continue;
                }
            }

            if (error.Category == ValidationCategory.IntraSheet && formulas.TryGetValue(error.RuleId, out var formula))
            {
                ApplyFormulaTarget(formula, row, fields);
            }
        }
    }

    private static ReturnDataRecord BuildRecord(CachedTemplate template, ReturnDataRow row)
    {
        var category = Enum.Parse<StructuralCategory>(template.StructuralCategory);
        var record = new ReturnDataRecord(template.ReturnCode, template.CurrentVersion.Id, category);
        record.AddRow(row);
        return record;
    }

    private static string BuildXml(CachedTemplate template, ReturnDataRow row, string institutionCode, DateTime reportingDate)
    {
        var fields = template.CurrentVersion.Fields.OrderBy(x => x.FieldOrder).ToList();
        XNamespace ns = template.XmlNamespace;

        var dataElement = new XElement(ns + "Data");
        foreach (var field in fields)
        {
            var value = row.GetValue(field.FieldName);
            if (value is null)
            {
                continue;
            }

            dataElement.Add(new XElement(ns + field.XmlElementName, FormatXmlValue(field, value)));
        }

        var root = new XElement(ns + template.XmlRootElement,
            new XElement(ns + "Header",
                new XElement(ns + "InstitutionCode", institutionCode),
                new XElement(ns + "ReportingDate", reportingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                new XElement(ns + "ReturnCode", template.ReturnCode)),
            dataElement);

        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).ToString(SaveOptions.DisableFormatting);
    }

    private async Task<List<ValidationError>> ValidateXsdAsync(string returnCode, string xml, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        var schemaSet = await _xsdGenerator.GenerateSchema(returnCode, ct);
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemaSet,
            Async = true
        };

        settings.ValidationEventHandler += (_, args) =>
        {
            errors.Add(new ValidationError
            {
                RuleId = "XSD",
                Field = "XML",
                Message = args.Message,
                Severity = args.Severity == System.Xml.Schema.XmlSeverityType.Error
                    ? ValidationSeverity.Error
                    : ValidationSeverity.Warning,
                Category = ValidationCategory.Schema
            });
        };

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var reader = XmlReader.Create(stream, settings);
        while (await reader.ReadAsync()) { }
        return errors;
    }

    private async Task<ReturnDataRecord> ParseXmlAsync(string returnCode, string xml, CancellationToken ct)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return await _xmlParser.Parse(stream, returnCode, ct);
    }

    private async Task<ValidationReport> ValidateParsedRecordAsync(
        ReturnDataRecord record,
        string returnCode,
        int institutionId,
        int returnPeriodId,
        CancellationToken ct)
    {
        var submission = Submission.Create(institutionId, returnPeriodId, returnCode, tenantId: null);
        var institutionTenantId = await _db.Institutions
            .Where(x => x.Id == institutionId)
            .Select(x => x.TenantId)
            .FirstAsync(ct);
        submission.TenantId = institutionTenantId;
        return await _validationOrchestrator.Validate(record, submission, institutionId, returnPeriodId, ct);
    }

    private async Task PersistRecordAsync(
        CachedTemplate template,
        ReturnDataRecord record,
        int submissionId,
        Guid tenantId,
        CancellationToken ct)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(tenantId, ct);
        foreach (var row in record.Rows)
        {
            var (sql, parameters) = _sqlBuilder.BuildInsert(
                template.PhysicalTableName,
                template.CurrentVersion.Fields,
                row,
                submissionId,
                tenantId);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
    }

    private async Task<int> RefreshVerificationBundleAsync(
        IReadOnlyList<CachedTemplate> templates,
        Institution institution,
        ReturnPeriod verificationPeriod,
        int demoUserId,
        CancellationToken ct)
    {
        await ArchiveExistingVerificationBundleAsync(templates, institution, verificationPeriod, ct);

        var created = 0;
        var submittedAtBase = DateTime.UtcNow;

        foreach (var template in templates)
        {
            var sample = await GenerateValidatedSampleAsync(
                template,
                institution.InstitutionCode,
                verificationPeriod.ReportingDate.Date,
                sampleIndex: 0,
                institution.Id,
                verificationPeriod.Id,
                ct);

            var submittedAt = submittedAtBase.AddSeconds(created + 1);
            var submission = Submission.Create(institution.Id, verificationPeriod.Id, template.ReturnCode, institution.TenantId);
            submission.SetTemplateVersion(template.CurrentVersion.Id);
            submission.SubmittedByUserId = demoUserId;
            submission.ApprovalRequired = false;
            submission.CreatedAt = submittedAt.AddMinutes(-5);
            submission.SubmittedAt = submittedAt;
            submission.ProcessingDurationMs = 240 + (created * 13);
            submission.StoreRawXml(sample.Xml);
            submission.StoreParsedDataJson(SubmissionPayloadSerializer.Serialize(sample.Record));

            _db.Submissions.Add(submission);
            await _db.SaveChangesAsync(ct);

            await PersistRecordAsync(template, sample.Record, submission.Id, institution.TenantId, ct);

            sample.ValidationReport.FinalizeAt(submittedAt.AddSeconds(2));
            submission.AttachValidationReport(sample.ValidationReport);
            if (sample.ValidationReport.HasWarnings)
            {
                submission.MarkAcceptedWithWarnings();
            }
            else
            {
                submission.MarkAccepted();
            }

            await _db.SaveChangesAsync(ct);
            created++;
        }

        return created;
    }

    private async Task ArchiveExistingVerificationBundleAsync(
        IReadOnlyList<CachedTemplate> templates,
        Institution institution,
        ReturnPeriod verificationPeriod,
        CancellationToken ct)
    {
        var returnCodes = templates
            .Select(x => x.ReturnCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingSubmissions = await _db.Submissions
            .Where(x => x.TenantId == institution.TenantId
                        && x.InstitutionId == institution.Id
                        && x.ReturnPeriodId == verificationPeriod.Id
                        && x.Status != SubmissionStatus.Historical
                        && returnCodes.Contains(x.ReturnCode))
            .ToListAsync(ct);

        if (existingSubmissions.Count == 0)
        {
            return;
        }

        var submissionIds = existingSubmissions
            .Select(x => x.Id)
            .ToList();

        var pendingApprovals = await _db.SubmissionApprovals
            .Where(x => submissionIds.Contains(x.SubmissionId))
            .ToListAsync(ct);

        if (pendingApprovals.Count > 0)
        {
            _db.SubmissionApprovals.RemoveRange(pendingApprovals);
        }

        foreach (var submission in existingSubmissions)
        {
            submission.ApprovalRequired = false;
            submission.Status = SubmissionStatus.Historical;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<CachedTemplate>> LoadDmbTemplatesAsync(CancellationToken ct)
    {
        var templates = (await _templateCache.GetAllPublishedTemplates(ct))
            .Where(x => string.Equals(x.ModuleCode, ModuleCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode)
            .ToList();

        if (templates.Count != 15)
        {
            throw new InvalidOperationException(
                $"Expected 15 published templates for {ModuleCode}, found {templates.Count}.");
        }

        if (templates.Any(x => !string.Equals(x.StructuralCategory, StructuralCategory.FixedRow.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{ModuleCode} sample generation expects fixed-row templates only.");
        }

        return templates;
    }

    private async Task<Institution> ResolveAccessBankAsync(CancellationToken ct)
    {
        var institution = await _db.Institutions
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.InstitutionCode == "ACCESSBA" || x.InstitutionName == "Access Bank Plc",
                ct);

        return institution ?? throw new InvalidOperationException("Access Bank Plc (ACCESSBA) was not found.");
    }

    private async Task<Module> ResolveDmbModuleAsync(CancellationToken ct)
    {
        var module = await _db.Modules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleCode == ModuleCode, ct);

        return module ?? throw new InvalidOperationException($"{ModuleCode} module was not found.");
    }

    private async Task<ReturnPeriod> EnsureOpenVerificationPeriodAsync(Guid tenantId, int moduleId, CancellationToken ct)
    {
        var reportingDate = ResolveLatestCompletedQuarterEnd(DateTime.UtcNow);
        var periodMonth = reportingDate.Month;
        var periodYear = reportingDate.Year;

        var existing = await _db.ReturnPeriods
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId
                     && x.ModuleId == moduleId
                     && x.Year == periodYear
                     && x.Month == periodMonth,
                ct);

        if (existing is not null)
        {
            if (!existing.IsOpen)
            {
                existing.IsOpen = true;
                existing.Status = "Open";
            }

            existing.ReportingDate = reportingDate;
            existing.DeadlineDate = reportingDate.AddDays(45);
            existing.Quarter = ((reportingDate.Month - 1) / 3) + 1;
            existing.Frequency = "Quarterly";
            await _db.SaveChangesAsync(ct);

            return existing;
        }

        var period = new ReturnPeriod
        {
            TenantId = tenantId,
            ModuleId = moduleId,
            Year = periodYear,
            Month = periodMonth,
            Quarter = ((reportingDate.Month - 1) / 3) + 1,
            Frequency = "Quarterly",
            ReportingDate = reportingDate,
            DeadlineDate = reportingDate.AddDays(45),
            CreatedAt = DateTime.UtcNow,
            IsOpen = true,
            Status = "Open",
            NotificationLevel = 0
        };

        _db.ReturnPeriods.Add(period);
        await _db.SaveChangesAsync(ct);
        return period;
    }

    private async Task<List<ReturnPeriod>> EnsureHistoricalPeriodsAsync(
        Guid tenantId,
        int moduleId,
        IReadOnlyList<HistoricalQuarterSpec> specs,
        CancellationToken ct)
    {
        var periods = await _db.ReturnPeriods
            .Where(x => x.TenantId == tenantId && x.ModuleId == moduleId)
            .ToListAsync(ct);

        foreach (var spec in specs)
        {
            if (periods.Any(x => x.Year == spec.Year && x.Month == spec.Month))
            {
                continue;
            }

            var period = new ReturnPeriod
            {
                TenantId = tenantId,
                ModuleId = moduleId,
                Year = spec.Year,
                Month = spec.Month,
                Quarter = spec.Quarter,
                Frequency = "Quarterly",
                ReportingDate = spec.PeriodEndDate,
                DeadlineDate = spec.PeriodEndDate.AddDays(45),
                CreatedAt = DateTime.UtcNow,
                IsOpen = false,
                Status = "Completed",
                NotificationLevel = 0
            };

            _db.ReturnPeriods.Add(period);
            periods.Add(period);
        }

        await _db.SaveChangesAsync(ct);
        return periods;
    }

    private async Task<InstitutionUser> EnsureDemoUserAsync(Institution institution, string demoPassword, CancellationToken ct)
    {
        var user = await _institutionUserRepository.GetByUsername(DemoUsername, ct);
        if (user is null)
        {
            user = await _institutionAuthService.CreateUser(
                institution.Id,
                DemoUsername,
                DemoEmail,
                DemoDisplayName,
                demoPassword,
                InstitutionRole.Admin,
                ct);
        }
        else
        {
            await _institutionAuthService.ResetPassword(user.Id, demoPassword, ct);
            user = await _institutionUserRepository.GetById(user.Id, ct)
                   ?? throw new InvalidOperationException($"Demo user {DemoUsername} disappeared during reset.");
        }

        user.TenantId = institution.TenantId;
        user.InstitutionId = institution.Id;
        user.DisplayName = DemoDisplayName;
        user.Email = DemoEmail;
        user.Role = InstitutionRole.Admin;
        user.IsActive = true;
        user.MustChangePassword = false;
        user.FailedLoginAttempts = 0;
        user.LockedUntil = null;
        await _institutionUserRepository.Update(user, ct);
        await _mfaService.Disable(user.Id, "InstitutionUser");

        return user;
    }

    private static List<string> ParseOperandFields(string operandFieldsJson)
    {
        if (string.IsNullOrWhiteSpace(operandFieldsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(operandFieldsJson)
                ?.Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList()
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> ParseAllowedValues(string? allowedValues)
    {
        if (string.IsNullOrWhiteSpace(allowedValues))
        {
            return [];
        }

        try
        {
            var json = JsonSerializer.Deserialize<List<string>>(allowedValues);
            if (json is { Count: > 0 })
            {
                return json
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Fall through to delimiter parsing.
        }

        return allowedValues
            .Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }

    private static object? TryResolveCoordinatedCrossModuleValue(
        string returnCode,
        TemplateField field,
        DateTime reportingDate)
    {
        var key = $"{returnCode}:{field.FieldName.Trim()}";
        if (!CoordinatedCrossModuleFieldKeys.Contains(key))
        {
            return null;
        }

        var value = reportingDate.Year;
        return field.DataType == FieldDataType.Integer
            ? value
            : ConvertNumericValue(field, value);
    }

    private static object? ConvertStringValue(TemplateField field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return field.DataType switch
        {
            FieldDataType.Integer => int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var integer)
                ? integer
                : 0,
            FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage => decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var decimalValue)
                ? decimalValue
                : 0m,
            FieldDataType.Boolean => bool.TryParse(value, out var boolValue) && boolValue,
            FieldDataType.Date => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateValue)
                ? dateValue.Date
                : DateTime.UtcNow.Date,
            _ => value.Trim()
        };
    }

    private static object ConvertNumericValue(TemplateField field, decimal value)
        => field.DataType switch
        {
            FieldDataType.Integer => (int)Math.Round(value, MidpointRounding.AwayFromZero),
            FieldDataType.Money => decimal.Round(value, 2, MidpointRounding.AwayFromZero),
            _ => decimal.Round(value, 6, MidpointRounding.AwayFromZero)
        };

    private static string FormatXmlValue(TemplateField field, object value)
        => value switch
        {
            DateTime dateValue => dateValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool boolValue => boolValue ? "true" : "false",
            decimal decimalValue => decimalValue.ToString("0.######", CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString("0.######", CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString("0.######", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

    private static decimal EvaluateCustomFunction(string functionName, IReadOnlyList<string> arguments, ReturnDataRow row)
    {
        decimal ValueAt(int index)
        {
            if (index >= arguments.Count)
            {
                return 0m;
            }

            var token = arguments[index];
            return decimal.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric)
                ? numeric
                : row.GetDecimal(token) ?? 0m;
        }

        return functionName.ToUpperInvariant() switch
        {
            "CAR" => CalculateRatio(ValueAt(0) + ValueAt(1), ValueAt(2)),
            "LCR" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "NSFR" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "NPL_RATIO" => CalculateRatio(ValueAt(0), ValueAt(1)),
            "ECL" => decimal.Round(ValueAt(0) * ValueAt(1) * ValueAt(2), 2, MidpointRounding.AwayFromZero),
            _ => 0m
        };
    }

    private static decimal CalculateRatio(decimal numerator, decimal denominator)
    {
        if (denominator == 0)
        {
            return 0m;
        }

        return decimal.Round((numerator / denominator) * 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static List<HistoricalQuarterSpec> BuildHistoricalQuarterSpecs(DateTime nowUtc, int count)
    {
        var latestCompletedQuarterEnd = ResolveLatestCompletedQuarterEnd(nowUtc);

        var specs = new List<HistoricalQuarterSpec>(capacity: count);
        for (var index = 0; index < count; index++)
        {
            var periodEnd = latestCompletedQuarterEnd.AddMonths(-3 * ((count - 1) - index));
            specs.Add(new HistoricalQuarterSpec(
                index,
                periodEnd.Year,
                periodEnd.Month,
                ((periodEnd.Month - 1) / 3) + 1,
                periodEnd,
                periodEnd.AddDays(12)));
        }

        return specs;
    }

    private static DateTime ResolveLatestCompletedQuarterEnd(DateTime utcNow)
    {
        var currentQuarterEndMonth = (((utcNow.Month - 1) / 3) + 1) * 3;
        var currentQuarterEnd = new DateTime(
            utcNow.Year,
            currentQuarterEndMonth,
            DateTime.DaysInMonth(utcNow.Year, currentQuarterEndMonth));

        return utcNow.Date >= currentQuarterEnd
            ? currentQuarterEnd
            : currentQuarterEnd.AddMonths(-3);
    }
}

public sealed class DmbDemoWorkspaceResult
{
    public string TemplatesDirectory { get; init; } = string.Empty;
    public int SampleFilesWritten { get; init; }
    public int SampleFilesDeleted { get; init; }
    public int HistoricalPeriodsCreated { get; init; }
    public int HistoricalSubmissionsCreated { get; init; }
    public string DemoUsername { get; init; } = string.Empty;
    public string DemoPassword { get; init; } = string.Empty;
    public string VerificationReturnCode { get; init; } = string.Empty;
    public int VerificationReturnPeriodId { get; init; }
    public string VerificationSamplePath { get; init; } = string.Empty;
}

public sealed class DmbSampleGenerationResult
{
    public int FilesWritten { get; init; }
    public int FilesDeleted { get; init; }
}

public sealed class DmbHistoricalSeedResult
{
    public int PeriodsCreated { get; init; }
    public int SubmissionsCreated { get; init; }
}

internal sealed record GeneratedTemplateSample(
    string ReturnCode,
    string Xml,
    ReturnDataRecord Record,
    ValidationReport ValidationReport);

internal sealed record HistoricalQuarterSpec(
    int Index,
    int Year,
    int Month,
    int Quarter,
    DateTime PeriodEndDate,
    DateTime SubmittedAt);
