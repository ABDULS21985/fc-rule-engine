using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Dapper;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Portal.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FC.Engine.Integration.Tests.Bdc;

[CollectionDefinition("BdcLifecycleIntegration", DisableParallelization = true)]
public sealed class BdcLifecycleIntegrationCollection : ICollectionFixture<BdcLifecycleFixture>;

[Collection("BdcLifecycleIntegration")]
public sealed class BdcLifecycleIntegrationTests : IClassFixture<BdcLifecycleFixture>
{
    private readonly BdcLifecycleFixture _fixture;

    public BdcLifecycleIntegrationTests(BdcLifecycleFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Bdc_Lifecycle_Onboard_Submit_Approve_RegulatorReview_QueryResponse_And_FinalAcceptance_Are_EndToEnd()
    {
        var sample = _fixture.LoadBdcSample();
        var onboarded = await _fixture.OnboardInstitutionAsync(sample, "full");

        onboarded.ActivatedModules.Should().Contain("BDC_CBN");
        onboarded.ActivatedModules.Should().Contain("NFIU_AML");

        await _fixture.EnableMakerCheckerAsync(onboarded.InstitutionId);

        var maker = await _fixture.CreateInstitutionUserAsync(
            onboarded.TenantId,
            onboarded.InstitutionId,
            "maker",
            InstitutionRole.Maker,
            "BDC Maker");
        var checker = await _fixture.CreateInstitutionUserAsync(
            onboarded.TenantId,
            onboarded.InstitutionId,
            "checker",
            InstitutionRole.Checker,
            "BDC Checker");
        var admin = await _fixture.GetInstitutionUserByEmailAsync(onboarded.AdminEmail);
        var regulator = await _fixture.CreateRegulatorUserAsync("CBN", "full");
        var returnPeriodId = await _fixture.CreateBdcReturnPeriodAsync(onboarded.TenantId, sample.ReportingPeriodEnd);

        var templateSelection = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var service = sp.GetRequiredService<SubmissionService>();
                return await service.GetTemplatesForInstitution(onboarded.InstitutionId);
            });

        templateSelection.Should().Contain(item => item.ReturnCode == BdcLifecycleFixture.BdcReturnCode);

        var openPeriods = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var service = sp.GetRequiredService<SubmissionService>();
                return await service.GetOpenPeriods(onboarded.InstitutionId, BdcLifecycleFixture.BdcReturnCode);
            });

        openPeriods.Should().Contain(period => period.ReturnPeriodId == returnPeriodId);

        var submissionResult = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var xmlService = sp.GetRequiredService<FormDataToXmlService>();
                var submissionService = sp.GetRequiredService<SubmissionService>();
                await using var xmlStream = await xmlService.ConvertToStream(
                    BdcLifecycleFixture.BdcReturnCode,
                    onboarded.InstitutionCode,
                    sample.ReportingPeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    new List<Dictionary<string, string>> { sample.BuildFxvSubmissionRow() });

                return await submissionService.ProcessSubmission(
                    xmlStream,
                    BdcLifecycleFixture.BdcReturnCode,
                    onboarded.InstitutionId,
                    returnPeriodId,
                    maker.Id,
                    "Submitting the March BDC FXV return for checker review.");
            });

        submissionResult.Status.Should().Be("PendingApproval");
        submissionResult.SubmissionId.Should().BeGreaterThan(0);
        submissionResult.ValidationReport.Should().NotBeNull();
        submissionResult.ValidationReport!.ErrorCount.Should().Be(0);
        submissionResult.ValidationReport.WarningCount.Should().Be(0);
        submissionResult.ValidationReport.IsValid.Should().BeTrue();

        var persistedRecord = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var repository = sp.GetRequiredService<IGenericDataRepository>();
                return await repository.GetBySubmission(
                    BdcLifecycleFixture.BdcReturnCode,
                    submissionResult.SubmissionId);
            });

        persistedRecord.Should().NotBeNull();
        persistedRecord!.Rows.Should().ContainSingle();
        persistedRecord.Rows[0].GetDecimal("usd_selling_volume").Should().Be(7000m);
        persistedRecord.Rows[0].GetDecimal("gbp_buying_volume").Should().Be(500m);
        persistedRecord.Rows[0].GetDecimal("net_position").Should().Be(-6500m);
        persistedRecord.Rows[0].GetDecimal("spread_avg").Should().Be(40m);

        var pendingApprovals = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var service = sp.GetRequiredService<ApprovalService>();
                return await service.GetPendingApprovals(onboarded.InstitutionId);
            });

        pendingApprovals.Should().ContainSingle(item => item.SubmissionId == submissionResult.SubmissionId);

        var reviewNotifications = (await _fixture.GetPortalNotificationsAsync(onboarded.TenantId))
            .Where(notification =>
                notification.Link == $"/submissions/{submissionResult.SubmissionId}"
                && notification.Message.Contains($"submitted {submissionResult.ReturnCode}", StringComparison.OrdinalIgnoreCase))
            .ToList();
        reviewNotifications.Should().Contain(notification => notification.UserId == checker.Id);
        reviewNotifications.Should().Contain(notification => notification.UserId == admin.Id);

        var approveResult = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var service = sp.GetRequiredService<ApprovalService>();
                return await service.Approve(
                    submissionResult.SubmissionId,
                    checker.Id,
                    "Checked against the imported BDC FX template.");
            });

        approveResult.Should().Be(ApprovalActionResult.Success);

        var approvedState = await _fixture.GetSubmissionStateAsync(submissionResult.SubmissionId);
        approvedState.Submission.Status.Should().Be(SubmissionStatus.Accepted);
        approvedState.Submission.ApprovalRequired.Should().BeTrue();
        approvedState.Submission.SubmittedByUserId.Should().Be(maker.Id);
        approvedState.Approval.Should().NotBeNull();
        approvedState.Approval!.Status.Should().Be(ApprovalStatus.Approved);
        approvedState.Approval.ReviewedByUserId.Should().Be(checker.Id);
        approvedState.Approval.ReviewerComments.Should().Be("Checked against the imported BDC FX template.");
        approvedState.Sla.Should().NotBeNull();
        approvedState.Sla!.OnTime.Should().BeTrue();

        var approvalNotifications = (await _fixture.GetPortalNotificationsAsync(onboarded.TenantId))
            .Where(notification =>
                notification.Title == "Submission Approved"
                && notification.Link == $"/submissions/{submissionResult.SubmissionId}")
            .ToList();
        approvalNotifications.Should().ContainSingle(notification => notification.UserId == maker.Id);

        var inbox = await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.GetInbox(regulator.TenantId, regulator.RegulatorCode);
            });

        var inboxItem = inbox.Should().ContainSingle(item => item.SubmissionId == submissionResult.SubmissionId).Subject;
        inboxItem.InstitutionName.Should().Be(onboarded.InstitutionName);
        inboxItem.ReceiptStatus.Should().Be(RegulatorReceiptStatus.Received);
        inboxItem.SubmissionStatus.Should().Be(SubmissionStatus.Accepted.ToString());

        var receiptInReview = await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.UpdateReceiptStatus(
                    regulator.TenantId,
                    submissionResult.SubmissionId,
                    RegulatorReceiptStatus.UnderReview,
                    regulator.UserId,
                    "Opened for examiner review.");
            });

        receiptInReview.Status.Should().Be(RegulatorReceiptStatus.UnderReview);
        receiptInReview.ReviewedBy.Should().Be(regulator.UserId);

        var examinerQuery = await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.RaiseQuery(
                    regulator.TenantId,
                    submissionResult.SubmissionId,
                    "usd_selling_volume",
                    "Provide the supporting trade blotter for the USD selling volume.",
                    regulator.UserId,
                    ExaminerQueryPriority.High);
            });

        examinerQuery.Status.Should().Be(ExaminerQueryStatus.Open);
        examinerQuery.Priority.Should().Be(ExaminerQueryPriority.High);
        examinerQuery.FieldCode.Should().Be("usd_selling_volume");

        var queryNotifications = (await _fixture.GetPortalNotificationsAsync(onboarded.TenantId))
            .Where(notification =>
                notification.Title == $"Regulator query raised for {submissionResult.ReturnCode}"
                && notification.Link == $"/submissions/{submissionResult.SubmissionId}")
            .ToList();
        queryNotifications.Should().Contain(notification => notification.UserId == maker.Id);
        queryNotifications.Should().Contain(notification => notification.UserId == checker.Id);
        queryNotifications.Should().Contain(notification => notification.UserId == admin.Id);

        var response = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.RespondToQueryAsInstitution(
                    examinerQuery.Id,
                    maker.Id,
                    "Trade blotter uploaded. Figures reconcile to the sample BDC template totals.");
            });

        response.Should().NotBeNull();
        response!.Status.Should().Be(ExaminerQueryStatus.Responded);
        response.RespondedBy.Should().Be(maker.Id);
        response.ResponseText.Should().Be("Trade blotter uploaded. Figures reconcile to the sample BDC template totals.");

        var detailAfterResponse = await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.GetSubmissionDetail(
                    regulator.TenantId,
                    regulator.RegulatorCode,
                    submissionResult.SubmissionId);
            });

        detailAfterResponse.Should().NotBeNull();
        detailAfterResponse!.Header.ReceiptStatus.Should().Be(RegulatorReceiptStatus.ResponseReceived);
        detailAfterResponse.Queries.Should().ContainSingle(query =>
            query.Id == examinerQuery.Id
            && query.Status == ExaminerQueryStatus.Responded
            && query.ResponseText == "Trade blotter uploaded. Figures reconcile to the sample BDC template totals.");

        await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.UpdateReceiptStatus(
                    regulator.TenantId,
                    submissionResult.SubmissionId,
                    RegulatorReceiptStatus.UnderReview,
                    regulator.UserId,
                    "Institution response reviewed.");
            });

        await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.UpdateReceiptStatus(
                    regulator.TenantId,
                    submissionResult.SubmissionId,
                    RegulatorReceiptStatus.Accepted,
                    regulator.UserId,
                    "Submission accepted after response review.");
            });

        var finalReceipt = await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.UpdateReceiptStatus(
                    regulator.TenantId,
                    submissionResult.SubmissionId,
                    RegulatorReceiptStatus.FinalAccepted,
                    regulator.UserId,
                    "Final regulator sign-off completed.");
            });

        finalReceipt.Status.Should().Be(RegulatorReceiptStatus.FinalAccepted);
        finalReceipt.FinalAcceptedAt.Should().NotBeNull();
        finalReceipt.Notes.Should().Be("Final regulator sign-off completed.");

        var finalDetail = await _fixture.ExecuteRegulatorAsync(
            regulator.TenantId,
            regulator.RegulatorCode,
            async sp =>
            {
                var service = sp.GetRequiredService<IRegulatorInboxService>();
                return await service.GetSubmissionDetail(
                    regulator.TenantId,
                    regulator.RegulatorCode,
                    submissionResult.SubmissionId);
            });

        finalDetail.Should().NotBeNull();
        finalDetail!.Header.ReceiptStatus.Should().Be(RegulatorReceiptStatus.FinalAccepted);
        finalDetail.Receipt.Should().NotBeNull();
        finalDetail.Receipt!.FinalAcceptedAt.Should().NotBeNull();

        var tenantEmailDeliveries = await _fixture.GetNotificationDeliveriesAsync(
            onboarded.TenantId,
            NotificationEvents.ReturnSubmittedForReview,
            NotificationEvents.ReturnApproved,
            NotificationEvents.ReturnQueryRaised);
        tenantEmailDeliveries.Should().NotBeEmpty();

    }

    [Fact]
    public async Task Bdc_Lifecycle_Checker_Rejection_Persists_ApprovalRejection_And_Stops_Before_RegulatorReview()
    {
        var sample = _fixture.LoadBdcSample();
        var onboarded = await _fixture.OnboardInstitutionAsync(sample, "reject");
        await _fixture.EnableMakerCheckerAsync(onboarded.InstitutionId);

        var maker = await _fixture.CreateInstitutionUserAsync(
            onboarded.TenantId,
            onboarded.InstitutionId,
            "maker",
            InstitutionRole.Maker,
            "BDC Maker Reject");
        var checker = await _fixture.CreateInstitutionUserAsync(
            onboarded.TenantId,
            onboarded.InstitutionId,
            "checker",
            InstitutionRole.Checker,
            "BDC Checker Reject");
        var returnPeriodId = await _fixture.CreateBdcReturnPeriodAsync(onboarded.TenantId, sample.ReportingPeriodEnd);

        var submission = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var xmlService = sp.GetRequiredService<FormDataToXmlService>();
                var submissionService = sp.GetRequiredService<SubmissionService>();
                await using var xmlStream = await xmlService.ConvertToStream(
                    BdcLifecycleFixture.BdcReturnCode,
                    onboarded.InstitutionCode,
                    sample.ReportingPeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    new List<Dictionary<string, string>> { sample.BuildFxvSubmissionRow() });

                return await submissionService.ProcessSubmission(
                    xmlStream,
                    BdcLifecycleFixture.BdcReturnCode,
                    onboarded.InstitutionId,
                    returnPeriodId,
                    maker.Id,
                    "Submitting a second pass for rejection coverage.");
            });

        submission.Status.Should().Be("PendingApproval");

        var rejectResult = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var service = sp.GetRequiredService<ApprovalService>();
                return await service.Reject(
                    submission.SubmissionId,
                    checker.Id,
                    "Rate evidence bundle is incomplete.");
            });

        rejectResult.Should().Be(ApprovalActionResult.Success);

        var rejectedState = await _fixture.GetSubmissionStateAsync(submission.SubmissionId);
        rejectedState.Submission.Status.Should().Be(SubmissionStatus.ApprovalRejected);
        rejectedState.Approval.Should().NotBeNull();
        rejectedState.Approval!.Status.Should().Be(ApprovalStatus.Rejected);
        rejectedState.Approval.ReviewerComments.Should().Be("Rate evidence bundle is incomplete.");
        rejectedState.Sla.Should().BeNull();

        var approvalDetail = await _fixture.ExecuteInstitutionAsync(
            onboarded.TenantId,
            async sp =>
            {
                var service = sp.GetRequiredService<ApprovalService>();
                return await service.GetApprovalDetail(submission.SubmissionId);
            });

        approvalDetail.Should().NotBeNull();
        approvalDetail!.Status.Should().Be(ApprovalStatus.Rejected);
        approvalDetail.ReviewerComments.Should().Be("Rate evidence bundle is incomplete.");
        approvalDetail.History.Should().Contain(history => history.EventType == "Submitted");
        approvalDetail.History.Should().Contain(history =>
            history.EventType == "Rejected"
            && history.Notes == "Rate evidence bundle is incomplete.");

        var rejectionNotifications = (await _fixture.GetPortalNotificationsAsync(onboarded.TenantId))
            .Where(notification =>
                notification.Title == "Submission Rejected"
                && notification.Link == $"/submissions/{submission.SubmissionId}")
            .ToList();
        rejectionNotifications.Should().ContainSingle(notification => notification.UserId == maker.Id);

        var regulatorArtifacts = await _fixture.GetRegulatorArtifactsAsync(submission.SubmissionId);
        regulatorArtifacts.Receipt.Should().BeNull();
        regulatorArtifacts.Queries.Should().BeEmpty();
    }
}

public sealed class BdcLifecycleFixture : IAsyncLifetime
{
    public const string BdcModuleCode = "BDC_CBN";
    public const string BdcReturnCode = "BDC_FXV";

    private WebApplication _application = null!;
    private string _connectionString = null!;
    private string _solutionRoot = null!;
    private string _samplePath = null!;
    private readonly List<Guid> _createdTenantIds = new();

    public int BdcModuleId { get; private set; }

    public async Task InitializeAsync()
    {
        _solutionRoot = FindSolutionRoot();
        _samplePath = ResolveAssetPath("Templates", "BDC_DTR_Sample_v1.0.xml");
        _connectionString = await TestSqlConnectionResolver.ResolveAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = _solutionRoot,
            EnvironmentName = Environments.Development
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FcEngine"] = _connectionString,
            ["RabbitMQ:Enabled"] = "false",
            ["Notifications:Email:Provider"] = "None",
            ["Notifications:Sms:Provider"] = "None",
            ["Notifications:SignalR:RedisBackplane"] = "false",
            ["FileStorage:Provider"] = "Local",
            ["FileStorage:Local:BasePath"] = Path.Combine(_solutionRoot, ".artifacts", "bdc-int-uploads"),
            ["FileStorage:Local:BaseUrl"] = "/bdc-int-uploads",
            ["DataResidency:DefaultRegion"] = "SouthAfricaNorth"
        });

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddScoped<ValidationOrchestrator>();
        builder.Services.AddScoped<IngestionOrchestrator>();
        builder.Services.AddScoped<TemplateService>();
        builder.Services.AddScoped<InstitutionAuthService>();
        builder.Services.AddScoped<FormDataToXmlService>();
        builder.Services.AddScoped<SubmissionService>();
        builder.Services.AddScoped<ApprovalService>();
        builder.Services.AddScoped<WorkflowService>();
        builder.Services.AddScoped<NotificationService>();

        _application = builder.Build();

        await ExecuteGlobalAsync(async sp =>
        {
            var db = sp.GetRequiredService<MetadataDbContext>();
            await db.Database.MigrateAsync();
        });

        await SeedReferenceDataAsync();
        await ImportAndPublishBdcModuleAsync();
    }

    public async Task DisposeAsync()
    {
        await CleanupCreatedTenantsAsync();

        if (_application is not null)
        {
            await _application.DisposeAsync();
        }
    }

    public BdcDtrSample LoadBdcSample()
    {
        File.Exists(_samplePath).Should().BeTrue($"Expected BDC sample XML at {_samplePath}");
        return BdcDtrSample.Load(_samplePath);
    }

    public Task<TResult> ExecuteInstitutionAsync<TResult>(
        Guid tenantId,
        Func<IServiceProvider, Task<TResult>> action)
    {
        return ExecuteAsync(tenantId, "Institution", null, action);
    }

    public Task ExecuteInstitutionAsync(
        Guid tenantId,
        Func<IServiceProvider, Task> action)
    {
        return ExecuteAsync<object?>(tenantId, "Institution", null, async sp =>
        {
            await action(sp);
            return null;
        });
    }

    public Task<TResult> ExecuteRegulatorAsync<TResult>(
        Guid tenantId,
        string regulatorCode,
        Func<IServiceProvider, Task<TResult>> action)
    {
        return ExecuteAsync(tenantId, "Regulator", regulatorCode, action);
    }

    public Task ExecuteRegulatorAsync(
        Guid tenantId,
        string regulatorCode,
        Func<IServiceProvider, Task> action)
    {
        return ExecuteAsync<object?>(tenantId, "Regulator", regulatorCode, async sp =>
        {
            await action(sp);
            return null;
        });
    }

    public Task<TResult> ExecuteGlobalAsync<TResult>(Func<IServiceProvider, Task<TResult>> action)
    {
        return ExecuteAsync(null, null, null, action);
    }

    public Task ExecuteGlobalAsync(Func<IServiceProvider, Task> action)
    {
        return ExecuteAsync<object?>(null, null, null, async sp =>
        {
            await action(sp);
            return null;
        });
    }

    public async Task<OnboardedInstitution> OnboardInstitutionAsync(BdcDtrSample sample, string scenarioKey)
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = BuildUniqueEmail(sample.ContactEmail, $"admin-{scenarioKey}-{uniqueSuffix}");
        var institutionCode = BuildInstitutionCode(sample.LicenceNumber, uniqueSuffix);
        var tenantSlug = $"bdc-{scenarioKey}-{uniqueSuffix}".ToLowerInvariant();

        var request = new TenantOnboardingRequest
        {
            TenantName = $"{sample.InstitutionName} {scenarioKey.ToUpperInvariant()} {uniqueSuffix}",
            TenantSlug = tenantSlug[..Math.Min(tenantSlug.Length, 24)],
            TenantType = TenantType.Institution,
            ContactEmail = sample.ContactEmail,
            ContactPhone = sample.ContactPhone,
            Address = sample.HeadOfficeLocation,
            LicenceTypeCodes = new List<string> { "BDC" },
            SubscriptionPlanCode = "STARTER",
            AdminEmail = adminEmail,
            AdminFullName = sample.ContactPerson,
            InstitutionCode = institutionCode,
            InstitutionName = $"{sample.InstitutionName} {scenarioKey.ToUpperInvariant()}",
            InstitutionType = "BDC",
            JurisdictionCode = "NG"
        };

        var result = await ExecuteGlobalAsync(async sp =>
        {
            var service = sp.GetRequiredService<ITenantOnboardingService>();
            return await service.OnboardTenant(request);
        });

        result.Success.Should().BeTrue(string.Join(" | ", result.Errors));
        _createdTenantIds.Add(result.TenantId);

        return new OnboardedInstitution(
            result.TenantId,
            result.InstitutionId,
            request.InstitutionCode,
            request.InstitutionName,
            request.AdminEmail,
            result.ActivatedModules);
    }

    public async Task EnableMakerCheckerAsync(int institutionId)
    {
        await ExecuteGlobalAsync(async sp =>
        {
            var db = sp.GetRequiredService<MetadataDbContext>();
            var institution = await db.Institutions.FirstAsync(item => item.Id == institutionId);
            institution.MakerCheckerEnabled = true;
            await db.SaveChangesAsync();
            return 0;
        });
    }

    public async Task<InstitutionUser> CreateInstitutionUserAsync(
        Guid tenantId,
        int institutionId,
        string userPrefix,
        InstitutionRole role,
        string displayName)
    {
        return await ExecuteGlobalAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IInstitutionUserRepository>();
            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
            var user = new InstitutionUser
            {
                TenantId = tenantId,
                InstitutionId = institutionId,
                Username = $"{userPrefix}_{uniqueSuffix}",
                Email = $"{userPrefix}.{uniqueSuffix}@bdc-int.test",
                DisplayName = displayName,
                PasswordHash = InstitutionAuthService.HashPassword("BdcInt!234"),
                PreferredLanguage = "en",
                Role = role,
                IsActive = true,
                MustChangePassword = false,
                CreatedAt = DateTime.UtcNow
            };

            await repository.Create(user);
            return user;
        });
    }

    public async Task<InstitutionUser> GetInstitutionUserByEmailAsync(string email)
    {
        return await ExecuteGlobalAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IInstitutionUserRepository>();
            var user = await repository.GetByEmail(email);
            user.Should().NotBeNull($"Expected institution user '{email}' to exist after onboarding.");
            return user!;
        });
    }

    public async Task<RegulatorUser> CreateRegulatorUserAsync(string regulatorCode, string scenarioKey)
    {
        return await ExecuteGlobalAsync(async sp =>
        {
            var portalUserRepository = sp.GetRequiredService<IPortalUserRepository>();
            var db = sp.GetRequiredService<MetadataDbContext>();
            var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];

            var tenant = Tenant.Create(
                $"{regulatorCode} Regulator {scenarioKey} {uniqueSuffix}",
                $"{regulatorCode.ToLowerInvariant()}-reg-{uniqueSuffix}",
                TenantType.Regulator,
                $"{regulatorCode.ToLowerInvariant()}.{uniqueSuffix}@regulator.test");
            tenant.Activate();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();

            var portalUser = new PortalUser
            {
                TenantId = tenant.TenantId,
                Username = $"{regulatorCode.ToLowerInvariant()}_{uniqueSuffix}",
                DisplayName = $"{regulatorCode} Examiner",
                Email = $"{regulatorCode.ToLowerInvariant()}.{uniqueSuffix}@regulator.test",
                PasswordHash = AuthService.HashPassword("BdcReg!234"),
                Role = PortalRole.Approver,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await portalUserRepository.Create(portalUser);
            _createdTenantIds.Add(tenant.TenantId);

            return new RegulatorUser(tenant.TenantId, portalUser.Id, regulatorCode);
        });
    }

    public async Task<int> CreateBdcReturnPeriodAsync(Guid tenantId, DateTime reportingPeriodEnd)
    {
        return await ExecuteGlobalAsync(async sp =>
        {
            var db = sp.GetRequiredService<MetadataDbContext>();
            var monthEnd = new DateTime(
                reportingPeriodEnd.Year,
                reportingPeriodEnd.Month,
                DateTime.DaysInMonth(reportingPeriodEnd.Year, reportingPeriodEnd.Month));

            var period = new ReturnPeriod
            {
                TenantId = tenantId,
                ModuleId = BdcModuleId,
                Year = monthEnd.Year,
                Month = monthEnd.Month,
                Frequency = "Monthly",
                ReportingDate = monthEnd,
                DeadlineDate = monthEnd.AddDays(30),
                IsOpen = true,
                Status = "Open",
                CreatedAt = DateTime.UtcNow,
                NotificationLevel = 0
            };

            db.ReturnPeriods.Add(period);
            await db.SaveChangesAsync();
            return period.Id;
        });
    }

    public async Task<SubmissionState> GetSubmissionStateAsync(int submissionId)
    {
        return await ExecuteGlobalAsync(async sp =>
        {
            var db = sp.GetRequiredService<MetadataDbContext>();

            var submission = await db.Submissions
                .Include(item => item.ValidationReport)
                    .ThenInclude(report => report!.Errors)
                .FirstAsync(item => item.Id == submissionId);
            var approval = await db.SubmissionApprovals
                .FirstOrDefaultAsync(item => item.SubmissionId == submissionId);
            var sla = await db.FilingSlaRecords
                .FirstOrDefaultAsync(item => item.SubmissionId == submissionId);

            return new SubmissionState(submission, approval, sla);
        });
    }

    public async Task<IReadOnlyList<PortalNotification>> GetPortalNotificationsAsync(Guid tenantId)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var notifications = await connection.QueryAsync<PortalNotification>(
            """
            SELECT
                Id,
                TenantId,
                UserId,
                InstitutionId,
                EventType,
                Title,
                Message,
                Link,
                MetadataJson AS Metadata,
                IsRead,
                CreatedAt,
                ReadAt
            FROM dbo.portal_notifications
            WHERE TenantId = @tenantId
            ORDER BY Id
            """,
            new { tenantId });

        return notifications.ToList();
    }

    public async Task<IReadOnlyList<NotificationDelivery>> GetNotificationDeliveriesAsync(Guid tenantId, params string[] eventTypes)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var deliveries = await connection.QueryAsync<NotificationDelivery>(
            """
            SELECT
                Id,
                TenantId,
                NotificationEventType,
                Channel,
                RecipientId,
                RecipientAddress,
                Status,
                AttemptCount,
                MaxAttempts,
                NextRetryAt,
                SentAt,
                DeliveredAt,
                ProviderMessageId,
                ErrorMessage,
                Payload,
                CreatedAt
            FROM dbo.notification_deliveries
            WHERE TenantId = @tenantId
              AND NotificationEventType IN @eventTypes
            ORDER BY Id
            """,
            new { tenantId, eventTypes });

        return deliveries.ToList();
    }

    public async Task<RegulatorArtifacts> GetRegulatorArtifactsAsync(int submissionId)
    {
        return await ExecuteGlobalAsync(async sp =>
        {
            var db = sp.GetRequiredService<MetadataDbContext>();
            var receipt = await db.RegulatorReceipts
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.SubmissionId == submissionId);
            var queries = await db.ExaminerQueries
                .AsNoTracking()
                .Where(item => item.SubmissionId == submissionId)
                .OrderBy(item => item.Id)
                .ToListAsync();

            return new RegulatorArtifacts(receipt, queries);
        });
    }

    private async Task SeedReferenceDataAsync()
    {
        await ExecuteGlobalAsync(async sp =>
        {
            var db = sp.GetRequiredService<MetadataDbContext>();

            var jurisdiction = await db.Jurisdictions.FirstOrDefaultAsync(item => item.CountryCode == "NG");
            if (jurisdiction is null)
            {
                jurisdiction = new Jurisdiction
                {
                    CountryCode = "NG",
                    CountryName = "Nigeria",
                    Currency = "NGN",
                    Timezone = "Africa/Lagos",
                    RegulatoryBodies = "[\"CBN\",\"NFIU\"]",
                    DateFormat = "dd/MM/yyyy",
                    DataProtectionLaw = "NDPA 2023",
                    DataResidencyRegion = "SouthAfricaNorth",
                    IsActive = true
                };
                db.Jurisdictions.Add(jurisdiction);
            }

            var licenceType = await db.LicenceTypes.FirstOrDefaultAsync(item => item.Code == "BDC");
            if (licenceType is null)
            {
                licenceType = new LicenceType
                {
                    Code = "BDC",
                    Name = "Bureau de Change",
                    Regulator = "CBN",
                    Description = "BDC licence type seeded for end-to-end lifecycle tests.",
                    DisplayOrder = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.LicenceTypes.Add(licenceType);
            }

            var plan = await db.SubscriptionPlans.FirstOrDefaultAsync(item => item.PlanCode == "STARTER");
            if (plan is null)
            {
                plan = new SubscriptionPlan
                {
                    PlanCode = "STARTER",
                    PlanName = "Starter",
                    Description = "Starter plan for BDC integration tests.",
                    Tier = 1,
                    MaxModules = 6,
                    MaxUsersPerEntity = 10,
                    MaxEntities = 1,
                    MaxApiCallsPerMonth = 10000,
                    MaxStorageMb = 512,
                    BasePriceMonthly = 0m,
                    BasePriceAnnual = 0m,
                    TrialDays = 14,
                    Features = "[\"xml_submission\",\"validation\",\"reporting\"]",
                    DisplayOrder = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.SubscriptionPlans.Add(plan);
            }

            var bdcModule = await db.Modules.FirstOrDefaultAsync(item => item.ModuleCode == BdcModuleCode);
            if (bdcModule is null)
            {
                bdcModule = new Module
                {
                    ModuleCode = BdcModuleCode,
                    ModuleName = "BDC CBN Returns",
                    RegulatorCode = "CBN",
                    Description = "BDC regulatory module seeded for lifecycle integration tests.",
                    SheetCount = 12,
                    DefaultFrequency = "Monthly",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Modules.Add(bdcModule);
            }

            var nfiuModule = await db.Modules.FirstOrDefaultAsync(item => item.ModuleCode == "NFIU_AML");
            if (nfiuModule is null)
            {
                nfiuModule = new Module
                {
                    ModuleCode = "NFIU_AML",
                    ModuleName = "NFIU AML Returns",
                    RegulatorCode = "NFIU",
                    Description = "NFIU AML module seeded for BDC entitlement coverage.",
                    SheetCount = 12,
                    DefaultFrequency = "Monthly",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Modules.Add(nfiuModule);
            }

            await db.SaveChangesAsync();

            var moduleIds = new[] { bdcModule.Id, nfiuModule.Id };
            foreach (var moduleId in moduleIds)
            {
                var matrixExists = await db.LicenceModuleMatrix.AnyAsync(item =>
                    item.LicenceTypeId == licenceType.Id && item.ModuleId == moduleId);
                if (!matrixExists)
                {
                    db.LicenceModuleMatrix.Add(new LicenceModuleMatrix
                    {
                        LicenceTypeId = licenceType.Id,
                        ModuleId = moduleId,
                        IsRequired = true,
                        IsOptional = false
                    });
                }

                var pricingExists = await db.PlanModulePricing.AnyAsync(item =>
                    item.PlanId == plan.Id && item.ModuleId == moduleId);
                if (!pricingExists)
                {
                    db.PlanModulePricing.Add(new PlanModulePricing
                    {
                        PlanId = plan.Id,
                        ModuleId = moduleId,
                        PriceMonthly = 0m,
                        PriceAnnual = 0m,
                        IsIncludedInBase = true
                    });
                }
            }

            await db.SaveChangesAsync();
            return 0;
        });
    }

    private async Task ImportAndPublishBdcModuleAsync()
    {
        await ExecuteGlobalAsync(async sp =>
        {
            var db = sp.GetRequiredService<MetadataDbContext>();
            var existingPublished = await db.TemplateVersions
                .Join(
                    db.ReturnTemplates.Where(item => item.Module != null && item.Module.ModuleCode == BdcModuleCode),
                    version => version.TemplateId,
                    template => template.Id,
                    (version, _) => version)
                .AnyAsync(version => version.Status == TemplateStatus.Published);

            if (!existingPublished)
            {
                var importService = sp.GetRequiredService<IModuleImportService>();
                var definitionPath = Path.Combine(
                    _solutionRoot,
                    "src",
                    "FC.Engine.Migrator",
                    "SeedData",
                    "ModuleDefinitions",
                    "rg08-bdc-cbn-module-definition.json");

                File.Exists(definitionPath).Should().BeTrue($"Expected BDC module definition at {definitionPath}");
                var definitionJson = await File.ReadAllTextAsync(definitionPath);

                var validation = await importService.ValidateDefinition(definitionJson);
                validation.IsValid.Should().BeTrue(string.Join(" | ", validation.Errors));

                var import = await importService.ImportModule(definitionJson, "bdc-lifecycle-test");
                import.Success.Should().BeTrue(string.Join(" | ", import.Errors));

                var publish = await importService.PublishModule(BdcModuleCode, "bdc-lifecycle-test");
                publish.Success.Should().BeTrue(string.Join(" | ", publish.Errors));
            }

            BdcModuleId = await db.Modules
                .Where(item => item.ModuleCode == BdcModuleCode)
                .Select(item => item.Id)
                .SingleAsync();

            return 0;
        });
    }

    private async Task CleanupCreatedTenantsAsync()
    {
        if (_createdTenantIds.Count == 0)
        {
            return;
        }

        var tenantIds = _createdTenantIds
            .Distinct()
            .ToArray();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        var tableNames = (await connection.QueryAsync<string>(
            """
            SELECT rt.PhysicalTableName
            FROM meta.return_templates rt
            INNER JOIN dbo.modules m ON m.Id = rt.ModuleId
            WHERE m.ModuleCode = @moduleCode
            """,
            new { moduleCode = BdcModuleCode }))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var tableName in tableNames)
        {
            var safeTableName = tableName.Replace("]", "]]", StringComparison.Ordinal);
            await connection.ExecuteAsync(
                $"""
                IF OBJECT_ID(N'dbo.[{safeTableName}]', N'U') IS NOT NULL
                    DELETE FROM dbo.[{safeTableName}] WHERE TenantId IN @TenantIds;
                """,
                new { TenantIds = tenantIds });
        }

        await connection.ExecuteAsync(
            """
            DELETE FROM meta.submission_field_sources WHERE TenantId IN @TenantIds;
            DELETE FROM meta.audit_log WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.examiner_queries WHERE TenantId IN @TenantIds OR RegulatorTenantId IN @TenantIds;
            DELETE FROM dbo.regulator_receipts WHERE TenantId IN @TenantIds OR RegulatorTenantId IN @TenantIds;
            DELETE FROM dbo.portal_notifications WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.notification_deliveries WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.notification_preferences WHERE TenantId IN @TenantIds;
            DELETE FROM meta.submission_approvals WHERE TenantId IN @TenantIds;
            DELETE ve
            FROM dbo.validation_errors ve
            INNER JOIN dbo.validation_reports vr ON vr.Id = ve.ValidationReportId
            WHERE vr.TenantId IN @TenantIds;
            DELETE FROM dbo.validation_reports WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.filing_sla_records WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.return_submissions WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.return_periods WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.consent_records WHERE TenantId IN @TenantIds;
            DELETE FROM meta.portal_users WHERE TenantId IN @TenantIds;
            DELETE FROM meta.institution_users WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.subscription_modules WHERE SubscriptionId IN (SELECT Id FROM dbo.subscriptions WHERE TenantId IN @TenantIds);
            DELETE FROM dbo.subscriptions WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.tenant_licence_types WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.institutions WHERE TenantId IN @TenantIds;
            DELETE FROM dbo.tenants WHERE TenantId IN @TenantIds;
            """,
            new { TenantIds = tenantIds });
    }

    private async Task<TResult> ExecuteAsync<TResult>(
        Guid? tenantId,
        string? tenantType,
        string? regulatorCode,
        Func<IServiceProvider, Task<TResult>> action)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };

        if (tenantId.HasValue)
        {
            context.Items["TenantId"] = tenantId.Value;
        }

        if (!string.IsNullOrWhiteSpace(tenantType))
        {
            context.Items["TenantType"] = tenantType;
        }

        if (!string.IsNullOrWhiteSpace(regulatorCode))
        {
            context.Items["RegulatorCode"] = regulatorCode;
        }

        accessor.HttpContext = context;

        try
        {
            return await action(scope.ServiceProvider);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "FCEngine.sln");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate FCEngine.sln from the integration test base directory.");
    }

    private string ResolveAssetPath(params string[] relativeSegments)
    {
        var candidates = new[]
        {
            Path.Combine(_solutionRoot, Path.Combine(relativeSegments)),
            Path.Combine(Directory.GetParent(_solutionRoot)?.FullName ?? _solutionRoot, Path.Combine(relativeSegments))
        };

        var resolved = candidates.FirstOrDefault(File.Exists);
        if (resolved is not null)
        {
            return resolved;
        }

        throw new FileNotFoundException(
            $"Unable to locate required test asset '{Path.Combine(relativeSegments)}'. Checked: {string.Join(" | ", candidates)}");
    }

    private static string BuildUniqueEmail(string baseEmail, string localPart)
    {
        var atIndex = baseEmail.IndexOf('@');
        if (atIndex <= 0 || atIndex == baseEmail.Length - 1)
        {
            return $"{localPart}@bdc-int.test";
        }

        return $"{localPart}@{baseEmail[(atIndex + 1)..]}";
    }

    private static string BuildInstitutionCode(string licenceNumber, string suffix)
    {
        var cleaned = Regex.Replace(licenceNumber, "[^A-Za-z0-9]", string.Empty);
        var candidate = $"{cleaned}{suffix}".ToUpperInvariant();
        return candidate[..Math.Min(candidate.Length, 20)];
    }
}

public sealed record OnboardedInstitution(
    Guid TenantId,
    int InstitutionId,
    string InstitutionCode,
    string InstitutionName,
    string AdminEmail,
    IReadOnlyList<string> ActivatedModules);

public sealed record RegulatorUser(
    Guid TenantId,
    int UserId,
    string RegulatorCode);

public sealed record SubmissionState(
    FC.Engine.Domain.Entities.Submission Submission,
    SubmissionApproval? Approval,
    FilingSlaRecord? Sla);

public sealed record RegulatorArtifacts(
    RegulatorReceipt? Receipt,
    IReadOnlyList<ExaminerQuery> Queries);

public sealed class BdcDtrSample
{
    private static readonly IReadOnlyDictionary<string, decimal> ReferenceRates =
        new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1500m,
            ["GBP"] = 1900m,
            ["EUR"] = 1650m,
            ["CNY"] = 210m,
            ["OTHER"] = 1000m
        };

    private readonly IReadOnlyList<BdcDtrTransaction> _transactions;

    private BdcDtrSample(
        string licenceNumber,
        string institutionName,
        string contactPerson,
        string contactEmail,
        string contactPhone,
        string headOfficeLocation,
        DateTime reportingPeriodEnd,
        IReadOnlyList<BdcDtrTransaction> transactions)
    {
        LicenceNumber = licenceNumber;
        InstitutionName = institutionName;
        ContactPerson = contactPerson;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        HeadOfficeLocation = headOfficeLocation;
        ReportingPeriodEnd = reportingPeriodEnd;
        _transactions = transactions;
    }

    public string LicenceNumber { get; }
    public string InstitutionName { get; }
    public string ContactPerson { get; }
    public string ContactEmail { get; }
    public string ContactPhone { get; }
    public string HeadOfficeLocation { get; }
    public DateTime ReportingPeriodEnd { get; }

    public static BdcDtrSample Load(string path)
    {
        var document = XDocument.Load(path);
        XNamespace ns = "urn:centralbank:bdc:reporting:v1";

        var header = document.Root?.Element(ns + "Header")
            ?? throw new InvalidOperationException("BDC sample XML is missing the Header element.");
        var transactionsNode = document.Root?.Element(ns + "Transactions")
            ?? throw new InvalidOperationException("BDC sample XML is missing the Transactions element.");

        var transactions = transactionsNode.Elements(ns + "Transaction")
            .Select(element => new BdcDtrTransaction(
                GetRequiredValue(element, ns + "TxnType"),
                GetCurrencyCode(GetRequiredValue(element, ns + "CurrencyPair")),
                decimal.Parse(GetRequiredValue(element, ns + "ForeignAmount"), CultureInfo.InvariantCulture),
                decimal.Parse(GetRequiredValue(element, ns + "ExchangeRate"), CultureInfo.InvariantCulture)))
            .ToList();

        return new BdcDtrSample(
            GetRequiredValue(header, ns + "BDCLicenceNo"),
            GetRequiredValue(header, ns + "BDCName"),
            GetRequiredValue(header, ns + "ContactPerson"),
            GetRequiredValue(header, ns + "ContactEmail"),
            GetRequiredValue(header, ns + "ContactPhone"),
            GetRequiredValue(header, ns + "HeadOfficeLocation"),
            DateTime.Parse(GetRequiredValue(header, ns + "ReportingPeriodEnd"), CultureInfo.InvariantCulture),
            transactions);
    }

    public Dictionary<string, string> BuildFxvSubmissionRow()
    {
        var usdBuyingRate = AverageRate("USD", "BUY", ReferenceRates["USD"]);
        var usdSellingRate = AverageRate("USD", "SELL", ReferenceRates["USD"]);
        var gbpBuyingRate = AverageRate("GBP", "BUY", ReferenceRates["GBP"]);
        var gbpSellingRate = AverageRate("GBP", "SELL", ReferenceRates["GBP"]);
        var eurBuyingRate = AverageRate("EUR", "BUY", ReferenceRates["EUR"]);
        var eurSellingRate = AverageRate("EUR", "SELL", ReferenceRates["EUR"]);
        var cnyBuyingRate = AverageRate("CNY", "BUY", ReferenceRates["CNY"]);
        var cnySellingRate = AverageRate("CNY", "SELL", ReferenceRates["CNY"]);
        var otherBuyingRate = AverageRate("OTHER", "BUY", ReferenceRates["OTHER"]);
        var otherSellingRate = AverageRate("OTHER", "SELL", ReferenceRates["OTHER"]);

        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["usd_buying_volume"] = ToText(Volume("USD", "BUY")),
            ["usd_buying_rate_avg"] = ToText(usdBuyingRate),
            ["usd_selling_volume"] = ToText(Volume("USD", "SELL")),
            ["usd_selling_rate_avg"] = ToText(usdSellingRate),
            ["gbp_buying_volume"] = ToText(Volume("GBP", "BUY")),
            ["gbp_buying_rate_avg"] = ToText(gbpBuyingRate),
            ["gbp_selling_volume"] = ToText(Volume("GBP", "SELL")),
            ["gbp_selling_rate_avg"] = ToText(gbpSellingRate),
            ["eur_buying_volume"] = ToText(Volume("EUR", "BUY")),
            ["eur_buying_rate_avg"] = ToText(eurBuyingRate),
            ["eur_selling_volume"] = ToText(Volume("EUR", "SELL")),
            ["eur_selling_rate_avg"] = ToText(eurSellingRate),
            ["cny_buying_volume"] = ToText(Volume("CNY", "BUY")),
            ["cny_buying_rate_avg"] = ToText(cnyBuyingRate),
            ["cny_selling_volume"] = ToText(Volume("CNY", "SELL")),
            ["cny_selling_rate_avg"] = ToText(cnySellingRate),
            ["other_buying_volume"] = ToText(Volume("OTHER", "BUY")),
            ["other_buying_rate_avg"] = ToText(otherBuyingRate),
            ["other_selling_volume"] = ToText(Volume("OTHER", "SELL")),
            ["other_selling_rate_avg"] = ToText(otherSellingRate)
        };

        var totalBuyingVolume = DecimalValue(row["usd_buying_volume"])
            + DecimalValue(row["gbp_buying_volume"])
            + DecimalValue(row["eur_buying_volume"])
            + DecimalValue(row["cny_buying_volume"])
            + DecimalValue(row["other_buying_volume"]);
        var totalSellingVolume = DecimalValue(row["usd_selling_volume"])
            + DecimalValue(row["gbp_selling_volume"])
            + DecimalValue(row["eur_selling_volume"])
            + DecimalValue(row["cny_selling_volume"])
            + DecimalValue(row["other_selling_volume"]);
        var totalBuyingRate = usdBuyingRate + gbpBuyingRate + eurBuyingRate + cnyBuyingRate + otherBuyingRate;
        var totalSellingRate = usdSellingRate + gbpSellingRate + eurSellingRate + cnySellingRate + otherSellingRate;

        row["total_buying_volume"] = ToText(totalBuyingVolume);
        row["total_selling_volume"] = ToText(totalSellingVolume);
        row["net_position"] = ToText(totalBuyingVolume - totalSellingVolume);
        row["total_buying_rate_avg"] = ToText(totalBuyingRate);
        row["total_selling_rate_avg"] = ToText(totalSellingRate);
        row["spread_avg"] = ToText(totalSellingRate - totalBuyingRate);

        return row;
    }

    private decimal Volume(string currencyCode, string transactionType)
    {
        return _transactions
            .Where(item =>
                string.Equals(item.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.TransactionType, transactionType, StringComparison.OrdinalIgnoreCase))
            .Sum(item => item.ForeignAmount);
    }

    private decimal AverageRate(string currencyCode, string transactionType, decimal fallback)
    {
        return _transactions
            .Where(item =>
                string.Equals(item.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.TransactionType, transactionType, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ExchangeRate)
            .DefaultIfEmpty(fallback)
            .Average();
    }

    private static decimal DecimalValue(string value)
    {
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string GetCurrencyCode(string currencyPair)
    {
        var pair = currencyPair.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pair.Length == 0)
        {
            return "OTHER";
        }

        return pair[0].ToUpperInvariant() switch
        {
            "USD" => "USD",
            "GBP" => "GBP",
            "EUR" => "EUR",
            "CNY" => "CNY",
            _ => "OTHER"
        };
    }

    private static string GetRequiredValue(XElement parent, XName name)
    {
        return parent.Element(name)?.Value.Trim()
            ?? throw new InvalidOperationException($"Expected XML element '{name.LocalName}' was not found in the BDC sample.");
    }

    private static string ToText(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}

public sealed record BdcDtrTransaction(
    string TransactionType,
    string CurrencyCode,
    decimal ForeignAmount,
    decimal ExchangeRate);
