using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class CarryForwardServiceTests
{
    [Fact]
    public async Task CarryForward_Uses_Last_Finalized_Submission_Not_PendingApproval()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(nameof(CarryForward_Uses_Last_Finalized_Submission_Not_PendingApproval));

        var previousPeriod = new ReturnPeriod
        {
            TenantId = tenantId,
            Year = 2026,
            Month = 1,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2026, 1, 31),
            DeadlineDate = new DateTime(2026, 3, 2),
            IsOpen = true,
            CreatedAt = DateTime.UtcNow
        };

        var currentPeriod = new ReturnPeriod
        {
            TenantId = tenantId,
            Year = 2026,
            Month = 2,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2026, 2, 28),
            DeadlineDate = new DateTime(2026, 3, 30),
            IsOpen = true,
            CreatedAt = DateTime.UtcNow
        };

        db.ReturnPeriods.AddRange(previousPeriod, currentPeriod);
        await db.SaveChangesAsync();

        var finalizedSubmission = new Submission
        {
            TenantId = tenantId,
            InstitutionId = 1,
            ReturnPeriodId = previousPeriod.Id,
            ReturnCode = "BDC_CAP",
            Status = SubmissionStatus.Accepted,
            SubmittedAt = DateTime.UtcNow.AddDays(-20),
            CreatedAt = DateTime.UtcNow.AddDays(-20)
        };

        var pendingSubmission = new Submission
        {
            TenantId = tenantId,
            InstitutionId = 1,
            ReturnPeriodId = previousPeriod.Id,
            ReturnCode = "BDC_CAP",
            Status = SubmissionStatus.PendingApproval,
            SubmittedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        db.Submissions.AddRange(finalizedSubmission, pendingSubmission);
        await db.SaveChangesAsync();

        var templateCache = new Mock<ITemplateMetadataCache>();
        templateCache
            .Setup(x => x.GetPublishedTemplate(tenantId, "BDC_CAP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedTemplate
            {
                ReturnCode = "BDC_CAP",
                StructuralCategory = "FixedRow",
                CurrentVersion = new CachedTemplateVersion
                {
                    Id = 12,
                    Fields = new List<TemplateField>
                    {
                        new()
                        {
                            FieldName = "closing_balance",
                            DisplayName = "Closing Balance",
                            DataType = FieldDataType.Decimal,
                            IsYtdField = true,
                            FieldOrder = 1
                        },
                        new()
                        {
                            FieldName = "non_carry_field",
                            DisplayName = "Not Carry",
                            DataType = FieldDataType.Decimal,
                            IsYtdField = false,
                            FieldOrder = 2
                        }
                    }
                }
            });

        var dataRepo = new Mock<IGenericDataRepository>();
        dataRepo
            .Setup(x => x.ReadFieldValue("BDC_CAP", finalizedSubmission.Id, "closing_balance", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1250.55m);

        dataRepo
            .Setup(x => x.ReadFieldValue("BDC_CAP", pendingSubmission.Id, "closing_balance", It.IsAny<CancellationToken>()))
            .ReturnsAsync(9999m);

        var sut = new CarryForwardService(templateCache.Object, dataRepo.Object, db);

        var result = await sut.GetCarryForwardValues(tenantId, "BDC_CAP", currentPeriod.Id);

        result.HasValues.Should().BeTrue();
        result.SourceSubmissionId.Should().Be(finalizedSubmission.Id);
        result.SourceReturnPeriodId.Should().Be(previousPeriod.Id);
        result.Values.Should().ContainKey("closing_balance");
        result.Values["closing_balance"].Should().Be(1250.55m);

        dataRepo.Verify(
            x => x.ReadFieldValue("BDC_CAP", pendingSubmission.Id, "closing_balance", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new MetadataDbContext(options);
    }
}
