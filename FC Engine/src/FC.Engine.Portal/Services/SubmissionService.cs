namespace FC.Engine.Portal.Services;

using FC.Engine.Application.DTOs;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

/// <summary>
/// Portal service that orchestrates the submission wizard workflow:
/// template listing, period queries, and delegating to IngestionOrchestrator.
/// Handles maker-checker routing after validation completes.
/// </summary>
public class SubmissionService
{
    private readonly TemplateService _templateService;
    private readonly IEntitlementService _entitlementService;
    private readonly ITenantContext _tenantContext;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ISubmissionApprovalRepository _approvalRepo;
    private readonly IngestionOrchestrator _orchestrator;
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly NotificationService _notificationSvc;
    private readonly IFilingCalendarService _filingCalendarService;
    private readonly ILogger<SubmissionService> _logger;

    public SubmissionService(
        TemplateService templateService,
        IEntitlementService entitlementService,
        ITenantContext tenantContext,
        ISubmissionRepository submissionRepo,
        ISubmissionApprovalRepository approvalRepo,
        IngestionOrchestrator orchestrator,
        IDbContextFactory<MetadataDbContext> dbFactory,
        NotificationService notificationSvc,
        IFilingCalendarService filingCalendarService,
        ILogger<SubmissionService> logger)
    {
        _templateService = templateService;
        _entitlementService = entitlementService;
        _tenantContext = tenantContext;
        _submissionRepo = submissionRepo;
        _approvalRepo = approvalRepo;
        _orchestrator = orchestrator;
        _dbFactory = dbFactory;
        _notificationSvc = notificationSvc;
        _filingCalendarService = filingCalendarService;
        _logger = logger;
    }

    /// <summary>
    /// Gets all published templates with "already submitted" status for the current period.
    /// </summary>
    public async Task<List<TemplateSelectItem>> GetTemplatesForInstitution(int institutionId, string? moduleCode = null)
    {
        var normalizedModuleCode = string.IsNullOrWhiteSpace(moduleCode)
            ? null
            : moduleCode.Trim().ToUpperInvariant();

        await using var db = await _dbFactory.CreateDbContextAsync();

        List<TemplateSelectItem> publishedTemplates;
        if (_tenantContext.CurrentTenantId is { } tenantId)
        {
            var entitlement = await _entitlementService.ResolveEntitlements(tenantId);
            var activeModuleIds = entitlement.ActiveModules
                .Select(x => x.ModuleId)
                .Distinct()
                .ToList();

            if (activeModuleIds.Count == 0)
            {
                return new List<TemplateSelectItem>();
            }

            var moduleCodeMap = await db.Modules
                .AsNoTracking()
                .Where(x => activeModuleIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.ModuleCode);

            var entitledTemplates = await _templateService.GetEntitledTemplates(tenantId);
            publishedTemplates = entitledTemplates
                .Where(x => x.CurrentPublishedVersion is not null)
                .Select(x => new TemplateSelectItem
                {
                    ReturnCode = x.ReturnCode,
                    TemplateName = x.Name,
                    Frequency = x.Frequency.ToString(),
                    StructuralCategory = x.StructuralCategory.ToString(),
                    ModuleCode = x.ModuleId.HasValue && moduleCodeMap.TryGetValue(x.ModuleId.Value, out var resolvedModuleCode)
                        ? resolvedModuleCode
                        : null
                })
                .Where(x => normalizedModuleCode is null
                    || string.Equals(x.ModuleCode, normalizedModuleCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.ReturnCode)
                .ToList();
        }
        else
        {
            var allTemplates = await _templateService.GetAllTemplates();
            publishedTemplates = allTemplates
                .Where(x => x.PublishedVersionId.HasValue)
                .Select(x => new TemplateSelectItem
                {
                    ReturnCode = x.ReturnCode,
                    TemplateName = x.Name,
                    Frequency = x.Frequency,
                    StructuralCategory = x.StructuralCategory
                })
                .OrderBy(x => x.ReturnCode)
                .ToList();
        }

        var submissions = await _submissionRepo.GetByInstitution(institutionId);
        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1);
        var currentMonthEnd = currentMonth.AddMonths(1).AddDays(-1);

        var submittedReturnCodes = submissions
            .Where(s => s.SubmittedAt >= currentMonth && s.SubmittedAt <= currentMonthEnd)
            .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
            .Select(s => s.ReturnCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return publishedTemplates
            .Select(t =>
            {
                t.AlreadySubmitted = submittedReturnCodes.Contains(t.ReturnCode);
                return t;
            })
            .ToList();
    }

    /// <summary>
    /// Gets open return periods, optionally checking for existing submissions.
    /// </summary>
    public async Task<List<PeriodSelectItem>> GetOpenPeriods(int institutionId, string returnCode)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.ReturnPeriods
            .AsNoTracking()
            .Where(rp => rp.IsOpen);

        if (_tenantContext.CurrentTenantId is { } tenantId)
        {
            query = query.Where(rp => rp.TenantId == tenantId);

            var entitledTemplates = await _templateService.GetEntitledTemplates(tenantId);
            var template = entitledTemplates.FirstOrDefault(x =>
                x.ReturnCode.Equals(returnCode, StringComparison.OrdinalIgnoreCase));

            if (template?.ModuleId is int moduleId)
            {
                query = query.Where(rp => rp.ModuleId == moduleId);
            }
        }

        var periods = await query
            .OrderByDescending(rp => rp.Year)
            .ThenByDescending(rp => rp.Month)
            .ToListAsync();

        var submissions = await _submissionRepo.GetByInstitution(institutionId);
        var existingByPeriod = submissions
            .Where(s => s.ReturnCode.Equals(returnCode, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.Status != SubmissionStatus.Rejected && s.Status != SubmissionStatus.ApprovalRejected)
            .GroupBy(s => s.ReturnPeriodId)
            .ToDictionary(g => g.Key, g => g.First());

        return periods.Select(p =>
        {
            var hasSub = existingByPeriod.TryGetValue(p.Id, out var sub);
            return new PeriodSelectItem
            {
                ReturnPeriodId = p.Id,
                Value = $"{p.Year}-{p.Month:00}",
                Label = new DateTime(p.Year, p.Month, 1).ToString("MMMM yyyy"),
                ReportingDate = p.ReportingDate,
                Year = p.Year,
                Month = p.Month,
                HasExistingSubmission = hasSub,
                DeadlineDate = p.EffectiveDeadline,
                IsLocked = false, // open periods are by definition not locked
                ExistingSubmissionStatus = hasSub ? sub!.Status : null,
                ExistingSubmissionId = hasSub ? sub!.Id : null
            };
        }).ToList();
    }

    /// <summary>
    /// Check if an institution has maker-checker enabled.
    /// </summary>
    public async Task<bool> IsMakerCheckerEnabled(int institutionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var inst = await db.Institutions.FindAsync(institutionId);
        return inst?.MakerCheckerEnabled ?? false;
    }

    /// <summary>
    /// Delegates to IngestionOrchestrator.Process — validates and persists the return.
    /// If maker-checker is enabled and validation passes, routes to PendingApproval.
    /// </summary>
    public async Task<SubmissionResultDto> ProcessSubmission(
        Stream xmlStream, string returnCode, int institutionId, int returnPeriodId,
        int? submittedByUserId = null, string? submitterNotes = null,
        int? originalSubmissionId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var makerCheckerEnabled = submittedByUserId.HasValue && await IsMakerCheckerEnabled(institutionId);
        SubmissionReviewNotificationContext? reviewNotificationContext = null;

        if (makerCheckerEnabled && submittedByUserId.HasValue)
        {
            var submitter = await db.InstitutionUsers.FindAsync(submittedByUserId.Value);
            var institution = await db.Institutions.FindAsync(institutionId);
            var period = await db.ReturnPeriods.FindAsync(returnPeriodId);

            reviewNotificationContext = new SubmissionReviewNotificationContext
            {
                NotifySubmittedForReview = true,
                SubmittedByName = submitter?.DisplayName ?? "Maker",
                InstitutionName = institution?.InstitutionName ?? string.Empty,
                PeriodLabel = period is null
                    ? DateTime.UtcNow.ToString("MMM yyyy")
                    : new DateTime(period.Year, period.Month, 1).ToString("MMM yyyy"),
                PortalBaseUrl = "https://portal.regos.app"
            };
        }

        var result = await _orchestrator.Process(
            xmlStream,
            returnCode,
            institutionId,
            returnPeriodId,
            reviewNotificationContext);

        // Check if maker-checker routing is needed
        var isAccepted = result.Status == "Accepted" || result.Status == "AcceptedWithWarnings";
        if (!isAccepted || submittedByUserId is null)
        {
            if (isAccepted)
            {
                await TryRecordSla(returnPeriodId, result.SubmissionId);
            }

            // Notify of submission result (rejected or non-user submissions)
            if (submittedByUserId.HasValue)
            {
                try
                {
                    var period = await db.ReturnPeriods.FindAsync(returnPeriodId);
                    var periodStr = period is not null
                        ? new DateTime(period.Year, period.Month, 1).ToString("MMM yyyy") : "";
                    var status = Enum.TryParse<SubmissionStatus>(result.Status, out var s) ? s : SubmissionStatus.Rejected;
                    await _notificationSvc.NotifySubmissionResult(
                        submittedByUserId.Value, institutionId, result.SubmissionId,
                        returnCode, periodStr, status, result.ValidationReport?.ErrorCount ?? 0, result.ValidationReport?.WarningCount ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send submission result notification for submission {SubmissionId}", result.SubmissionId);
                }
            }
            return result;
        }

        if (!makerCheckerEnabled)
        {
            // Even without maker-checker, record who submitted
            var sub = await _submissionRepo.GetById(result.SubmissionId);
            if (sub is not null)
            {
                sub.SubmittedByUserId = submittedByUserId;
                await _submissionRepo.Update(sub);
            }

            await TryRecordSla(returnPeriodId, result.SubmissionId);

            // Notify of direct acceptance
            try
            {
                var period = await db.ReturnPeriods.FindAsync(returnPeriodId);
                var periodStr = period is not null
                    ? new DateTime(period.Year, period.Month, 1).ToString("MMM yyyy") : "";
                var status = Enum.TryParse<SubmissionStatus>(result.Status, out var s) ? s : SubmissionStatus.Accepted;
                await _notificationSvc.NotifySubmissionResult(
                    submittedByUserId.Value, institutionId, result.SubmissionId,
                    returnCode, periodStr, status, result.ValidationReport?.ErrorCount ?? 0, result.ValidationReport?.WarningCount ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send direct acceptance notification for submission {SubmissionId}", result.SubmissionId);
            }

            return result;
        }

        // Route to PendingApproval
        var submission = await _submissionRepo.GetById(result.SubmissionId);
        if (submission is not null)
        {
            submission.SubmittedByUserId = submittedByUserId;
            submission.ApprovalRequired = true;
            submission.MarkPendingApproval();
            await _submissionRepo.Update(submission);

            // Create approval record
            var approval = new SubmissionApproval
            {
                TenantId = submission.TenantId,
                SubmissionId = submission.Id,
                RequestedByUserId = submittedByUserId.Value,
                RequestedAt = DateTime.UtcNow,
                SubmitterNotes = submitterNotes,
                Status = ApprovalStatus.Pending,
                OriginalSubmissionId = originalSubmissionId
            };
            await _approvalRepo.Create(approval);

            // Update the result DTO
            result.Status = "PendingApproval";
        }

        return result;
    }

    private async Task TryRecordSla(int periodId, int submissionId)
    {
        try
        {
            await _filingCalendarService.RecordSla(periodId, submissionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SLA recording failed for period {PeriodId}, submission {SubmissionId}", periodId, submissionId);
        }
    }
}
