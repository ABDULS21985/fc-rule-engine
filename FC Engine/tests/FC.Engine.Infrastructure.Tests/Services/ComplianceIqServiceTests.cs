using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UglyToad.PdfPig;

namespace FC.Engine.Infrastructure.Tests.Services;

public class ComplianceIqServiceTests
{
    [Fact]
    public async Task QueryAsync_CurrentValueQuestion_ReturnsGroundedMetric_AndPersistsHistory()
    {
        var factory = CreateFactory(nameof(QueryAsync_CurrentValueQuestion_ReturnsGroundedMetric_AndPersistsHistory));
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.Database.EnsureDeleted();
            seedDb.Database.EnsureCreated();
            SeedInstitutionEnvironment(seedDb, "Alpha Bank", "alpha-bank", 18.5m);
            await seedDb.SaveChangesAsync();
        }

        var sut = CreateSut(factory);
        var tenantId = await ResolveTenantIdAsync(factory, "alpha-bank");

        var response = await sut.QueryAsync(new ComplianceIqQueryRequest
        {
            Query = "What is our current CAR?",
            TenantId = tenantId,
            UserId = "maker.user",
            UserRole = "Admin",
            IsRegulatorContext = false
        });

        response.IsError.Should().BeFalse();
        response.IntentCode.Should().Be("CURRENT_VALUE");
        response.Rows.Should().ContainSingle();
        Convert.ToDecimal(response.Rows[0]["value"]).Should().Be(18.5m);
        response.Answer.Should().Contain("18.50%");
        response.Citations.Should().NotBeEmpty();

        var history = await sut.GetQueryHistoryAsync(tenantId, "maker.user");
        history.Should().ContainSingle();
        history[0].IntentCode.Should().Be("CURRENT_VALUE");
    }

    [Fact]
    public async Task QueryAsync_RegulatorAggregateQuestion_ReturnsSectorAggregate()
    {
        var factory = CreateFactory(nameof(QueryAsync_RegulatorAggregateQuestion_ReturnsSectorAggregate));
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.Database.EnsureDeleted();
            seedDb.Database.EnsureCreated();

            SeedInstitutionEnvironment(seedDb, "Access Example", "access-example", 18m);
            SeedInstitutionEnvironment(seedDb, "GT Example", "gt-example", 16m);

            var regulator = Tenant.Create("CBN Regulator", "cbn-regulator", TenantType.Regulator, "cbn@example.com");
            regulator.Activate();
            seedDb.Tenants.Add(regulator);

            await seedDb.SaveChangesAsync();
        }

        var sut = CreateSut(factory);
        var regulatorTenantId = await ResolveTenantIdAsync(factory, "cbn-regulator");

        var response = await sut.QueryAsync(new ComplianceIqQueryRequest
        {
            Query = "What is aggregate CAR across commercial banks?",
            TenantId = regulatorTenantId,
            UserId = "regulator.user",
            UserRole = "Regulator",
            IsRegulatorContext = true,
            RegulatorCode = "CBN"
        });

        response.IsError.Should().BeFalse();
        response.IntentCode.Should().Be("SECTOR_AGGREGATE");
        response.Rows.Should().ContainSingle();
        Convert.ToInt32(response.Rows[0]["entity_count"]).Should().Be(2);
        Convert.ToDecimal(response.Rows[0]["sector_average"]).Should().Be(17m);
        response.Answer.Should().Contain("sector average");
    }

    [Fact]
    public async Task GetQuickQuestionsAsync_FiltersByContext()
    {
        var factory = CreateFactory(nameof(GetQuickQuestionsAsync_FiltersByContext));
        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.Database.EnsureDeleted();
            seedDb.Database.EnsureCreated();
        }

        var sut = CreateSut(factory);

        var institutionQuestions = await sut.GetQuickQuestionsAsync(false);
        var regulatorQuestions = await sut.GetQuickQuestionsAsync(true);

        institutionQuestions.Should().NotBeEmpty();
        institutionQuestions.Should().OnlyContain(x => !x.RequiresRegulatorContext);
        regulatorQuestions.Should().NotBeEmpty();
        regulatorQuestions.Should().OnlyContain(x => x.RequiresRegulatorContext);
    }

    [Fact]
    public async Task ExportConversationPdfAsync_GeneratesReadableConversationPdf()
    {
        var factory = CreateFactory(nameof(ExportConversationPdfAsync_GeneratesReadableConversationPdf));
        Guid tenantId;

        await using (var seedDb = await factory.CreateDbContextAsync())
        {
            seedDb.Database.EnsureDeleted();
            seedDb.Database.EnsureCreated();
            tenantId = SeedInstitutionEnvironment(seedDb, "Beta Bank", "beta-bank", 17.25m);
            await seedDb.SaveChangesAsync();
        }

        var sut = CreateSut(factory);
        var response = await sut.QueryAsync(new ComplianceIqQueryRequest
        {
            Query = "What is our current CAR?",
            TenantId = tenantId,
            UserId = "checker.user",
            UserRole = "Checker",
            IsRegulatorContext = false
        });

        var pdf = await sut.ExportConversationPdfAsync(response.ConversationId, tenantId);

        pdf.Should().NotBeEmpty();
        using var document = PdfDocument.Open(pdf);
        document.GetPages().Select(x => x.Text).Should().Contain(text =>
            text.Contains("What is our current CAR?", StringComparison.Ordinal));
    }

    private static ComplianceIqService CreateSut(IDbContextFactory<MetadataDbContext> factory)
    {
        var auditLogger = new Mock<IAuditLogger>();
        auditLogger.Setup(x => x.Log(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<object?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var complianceHealth = new Mock<IComplianceHealthService>();
        complianceHealth.Setup(x => x.GetCurrentScore(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComplianceHealthScore
            {
                TenantId = Guid.NewGuid(),
                TenantName = "Test Tenant",
                OverallScore = 82m,
                Rating = ChsRating.A,
                Trend = ChsTrend.Stable,
                FilingTimeliness = 80m,
                DataQuality = 84m,
                RegulatoryCapital = 81m,
                AuditGovernance = 83m,
                Engagement = 79m,
                PeriodLabel = "2026-Q1"
            });

        var anomalyService = new Mock<IAnomalyDetectionService>();
        anomalyService.Setup(x => x.GetReportsForTenantAsync(
                It.IsAny<Guid>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AnomalyReport>());

        return new ComplianceIqService(
            factory,
            auditLogger.Object,
            complianceHealth.Object,
            anomalyService.Object,
            NullLogger<ComplianceIqService>.Instance);
    }

    private static TestDbContextFactory CreateFactory(string databaseName)
    {
        var root = new InMemoryDatabaseRoot();
        var options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        return new TestDbContextFactory(options);
    }

    private static Guid SeedInstitutionEnvironment(
        MetadataDbContext db,
        string tenantName,
        string tenantSlug,
        decimal carRatio)
    {
        var module = db.Modules.Local.FirstOrDefault(x => x.ModuleCode == "CBN_PRUDENTIAL")
            ?? db.Modules.FirstOrDefault(x => x.ModuleCode == "CBN_PRUDENTIAL");
        if (module is null)
        {
            module = new Module
            {
                Id = 100,
                ModuleCode = "CBN_PRUDENTIAL",
                ModuleName = "CBN Prudential Return",
                RegulatorCode = "CBN",
                DefaultFrequency = "Quarterly",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Modules.Add(module);
        }

        var licenceType = db.LicenceTypes.Local.FirstOrDefault(x => x.Code == "COMMERCIAL_BANK")
            ?? db.LicenceTypes.FirstOrDefault(x => x.Code == "COMMERCIAL_BANK");
        if (licenceType is null)
        {
            licenceType = new LicenceType
            {
                Id = 700,
                Code = "COMMERCIAL_BANK",
                Name = "Commercial Bank",
                Regulator = "CBN",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };
            db.LicenceTypes.Add(licenceType);
        }

        var tenant = Tenant.Create(tenantName, tenantSlug, TenantType.Institution, $"{tenantSlug}@example.com");
        tenant.Activate();
        db.Tenants.Add(tenant);

        var institution = new Institution
        {
            Id = NextInstitutionId(db),
            TenantId = tenant.TenantId,
            InstitutionCode = tenantSlug.ToUpperInvariant(),
            InstitutionName = tenantName,
            LicenseType = "COMMERCIAL_BANK",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Institutions.Add(institution);

        db.TenantLicenceTypes.Add(new TenantLicenceType
        {
            Id = NextTenantLicenceId(db),
            TenantId = tenant.TenantId,
            LicenceTypeId = licenceType.Id,
            EffectiveDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true
        });

        var period = new ReturnPeriod
        {
            Id = NextReturnPeriodId(db),
            TenantId = tenant.TenantId,
            ModuleId = module.Id,
            Module = module,
            Year = 2026,
            Month = 3,
            Quarter = 1,
            Frequency = "Quarterly",
            ReportingDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            DeadlineDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            IsOpen = true,
            Status = "Completed",
            CreatedAt = DateTime.UtcNow
        };
        db.ReturnPeriods.Add(period);

        var submission = new Submission
        {
            Id = NextSubmissionId(db),
            TenantId = tenant.TenantId,
            InstitutionId = institution.Id,
            Institution = institution,
            ReturnPeriodId = period.Id,
            ReturnPeriod = period,
            ReturnCode = $"{tenantSlug.ToUpperInvariant()}-2026Q1",
            Status = SubmissionStatus.Accepted,
            SubmittedAt = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
            ParsedDataJson = BuildSubmissionJson(carRatio)
        };
        db.Submissions.Add(submission);

        return tenant.TenantId;
    }

    private static async Task<Guid> ResolveTenantIdAsync(
        IDbContextFactory<MetadataDbContext> factory,
        string slug)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Tenants
            .AsNoTracking()
            .Where(x => x.TenantSlug == slug)
            .Select(x => x.TenantId)
            .SingleAsync();
    }

    private static string BuildSubmissionJson(decimal carRatio)
    {
        var payload = new
        {
            Rows = new[]
            {
                new
                {
                    Fields = new Dictionary<string, object?>
                    {
                        ["CAR_RATIO"] = carRatio,
                        ["NPL_RATIO"] = 4.1m,
                        ["LIQUIDITY_RATIO"] = 35m,
                        ["TOTAL_ASSETS"] = 1_500_000_000m
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static int NextInstitutionId(MetadataDbContext db) =>
        NextId(db.Institutions.Local.Select(x => x.Id), db.Institutions.Select(x => (int?)x.Id).Max());

    private static int NextReturnPeriodId(MetadataDbContext db) =>
        NextId(db.ReturnPeriods.Local.Select(x => x.Id), db.ReturnPeriods.Select(x => (int?)x.Id).Max());

    private static int NextSubmissionId(MetadataDbContext db) =>
        NextId(db.Submissions.Local.Select(x => x.Id), db.Submissions.Select(x => (int?)x.Id).Max());

    private static int NextTenantLicenceId(MetadataDbContext db) =>
        NextId(db.TenantLicenceTypes.Local.Select(x => x.Id), db.TenantLicenceTypes.Select(x => (int?)x.Id).Max());

    private static int NextId(IEnumerable<int> localIds, int? storeMax)
    {
        var localMax = localIds.DefaultIfEmpty(0).Max();
        return Math.Max(localMax, storeMax ?? 0) + 1;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<MetadataDbContext>
    {
        private readonly DbContextOptions<MetadataDbContext> _options;

        public TestDbContextFactory(DbContextOptions<MetadataDbContext> options)
        {
            _options = options;
        }

        public MetadataDbContext CreateDbContext()
        {
            return new MetadataDbContext(_options);
        }

        public Task<MetadataDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
