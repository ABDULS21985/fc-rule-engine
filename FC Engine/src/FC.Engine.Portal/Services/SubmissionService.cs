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
    private readonly ITemplateMetadataCache _templateCache;
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
        ITemplateMetadataCache templateCache,
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
        _templateCache = templateCache;
        _logger = logger;
    }

    // ── List & Detail reads (service-layer replacements for direct repo access) ──

    /// <summary>
    /// Gets all submissions for an institution, mapped to list view models.
    /// Replaces direct ISubmissionRepository + ITemplateMetadataCache usage in pages.
    /// </summary>
    public async Task<List<SubmissionListItem>> GetSubmissionsForInstitution(int institutionId)
    {
        var rawSubmissions = await _submissionRepo.GetByInstitution(institutionId);

        var allTemplates = await _templateCache.GetAllPublishedTemplates();
        var templateMetaMap = allTemplates.ToDictionary(
            t => t.ReturnCode,
            t => new { t.Name, t.ModuleCode },
            StringComparer.OrdinalIgnoreCase
        );

        return rawSubmissions.Select(s =>
        {
            templateMetaMap.TryGetValue(s.ReturnCode, out var templateMeta);
            return new SubmissionListItem
            {
                Id = s.Id,
                ReturnCode = s.ReturnCode,
                TemplateName = templateMeta?.Name ?? s.ReturnCode,
                ModuleCode = templateMeta?.ModuleCode,
                PeriodLabel = s.ReturnPeriod is not null
                    ? new DateTime(s.ReturnPeriod.Year, s.ReturnPeriod.Month, 1).ToString("MMM yyyy")
                    : "—",
                SubmittedAt = s.SubmittedAt,
                Status = s.Status.ToString(),
                ErrorCount = s.ValidationReport?.ErrorCount ?? 0,
                WarningCount = s.ValidationReport?.WarningCount ?? 0,
                ProcessingDurationMs = s.ProcessingDurationMs,
                AssigneeInitials = new string(s.ReturnCode.Where(char.IsLetter).Take(2).ToArray()).ToUpperInvariant() is { Length: > 0 } ini ? ini : "ME",
                IsCurrentUser = true,
                DeadlineDate = s.ReturnPeriod?.EffectiveDeadline
            };
        }).OrderByDescending(s => s.SubmittedAt).ToList();
    }

    /// <summary>
    /// Gets a submission detail by ID with validation errors mapped to DTOs.
    /// Returns null if not found or doesn't belong to the institution.
    /// </summary>
    public async Task<SubmissionDetailModel?> GetSubmissionDetail(int submissionId, int institutionId)
    {
        var submission = await _submissionRepo.GetByIdWithReport(submissionId);
        if (submission is null || submission.InstitutionId != institutionId)
            return null;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Resolve template metadata
        string templateName;
        string? moduleCode;
        try
        {
            var template = _tenantContext.CurrentTenantId is { } tid
                ? await db.ReturnTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ReturnCode == submission.ReturnCode && t.TenantId == tid)
                : await db.ReturnTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.ReturnCode == submission.ReturnCode);
            templateName = template?.Name ?? submission.ReturnCode;
            var module = template?.ModuleId is int modId
                ? await db.Modules.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modId)
                : null;
            moduleCode = module?.ModuleCode;
        }
        catch
        {
            templateName = submission.ReturnCode;
            moduleCode = null;
        }

        // Period label
        var periodLabel = submission.ReturnPeriod is not null
            ? new DateTime(submission.ReturnPeriod.Year, submission.ReturnPeriod.Month, 1).ToString("MMM yyyy")
            : "—";

        // Submitter name
        string? submitterName = null;
        if (submission.SubmittedByUserId.HasValue)
        {
            var user = await db.InstitutionUsers.FindAsync(submission.SubmittedByUserId.Value);
            submitterName = user?.DisplayName ?? "Unknown";
        }

        // Map validation errors to unified DTOs
        var validationErrors = submission.ValidationReport?.Errors
            .Select(e => new Application.DTOs.ValidationErrorDto
            {
                RuleId = e.RuleId,
                Field = e.Field,
                Message = e.Message,
                Severity = e.Severity.ToString(),
                Category = e.Category.ToString(),
                ExpectedValue = e.ExpectedValue,
                ActualValue = e.ActualValue,
                ReferencedReturnCode = e.ReferencedReturnCode
            })
            .OrderByDescending(e => e.Severity == "Error" ? 2 : e.Severity == "Warning" ? 1 : 0)
            .ToList() ?? new();

        var detail = new SubmissionDetailModel
        {
            Id = submission.Id,
            ReturnCode = submission.ReturnCode,
            InstitutionId = submission.InstitutionId,
            ReturnPeriodId = submission.ReturnPeriodId,
            Status = submission.Status.ToString(),
            SubmittedAt = submission.SubmittedAt,
            CreatedAt = submission.CreatedAt,
            ProcessingDurationMs = submission.ProcessingDurationMs,
            RawXml = submission.RawXml,
            ApprovalRequired = submission.ApprovalRequired,
            SubmittedByUserId = submission.SubmittedByUserId,
            TenantId = submission.TenantId,
            TemplateName = templateName,
            ModuleCode = moduleCode,
            PeriodLabel = periodLabel,
            SubmitterName = submitterName,
            ErrorCount = submission.ValidationReport?.ErrorCount ?? 0,
            WarningCount = submission.ValidationReport?.WarningCount ?? 0,
            ValidationErrors = validationErrors
        };

        // Approval
        if (submission.ApprovalRequired ||
            submission.Status == SubmissionStatus.PendingApproval ||
            submission.Status == SubmissionStatus.ApprovalRejected)
        {
            var approval = await _approvalRepo.GetBySubmission(submission.Id);
            if (approval is not null)
            {
                string? reviewerName = null;
                if (approval.ReviewedByUserId.HasValue)
                {
                    var reviewer = await db.InstitutionUsers.FindAsync(approval.ReviewedByUserId.Value);
                    reviewerName = reviewer?.DisplayName ?? "Unknown";
                }

                detail.Approval = new SubmissionApprovalModel
                {
                    Id = approval.Id,
                    RequestedByUserId = approval.RequestedByUserId,
                    RequestedAt = approval.RequestedAt,
                    SubmitterNotes = approval.SubmitterNotes,
                    Status = approval.Status.ToString(),
                    ReviewedByUserId = approval.ReviewedByUserId,
                    ReviewedAt = approval.ReviewedAt,
                    ReviewerComments = approval.ReviewerComments,
                    ReviewerName = reviewerName,
                    OriginalSubmissionId = approval.OriginalSubmissionId
                };
            }
        }

        return detail;
    }

    /// <summary>
    /// Transition a submission's status (for kanban move, withdraw, etc.).
    /// Returns true if the transition succeeded.
    /// </summary>
    public async Task<bool> TransitionStatus(int submissionId, int institutionId, SubmissionStatus targetStatus)
    {
        var submission = await _submissionRepo.GetById(submissionId);
        if (submission is null || submission.InstitutionId != institutionId)
            return false;

        switch (targetStatus)
        {
            case SubmissionStatus.Historical:
                submission.MarkHistorical();
                break;
            case SubmissionStatus.ApprovalRejected:
                submission.MarkApprovalRejected();
                break;
            default:
                return false;
        }

        await _submissionRepo.Update(submission);
        return true;
    }

    /// <summary>
    /// Withdraw submissions: mark as Historical and delete any associated approval records.
    /// Returns the number of submissions actually withdrawn.
    /// </summary>
    public async Task<int> WithdrawSubmissions(IEnumerable<int> submissionIds, int institutionId)
    {
        var withdrawn = 0;
        foreach (var submissionId in submissionIds)
        {
            var submission = await _submissionRepo.GetById(submissionId);
            if (submission is null || submission.InstitutionId != institutionId)
                continue;

            if (submission.Status is SubmissionStatus.Accepted
                or SubmissionStatus.AcceptedWithWarnings
                or SubmissionStatus.Historical
                or SubmissionStatus.RegulatorAccepted
                or SubmissionStatus.RegulatorAcknowledged
                or SubmissionStatus.SubmittedToRegulator
                or SubmissionStatus.RegulatorQueriesRaised)
                continue;

            var approval = await _approvalRepo.GetBySubmission(submission.Id);
            if (approval is not null)
                await _approvalRepo.Delete(approval);

            submission.Status = SubmissionStatus.Historical;
            await _submissionRepo.Update(submission);
            withdrawn++;
        }
        return withdrawn;
    }

    /// <summary>
    /// Gets return periods for a template (used by MigrationHome instead of direct DbContext access).
    /// </summary>
    public async Task<List<PeriodSelectItem>> GetPeriodsForTemplate(string returnCode, int take = 120)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue) return new();

        var entitledTemplates = await _templateService.GetEntitledTemplates(tenantId.Value);
        var template = entitledTemplates.FirstOrDefault(x =>
            x.ReturnCode.Equals(returnCode, StringComparison.OrdinalIgnoreCase));
        var moduleId = template?.ModuleId;

        IQueryable<ReturnPeriod> query = db.ReturnPeriods.Where(x => x.TenantId == tenantId.Value);
        query = moduleId.HasValue
            ? query.Where(x => x.ModuleId == moduleId.Value)
            : query.Where(x => x.ModuleId == null);

        var periods = await query
            .OrderByDescending(x => x.Year)
            .ThenByDescending(x => x.Quarter)
            .ThenByDescending(x => x.Month)
            .Take(take)
            .ToListAsync();

        return periods.Select(p => new PeriodSelectItem
        {
            ReturnPeriodId = p.Id,
            Value = $"{p.Year}-{p.Month:00}",
            Label = p.Quarter.HasValue
                ? $"Q{p.Quarter} {p.Year}"
                : new DateTime(p.Year, p.Month, 1).ToString("MMMM yyyy"),
            ReportingDate = p.ReportingDate,
            Year = p.Year,
            Month = p.Month,
            DeadlineDate = p.EffectiveDeadline
        }).ToList();
    }

    /// <summary>
    /// Admin override: set status to any allowed value, cleaning up approval records if needed.
    /// </summary>
    public async Task<(bool Success, string? Error)> AdminOverrideStatus(int submissionId, int institutionId, SubmissionStatus targetStatus)
    {
        var submission = await _submissionRepo.GetById(submissionId);
        if (submission is null || submission.InstitutionId != institutionId)
            return (false, "Submission not found.");

        var approval = await _approvalRepo.GetBySubmission(submission.Id);
        if (approval is not null && targetStatus != SubmissionStatus.PendingApproval)
            await _approvalRepo.Delete(approval);

        submission.Status = targetStatus;
        await _submissionRepo.Update(submission);
        return (true, null);
    }

    /// <summary>
    /// Admin unlock: return submission to draft, deleting any approval record.
    /// </summary>
    public async Task<(bool Success, string? Error)> AdminUnlockForEdit(int submissionId, int institutionId)
    {
        var submission = await _submissionRepo.GetById(submissionId);
        if (submission is null || submission.InstitutionId != institutionId)
            return (false, "Submission not found.");

        if (submission.Status is SubmissionStatus.SubmittedToRegulator
            or SubmissionStatus.RegulatorAcknowledged
            or SubmissionStatus.RegulatorAccepted
            or SubmissionStatus.RegulatorQueriesRaised)
            return (false, "Regulator-submitted returns cannot be unlocked from the portal.");

        var approval = await _approvalRepo.GetBySubmission(submission.Id);
        if (approval is not null)
            await _approvalRepo.Delete(approval);

        submission.Status = SubmissionStatus.Draft;
        await _submissionRepo.Update(submission);
        return (true, null);
    }

    /// <summary>
    /// Admin withdraw: mark PendingApproval submission as Historical.
    /// </summary>
    public async Task<(bool Success, string? Error)> AdminWithdraw(int submissionId, int institutionId)
    {
        var submission = await _submissionRepo.GetById(submissionId);
        if (submission is null || submission.InstitutionId != institutionId)
            return (false, "Submission not found.");

        if (submission.Status != SubmissionStatus.PendingApproval)
            return (false, "Only submissions awaiting checker review can be withdrawn from this screen.");

        var approval = await _approvalRepo.GetBySubmission(submission.Id);
        if (approval is not null)
            await _approvalRepo.Delete(approval);

        submission.MarkHistorical();
        await _submissionRepo.Update(submission);
        return (true, null);
    }

    // ── Wizard flows ──

    /// <summary>
    /// Gets all published templates with "already submitted" status for the current period.
    /// </summary>
    public async Task<List<TemplateSelectItem>> GetTemplatesForInstitution(int institutionId, string? moduleCode = null)
    {
        var normalizedModuleCode = string.IsNullOrWhiteSpace(moduleCode)
            ? null
            : moduleCode.Trim().ToUpperInvariant();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var tenantId = await ResolveTenantIdForInstitutionAsync(db, institutionId);

        List<TemplateSelectItem> publishedTemplates;
        if (tenantId.HasValue)
        {
            var entitlement = await _entitlementService.ResolveEntitlements(tenantId.Value);
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

            var entitledTemplates = await _templateService.GetEntitledTemplates(tenantId.Value);
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
            _logger.LogWarning(
                "Unable to resolve tenant scope for institution {InstitutionId}; refusing to load unscoped submission templates.",
                institutionId);
            return new List<TemplateSelectItem>();
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
        var tenantId = await ResolveTenantIdForInstitutionAsync(db, institutionId);

        if (!tenantId.HasValue)
        {
            _logger.LogWarning(
                "Unable to resolve tenant scope for institution {InstitutionId}; refusing to load unscoped return periods for {ReturnCode}.",
                institutionId,
                returnCode);
            return new List<PeriodSelectItem>();
        }

        var query = db.ReturnPeriods
            .AsNoTracking()
            .Where(rp => rp.IsOpen && rp.TenantId == tenantId.Value);

        var entitledTemplates = await _templateService.GetEntitledTemplates(tenantId.Value);
        var template = entitledTemplates.FirstOrDefault(x =>
            x.ReturnCode.Equals(returnCode, StringComparison.OrdinalIgnoreCase));

        if (template?.ModuleId is int moduleId)
        {
            query = query.Where(rp => rp.ModuleId == moduleId);
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

    private async Task<Guid?> ResolveTenantIdForInstitutionAsync(MetadataDbContext db, int institutionId)
    {
        if (_tenantContext.CurrentTenantId is { } currentTenantId)
        {
            return currentTenantId;
        }

        return await db.Institutions
            .AsNoTracking()
            .Where(i => i.Id == institutionId)
            .Select(i => (Guid?)i.TenantId)
            .FirstOrDefaultAsync();
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
