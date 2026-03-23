using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Portal.Services;
using FC.Engine.Portal.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FC.Engine.Portal.Tests.Services;

public class SubmissionServiceTests
{
    [Fact]
    public async Task GetTemplatesForInstitution_Returns_Only_Entitled_Templates_And_Marks_Current_Submission()
    {
        var tenantId = Guid.NewGuid();
        var dbFactory = new TestMetadataDbContextFactory(nameof(GetTemplatesForInstitution_Returns_Only_Entitled_Templates_And_Marks_Current_Submission));
        await using var db = dbFactory.CreateDbContext();

        db.Modules.AddRange(
            CreateModule(1, "CAPITAL_SUPERVISION", "Capital Supervision"),
            CreateModule(2, "MODEL_RISK", "Model Risk"));
        await db.SaveChangesAsync();

        var entitlementService = CreateEntitlementService(tenantId, activeModules:
        [
            CreateEntitledModule(1, "CAPITAL_SUPERVISION", "Capital Supervision")
        ]);

        var templateService = CreateTemplateService(entitlementService.Object,
        [
            CreatePublishedTemplate(1, null, "CAP_BUF", "Capital Buffer Register", ReturnFrequency.Monthly, StructuralCategory.FixedRow),
            CreatePublishedTemplate(2, null, "MRM_INV", "Model Inventory", ReturnFrequency.Quarterly, StructuralCategory.FixedRow)
        ]);

        var submissionRepo = new Mock<ISubmissionRepository>();
        submissionRepo
            .Setup(x => x.GetByInstitution(44, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Submission
                {
                    Id = 901,
                    InstitutionId = 44,
                    TenantId = tenantId,
                    ReturnCode = "CAP_BUF",
                    ReturnPeriodId = 5,
                    Status = SubmissionStatus.Accepted,
                    SubmittedAt = DateTime.UtcNow
                }
            ]);

        var sut = CreateSubmissionService(
            templateService,
            entitlementService.Object,
            new TestTenantContext { CurrentTenantId = tenantId },
            submissionRepo.Object,
            dbFactory);

        var templates = await sut.GetTemplatesForInstitution(44);

        templates.Should().HaveCount(1);
        templates[0].ReturnCode.Should().Be("CAP_BUF");
        templates[0].ModuleCode.Should().Be("CAPITAL_SUPERVISION");
        templates[0].AlreadySubmitted.Should().BeTrue();
    }

    [Fact]
    public async Task GetTemplatesForInstitution_Applies_Module_Filter()
    {
        var tenantId = Guid.NewGuid();
        var dbFactory = new TestMetadataDbContextFactory(nameof(GetTemplatesForInstitution_Applies_Module_Filter));
        await using var db = dbFactory.CreateDbContext();

        db.Modules.AddRange(
            CreateModule(1, "CAPITAL_SUPERVISION", "Capital Supervision"),
            CreateModule(2, "MODEL_RISK", "Model Risk"));
        await db.SaveChangesAsync();

        var entitlementService = CreateEntitlementService(tenantId, activeModules:
        [
            CreateEntitledModule(1, "CAPITAL_SUPERVISION", "Capital Supervision"),
            CreateEntitledModule(2, "MODEL_RISK", "Model Risk")
        ]);

        var templateService = CreateTemplateService(entitlementService.Object,
        [
            CreatePublishedTemplate(1, null, "CAP_BUF", "Capital Buffer Register", ReturnFrequency.Monthly, StructuralCategory.FixedRow),
            CreatePublishedTemplate(2, null, "MRM_INV", "Model Inventory", ReturnFrequency.Quarterly, StructuralCategory.FixedRow)
        ]);

        var submissionRepo = new Mock<ISubmissionRepository>();
        submissionRepo
            .Setup(x => x.GetByInstitution(44, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Submission>());

        var sut = CreateSubmissionService(
            templateService,
            entitlementService.Object,
            new TestTenantContext { CurrentTenantId = tenantId },
            submissionRepo.Object,
            dbFactory);

        var templates = await sut.GetTemplatesForInstitution(44, "model_risk");

        templates.Select(x => x.ReturnCode).Should().Equal("MRM_INV");
        templates[0].ModuleCode.Should().Be("MODEL_RISK");
    }

    [Fact]
    public async Task GetOpenPeriods_Filters_To_Template_Module_And_Marks_Existing_Submissions()
    {
        var tenantId = Guid.NewGuid();
        var dbFactory = new TestMetadataDbContextFactory(nameof(GetOpenPeriods_Filters_To_Template_Module_And_Marks_Existing_Submissions));
        await using var db = dbFactory.CreateDbContext();

        db.Modules.AddRange(
            CreateModule(1, "CAPITAL_SUPERVISION", "Capital Supervision"),
            CreateModule(2, "MODEL_RISK", "Model Risk"));

        db.ReturnPeriods.AddRange(
            CreateReturnPeriod(tenantId, 101, 1, 2026, 2, isOpen: true),
            CreateReturnPeriod(tenantId, 102, 1, 2026, 1, isOpen: true),
            CreateReturnPeriod(tenantId, 201, 2, 2026, 2, isOpen: true),
            CreateReturnPeriod(tenantId, 301, 1, 2025, 12, isOpen: false));
        await db.SaveChangesAsync();

        var entitlementService = CreateEntitlementService(tenantId, activeModules:
        [
            CreateEntitledModule(1, "CAPITAL_SUPERVISION", "Capital Supervision"),
            CreateEntitledModule(2, "MODEL_RISK", "Model Risk")
        ]);

        var templateService = CreateTemplateService(entitlementService.Object,
        [
            CreatePublishedTemplate(1, null, "CAP_BUF", "Capital Buffer Register", ReturnFrequency.Monthly, StructuralCategory.FixedRow),
            CreatePublishedTemplate(2, null, "MRM_INV", "Model Inventory", ReturnFrequency.Quarterly, StructuralCategory.FixedRow)
        ]);

        var submissionRepo = new Mock<ISubmissionRepository>();
        submissionRepo
            .Setup(x => x.GetByInstitution(44, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new Submission
                {
                    Id = 7001,
                    InstitutionId = 44,
                    TenantId = tenantId,
                    ReturnCode = "CAP_BUF",
                    ReturnPeriodId = 101,
                    Status = SubmissionStatus.Accepted,
                    SubmittedAt = DateTime.UtcNow
                },
                new Submission
                {
                    Id = 7002,
                    InstitutionId = 44,
                    TenantId = tenantId,
                    ReturnCode = "CAP_BUF",
                    ReturnPeriodId = 102,
                    Status = SubmissionStatus.Rejected,
                    SubmittedAt = DateTime.UtcNow
                }
            ]);

        var sut = CreateSubmissionService(
            templateService,
            entitlementService.Object,
            new TestTenantContext { CurrentTenantId = tenantId },
            submissionRepo.Object,
            dbFactory);

        var periods = await sut.GetOpenPeriods(44, "CAP_BUF");

        periods.Select(x => x.ReturnPeriodId).Should().Equal(101, 102);
        periods.Select(x => x.Label).Should().Equal("February 2026", "January 2026");
        periods.Single(x => x.ReturnPeriodId == 101).HasExistingSubmission.Should().BeTrue();
        periods.Single(x => x.ReturnPeriodId == 101).ExistingSubmissionId.Should().Be(7001);
        periods.Single(x => x.ReturnPeriodId == 102).HasExistingSubmission.Should().BeFalse();
    }

    private static SubmissionService CreateSubmissionService(
        TemplateService templateService,
        IEntitlementService entitlementService,
        ITenantContext tenantContext,
        ISubmissionRepository submissionRepository,
        IDbContextFactory<MetadataDbContext> dbFactory)
    {
        return new SubmissionService(
            templateService,
            entitlementService,
            tenantContext,
            submissionRepository,
            Mock.Of<ISubmissionApprovalRepository>(),
            null!,
            dbFactory,
            null!,
            Mock.Of<IFilingCalendarService>(),
            Mock.Of<ITemplateMetadataCache>(),
            Mock.Of<ILogger<SubmissionService>>(),
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>());
    }

    private static Mock<IEntitlementService> CreateEntitlementService(Guid tenantId, IReadOnlyList<EntitledModule> activeModules)
    {
        var entitlement = new TenantEntitlement
        {
            TenantId = tenantId,
            TenantStatus = TenantStatus.Active,
            ActiveModules = activeModules,
            EligibleModules = activeModules,
            ResolvedAt = DateTime.UtcNow
        };

        var mock = new Mock<IEntitlementService>();
        mock.Setup(x => x.ResolveEntitlements(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entitlement);
        return mock;
    }

    private static TemplateService CreateTemplateService(
        IEntitlementService entitlementService,
        IReadOnlyList<ReturnTemplate> templates)
    {
        var templateRepository = new Mock<ITemplateRepository>();
        templateRepository
            .Setup(x => x.GetByModuleIds(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<int> moduleIds, CancellationToken _) =>
                templates.Where(template => template.ModuleId.HasValue && moduleIds.Contains(template.ModuleId.Value)).ToList());

        templateRepository
            .Setup(x => x.GetAll(It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        return new TemplateService(
            templateRepository.Object,
            Mock.Of<IAuditLogger>(),
            Mock.Of<ITemplateMetadataCache>(),
            Mock.Of<ISqlTypeMapper>(),
            entitlementService,
            new TestTenantContext());
    }

    private static Module CreateModule(int id, string code, string name) =>
        new()
        {
            Id = id,
            ModuleCode = code,
            ModuleName = name,
            RegulatorCode = "CBN",
            DefaultFrequency = "Monthly",
            SheetCount = 4,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

    private static EntitledModule CreateEntitledModule(int id, string code, string name) =>
        new()
        {
            ModuleId = id,
            ModuleCode = code,
            ModuleName = name,
            RegulatorCode = "CBN",
            IsActive = true,
            DefaultFrequency = "Monthly",
            SheetCount = 4
        };

    private static ReturnTemplate CreatePublishedTemplate(
        int moduleId,
        Guid? tenantId,
        string returnCode,
        string name,
        ReturnFrequency frequency,
        StructuralCategory structuralCategory)
    {
        var template = new ReturnTemplate
        {
            Id = moduleId * 100,
            ModuleId = moduleId,
            TenantId = tenantId,
            ReturnCode = returnCode,
            Name = name,
            Frequency = frequency,
            StructuralCategory = structuralCategory,
            PhysicalTableName = returnCode.ToLowerInvariant(),
            XmlRootElement = returnCode,
            XmlNamespace = $"urn:{returnCode.ToLowerInvariant()}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            UpdatedBy = "test"
        };

        template.AddVersion(new TemplateVersion
        {
            Id = moduleId * 1000,
            TemplateId = template.Id,
            VersionNumber = 1,
            Status = TemplateStatus.Published,
            PublishedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        return template;
    }

    private static ReturnPeriod CreateReturnPeriod(Guid tenantId, int id, int moduleId, int year, int month, bool isOpen) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ModuleId = moduleId,
            Year = year,
            Month = month,
            Frequency = "Monthly",
            ReportingDate = new DateTime(year, month, 1),
            DeadlineDate = new DateTime(year, month, 1).AddMonths(1).AddDays(15),
            Status = isOpen ? "Open" : "Closed",
            NotificationLevel = 0,
            CreatedAt = DateTime.UtcNow,
            IsOpen = isOpen
        };
}
