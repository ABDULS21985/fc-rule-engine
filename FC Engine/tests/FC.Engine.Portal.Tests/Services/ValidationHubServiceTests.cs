using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Portal.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace FC.Engine.Portal.Tests.Services;

public class ValidationHubServiceTests
{
    [Fact]
    public async Task GetHubDataAsync_Enriches_Module_Workspace_And_Fix_Links()
    {
        var tenantId = Guid.NewGuid();

        var submissionRepo = new Mock<ISubmissionRepository>();
        submissionRepo
            .Setup(x => x.GetByIdWithReport(9001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Submission
            {
                Id = 9001,
                TenantId = tenantId,
                InstitutionId = 44,
                ReturnCode = "CAP_BUF",
                ReturnPeriodId = 243,
                Status = SubmissionStatus.Rejected,
                SubmittedAt = new DateTime(2026, 3, 12, 10, 30, 0, DateTimeKind.Utc),
                ReturnPeriod = new ReturnPeriod
                {
                    Id = 243,
                    TenantId = tenantId,
                    Year = 2026,
                    Month = 3,
                    Frequency = "Monthly",
                    ReportingDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                    DeadlineDate = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
                    IsOpen = true
                }
            });
        submissionRepo
            .Setup(x => x.GetByInstitution(44, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Submission>());

        var institutionRepo = new Mock<IInstitutionRepository>();
        institutionRepo
            .Setup(x => x.GetById(44, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Institution
            {
                Id = 44,
                TenantId = tenantId,
                InstitutionName = "Capital Trust Bank"
            });

        var userRepo = new Mock<IInstitutionUserRepository>();

        var brandingService = new Mock<ITenantBrandingService>();
        brandingService
            .Setup(x => x.GetBrandingConfig(tenantId))
            .ReturnsAsync(BrandingConfig.WithDefaults());

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache
            .Setup(x => x.GetPublishedTemplate("CAP_BUF", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedTemplate
            {
                ReturnCode = "CAP_BUF",
                Name = "Capital Buffer Register",
                ModuleCode = "CAPITAL_SUPERVISION",
                CurrentVersion = new CachedTemplateVersion()
            });

        var sut = new ValidationHubService(
            submissionRepo.Object,
            institutionRepo.Object,
            userRepo.Object,
            brandingService.Object,
            templateCache.Object);

        var result = await sut.GetHubDataAsync(9001, 44);

        result.Should().NotBeNull();
        result!.ModuleCode.Should().Be("CAPITAL_SUPERVISION");
        result.ModuleName.Should().Be("Capital Supervision");
        result.WorkspaceHref.Should().Be("/workflows/capital-supervision");
        result.SubmitHref.Should().Be("/submit?module=CAPITAL_SUPERVISION&returnCode=CAP_BUF");
        result.FixSubmissionHref.Should().Be("/submit?module=CAPITAL_SUPERVISION&returnCode=CAP_BUF&periodId=243");
    }
}
