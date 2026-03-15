using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Events;
using FC.Engine.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Handles maker-checker workflow actions and emits workflow notifications.
/// </summary>
public class WorkflowService
{
    private readonly ISubmissionRepository _submissionRepo;
    private readonly ISubmissionApprovalRepository _approvalRepo;
    private readonly IInstitutionUserRepository _userRepo;
    private readonly INotificationOrchestrator _notificationOrchestrator;
    private readonly IFilingCalendarService _filingCalendarService;
    private readonly IDomainEventPublisher? _domainEventPublisher;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        ISubmissionRepository submissionRepo,
        ISubmissionApprovalRepository approvalRepo,
        IInstitutionUserRepository userRepo,
        INotificationOrchestrator notificationOrchestrator,
        IFilingCalendarService filingCalendarService,
        ILogger<WorkflowService> logger,
        IDomainEventPublisher? domainEventPublisher = null)
    {
        _submissionRepo = submissionRepo;
        _approvalRepo = approvalRepo;
        _userRepo = userRepo;
        _notificationOrchestrator = notificationOrchestrator;
        _filingCalendarService = filingCalendarService;
        _logger = logger;
        _domainEventPublisher = domainEventPublisher;
    }

    public async Task<ApprovalActionResult> Approve(
        int submissionId,
        int reviewerUserId,
        string? comments = null,
        CancellationToken ct = default)
    {
        var approval = await _approvalRepo.GetBySubmission(submissionId, ct);
        if (approval is null)
        {
            return ApprovalActionResult.NotFound;
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            return ApprovalActionResult.AlreadyProcessed;
        }

        if (approval.RequestedByUserId == reviewerUserId)
        {
            return ApprovalActionResult.SelfApprovalNotAllowed;
        }

        var submission = await _submissionRepo.GetByIdWithReport(submissionId, ct);
        if (submission is null)
        {
            return ApprovalActionResult.NotFound;
        }

        var hasWarnings = (submission.ValidationReport?.WarningCount ?? 0) > 0;
        if (hasWarnings)
        {
            submission.MarkAcceptedWithWarnings();
        }
        else
        {
            submission.MarkAccepted();
        }

        approval.Status = ApprovalStatus.Approved;
        approval.ReviewedByUserId = reviewerUserId;
        approval.ReviewedAt = DateTime.UtcNow;
        approval.ReviewerComments = comments;

        await _approvalRepo.Update(approval, ct);
        await _submissionRepo.Update(submission, ct);

        try
        {
            await _filingCalendarService.RecordSla(submission.ReturnPeriodId, submission.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SLA recording failed during approval for submission {SubmissionId}", submission.Id);
        }

        try
        {
            var reviewer = await _userRepo.GetById(reviewerUserId, ct);
            await NotifyApprovalOutcome(
                submission,
                approval.RequestedByUserId,
                approved: true,
                reviewer?.DisplayName ?? "Reviewer",
                comments,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send approval notification for submission {SubmissionId}", submission.Id);
        }

        // Publish domain event for webhook/event bus (RG-30)
        if (_domainEventPublisher is not null && submission.TenantId != Guid.Empty)
        {
            try
            {
                await _domainEventPublisher.PublishAsync(new ReturnApprovedEvent(
                    submission.TenantId, submission.Id,
                    string.Empty, submission.ReturnCode, string.Empty,
                    (await _userRepo.GetById(reviewerUserId, ct))?.DisplayName ?? "Reviewer",
                    DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid()), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish ReturnApprovedEvent for submission {SubmissionId}", submission.Id);
            }
        }

        return ApprovalActionResult.Success;
    }

    public async Task<ApprovalActionResult> Reject(
        int submissionId,
        int reviewerUserId,
        string comments,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comments))
        {
            return ApprovalActionResult.CommentsRequired;
        }

        var approval = await _approvalRepo.GetBySubmission(submissionId, ct);
        if (approval is null)
        {
            return ApprovalActionResult.NotFound;
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            return ApprovalActionResult.AlreadyProcessed;
        }

        if (approval.RequestedByUserId == reviewerUserId)
        {
            return ApprovalActionResult.SelfApprovalNotAllowed;
        }

        approval.Status = ApprovalStatus.Rejected;
        approval.ReviewedByUserId = reviewerUserId;
        approval.ReviewedAt = DateTime.UtcNow;
        approval.ReviewerComments = comments;
        await _approvalRepo.Update(approval, ct);

        var submission = await _submissionRepo.GetById(submissionId, ct);
        if (submission is not null)
        {
            submission.MarkApprovalRejected();
            await _submissionRepo.Update(submission, ct);

            try
            {
                var reviewer = await _userRepo.GetById(reviewerUserId, ct);
                await NotifyApprovalOutcome(
                    submission,
                    approval.RequestedByUserId,
                    approved: false,
                    reviewer?.DisplayName ?? "Reviewer",
                    comments,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send rejection notification for submission {SubmissionId}", submission.Id);
            }

            // Publish domain event for webhook/event bus (RG-30)
            if (_domainEventPublisher is not null && submission.TenantId != Guid.Empty)
            {
                try
                {
                    await _domainEventPublisher.PublishAsync(new ReturnRejectedEvent(
                        submission.TenantId, submission.Id,
                        string.Empty, submission.ReturnCode, string.Empty,
                        (await _userRepo.GetById(reviewerUserId, ct))?.DisplayName ?? "Reviewer",
                        comments, DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid()), ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish ReturnRejectedEvent for submission {SubmissionId}", submission.Id);
                }
            }
        }

        return ApprovalActionResult.Success;
    }

    private async Task NotifyApprovalOutcome(
        Submission submission,
        int makerUserId,
        bool approved,
        string reviewerName,
        string? reviewerComments,
        CancellationToken ct)
    {
        if (submission.TenantId == Guid.Empty)
        {
            return;
        }

        var period = submission.ReturnPeriod is null
            ? DateTime.UtcNow.ToString("MMM yyyy")
            : new DateTime(submission.ReturnPeriod.Year, submission.ReturnPeriod.Month, 1).ToString("MMM yyyy");

        var eventType = approved
            ? NotificationEvents.ReturnApproved
            : NotificationEvents.ReturnRejected;

        var title = approved ? "Submission Approved" : "Submission Rejected";
        var message = approved
            ? $"Your {submission.ReturnCode} return for {period} was approved by {reviewerName}."
            : $"Your {submission.ReturnCode} return for {period} was rejected by {reviewerName}." +
              (string.IsNullOrWhiteSpace(reviewerComments) ? string.Empty : $" Reason: \"{reviewerComments}\"");

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = submission.TenantId,
            EventType = eventType,
            Title = title,
            Message = message,
            Priority = approved ? NotificationPriority.Normal : NotificationPriority.High,
            ActionUrl = $"/submissions/{submission.Id}",
            RecipientUserIds = new List<int> { makerUserId },
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ReturnCode"] = submission.ReturnCode,
                ["PeriodLabel"] = period,
                ["SubmissionId"] = submission.Id.ToString(),
                ["ApprovedBy"] = reviewerName,
                ["RejectedBy"] = reviewerName,
                ["RejectionReason"] = reviewerComments ?? string.Empty
            }
        }, ct);
    }
}
