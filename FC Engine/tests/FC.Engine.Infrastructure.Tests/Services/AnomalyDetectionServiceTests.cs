using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UglyToad.PdfPig;

namespace FC.Engine.Infrastructure.Tests.Services;

public class AnomalyDetectionServiceTests
{
    [Fact]
    public async Task AnalyzeSubmissionAsync_Raises_Field_Relationship_Temporal_And_Peer_Findings()
    {
        await using var db = CreateDb(nameof(AnalyzeSubmissionAsync_Raises_Field_Relationship_Temporal_And_Peer_Findings));
        var module = SeedBaselineEnvironment(db);
        var licenceType = SeedLicenceType(db);

        var peerTenants = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            var tenant = SeedTenant(db, $"Peer {i}", $"peer-{i}");
            peerTenants.Add(tenant.TenantId);
            AttachLicence(db, tenant.TenantId, licenceType);
            var institution = SeedInstitution(db, tenant.TenantId, i + 10, $"Peer Bank {i}");
            var period = SeedPeriod(db, tenant.TenantId, 100 + i, module, 2026, 3, 1, new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));
            SeedSubmission(db, tenant.TenantId, institution, period, 1000m + (i * 40m), 840m + (i * 35m), 780m + (i * 30m), 620m + (i * 20m), 18m);
        }

        var currentTenant = SeedTenant(db, "Current Bank", "current-bank");
        AttachLicence(db, currentTenant.TenantId, licenceType);
        var currentInstitution = SeedInstitution(db, currentTenant.TenantId, 99, "Current Bank");
        var previousPeriod = SeedPeriod(db, currentTenant.TenantId, 400, module, 2025, 12, 4, new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        SeedSubmission(db, currentTenant.TenantId, currentInstitution, previousPeriod, 1025m, 860m, 805m, 635m, 17.5m);

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

        var trainer = new AnomalyModelTrainingService(db, audit.Object, NullLogger<AnomalyModelTrainingService>.Instance);
        await trainer.TrainModuleModelAsync(module.ModuleCode, "tester", true);

        var currentPeriod = SeedPeriod(db, currentTenant.TenantId, 401, module, 2026, 3, 1, new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));
        var currentSubmission = SeedSubmission(db, currentTenant.TenantId, currentInstitution, currentPeriod, 3000m, 1200m, 900m, 650m, 5m);
        await db.SaveChangesAsync();

        var sut = new AnomalyDetectionService(
            db,
            trainer,
            audit.Object,
            NullLogger<AnomalyDetectionService>.Instance);

        var report = await sut.AnalyzeSubmissionAsync(currentSubmission.Id, currentTenant.TenantId, "tester");

        report.TotalFindings.Should().BeGreaterThan(0);
        report.Findings.Should().Contain(x => x.FindingType == "FIELD");
        report.Findings.Should().Contain(x => x.FindingType == "RELATIONSHIP");
        report.Findings.Should().Contain(x => x.FindingType == "TEMPORAL");
        report.Findings.Should().Contain(x => x.FindingType == "PEER");
        report.OverallQualityScore.Should().BeLessThan(80m);
        report.TrafficLight.Should().NotBe("GREEN");
    }

    [Fact]
    public async Task AcknowledgeFindingAsync_Recalculates_Report_Quality_Score()
    {
        await using var db = CreateDb(nameof(AcknowledgeFindingAsync_Recalculates_Report_Quality_Score));
        var report = await BuildAnalyzedReportAsync(db);
        var initialScore = report.OverallQualityScore;
        var finding = report.Findings.OrderByDescending(x => x.Severity).First();

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

        var trainer = new AnomalyModelTrainingService(db, audit.Object, NullLogger<AnomalyModelTrainingService>.Instance);
        var sut = new AnomalyDetectionService(
            db,
            trainer,
            audit.Object,
            NullLogger<AnomalyDetectionService>.Instance);

        await sut.AcknowledgeFindingAsync(new FC.Engine.Domain.Models.AnomalyAcknowledgementRequest
        {
            FindingId = finding.Id,
            TenantId = report.TenantId,
            Reason = "Verified against board-approved corporate action.",
            AcknowledgedBy = "checker.user"
        });

        var refreshed = await sut.GetReportByIdAsync(report.Id, report.TenantId);
        refreshed.Should().NotBeNull();
        refreshed!.OverallQualityScore.Should().BeGreaterThan(initialScore);
        refreshed.Findings.Single(x => x.Id == finding.Id).IsAcknowledged.Should().BeTrue();
    }

    [Fact]
    public async Task ExportReportPdfAsync_Generates_Pdf_With_Report_Title()
    {
        await using var db = CreateDb(nameof(ExportReportPdfAsync_Generates_Pdf_With_Report_Title));
        var report = await BuildAnalyzedReportAsync(db);

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

        var trainer = new AnomalyModelTrainingService(db, audit.Object, NullLogger<AnomalyModelTrainingService>.Instance);
        var sut = new AnomalyDetectionService(
            db,
            trainer,
            audit.Object,
            NullLogger<AnomalyDetectionService>.Instance);

        var pdf = await sut.ExportReportPdfAsync(report.Id, report.TenantId);

        pdf.Should().NotBeEmpty();
        using var document = PdfDocument.Open(pdf);
        document.GetPages().Select(x => x.Text).Should().Contain(x => x.Contains("AI Anomaly Detection Report", StringComparison.Ordinal));
    }

    private static async Task<AnomalyReport> BuildAnalyzedReportAsync(MetadataDbContext db)
    {
        var module = SeedBaselineEnvironment(db);
        var licenceType = SeedLicenceType(db);

        for (var i = 1; i <= 5; i++)
        {
            var tenant = SeedTenant(db, $"Peer {i}", $"peer-a-{i}");
            AttachLicence(db, tenant.TenantId, licenceType);
            var institution = SeedInstitution(db, tenant.TenantId, i + 200, $"Peer Institution {i}");
            var period = SeedPeriod(db, tenant.TenantId, 500 + i, module, 2026, 3, 1, new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));
            SeedSubmission(db, tenant.TenantId, institution, period, 1000m + (i * 25m), 860m + (i * 20m), 790m + (i * 15m), 620m + (i * 10m), 18m);
        }

        var currentTenant = SeedTenant(db, "Target", "target");
        AttachLicence(db, currentTenant.TenantId, licenceType);
        var institutionTarget = SeedInstitution(db, currentTenant.TenantId, 900, "Target Institution");
        var previousPeriodTarget = SeedPeriod(db, currentTenant.TenantId, 601, module, 2025, 12, 4, new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        SeedSubmission(db, currentTenant.TenantId, institutionTarget, previousPeriodTarget, 1015m, 850m, 800m, 630m, 17m);

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

        var trainer = new AnomalyModelTrainingService(db, audit.Object, NullLogger<AnomalyModelTrainingService>.Instance);
        await trainer.TrainModuleModelAsync(module.ModuleCode, "tester", true);

        var currentPeriod = SeedPeriod(db, currentTenant.TenantId, 602, module, 2026, 3, 1, new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));
        var currentSubmission = SeedSubmission(db, currentTenant.TenantId, institutionTarget, currentPeriod, 2600m, 1250m, 910m, 680m, 6m);
        await db.SaveChangesAsync();

        var sut = new AnomalyDetectionService(
            db,
            trainer,
            audit.Object,
            NullLogger<AnomalyDetectionService>.Instance);

        return await sut.AnalyzeSubmissionAsync(currentSubmission.Id, currentTenant.TenantId, "tester");
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

    private static Module SeedBaselineEnvironment(MetadataDbContext db)
    {
        var module = new Module
        {
            Id = 1,
            ModuleCode = "CBN_PRUDENTIAL",
            ModuleName = "CBN Prudential Return",
            RegulatorCode = "CBN",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        if (!db.Modules.Any(x => x.Id == module.Id))
        {
            db.Modules.Add(module);
        }

        return module;
    }

    private static LicenceType SeedLicenceType(MetadataDbContext db)
    {
        var licence = new LicenceType
        {
            Id = 77,
            Code = "COMMERCIAL_BANK",
            Name = "Commercial Bank",
            Regulator = "CBN",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        if (!db.LicenceTypes.Any(x => x.Id == licence.Id))
        {
            db.LicenceTypes.Add(licence);
        }

        return licence;
    }

    private static Tenant SeedTenant(MetadataDbContext db, string name, string slug)
    {
        var tenant = Tenant.Create(name, slug, TenantType.Institution, $"{slug}@example.com");
        db.Tenants.Add(tenant);
        return tenant;
    }

    private static void AttachLicence(MetadataDbContext db, Guid tenantId, LicenceType licenceType)
    {
        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            TenantId = tenantId,
            LicenceTypeId = licenceType.Id,
            LicenceType = licenceType,
            EffectiveDate = DateTime.UtcNow,
            IsActive = true
        });
    }

    private static Institution SeedInstitution(MetadataDbContext db, Guid tenantId, int institutionId, string name)
    {
        var institution = new Institution
        {
            Id = institutionId,
            TenantId = tenantId,
            InstitutionCode = $"INST-{institutionId}",
            InstitutionName = name,
            LicenseType = "COMMERCIAL_BANK",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        db.Institutions.Add(institution);
        return institution;
    }

    private static ReturnPeriod SeedPeriod(
        MetadataDbContext db,
        Guid tenantId,
        int periodId,
        Module module,
        int year,
        int month,
        int? quarter,
        DateTime reportingDate)
    {
        var period = new ReturnPeriod
        {
            Id = periodId,
            TenantId = tenantId,
            ModuleId = module.Id,
            Module = module,
            Year = year,
            Month = month,
            Quarter = quarter,
            Frequency = quarter.HasValue ? "Quarterly" : "Monthly",
            ReportingDate = reportingDate,
            DeadlineDate = reportingDate.AddDays(30),
            IsOpen = false,
            CreatedAt = DateTime.UtcNow,
            Status = "Closed"
        };
        db.ReturnPeriods.Add(period);
        return period;
    }

    private static Submission SeedSubmission(
        MetadataDbContext db,
        Guid tenantId,
        Institution institution,
        ReturnPeriod period,
        decimal totalAssets,
        decimal totalLiabilities,
        decimal totalDeposits,
        decimal totalLoans,
        decimal carRatio)
    {
        var submission = Submission.Create(institution.Id, period.Id, "CBN-PRUDENTIAL", tenantId);
        submission.ReturnPeriod = period;
        submission.Institution = institution;
        submission.Status = SubmissionStatus.Accepted;
        submission.ParsedDataJson = BuildSubmissionJson(new Dictionary<string, decimal>
        {
            ["total_assets"] = totalAssets,
            ["total_liabilities"] = totalLiabilities,
            ["total_deposits"] = totalDeposits,
            ["total_loans"] = totalLoans,
            ["car_ratio"] = carRatio
        });
        db.Submissions.Add(submission);
        return submission;
    }

    private static string BuildSubmissionJson(Dictionary<string, decimal> fields)
    {
        var payload = new
        {
            ReturnCode = "CBN-PRUDENTIAL",
            TemplateVersionId = 1,
            Category = "FixedRow",
            Rows = new[]
            {
                new
                {
                    RowKey = (string?)null,
                    Fields = fields.ToDictionary(x => x.Key, x => (object)x.Value)
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }
}
