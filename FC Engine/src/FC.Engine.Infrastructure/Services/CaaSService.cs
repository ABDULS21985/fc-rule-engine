using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Compliance-as-a-Service orchestrator — delegates to existing services
/// to provide a unified, embeddable compliance API.
/// </summary>
public class CaaSService : ICaaSService
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly ValidationOrchestrator _validationOrchestrator;
    private readonly IFilingCalendarService _filingCalendarService;
    private readonly IEntitlementService _entitlementService;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IDataFeedService _dataFeedService;
    private readonly ILogger<CaaSService> _logger;

    public CaaSService(
        ITemplateMetadataCache templateCache,
        ValidationOrchestrator validationOrchestrator,
        IFilingCalendarService filingCalendarService,
        IEntitlementService entitlementService,
        ISubmissionRepository submissionRepo,
        IDataFeedService dataFeedService,
        ILogger<CaaSService> logger)
    {
        _templateCache = templateCache;
        _validationOrchestrator = validationOrchestrator;
        _filingCalendarService = filingCalendarService;
        _entitlementService = entitlementService;
        _submissionRepo = submissionRepo;
        _dataFeedService = dataFeedService;
        _logger = logger;
    }

    public async Task<CaaSValidationResponse> ValidateAsync(
        Guid tenantId, CaaSValidateRequest request, CancellationToken ct = default)
    {
        var template = await _templateCache.GetPublishedTemplate(tenantId, request.ReturnCode, ct);

        var report = await _validationOrchestrator.ValidateRelaxed(
            request.Records, template, tenantId, ct);

        var response = new CaaSValidationResponse
        {
            IsValid = report.IsValid,
            ErrorCount = report.ErrorCount,
            WarningCount = report.WarningCount,
            Errors = report.Errors.Select(e => new CaaSValidationError
            {
                RuleId = e.RuleId,
                Field = e.Field,
                Message = e.Message,
                Severity = e.Severity.ToString(),
                Category = e.Category.ToString(),
                ExpectedValue = e.ExpectedValue,
                ActualValue = e.ActualValue
            }).ToList()
        };

        // Compute a preview score: 100 - (errors * 10) - (warnings * 2), clamped 0..100
        var rawScore = 100.0 - (report.ErrorCount * 10) - (report.WarningCount * 2);
        response.ComplianceScorePreview = Math.Clamp(rawScore, 0, 100);

        return response;
    }

    public async Task<CaaSSubmitResponse> SubmitReturnAsync(
        Guid tenantId, int institutionId, CaaSSubmitRequest request, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Use the DataFeed service for JSON-based submission
        var feedRequest = new DataFeedRequest
        {
            PeriodCode = request.PeriodCode,
            InstitutionCode = request.InstitutionCode,
            Fields = request.Records.SelectMany(row =>
                row.Select(kv => new DataFeedFieldValue
                {
                    FieldCode = kv.Key,
                    Value = kv.Value
                })).ToList()
        };

        var feedResult = await _dataFeedService.ProcessFeed(
            tenantId, request.ReturnCode, feedRequest, null, ct);

        sw.Stop();

        // Also run validation for the response
        CaaSValidationResponse? validationResult = null;
        if (feedResult.Success)
        {
            try
            {
                var template = await _templateCache.GetPublishedTemplate(tenantId, request.ReturnCode, ct);
                var report = await _validationOrchestrator.ValidateRelaxed(
                    request.Records, template, tenantId, ct);

                validationResult = new CaaSValidationResponse
                {
                    IsValid = report.IsValid,
                    ErrorCount = report.ErrorCount,
                    WarningCount = report.WarningCount,
                    Errors = report.Errors.Select(e => new CaaSValidationError
                    {
                        RuleId = e.RuleId,
                        Field = e.Field,
                        Message = e.Message,
                        Severity = e.Severity.ToString(),
                        Category = e.Category.ToString(),
                        ExpectedValue = e.ExpectedValue,
                        ActualValue = e.ActualValue
                    }).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-submission validation preview failed for {ReturnCode}", request.ReturnCode);
            }
        }

        return new CaaSSubmitResponse
        {
            Success = feedResult.Success,
            SubmissionId = feedResult.SubmissionId,
            Status = feedResult.Status,
            ProcessingDurationMs = sw.ElapsedMilliseconds,
            ValidationResult = validationResult
        };
    }

    public async Task<CaaSTemplateResponse?> GetTemplateStructureAsync(
        Guid tenantId, string moduleCode, CancellationToken ct = default)
    {
        var allTemplates = await _templateCache.GetAllPublishedTemplates(tenantId, ct);
        var moduleTemplates = allTemplates
            .Where(t => string.Equals(t.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (moduleTemplates.Count == 0)
            return null;

        return new CaaSTemplateResponse
        {
            ModuleCode = moduleCode,
            ModuleName = moduleTemplates.FirstOrDefault()?.Name ?? moduleCode,
            Returns = moduleTemplates.Select(t => new CaaSTemplateReturn
            {
                ReturnCode = t.ReturnCode,
                ReturnName = t.Name,
                Frequency = t.Frequency.ToString(),
                VersionNumber = t.CurrentVersion.VersionNumber,
                Fields = t.CurrentVersion.Fields.Select(f => new CaaSFieldDefinition
                {
                    FieldName = f.FieldName,
                    DisplayName = f.DisplayName,
                    DataType = f.DataType.ToString(),
                    IsRequired = f.IsRequired,
                    MinValue = f.MinValue,
                    MaxValue = f.MaxValue,
                    MaxLength = f.MaxLength,
                    AllowedValues = f.AllowedValues,
                    Description = f.HelpText
                }).ToList(),
                Formulas = t.CurrentVersion.IntraSheetFormulas.Select(f => new CaaSFormulaDefinition
                {
                    RuleId = f.RuleCode,
                    Expression = f.CustomExpression ?? string.Empty,
                    Description = f.RuleName
                }).ToList()
            }).ToList()
        };
    }

    public async Task<List<CaaSDeadlineItem>> GetDeadlinesAsync(
        Guid tenantId, CancellationToken ct = default)
    {
        var ragItems = await _filingCalendarService.GetRagStatus(tenantId, ct);

        return ragItems.Select(r => new CaaSDeadlineItem
        {
            ModuleCode = r.ModuleCode,
            ModuleName = r.ModuleName,
            ReturnCode = r.ModuleCode,
            PeriodCode = r.PeriodLabel,
            Deadline = r.Deadline,
            RagStatus = r.Color.ToString(),
            DaysRemaining = (r.Deadline.Date - DateTime.UtcNow.Date).Days
        }).ToList();
    }

    public async Task<CaaSScoreResponse> GetComplianceScoreAsync(
        Guid tenantId, int institutionId, CaaSScoreRequest request, CancellationToken ct = default)
    {
        var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);

        if (!string.IsNullOrWhiteSpace(request.ModuleCode))
        {
            submissions = submissions
                .Where(s => string.Equals(s.ReturnCode, request.ModuleCode, StringComparison.OrdinalIgnoreCase)
                            || (s.ReturnCode?.StartsWith(request.ModuleCode, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        var total = submissions.Count;
        var clean = submissions.Count(s => s.Status == SubmissionStatus.Accepted);
        var late = 0; // Would need SLA data to compute

        var passRate = total > 0 ? (double)clean / total * 100 : 100;
        var deadlineAdherence = total > 0 ? (double)(total - late) / total * 100 : 100;
        var completeness = total > 0 ? Math.Min(100, total * 10.0) : 0;

        var overall = (passRate * 0.5) + (deadlineAdherence * 0.3) + (completeness * 0.2);

        return new CaaSScoreResponse
        {
            OverallScore = Math.Round(overall, 1),
            Rating = overall switch
            {
                >= 90 => "Excellent",
                >= 75 => "Good",
                >= 50 => "Fair",
                _ => "Poor"
            },
            Breakdown = new CaaSScoreBreakdown
            {
                ValidationPassRate = Math.Round(passRate, 1),
                DeadlineAdherence = Math.Round(deadlineAdherence, 1),
                CompletenessScore = Math.Round(completeness, 1),
                TotalSubmissions = total,
                CleanSubmissions = clean,
                LateSubmissions = late
            }
        };
    }

    public async Task<List<CaaSRegulatoryChange>> GetRegulatoryChangesAsync(
        Guid tenantId, string? moduleCode, CancellationToken ct = default)
    {
        var allTemplates = await _templateCache.GetAllPublishedTemplates(tenantId, ct);

        if (!string.IsNullOrWhiteSpace(moduleCode))
        {
            allTemplates = allTemplates
                .Where(t => string.Equals(t.ModuleCode, moduleCode, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Surface templates where version > 1 as "changes"
        return allTemplates
            .Where(t => t.CurrentVersion.VersionNumber > 1)
            .Select(t => new CaaSRegulatoryChange
            {
                ModuleCode = t.ModuleCode ?? string.Empty,
                ReturnCode = t.ReturnCode,
                FromVersion = t.CurrentVersion.VersionNumber - 1,
                ToVersion = t.CurrentVersion.VersionNumber,
                ChangeType = "TemplateUpdated",
                Description = $"Template '{t.Name}' updated to version {t.CurrentVersion.VersionNumber}",
                EffectiveDate = DateTime.UtcNow // Would need publish date from version entity
            }).ToList();
    }

    public async Task<CaaSSimulateResponse> SimulateAsync(
        Guid tenantId, CaaSSimulateRequest request, CancellationToken ct = default)
    {
        var template = await _templateCache.GetPublishedTemplate(tenantId, request.ReturnCode, ct);

        // Apply overrides to each record
        var records = request.Records.Select(r =>
        {
            var merged = new Dictionary<string, object?>(r);
            if (request.Overrides != null)
            {
                foreach (var ov in request.Overrides)
                    merged[ov.Key] = ov.Value;
            }
            return merged;
        }).ToList();

        var report = await _validationOrchestrator.ValidateRelaxed(records, template, tenantId, ct);

        var validationResponse = new CaaSValidationResponse
        {
            IsValid = report.IsValid,
            ErrorCount = report.ErrorCount,
            WarningCount = report.WarningCount,
            Errors = report.Errors.Select(e => new CaaSValidationError
            {
                RuleId = e.RuleId,
                Field = e.Field,
                Message = e.Message,
                Severity = e.Severity.ToString(),
                Category = e.Category.ToString(),
                ExpectedValue = e.ExpectedValue,
                ActualValue = e.ActualValue
            }).ToList()
        };

        var score = 100.0 - (report.ErrorCount * 10) - (report.WarningCount * 2);
        var recommendations = new List<string>();
        if (report.ErrorCount > 0)
            recommendations.Add($"Fix {report.ErrorCount} validation error(s) before submission.");
        if (report.WarningCount > 0)
            recommendations.Add($"Review {report.WarningCount} warning(s) for potential data quality issues.");
        if (report.IsValid)
            recommendations.Add("Data is compliant — ready for submission.");

        return new CaaSSimulateResponse
        {
            ScenarioName = request.ScenarioName ?? "Unnamed Scenario",
            ValidationResult = validationResponse,
            ProjectedComplianceScore = Math.Clamp(score, 0, 100),
            Recommendations = recommendations
        };
    }
}
