using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class InterModuleDataFlowEngineTests
{
    [Fact]
    public async Task DataFlow_AutoPopulates_Target_Field()
    {
        await using var db = CreateDbContext(nameof(DataFlow_AutoPopulates_Target_Field));
        var tenantId = Guid.NewGuid();
        var sourceSubmission = await SeedFlowGraph(db, tenantId);

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, "NFIU_AML", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var genericRepo = new Mock<IGenericDataRepository>();
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", sourceSubmission.Id, "str_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(9m);

        var sut = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await sut.ProcessDataFlows(
            tenantId,
            sourceSubmission.Id,
            "BDC_CBN",
            "BDC_AML",
            sourceSubmission.InstitutionId,
            sourceSubmission.ReturnPeriodId);

        var targetSubmission = await db.Submissions.SingleAsync(s => s.ReturnCode == "NFIU_STR");
        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_STR",
                targetSubmission.Id,
                "str_filed_count",
                9m,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DataFlow_Skipped_When_Target_Module_Inactive()
    {
        await using var db = CreateDbContext(nameof(DataFlow_Skipped_When_Target_Module_Inactive));
        var tenantId = Guid.NewGuid();
        var sourceSubmission = await SeedFlowGraph(db, tenantId);

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, "NFIU_AML", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var genericRepo = new Mock<IGenericDataRepository>();

        var sut = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await sut.ProcessDataFlows(
            tenantId,
            sourceSubmission.Id,
            "BDC_CBN",
            "BDC_AML",
            sourceSubmission.InstitutionId,
            sourceSubmission.ReturnPeriodId);

        genericRepo.Verify(r => r.WriteFieldValue(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        (await db.Submissions.CountAsync(s => s.ReturnCode == "NFIU_STR")).Should().Be(0);
    }

    [Fact]
    public async Task DataFlow_Sum_Aggregates_From_Multiple_Source_Modules_For_Same_Target()
    {
        await using var db = CreateDbContext(nameof(DataFlow_Sum_Aggregates_From_Multiple_Source_Modules_For_Same_Target));
        var tenantId = Guid.NewGuid();
        var (bdcSubmission, mfbSubmission) = await SeedSumFlowGraph(db, tenantId);

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, "NFIU_AML", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var genericRepo = new Mock<IGenericDataRepository>();
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", bdcSubmission.Id, "str_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(7m);
        genericRepo.Setup(r => r.ReadFieldValue("MFB_AML", mfbSubmission.Id, "str_filed_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(5m);

        var sut = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await sut.ProcessDataFlows(
            tenantId,
            bdcSubmission.Id,
            "BDC_CBN",
            "BDC_AML",
            bdcSubmission.InstitutionId,
            bdcSubmission.ReturnPeriodId);

        var targetSubmission = await db.Submissions.SingleAsync(s => s.ReturnCode == "NFIU_STR");
        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_STR",
                targetSubmission.Id,
                "str_filed_count",
                12m,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DataFlow_Records_DataSource_Metadata()
    {
        await using var db = CreateDbContext(nameof(DataFlow_Records_DataSource_Metadata));
        var tenantId = Guid.NewGuid();
        var sourceSubmission = await SeedFlowGraph(db, tenantId);

        var entitlement = new Mock<IEntitlementService>();
        entitlement.Setup(e => e.HasModuleAccess(tenantId, "NFIU_AML", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var genericRepo = new Mock<IGenericDataRepository>();
        genericRepo.Setup(r => r.ReadFieldValue("BDC_AML", sourceSubmission.Id, "str_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(6m);

        var sut = new InterModuleDataFlowEngine(
            db,
            entitlement.Object,
            genericRepo.Object,
            NullLogger<InterModuleDataFlowEngine>.Instance);

        await sut.ProcessDataFlows(
            tenantId,
            sourceSubmission.Id,
            "BDC_CBN",
            "BDC_AML",
            sourceSubmission.InstitutionId,
            sourceSubmission.ReturnPeriodId);

        var targetSubmission = await db.Submissions.SingleAsync(s => s.ReturnCode == "NFIU_STR");
        genericRepo.Verify(r => r.WriteFieldValue(
                "NFIU_STR",
                targetSubmission.Id,
                "str_filed_count",
                6m,
                "InterModule",
                "BDC_CBN/BDC_AML/str_count",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static MetadataDbContext CreateDbContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new MetadataDbContext(options);
    }

    private static async Task<Submission> SeedFlowGraph(MetadataDbContext db, Guid tenantId)
    {
        var sourceModule = new Module
        {
            ModuleCode = "BDC_CBN",
            ModuleName = "BDC",
            RegulatorCode = "CBN",
            DefaultFrequency = "Monthly",
            CreatedAt = DateTime.UtcNow
        };
        var targetModule = new Module
        {
            ModuleCode = "NFIU_AML",
            ModuleName = "NFIU",
            RegulatorCode = "NFIU",
            DefaultFrequency = "Monthly",
            CreatedAt = DateTime.UtcNow
        };
        db.Modules.AddRange(sourceModule, targetModule);
        await db.SaveChangesAsync();

        db.InterModuleDataFlows.Add(new InterModuleDataFlow
        {
            SourceModuleId = sourceModule.Id,
            SourceTemplateCode = "BDC_AML",
            SourceFieldCode = "str_count",
            TargetModuleCode = "NFIU_AML",
            TargetTemplateCode = "NFIU_STR",
            TargetFieldCode = "str_filed_count",
            TransformationType = "DirectCopy",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        var sourceSubmission = Submission.Create(77, 202601, "BDC_AML", tenantId);
        db.Submissions.Add(sourceSubmission);
        await db.SaveChangesAsync();
        return sourceSubmission;
    }

    private static async Task<(Submission BdcSubmission, Submission MfbSubmission)> SeedSumFlowGraph(
        MetadataDbContext db,
        Guid tenantId)
    {
        var bdcModule = new Module
        {
            ModuleCode = "BDC_CBN",
            ModuleName = "BDC",
            RegulatorCode = "CBN",
            DefaultFrequency = "Monthly",
            CreatedAt = DateTime.UtcNow
        };

        var mfbModule = new Module
        {
            ModuleCode = "MFB_PAR",
            ModuleName = "MFB",
            RegulatorCode = "CBN",
            DefaultFrequency = "Monthly",
            CreatedAt = DateTime.UtcNow
        };

        var nfiuModule = new Module
        {
            ModuleCode = "NFIU_AML",
            ModuleName = "NFIU",
            RegulatorCode = "NFIU",
            DefaultFrequency = "Monthly",
            CreatedAt = DateTime.UtcNow
        };

        db.Modules.AddRange(bdcModule, mfbModule, nfiuModule);
        await db.SaveChangesAsync();

        db.InterModuleDataFlows.AddRange(
            new InterModuleDataFlow
            {
                SourceModuleId = bdcModule.Id,
                SourceTemplateCode = "BDC_AML",
                SourceFieldCode = "str_filed_count",
                TargetModuleCode = "NFIU_AML",
                TargetTemplateCode = "NFIU_STR",
                TargetFieldCode = "str_filed_count",
                TransformationType = "Sum",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            },
            new InterModuleDataFlow
            {
                SourceModuleId = mfbModule.Id,
                SourceTemplateCode = "MFB_AML",
                SourceFieldCode = "str_filed_count",
                TargetModuleCode = "NFIU_AML",
                TargetTemplateCode = "NFIU_STR",
                TargetFieldCode = "str_filed_count",
                TransformationType = "Sum",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

        var bdcSubmission = Submission.Create(77, 202601, "BDC_AML", tenantId);
        var mfbSubmission = Submission.Create(77, 202601, "MFB_AML", tenantId);
        db.Submissions.AddRange(bdcSubmission, mfbSubmission);
        await db.SaveChangesAsync();

        return (bdcSubmission, mfbSubmission);
    }
}
