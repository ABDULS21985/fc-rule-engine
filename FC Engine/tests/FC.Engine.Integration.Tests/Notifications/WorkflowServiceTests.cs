using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using SubmissionEntity = FC.Engine.Domain.Entities.Submission;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Portal.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FC.Engine.Integration.Tests.Notifications;

public class WorkflowServiceTests
{
    [Fact]
    public async Task Return_Approved_Notifies_Maker()
    {
        var tenantId = Guid.NewGuid();
        var submissionId = 301;
        var makerUserId = 41;
        var checkerUserId = 77;

        var submissionRepo = new Mock<ISubmissionRepository>();
        var approvalRepo = new Mock<ISubmissionApprovalRepository>();
        var userRepo = new Mock<IInstitutionUserRepository>();
        var notificationOrchestrator = new Mock<INotificationOrchestrator>();
        var filingCalendarService = new Mock<IFilingCalendarService>();

        var approval = new SubmissionApproval
        {
            SubmissionId = submissionId,
            RequestedByUserId = makerUserId,
            Status = ApprovalStatus.Pending
        };

        var submission = new SubmissionEntity
        {
            Id = submissionId,
            TenantId = tenantId,
            InstitutionId = 12,
            ReturnPeriodId = 55,
            ReturnCode = "MFCR 310",
            ReturnPeriod = new ReturnPeriod { Year = 2026, Month = 3 },
            Status = SubmissionStatus.PendingApproval
        };

        approvalRepo.Setup(x => x.GetBySubmission(submissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(approval);
        approvalRepo.Setup(x => x.Update(approval, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        submissionRepo.Setup(x => x.GetByIdWithReport(submissionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(submission);
        submissionRepo.Setup(x => x.Update(submission, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        userRepo.Setup(x => x.GetById(checkerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InstitutionUser
            {
                Id = checkerUserId,
                DisplayName = "Checker Jane",
                Role = InstitutionRole.Checker,
                PasswordHash = "hash",
                TenantId = tenantId,
                InstitutionId = 12
            });

        notificationOrchestrator
            .Setup(x => x.Notify(It.IsAny<NotificationRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        filingCalendarService
            .Setup(x => x.RecordSla(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new WorkflowService(
            submissionRepo.Object,
            approvalRepo.Object,
            userRepo.Object,
            notificationOrchestrator.Object,
            filingCalendarService.Object,
            NullLogger<WorkflowService>.Instance);

        var result = await sut.Approve(submissionId, checkerUserId, "Looks good", CancellationToken.None);

        result.Should().Be(ApprovalActionResult.Success);
        notificationOrchestrator.Verify(x => x.Notify(
            It.Is<NotificationRequest>(n =>
                n.TenantId == tenantId &&
                n.EventType == NotificationEvents.ReturnApproved &&
                n.RecipientUserIds.SequenceEqual(new[] { makerUserId }) &&
                n.Priority == NotificationPriority.Normal &&
                n.ActionUrl == $"/submissions/{submissionId}"),
            It.IsAny<CancellationToken>()), Times.Once);

        filingCalendarService.Verify(
            x => x.RecordSla(submission.ReturnPeriodId, submissionId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
