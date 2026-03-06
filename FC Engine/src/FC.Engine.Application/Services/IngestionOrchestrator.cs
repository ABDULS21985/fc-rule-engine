using System.Diagnostics;
using FC.Engine.Application.DTOs;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Events;
using FC.Engine.Domain.Notifications;

namespace FC.Engine.Application.Services;

public class IngestionOrchestrator
{
    private readonly ITemplateMetadataCache _cache;
    private readonly IXsdGenerator _xsdGenerator;
    private readonly IGenericXmlParser _xmlParser;
    private readonly IGenericDataRepository _dataRepo;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ValidationOrchestrator _validationOrchestrator;
    private readonly IEntitlementService? _entitlementService;
    private readonly ITenantContext? _tenantContext;
    private readonly IInterModuleDataFlowEngine? _dataFlowEngine;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly IDomainEventPublisher? _domainEventPublisher;

    public IngestionOrchestrator(
        ITemplateMetadataCache cache,
        IXsdGenerator xsdGenerator,
        IGenericXmlParser xmlParser,
        IGenericDataRepository dataRepo,
        ISubmissionRepository submissionRepo,
        ValidationOrchestrator validationOrchestrator,
        IEntitlementService? entitlementService = null,
        ITenantContext? tenantContext = null,
        IInterModuleDataFlowEngine? dataFlowEngine = null,
        INotificationOrchestrator? notificationOrchestrator = null,
        IDomainEventPublisher? domainEventPublisher = null)
    {
        _cache = cache;
        _xsdGenerator = xsdGenerator;
        _xmlParser = xmlParser;
        _dataRepo = dataRepo;
        _submissionRepo = submissionRepo;
        _validationOrchestrator = validationOrchestrator;
        _entitlementService = entitlementService;
        _tenantContext = tenantContext;
        _dataFlowEngine = dataFlowEngine;
        _notificationOrchestrator = notificationOrchestrator;
        _domainEventPublisher = domainEventPublisher;
    }

    public Task<SubmissionResultDto> Process(
        Stream xmlStream, string returnCode, int institutionId, int returnPeriodId,
        CancellationToken ct = default)
    {
        return Process(xmlStream, returnCode, institutionId, returnPeriodId, null, ct);
    }

    public async Task<SubmissionResultDto> Process(
        Stream xmlStream, string returnCode, int institutionId, int returnPeriodId,
        SubmissionReviewNotificationContext? reviewNotificationContext,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var tenantId = _tenantContext?.CurrentTenantId;

        // 1. Create submission record
        var submission = Submission.Create(institutionId, returnPeriodId, returnCode, tenantId);
        await _submissionRepo.Add(submission, ct);

        try
        {
            // 2. Resolve template
            var template = await _cache.GetPublishedTemplate(returnCode, ct);
            submission.SetTemplateVersion(template.CurrentVersion.Id);

            // 2b. Entitlement check — verify tenant has access to this module
            if (_entitlementService != null && _tenantContext?.CurrentTenantId != null
                && template.ModuleCode != null)
            {
                var hasAccess = await _entitlementService.HasModuleAccess(
                    _tenantContext.CurrentTenantId.Value, template.ModuleCode, ct);
                if (!hasAccess)
                {
                    var entitlementReport = ValidationReport.Create(submission.Id, submission.TenantId);
                    entitlementReport.AddError(new ValidationError
                    {
                        RuleId = "MODULE_NOT_ENTITLED",
                        Field = "N/A",
                        Message = $"Tenant is not entitled to submit returns for module '{template.ModuleCode}'",
                        Severity = ValidationSeverity.Error,
                        Category = ValidationCategory.Schema
                    });
                    entitlementReport.FinalizeAt(DateTime.UtcNow);
                    submission.AttachValidationReport(entitlementReport);
                    submission.MarkRejected();
                    submission.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
                    await _submissionRepo.Update(submission, ct);
                    return MapResult(submission);
                }
            }

            submission.MarkParsing();
            await _submissionRepo.Update(submission, ct);

            // 3. Buffer stream (XmlReader requires synchronous reads not supported by request body)
            var bufferedStream = new MemoryStream();
            await xmlStream.CopyToAsync(bufferedStream, ct);
            bufferedStream.Position = 0;

            // 3b. XSD validation
            var schemaErrors = await ValidateXsd(bufferedStream, returnCode, ct);
            if (schemaErrors.Count > 0)
            {
                var schemaReport = ValidationReport.Create(submission.Id, submission.TenantId);
                foreach (var err in schemaErrors)
                    schemaReport.AddError(err);
                schemaReport.FinalizeAt(DateTime.UtcNow);

                submission.AttachValidationReport(schemaReport);
                submission.MarkRejected();
                submission.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
                await _submissionRepo.Update(submission, ct);

                return MapResult(submission);
            }

            // 4. Reset stream and parse XML
            bufferedStream.Position = 0;
            var record = await _xmlParser.Parse(bufferedStream, returnCode, ct);

            // 5. Run validation pipeline
            submission.MarkValidating();
            await _submissionRepo.Update(submission, ct);

            var report = await _validationOrchestrator.Validate(
                record, submission, institutionId, returnPeriodId, ct);

            submission.AttachValidationReport(report);

            // 6. Persist data if valid
            if (report.IsValid || !report.HasErrors)
            {
                // Delete any previous data for this submission
                await _dataRepo.DeleteBySubmission(returnCode, submission.Id, ct);
                await _dataRepo.Save(record, submission.Id, ct);

                if (_dataFlowEngine != null
                    && tenantId.HasValue
                    && !string.IsNullOrWhiteSpace(template.ModuleCode))
                {
                    await _dataFlowEngine.ProcessDataFlows(
                        tenantId.Value,
                        submission.Id,
                        template.ModuleCode,
                        returnCode,
                        institutionId,
                        returnPeriodId,
                        ct);
                }

                submission.MarkAccepted();
                if (report.HasWarnings)
                    submission.MarkAcceptedWithWarnings();
            }
            else
            {
                submission.MarkRejected();
            }

            report.FinalizeAt(DateTime.UtcNow);
            submission.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;
            await _submissionRepo.Update(submission, ct);

            if ((submission.Status == SubmissionStatus.Accepted
                    || submission.Status == SubmissionStatus.AcceptedWithWarnings)
                && reviewNotificationContext?.NotifySubmittedForReview == true
                && submission.TenantId != Guid.Empty)
            {
                await PublishSubmissionForReviewNotification(
                    submission,
                    template,
                    reviewNotificationContext,
                    ct);
            }

            // Publish domain events for webhook/event bus (RG-30)
            if (_domainEventPublisher is not null && tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                try
                {
                    var moduleCode = template.ModuleCode ?? string.Empty;
                    var correlationId = Guid.NewGuid();

                    await _domainEventPublisher.PublishAsync(new ReturnCreatedEvent(
                        tenantId.Value, submission.Id, moduleCode, returnCode,
                        reviewNotificationContext?.PeriodLabel ?? string.Empty,
                        submission.CreatedAt, DateTime.UtcNow, correlationId), ct);

                    if (submission.Status == SubmissionStatus.Accepted
                        || submission.Status == SubmissionStatus.AcceptedWithWarnings)
                    {
                        await _domainEventPublisher.PublishAsync(new ReturnSubmittedEvent(
                            tenantId.Value, submission.Id, moduleCode, returnCode,
                            reviewNotificationContext?.PeriodLabel ?? string.Empty,
                            reviewNotificationContext?.SubmittedByName ?? "system",
                            DateTime.UtcNow, DateTime.UtcNow, correlationId), ct);
                    }

                    await _domainEventPublisher.PublishAsync(new ValidationCompletedEvent(
                        tenantId.Value, submission.Id, moduleCode,
                        submission.ValidationReport?.ErrorCount ?? 0,
                        submission.ValidationReport?.WarningCount ?? 0,
                        DateTime.UtcNow, DateTime.UtcNow, correlationId), ct);
                }
                catch
                {
                    // Domain event publishing must not block submission processing
                }
            }

            return MapResult(submission);
        }
        catch (Exception ex)
        {
            submission.MarkRejected();
            submission.ProcessingDurationMs = (int)sw.ElapsedMilliseconds;

            var errorReport = ValidationReport.Create(submission.Id, submission.TenantId);
            errorReport.AddError(new ValidationError
            {
                RuleId = "SYSTEM",
                Field = "N/A",
                Message = $"Processing error: {ex.Message}",
                Severity = ValidationSeverity.Error,
                Category = ValidationCategory.Schema
            });
            errorReport.FinalizeAt(DateTime.UtcNow);
            submission.AttachValidationReport(errorReport);

            await _submissionRepo.Update(submission, ct);
            return MapResult(submission);
        }
    }

    private async Task PublishSubmissionForReviewNotification(
        Submission submission,
        CachedTemplate template,
        SubmissionReviewNotificationContext context,
        CancellationToken ct)
    {
        if (_notificationOrchestrator is null || submission.TenantId == Guid.Empty)
        {
            return;
        }

        var periodLabel = string.IsNullOrWhiteSpace(context.PeriodLabel)
            ? DateTime.UtcNow.ToString("MMM yyyy")
            : context.PeriodLabel!;
        var submitterName = string.IsNullOrWhiteSpace(context.SubmittedByName)
            ? "Maker"
            : context.SubmittedByName!;
        var reviewPath = $"/submissions/{submission.Id}";

        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["InstitutionName"] = context.InstitutionName ?? string.Empty,
            ["UserName"] = submitterName,
            ["ModuleName"] = template.Name,
            ["ReturnCode"] = submission.ReturnCode,
            ["PeriodLabel"] = periodLabel,
            ["ReviewUrl"] = BuildReviewUrl(context.PortalBaseUrl, reviewPath),
            ["SubmissionId"] = submission.Id.ToString()
        };

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = submission.TenantId,
            EventType = NotificationEvents.ReturnSubmittedForReview,
            Title = $"{template.Name} Return Submitted for Review",
            Message = $"{submitterName} has submitted {submission.ReturnCode} for {periodLabel}. Please review.",
            Priority = NotificationPriority.Normal,
            ActionUrl = reviewPath,
            RecipientInstitutionId = submission.InstitutionId,
            RecipientRoles = new List<string> { "Checker", "Admin" },
            Data = data
        }, ct);
    }

    private static string BuildReviewUrl(string? portalBaseUrl, string reviewPath)
    {
        if (string.IsNullOrWhiteSpace(portalBaseUrl))
        {
            return $"https://portal.regos.app{reviewPath}";
        }

        return $"{portalBaseUrl.TrimEnd('/')}{reviewPath}";
    }

    private async Task<List<ValidationError>> ValidateXsd(Stream xmlStream, string returnCode, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        try
        {
            var schemaSet = await _xsdGenerator.GenerateSchema(returnCode, ct);
            var settings = new System.Xml.XmlReaderSettings
            {
                ValidationType = System.Xml.ValidationType.Schema,
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
                    Severity = e.Severity == System.Xml.Schema.XmlSeverityType.Error
                        ? ValidationSeverity.Error : ValidationSeverity.Warning,
                    Category = ValidationCategory.Schema
                });
            };

            using var reader = System.Xml.XmlReader.Create(xmlStream, settings);
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

    private static SubmissionResultDto MapResult(Submission submission)
    {
        var dto = new SubmissionResultDto
        {
            SubmissionId = submission.Id,
            ReturnCode = submission.ReturnCode,
            Status = submission.Status.ToString(),
            ProcessingDurationMs = submission.ProcessingDurationMs
        };

        if (submission.ValidationReport != null)
        {
            dto.ValidationReport = new ValidationReportDto
            {
                IsValid = submission.ValidationReport.IsValid,
                ErrorCount = submission.ValidationReport.ErrorCount,
                WarningCount = submission.ValidationReport.WarningCount,
                Errors = submission.ValidationReport.Errors.Select(e => new ValidationErrorDto
                {
                    RuleId = e.RuleId,
                    Field = e.Field,
                    Message = e.Message,
                    Severity = e.Severity.ToString(),
                    Category = e.Category.ToString(),
                    ExpectedValue = e.ExpectedValue,
                    ActualValue = e.ActualValue,
                    ReferencedReturnCode = e.ReferencedReturnCode
                }).ToList()
            };
        }

        return dto;
    }
}

public sealed class SubmissionReviewNotificationContext
{
    public bool NotifySubmittedForReview { get; set; }
    public string? SubmittedByName { get; set; }
    public string? InstitutionName { get; set; }
    public string? PeriodLabel { get; set; }
    public string? PortalBaseUrl { get; set; }
}
