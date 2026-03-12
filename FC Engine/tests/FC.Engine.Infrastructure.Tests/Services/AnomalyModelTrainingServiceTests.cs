using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Infrastructure.Tests.Services;

public class AnomalyModelTrainingServiceTests
{
    [Fact]
    public async Task TrainModuleModelAsync_Creates_Field_Models_Correlation_Rules_And_Peer_Stats()
    {
        await using var db = CreateDb(nameof(TrainModuleModelAsync_Creates_Field_Models_Correlation_Rules_And_Peer_Stats));
        var module = new Module
        {
            Id = 1,
            ModuleCode = "CBN_PRUDENTIAL",
            ModuleName = "CBN Prudential",
            RegulatorCode = "CBN",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.Modules.Add(module);

        var licence = new LicenceType
        {
            Id = 1,
            Code = "COMMERCIAL_BANK",
            Name = "Commercial Bank",
            Regulator = "CBN",
            CreatedAt = DateTime.UtcNow
        };
        db.LicenceTypes.Add(licence);

        for (var i = 1; i <= 6; i++)
        {
            var tenant = Tenant.Create($"Train {i}", $"train-{i}", TenantType.Institution, $"train-{i}@example.com");
            db.Tenants.Add(tenant);
            db.TenantLicenceTypes.Add(new TenantLicenceType
            {
                TenantId = tenant.TenantId,
                LicenceTypeId = licence.Id,
                LicenceType = licence,
                EffectiveDate = DateTime.UtcNow,
                IsActive = true
            });

            var institution = new Institution
            {
                Id = i,
                TenantId = tenant.TenantId,
                InstitutionCode = $"I-{i}",
                InstitutionName = $"Institution {i}",
                LicenseType = "COMMERCIAL_BANK",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            var period = new ReturnPeriod
            {
                Id = 100 + i,
                TenantId = tenant.TenantId,
                ModuleId = module.Id,
                Module = module,
                Year = 2026,
                Month = 3,
                Quarter = 1,
                Frequency = "Quarterly",
                ReportingDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
                DeadlineDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = DateTime.UtcNow,
                Status = "Closed"
            };

            var submission = Submission.Create(institution.Id, period.Id, "CBN-PRUDENTIAL", tenant.TenantId);
            submission.Institution = institution;
            submission.ReturnPeriod = period;
            submission.Status = SubmissionStatus.Accepted;
            submission.ParsedDataJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                ReturnCode = "CBN-PRUDENTIAL",
                TemplateVersionId = 1,
                Category = "FixedRow",
                Rows = new[]
                {
                    new
                    {
                        RowKey = (string?)null,
                        Fields = new Dictionary<string, object>
                        {
                            ["total_assets"] = 1000m + (i * 40m),
                            ["total_liabilities"] = 850m + (i * 30m),
                            ["total_deposits"] = 790m + (i * 20m),
                            ["total_loans"] = 620m + (i * 15m),
                            ["car_ratio"] = 18m + i
                        }
                    }
                }
            });

            db.Institutions.Add(institution);
            db.ReturnPeriods.Add(period);
            db.Submissions.Add(submission);
        }

        await db.SaveChangesAsync();

        var audit = new Mock<IAuditLogger>();
        audit.Setup(x => x.Log(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<object?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new AnomalyModelTrainingService(db, audit.Object, NullLogger<AnomalyModelTrainingService>.Instance);

        var version = await sut.TrainModuleModelAsync(module.ModuleCode, "tester", true);

        version.Status.Should().Be("ACTIVE");
        db.AnomalyFieldModels.Count(x => x.ModelVersionId == version.Id).Should().BeGreaterThan(0);
        db.AnomalyCorrelationRules.Count(x => x.ModelVersionId == version.Id).Should().BeGreaterThan(0);
        db.AnomalyPeerGroupStatistics.Count(x => x.ModelVersionId == version.Id).Should().BeGreaterThan(0);
    }

    private static MetadataDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var db = new MetadataDbContext(options);
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
        return db;
    }
}
