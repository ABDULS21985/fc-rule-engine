using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class DataFeedServiceTests
{
    [Fact]
    public async Task GetByIdempotencyKey_Returns_Previous_Result()
    {
        var tenantId = Guid.NewGuid();
        const string key = "idem-001";

        await using var db = CreateDb(nameof(GetByIdempotencyKey_Returns_Previous_Result));

        var expected = new DataFeedResult
        {
            Success = true,
            ReturnCode = "BDC_AML",
            SubmissionId = 55,
            Status = "Accepted",
            Message = "Data feed processed successfully.",
            RowsPersisted = 1
        };

        db.DataFeedRequestLogs.Add(new DataFeedRequestLog
        {
            TenantId = tenantId,
            ReturnCode = "BDC_AML",
            IdempotencyKey = key,
            RequestHash = new string('a', 64),
            ResultJson = System.Text.Json.JsonSerializer.Serialize(expected),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var sut = CreateService(db);

        var actual = await sut.GetByIdempotencyKey(tenantId, key);

        actual.Should().NotBeNull();
        actual!.Success.Should().BeTrue();
        actual.ReturnCode.Should().Be("BDC_AML");
        actual.SubmissionId.Should().Be(55);
        actual.Status.Should().Be("Accepted");
        actual.RowsPersisted.Should().Be(1);
    }

    [Fact]
    public async Task UpsertFieldMapping_Creates_And_Updates_Mapping()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(UpsertFieldMapping_Creates_And_Updates_Mapping));

        var sut = CreateService(db);

        await sut.UpsertFieldMapping(tenantId, "corebank", "MFB_PAR", "GL_BAL_1001", "cash_and_bank");
        await sut.UpsertFieldMapping(tenantId, "corebank", "MFB_PAR", "GL_BAL_1001", "cash_and_bank_updated");

        var mappings = await sut.GetFieldMappings(tenantId, "corebank", "MFB_PAR");

        mappings.Should().ContainSingle();
        mappings[0].ExternalFieldName.Should().Be("GL_BAL_1001");
        mappings[0].TemplateFieldName.Should().Be("cash_and_bank_updated");
        mappings[0].IntegrationName.Should().Be("corebank");
    }

    private static DataFeedService CreateService(MetadataDbContext db)
    {
        var templateCache = new Mock<ITemplateMetadataCache>();
        var dataRepo = new Mock<IGenericDataRepository>();
        var submissionRepo = new Mock<ISubmissionRepository>();

        var validationOrchestrator = new ValidationOrchestrator(
            new Mock<ITemplateMetadataCache>().Object,
            new Mock<IFormulaEvaluator>().Object,
            new Mock<ICrossSheetValidator>().Object,
            new Mock<IBusinessRuleEvaluator>().Object);

        return new DataFeedService(
            templateCache.Object,
            dataRepo.Object,
            submissionRepo.Object,
            validationOrchestrator,
            db,
            NullLogger<DataFeedService>.Instance);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }
}
