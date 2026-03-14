using System.Data;
using System.Net;
using System.Security.Claims;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.MsSql;
using Xunit;
using SubmissionEntity = FC.Engine.Domain.Entities.Submission;

namespace FC.Engine.Integration.Tests.RegulatorIQ;

[CollectionDefinition("RegulatorIqIntegration")]
public sealed class RegulatorIqIntegrationCollection : ICollectionFixture<RegulatorIqFixture>;

[Collection("RegulatorIqIntegration")]
public sealed class RegulatorDataServiceIntegrationTests : IClassFixture<RegulatorIqFixture>
{
    private readonly RegulatorIqFixture _fixture;

    public RegulatorDataServiceIntegrationTests(RegulatorIqFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ResolveEntityByName_ExactAlias_ResolvesTenantAndBackfillsAlias()
    {
        var service = _fixture.CreateService();

        var resolved = await service.ResolveEntityByName("Access");

        resolved.Should().Be(_fixture.AccessBankTenantId);

        await using var db = _fixture.CreateDbContext();
        var alias = await db.RegIqEntityAliases
            .AsNoTracking()
            .FirstAsync(x => x.CanonicalName == "Access Bank Plc" && x.NormalizedAlias == "access");

        alias.TenantId.Should().Be(_fixture.AccessBankTenantId);
    }

    [Fact]
    public async Task SearchEntities_FuzzyTypo_ReturnsExpectedInstitution()
    {
        var service = _fixture.CreateService();

        var results = await service.SearchEntities("Acess Bank");

        results.Should().Contain(x => x.TenantId == _fixture.AccessBankTenantId && x.Name == "Access Bank Plc");
    }

    [Fact]
    public async Task ExecuteRegulatorQuery_CrossTenantRanking_WritesAccessAndAuditLogs()
    {
        var service = _fixture.CreateService();

        var rows = await service.ExecuteRegulatorQuery(
            "CHS_RANKING_LATEST",
            new Dictionary<string, object>
            {
                ["LicenceCategory"] = "DMB",
                ["Limit"] = 10
            },
            "examiner-001",
            "CBN");

        rows.Should().HaveCountGreaterOrEqualTo(2);
        var returnedTenantIds = rows
            .Select(x => x.TryGetValue("tenant_id", out var value) && value is Guid guid ? guid : Guid.Empty)
            .Where(x => x != Guid.Empty)
            .ToList();

        returnedTenantIds.Should().Contain(_fixture.AccessBankTenantId);
        returnedTenantIds.Should().Contain(_fixture.ZenithBankTenantId);

        await using var db = _fixture.CreateDbContext();
        var accessLog = await db.RegIqAccessLogs
            .OrderByDescending(x => x.Id)
            .FirstAsync();

        accessLog.RegulatorTenantId.Should().Be(_fixture.CbnRegulatorTenantId);
        accessLog.RegulatorAgency.Should().Be("CBN");
        accessLog.ClassificationLevel.Should().Be("RESTRICTED");
        accessLog.EntitiesAccessedJson.Should().Contain(_fixture.AccessBankTenantId.ToString());
        accessLog.EntitiesAccessedJson.Should().Contain(_fixture.ZenithBankTenantId.ToString());

        var auditLog = await db.AuditLog
            .OrderByDescending(x => x.Id)
            .FirstAsync(x => x.Action == "REGIQ_ACCESS");

        auditLog.TenantId.Should().Be(_fixture.CbnRegulatorTenantId);
        auditLog.EntityType.Should().Be("RegIqAccessLog");
        auditLog.Action.Should().Be("REGIQ_ACCESS");
    }

    [Fact]
    public async Task ExecuteRegulatorQuery_NonReadOnlyTemplate_IsRejected()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            db.RegIqQueryTemplates.Add(new RegIqQueryTemplate
            {
                IntentCode = "HELP",
                TemplateCode = "DANGEROUS_TEMPLATE_TEST",
                DisplayName = "Dangerous Template",
                Description = "Should be rejected by read-only guard.",
                SqlTemplate = "DELETE FROM dbo.institutions",
                ParameterSchema = "{}",
                ResultFormat = "TABLE",
                VisualizationType = "table",
                Scope = "HELP",
                ClassificationLevel = "UNCLASSIFIED",
                DataSourcesJson = "[]",
                CrossTenantEnabled = true,
                RequiresEntityContext = false,
                IsActive = true,
                SortOrder = 999,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }

        var service = _fixture.CreateService();

        var act = () => service.ExecuteRegulatorQuery(
            "DANGEROUS_TEMPLATE_TEST",
            new Dictionary<string, object>(),
            "examiner-001",
            "CBN");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SELECT or WITH*");
    }
}

public sealed class RegulatorIqFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("RegIq_T3st_Pass!")
        .Build();

    private DbContextOptions<MetadataDbContext> _options = null!;

    public Guid CbnRegulatorTenantId { get; private set; }
    public Guid AccessBankTenantId { get; private set; }
    public Guid ZenithBankTenantId { get; private set; }
    public Guid GtBankTenantId { get; private set; }
    public Guid FirstBankTenantId { get; private set; }
    public Guid FcmbTenantId { get; private set; }
    public string ConnectionString => _sql.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        _options = new DbContextOptionsBuilder<MetadataDbContext>()
            .UseSqlServer(_sql.GetConnectionString())
            .Options;

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await SeedBaseDataAsync(db, this);
    }

    public async Task DisposeAsync()
    {
        await _sql.DisposeAsync();
    }

    public MetadataDbContext CreateDbContext() => new(_options);

    public RegulatorIntelligenceService CreateIntelligenceService()
    {
        var db = CreateDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var connectionFactory = new TestSqlConnectionFactory(ConnectionString);
        var anomalyTraining = new NoopAnomalyModelTrainingService();
        var auditLogger = new NoopAuditLogger();
        var complianceHealth = new ComplianceHealthService(db, cache, NullLogger<ComplianceHealthService>.Instance);
        var anomaly = new AnomalyDetectionService(db, anomalyTraining, auditLogger, NullLogger<AnomalyDetectionService>.Instance);
        var foreSight = new ForeSightService(db, connectionFactory, cache, NullLogger<ForeSightService>.Instance);
        var earlyWarning = new EarlyWarningService(db);
        var systemicRisk = new SystemicRiskService(db, earlyWarning, NullLogger<SystemicRiskService>.Instance);
        var heatmap = new HeatmapQueryService(connectionFactory);
        var sectorAnalytics = new SectorAnalyticsService(db, NullLogger<SectorAnalyticsService>.Instance);

        return new RegulatorIntelligenceService(
            new TestMetadataDbContextFactory(_options),
            connectionFactory,
            complianceHealth,
            anomaly,
            foreSight,
            earlyWarning,
            systemicRisk,
            heatmap,
            sectorAnalytics,
            NullLogger<RegulatorIntelligenceService>.Instance);
    }

    public RegulatorIntentClassifier CreateIntentClassifier(ILlmService? llmService = null)
    {
        return new RegulatorIntentClassifier(
            new TestMetadataDbContextFactory(_options),
            llmService ?? new NoopLlmService(),
            CreateIntelligenceService(),
            NullLogger<RegulatorIntentClassifier>.Instance);
    }

    public RegulatorResponseGenerator CreateResponseGenerator(ILlmService? llmService = null)
    {
        var db = CreateDbContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var connectionFactory = new TestSqlConnectionFactory(ConnectionString);
        var anomalyTraining = new NoopAnomalyModelTrainingService();
        var auditLogger = new NoopAuditLogger();
        var complianceHealth = new ComplianceHealthService(db, cache, NullLogger<ComplianceHealthService>.Instance);
        var anomaly = new AnomalyDetectionService(db, anomalyTraining, auditLogger, NullLogger<AnomalyDetectionService>.Instance);
        var foreSight = new ForeSightService(db, connectionFactory, cache, NullLogger<ForeSightService>.Instance);
        var earlyWarning = new EarlyWarningService(db);
        var systemicRisk = new SystemicRiskService(db, earlyWarning, NullLogger<SystemicRiskService>.Instance);
        var heatmap = new HeatmapQueryService(connectionFactory);
        var sectorAnalytics = new SectorAnalyticsService(db, NullLogger<SectorAnalyticsService>.Instance);
        var intelligence = new RegulatorIntelligenceService(
            new TestMetadataDbContextFactory(_options),
            connectionFactory,
            complianceHealth,
            anomaly,
            foreSight,
            earlyWarning,
            systemicRisk,
            heatmap,
            sectorAnalytics,
            NullLogger<RegulatorIntelligenceService>.Instance);

        return new RegulatorResponseGenerator(
            new TestMetadataDbContextFactory(_options),
            intelligence,
            complianceHealth,
            anomaly,
            foreSight,
            earlyWarning,
            systemicRisk,
            sectorAnalytics,
            new StubStressTestService(),
            new StubPanAfricanDashboardService(),
            new StubPolicyScenarioService(),
            llmService ?? new NoopLlmService(),
            NullLogger<RegulatorResponseGenerator>.Instance);
    }

    public RegulatorIqService CreateOrchestrator(
        Guid? currentTenantId = null,
        string regulatorCode = "CBN",
        string regulatorId = "examiner-001",
        string role = "Examiner",
        ILlmService? llmService = null)
    {
        var tenantContext = new TestTenantContext(currentTenantId ?? CbnRegulatorTenantId);
        var httpContext = BuildHttpContext(regulatorCode, regulatorId, role);
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var factory = new TestMetadataDbContextFactory(_options);
        var effectiveLlm = llmService ?? new NoopLlmService();
        var intelligence = CreateIntelligenceService();
        var classifier = new RegulatorIntentClassifier(
            factory,
            effectiveLlm,
            intelligence,
            NullLogger<RegulatorIntentClassifier>.Instance);
        var responseGenerator = CreateResponseGenerator(effectiveLlm);
        var auditLogger = new AuditLogger(CreateDbContext(), tenantContext);

        return new RegulatorIqService(
            factory,
            auditLogger,
            classifier,
            responseGenerator,
            intelligence,
            effectiveLlm,
            tenantContext,
            accessor,
            NullLogger<RegulatorIqService>.Instance);
    }

    public RegulatorDataService CreateService(
        Guid? currentTenantId = null,
        string regulatorCode = "CBN",
        string regulatorId = "examiner-001",
        string role = "Examiner")
    {
        var tenantContext = new TestTenantContext(currentTenantId ?? CbnRegulatorTenantId);
        var factory = new TestMetadataDbContextFactory(_options);
        var httpContext = BuildHttpContext(regulatorCode, regulatorId, role);
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        return new RegulatorDataService(factory, tenantContext, accessor, NullLogger<RegulatorDataService>.Instance);
    }

    private DefaultHttpContext BuildHttpContext(string regulatorCode, string regulatorId, string role)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "regiq-test-session"
        };

        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        httpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, regulatorId),
                    new Claim("TenantId", CbnRegulatorTenantId.ToString("D")),
                    new Claim("RegulatorCode", regulatorCode),
                    new Claim(ClaimTypes.Role, role)
                },
                "RegIqTest"));

        return httpContext;
    }

    private static async Task SeedBaseDataAsync(MetadataDbContext db, RegulatorIqFixture fixture)
    {
        if (!await db.Jurisdictions.AnyAsync(x => x.Id == 1))
        {
            db.Jurisdictions.Add(new Jurisdiction
            {
                Id = 1,
                CountryCode = "NG",
                CountryName = "Nigeria",
                Currency = "NGN",
                Timezone = "Africa/Lagos",
                RegulatoryBodies = """["CBN","NDIC","NAICOM","SEC","NFIU"]""",
                DateFormat = "dd/MM/yyyy",
                DataProtectionLaw = "NDPA",
                DataResidencyRegion = "ng-west",
                IsActive = true
            });
        }

        if (!await db.LicenceTypes.AnyAsync(x => x.Code == "DMB"))
        {
            db.LicenceTypes.Add(new LicenceType
            {
                Code = "DMB",
                Name = "Commercial Bank",
                Regulator = "CBN",
                Description = "Deposit money bank",
                IsActive = true,
                DisplayOrder = 1,
                CreatedAt = DateTime.UtcNow
            });
        }

        var regulator = await db.Tenants.FirstOrDefaultAsync(x => x.TenantSlug == "cbn");
        if (regulator is null)
        {
            regulator = Tenant.Create("Central Bank of Nigeria", "cbn", TenantType.Regulator, "examiner@cbn.gov.ng");
            regulator.Activate();
            db.Tenants.Add(regulator);
        }
        fixture.CbnRegulatorTenantId = regulator.TenantId;

        var accessTenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantSlug == "access-bank");
        if (accessTenant is null)
        {
            accessTenant = Tenant.Create("Access Bank Plc", "access-bank", TenantType.Institution, "supervision@accessbankplc.com");
            accessTenant.Activate();
            db.Tenants.Add(accessTenant);
        }
        fixture.AccessBankTenantId = accessTenant.TenantId;

        var zenithTenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantSlug == "zenith-bank");
        if (zenithTenant is null)
        {
            zenithTenant = Tenant.Create("Zenith Bank Plc", "zenith-bank", TenantType.Institution, "supervision@zenithbank.com");
            zenithTenant.Activate();
            db.Tenants.Add(zenithTenant);
        }
        fixture.ZenithBankTenantId = zenithTenant.TenantId;

        var gtBankTenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantSlug == "gtbank");
        if (gtBankTenant is null)
        {
            gtBankTenant = Tenant.Create("Guaranty Trust Bank Plc", "gtbank", TenantType.Institution, "supervision@gtbank.com");
            gtBankTenant.Activate();
            db.Tenants.Add(gtBankTenant);
        }
        fixture.GtBankTenantId = gtBankTenant.TenantId;

        var firstBankTenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantSlug == "first-bank");
        if (firstBankTenant is null)
        {
            firstBankTenant = Tenant.Create("First Bank Nigeria Limited", "first-bank", TenantType.Institution, "supervision@firstbank.ng");
            firstBankTenant.Activate();
            db.Tenants.Add(firstBankTenant);
        }
        fixture.FirstBankTenantId = firstBankTenant.TenantId;

        var fcmbTenant = await db.Tenants.FirstOrDefaultAsync(x => x.TenantSlug == "fcmb");
        if (fcmbTenant is null)
        {
            fcmbTenant = Tenant.Create("First City Monument Bank Plc", "fcmb", TenantType.Institution, "supervision@fcmb.com");
            fcmbTenant.Activate();
            db.Tenants.Add(fcmbTenant);
        }
        fixture.FcmbTenantId = fcmbTenant.TenantId;

        await db.SaveChangesAsync();

        if (!await db.Institutions.AnyAsync(x => x.TenantId == fixture.AccessBankTenantId))
        {
            db.Institutions.Add(new Institution
            {
                TenantId = fixture.AccessBankTenantId,
                JurisdictionId = 1,
                InstitutionCode = "ACC001",
                InstitutionName = "Access Bank Plc",
                LicenseType = "DMB",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!await db.Institutions.AnyAsync(x => x.TenantId == fixture.ZenithBankTenantId))
        {
            db.Institutions.Add(new Institution
            {
                TenantId = fixture.ZenithBankTenantId,
                JurisdictionId = 1,
                InstitutionCode = "ZEN001",
                InstitutionName = "Zenith Bank Plc",
                LicenseType = "DMB",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!await db.Institutions.AnyAsync(x => x.TenantId == fixture.GtBankTenantId))
        {
            db.Institutions.Add(new Institution
            {
                TenantId = fixture.GtBankTenantId,
                JurisdictionId = 1,
                InstitutionCode = "GTB001",
                InstitutionName = "Guaranty Trust Bank Plc",
                LicenseType = "DMB",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!await db.Institutions.AnyAsync(x => x.TenantId == fixture.FirstBankTenantId))
        {
            db.Institutions.Add(new Institution
            {
                TenantId = fixture.FirstBankTenantId,
                JurisdictionId = 1,
                InstitutionCode = "FBN001",
                InstitutionName = "First Bank Nigeria Limited",
                LicenseType = "DMB",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (!await db.Institutions.AnyAsync(x => x.TenantId == fixture.FcmbTenantId))
        {
            db.Institutions.Add(new Institution
            {
                TenantId = fixture.FcmbTenantId,
                JurisdictionId = 1,
                InstitutionCode = "FCM001",
                InstitutionName = "First City Monument Bank Plc",
                LicenseType = "DMB",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var dmbId = await db.LicenceTypes.Where(x => x.Code == "DMB").Select(x => x.Id).SingleAsync();
        if (!await db.TenantLicenceTypes.AnyAsync(x => x.TenantId == fixture.AccessBankTenantId && x.LicenceTypeId == dmbId))
        {
            db.TenantLicenceTypes.Add(new TenantLicenceType
            {
                TenantId = fixture.AccessBankTenantId,
                LicenceTypeId = dmbId,
                RegistrationNumber = "CBN-DMB-ACCESS",
                EffectiveDate = new DateTime(2025, 1, 1),
                IsActive = true
            });
        }

        if (!await db.TenantLicenceTypes.AnyAsync(x => x.TenantId == fixture.ZenithBankTenantId && x.LicenceTypeId == dmbId))
        {
            db.TenantLicenceTypes.Add(new TenantLicenceType
            {
                TenantId = fixture.ZenithBankTenantId,
                LicenceTypeId = dmbId,
                RegistrationNumber = "CBN-DMB-ZENITH",
                EffectiveDate = new DateTime(2025, 1, 1),
                IsActive = true
            });
        }

        if (!await db.TenantLicenceTypes.AnyAsync(x => x.TenantId == fixture.GtBankTenantId && x.LicenceTypeId == dmbId))
        {
            db.TenantLicenceTypes.Add(new TenantLicenceType
            {
                TenantId = fixture.GtBankTenantId,
                LicenceTypeId = dmbId,
                RegistrationNumber = "CBN-DMB-GTBANK",
                EffectiveDate = new DateTime(2025, 1, 1),
                IsActive = true
            });
        }

        if (!await db.TenantLicenceTypes.AnyAsync(x => x.TenantId == fixture.FirstBankTenantId && x.LicenceTypeId == dmbId))
        {
            db.TenantLicenceTypes.Add(new TenantLicenceType
            {
                TenantId = fixture.FirstBankTenantId,
                LicenceTypeId = dmbId,
                RegistrationNumber = "CBN-DMB-FIRSTBANK",
                EffectiveDate = new DateTime(2025, 1, 1),
                IsActive = true
            });
        }

        if (!await db.TenantLicenceTypes.AnyAsync(x => x.TenantId == fixture.FcmbTenantId && x.LicenceTypeId == dmbId))
        {
            db.TenantLicenceTypes.Add(new TenantLicenceType
            {
                TenantId = fixture.FcmbTenantId,
                LicenceTypeId = dmbId,
                RegistrationNumber = "CBN-DMB-FCMB",
                EffectiveDate = new DateTime(2025, 1, 1),
                IsActive = true
            });
        }

        if (!await db.ChsScoreSnapshots.AnyAsync(x => x.TenantId == fixture.AccessBankTenantId))
        {
            db.ChsScoreSnapshots.Add(new ChsScoreSnapshot
            {
                TenantId = fixture.AccessBankTenantId,
                PeriodLabel = "2026-Q1",
                ComputedAt = new DateTime(2026, 3, 10, 8, 0, 0, DateTimeKind.Utc),
                OverallScore = 82m,
                Rating = 2,
                FilingTimeliness = 85m,
                DataQuality = 80m,
                RegulatoryCapital = 78m,
                AuditGovernance = 83m,
                Engagement = 84m
            });
        }

        if (!await db.ChsScoreSnapshots.AnyAsync(x => x.TenantId == fixture.ZenithBankTenantId))
        {
            db.ChsScoreSnapshots.Add(new ChsScoreSnapshot
            {
                TenantId = fixture.ZenithBankTenantId,
                PeriodLabel = "2026-Q1",
                ComputedAt = new DateTime(2026, 3, 10, 8, 5, 0, DateTimeKind.Utc),
                OverallScore = 76m,
                Rating = 3,
                FilingTimeliness = 79m,
                DataQuality = 74m,
                RegulatoryCapital = 77m,
                AuditGovernance = 73m,
                Engagement = 75m
            });
        }

        await db.SaveChangesAsync();
        await SeedAnalyticsDataAsync(db, fixture);
    }

    private static async Task SeedAnalyticsDataAsync(MetadataDbContext db, RegulatorIqFixture fixture)
    {
        await EnsureMissingAiTablesAsync(db);

        var module = await db.Modules.FirstOrDefaultAsync(x => x.ModuleCode == "CBN_PRUDENTIAL");
        if (module is null)
        {
            module = new Module
            {
                ModuleCode = "CBN_PRUDENTIAL",
                ModuleName = "CBN Prudential Return",
                RegulatorCode = "CBN",
                Description = "Quarterly prudential return for DMB supervision.",
                SheetCount = 1,
                DefaultFrequency = "Quarterly",
                IsActive = true,
                DisplayOrder = 1,
                DeadlineOffsetDays = 30,
                CreatedAt = DateTime.UtcNow
            };
            db.Modules.Add(module);
            await db.SaveChangesAsync();
        }

        var accessInstitution = await db.Institutions.SingleAsync(x => x.TenantId == fixture.AccessBankTenantId);
        var zenithInstitution = await db.Institutions.SingleAsync(x => x.TenantId == fixture.ZenithBankTenantId);

        var accessQ42025 = await EnsureReturnPeriodAsync(
            db,
            fixture.AccessBankTenantId,
            module.Id,
            2025,
            12,
            4,
            new DateTime(2025, 12, 31),
            new DateTime(2026, 1, 31));
        var accessQ12026 = await EnsureReturnPeriodAsync(
            db,
            fixture.AccessBankTenantId,
            module.Id,
            2026,
            3,
            1,
            new DateTime(2026, 3, 31),
            new DateTime(2026, 4, 30));
        var zenithQ42025 = await EnsureReturnPeriodAsync(
            db,
            fixture.ZenithBankTenantId,
            module.Id,
            2025,
            12,
            4,
            new DateTime(2025, 12, 31),
            new DateTime(2026, 1, 31));
        var zenithQ12026 = await EnsureReturnPeriodAsync(
            db,
            fixture.ZenithBankTenantId,
            module.Id,
            2026,
            3,
            1,
            new DateTime(2026, 3, 31),
            new DateTime(2026, 4, 30));

        await db.SaveChangesAsync();

        var accessQ4Submission = await EnsureSubmissionAsync(
            db,
            fixture.AccessBankTenantId,
            accessInstitution.Id,
            accessQ42025.Id,
            new DateTime(2026, 1, 28, 10, 0, 0, DateTimeKind.Utc),
            """{"carratio":17.2,"nplratio":4.9,"liquidityratio":34.2,"roa":1.4,"totalassets":25500000000000}""");
        var accessQ1Submission = await EnsureSubmissionAsync(
            db,
            fixture.AccessBankTenantId,
            accessInstitution.Id,
            accessQ12026.Id,
            new DateTime(2026, 4, 29, 10, 0, 0, DateTimeKind.Utc),
            """{"carratio":16.4,"nplratio":5.8,"liquidityratio":32.1,"roa":1.2,"totalassets":27000000000000}""");
        var zenithQ4Submission = await EnsureSubmissionAsync(
            db,
            fixture.ZenithBankTenantId,
            zenithInstitution.Id,
            zenithQ42025.Id,
            new DateTime(2026, 1, 24, 11, 0, 0, DateTimeKind.Utc),
            """{"carratio":18.2,"nplratio":4.5,"liquidityratio":36.8,"roa":1.6,"totalassets":30500000000000}""");
        var zenithQ1Submission = await EnsureSubmissionAsync(
            db,
            fixture.ZenithBankTenantId,
            zenithInstitution.Id,
            zenithQ12026.Id,
            new DateTime(2026, 4, 22, 11, 0, 0, DateTimeKind.Utc),
            """{"carratio":19.1,"nplratio":4.1,"liquidityratio":38.4,"roa":1.7,"totalassets":32000000000000}""");

        await db.SaveChangesAsync();

        await EnsureFilingSlaAsync(db, fixture.AccessBankTenantId, module.Id, accessQ12026.Id, accessQ1Submission.Id, accessQ12026.DeadlineDate, accessQ1Submission.SubmittedAt, false);
        await EnsureFilingSlaAsync(db, fixture.ZenithBankTenantId, module.Id, zenithQ12026.Id, zenithQ1Submission.Id, zenithQ12026.DeadlineDate, zenithQ1Submission.SubmittedAt, true);

        var anomalyModel = await db.AnomalyModelVersions
            .FirstOrDefaultAsync(x => x.ModuleCode == module.ModuleCode && x.RegulatorCode == "CBN" && x.VersionNumber == 1);
        if (anomalyModel is null)
        {
            anomalyModel = new AnomalyModelVersion
            {
                ModuleCode = module.ModuleCode,
                RegulatorCode = "CBN",
                VersionNumber = 1,
                Status = "ACTIVE",
                TrainingStartedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                TrainingCompletedAt = new DateTime(2026, 3, 1, 1, 0, 0, DateTimeKind.Utc),
                SubmissionCount = 100,
                ObservationCount = 1000,
                TenantCount = 20,
                PeriodCount = 8,
                PromotedAt = new DateTime(2026, 3, 1, 2, 0, 0, DateTimeKind.Utc),
                PromotedBy = "SYSTEM",
                Notes = "Integration test seed.",
                CreatedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            db.AnomalyModelVersions.Add(anomalyModel);
            await db.SaveChangesAsync();
        }

        await EnsureAnomalyReportAsync(db, fixture.AccessBankTenantId, accessInstitution.Id, accessInstitution.InstitutionName, accessQ1Submission.Id, module.ModuleCode, anomalyModel.Id, "2026-Q1", 68m, "RED", 2, 3, "Material cross-field and peer anomalies detected.");
        await EnsureAnomalyReportAsync(db, fixture.ZenithBankTenantId, zenithInstitution.Id, zenithInstitution.InstitutionName, zenithQ1Submission.Id, module.ModuleCode, anomalyModel.Id, "2026-Q1", 82m, "AMBER", 1, 2, "Minor peer-group deviations detected.");

        var filingRiskVersion = await EnsureForeSightModelVersionAsync(db, ForeSightModelCodes.FilingRisk, 1);
        var capitalVersion = await EnsureForeSightModelVersionAsync(db, ForeSightModelCodes.CapitalBreach, 1);
        var regActionVersion = await EnsureForeSightModelVersionAsync(db, ForeSightModelCodes.RegulatoryAction, 1);

        await EnsureForeSightPredictionAsync(db, fixture.AccessBankTenantId, filingRiskVersion.Id, ForeSightModelCodes.FilingRisk, 0.72m, "HIGH", "2026-Q1", "filingrisk", "Filing risk for Access Bank", "Late preparatory evidence and anomaly pressure.", "Accelerate regulatory reporting remediation.");
        var accessCapitalPrediction = await EnsureForeSightPredictionAsync(db, fixture.AccessBankTenantId, capitalVersion.Id, ForeSightModelCodes.CapitalBreach, 13.8m, "HIGH", "2026-Q2", "carratio", "Projected CAR", "CAR is projected to trend toward the minimum threshold.", "Escalate capital restoration review.");
        await EnsureForeSightPredictionAsync(db, fixture.AccessBankTenantId, regActionVersion.Id, ForeSightModelCodes.RegulatoryAction, 0.81m, "HIGH", "2026-Q1", "regaction", "Regulatory action risk", "Composite supervisory pressure is elevated.", "Prioritise Access Bank for closer examination.");
        await EnsureForeSightPredictionAsync(db, fixture.ZenithBankTenantId, filingRiskVersion.Id, ForeSightModelCodes.FilingRisk, 0.21m, "LOW", "2026-Q1", "filingrisk", "Filing risk for Zenith Bank", "Recent filings remain timely.", "Maintain current filing controls.");
        await EnsureForeSightPredictionAsync(db, fixture.ZenithBankTenantId, regActionVersion.Id, ForeSightModelCodes.RegulatoryAction, 0.42m, "MEDIUM", "2026-Q1", "regaction", "Regulatory action risk", "Moderate supervisory pressure from concentration metrics.", "Continue close monitoring.");

        if (!await db.ForeSightAlerts.AnyAsync(x => x.TenantId == fixture.AccessBankTenantId))
        {
            db.ForeSightAlerts.Add(new ForeSightAlert
            {
                PredictionId = accessCapitalPrediction.Id,
                TenantId = fixture.AccessBankTenantId,
                AlertType = "CAPITAL_WARNING",
                Severity = "WARNING",
                Title = "Access Bank is trending toward the CAR threshold.",
                Body = "Forecasted capital adequacy points to a narrowing buffer within the next quarter.",
                Recommendation = "Schedule supervisory engagement and request a capital action update.",
                RecipientRole = "Examiner",
                IsRead = false,
                IsDismissed = false,
                DispatchedAt = new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 3, 12, 9, 0, 0, DateTimeKind.Utc)
            });
        }

        if (!await db.SanctionsScreeningResults.AnyAsync(x => x.Subject == accessInstitution.InstitutionName))
        {
            db.SanctionsScreeningResults.Add(new SanctionsScreeningResultRecord
            {
                ScreeningKey = "regiq-access-screening",
                SortOrder = 1,
                Subject = accessInstitution.InstitutionName,
                Disposition = "POTENTIAL_MATCH",
                MatchScore = 92.4,
                MatchedName = "Access Bank Group Related Party",
                SourceCode = "OFAC",
                SourceName = "OFAC SDN",
                Category = "ENTITY",
                RiskLevel = "HIGH",
                CreatedAt = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc)
            });
        }

        await db.SaveChangesAsync();
    }

    private static Task EnsureMissingAiTablesAsync(MetadataDbContext db)
    {
        return db.Database.ExecuteSqlRawAsync(
            """
IF OBJECT_ID(N'[meta].[anomaly_model_versions]', N'U') IS NULL
BEGIN
    CREATE TABLE [meta].[anomaly_model_versions]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ModuleCode] VARCHAR(40) NOT NULL,
        [RegulatorCode] VARCHAR(10) NOT NULL,
        [VersionNumber] INT NOT NULL,
        [Status] VARCHAR(20) NOT NULL,
        [TrainingStartedAt] DATETIME2(3) NOT NULL,
        [TrainingCompletedAt] DATETIME2(3) NULL,
        [SubmissionCount] INT NOT NULL,
        [ObservationCount] INT NOT NULL,
        [TenantCount] INT NOT NULL,
        [PeriodCount] INT NOT NULL,
        [PromotedAt] DATETIME2(3) NULL,
        [PromotedBy] NVARCHAR(100) NULL,
        [RetiredAt] DATETIME2(3) NULL,
        [Notes] NVARCHAR(2000) NULL,
        [CreatedAt] DATETIME2(3) NOT NULL
    );
END;

IF OBJECT_ID(N'[meta].[anomaly_reports]', N'U') IS NULL
BEGIN
    CREATE TABLE [meta].[anomaly_reports]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [InstitutionId] INT NOT NULL,
        [InstitutionName] NVARCHAR(200) NOT NULL,
        [SubmissionId] INT NOT NULL,
        [ModuleCode] VARCHAR(40) NOT NULL,
        [RegulatorCode] VARCHAR(10) NOT NULL,
        [PeriodCode] VARCHAR(20) NOT NULL,
        [ModelVersionId] INT NOT NULL,
        [OverallQualityScore] DECIMAL(6,2) NULL,
        [TotalFieldsAnalysed] INT NOT NULL,
        [TotalFindings] INT NOT NULL,
        [AlertCount] INT NOT NULL,
        [WarningCount] INT NOT NULL,
        [InfoCount] INT NOT NULL,
        [RelationshipFindings] INT NOT NULL,
        [TemporalFindings] INT NOT NULL,
        [PeerFindings] INT NOT NULL,
        [TrafficLight] VARCHAR(10) NOT NULL,
        [NarrativeSummary] NVARCHAR(2000) NOT NULL,
        [AnalysedAt] DATETIME2(3) NOT NULL,
        [AnalysisDurationMs] INT NULL,
        [CreatedAt] DATETIME2(3) NOT NULL
    );
END;

IF OBJECT_ID(N'[meta].[anomaly_findings]', N'U') IS NULL
BEGIN
    CREATE TABLE [meta].[anomaly_findings]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [SubmissionId] INT NOT NULL,
        [AnomalyReportId] INT NOT NULL,
        [IsAcknowledged] BIT NOT NULL CONSTRAINT [DF_anomaly_findings_IsAcknowledged] DEFAULT 0,
        [CreatedAt] DATETIME2(3) NOT NULL CONSTRAINT [DF_anomaly_findings_CreatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'[meta].[foresight_model_versions]', N'U') IS NULL
BEGIN
    CREATE TABLE [meta].[foresight_model_versions]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ModelCode] VARCHAR(30) NOT NULL,
        [VersionNumber] INT NOT NULL,
        [Status] VARCHAR(20) NOT NULL,
        [TrainedAt] DATETIME2(3) NULL,
        [ObservationsCount] INT NOT NULL,
        [AccuracyMetric] DECIMAL(9,4) NULL,
        [AccuracyMetricName] VARCHAR(100) NOT NULL,
        [Notes] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIME2(3) NOT NULL
    );
END;

IF OBJECT_ID(N'[meta].[foresight_predictions]', N'U') IS NULL
BEGIN
    CREATE TABLE [meta].[foresight_predictions]
    (
        [Id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [ModelCode] VARCHAR(30) NOT NULL,
        [ModelVersionId] INT NOT NULL,
        [PredictionDate] DATE NOT NULL,
        [HorizonLabel] VARCHAR(30) NOT NULL,
        [HorizonDate] DATE NULL,
        [PredictedValue] DECIMAL(18,6) NOT NULL,
        [ConfidenceLower] DECIMAL(18,6) NULL,
        [ConfidenceUpper] DECIMAL(18,6) NULL,
        [ConfidenceScore] DECIMAL(5,4) NOT NULL,
        [RiskBand] VARCHAR(10) NOT NULL,
        [TargetModuleCode] VARCHAR(40) NOT NULL,
        [TargetPeriodCode] VARCHAR(40) NOT NULL,
        [TargetMetric] VARCHAR(60) NOT NULL,
        [TargetLabel] NVARCHAR(200) NOT NULL,
        [Explanation] NVARCHAR(2000) NOT NULL,
        [RootCauseNarrative] NVARCHAR(2000) NOT NULL,
        [Recommendation] NVARCHAR(2000) NOT NULL,
        [RootCausePillar] VARCHAR(100) NOT NULL,
        [FeatureImportanceJson] NVARCHAR(MAX) NOT NULL,
        [IsSuppressed] BIT NOT NULL CONSTRAINT [DF_foresight_predictions_IsSuppressed] DEFAULT 0,
        [SuppressionReason] NVARCHAR(500) NULL,
        [CreatedAt] DATETIME2(3) NOT NULL,
        [UpdatedAt] DATETIME2(3) NOT NULL
    );
END;

IF OBJECT_ID(N'[meta].[foresight_alerts]', N'U') IS NULL
BEGIN
    CREATE TABLE [meta].[foresight_alerts]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [PredictionId] BIGINT NOT NULL,
        [TenantId] UNIQUEIDENTIFIER NOT NULL,
        [AlertType] VARCHAR(40) NOT NULL,
        [Severity] VARCHAR(10) NOT NULL,
        [Title] NVARCHAR(200) NOT NULL,
        [Body] NVARCHAR(2000) NOT NULL,
        [Recommendation] NVARCHAR(2000) NOT NULL,
        [RecipientRole] VARCHAR(60) NOT NULL,
        [IsRead] BIT NOT NULL,
        [ReadBy] NVARCHAR(100) NULL,
        [ReadAt] DATETIME2(3) NULL,
        [IsDismissed] BIT NOT NULL,
        [DismissedBy] NVARCHAR(100) NULL,
        [DismissedAt] DATETIME2(3) NULL,
        [DispatchedAt] DATETIME2(3) NOT NULL,
        [CreatedAt] DATETIME2(3) NOT NULL
    );
END;

IF OBJECT_ID(N'[meta].[sanctions_screening_results]', N'U') IS NULL
BEGIN
    CREATE TABLE [meta].[sanctions_screening_results]
    (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [ScreeningKey] NVARCHAR(80) NOT NULL,
        [SortOrder] INT NOT NULL,
        [Subject] NVARCHAR(300) NOT NULL,
        [Disposition] NVARCHAR(50) NOT NULL,
        [MatchScore] FLOAT NOT NULL,
        [MatchedName] NVARCHAR(300) NOT NULL,
        [SourceCode] NVARCHAR(40) NOT NULL,
        [SourceName] NVARCHAR(150) NOT NULL,
        [Category] NVARCHAR(80) NOT NULL,
        [RiskLevel] NVARCHAR(40) NOT NULL,
        [CreatedAt] DATETIME2(3) NOT NULL
    );
END;
""");
    }

    private static async Task<ReturnPeriod> EnsureReturnPeriodAsync(
        MetadataDbContext db,
        Guid tenantId,
        int moduleId,
        int year,
        int month,
        int quarter,
        DateTime reportingDate,
        DateTime deadlineDate)
    {
        var period = await db.ReturnPeriods.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId
            && x.ModuleId == moduleId
            && x.Year == year
            && x.Month == month
            && x.Quarter == quarter);

        if (period is not null)
        {
            return period;
        }

        period = new ReturnPeriod
        {
            TenantId = tenantId,
            ModuleId = moduleId,
            Year = year,
            Month = month,
            Quarter = quarter,
            Frequency = "Quarterly",
            ReportingDate = reportingDate,
            IsOpen = false,
            CreatedAt = reportingDate,
            DeadlineDate = deadlineDate,
            Status = "Completed",
            NotificationLevel = 0
        };

        db.ReturnPeriods.Add(period);
        await db.SaveChangesAsync();
        return period;
    }

    private static async Task<SubmissionEntity> EnsureSubmissionAsync(
        MetadataDbContext db,
        Guid tenantId,
        int institutionId,
        int periodId,
        DateTime submittedAt,
        string parsedDataJson)
    {
        var submission = await db.Submissions.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId
            && x.InstitutionId == institutionId
            && x.ReturnPeriodId == periodId
            && x.Status == SubmissionStatus.Accepted);

        if (submission is not null)
        {
            return submission;
        }

        submission = new SubmissionEntity
        {
            TenantId = tenantId,
            InstitutionId = institutionId,
            ReturnPeriodId = periodId,
            ReturnCode = "CBN_PRUDENTIAL",
            Status = SubmissionStatus.Accepted,
            SubmittedAt = submittedAt,
            ParsedDataJson = parsedDataJson,
            CreatedAt = submittedAt,
            ApprovalRequired = false,
            IsRetentionAnonymised = false
        };

        db.Submissions.Add(submission);
        await db.SaveChangesAsync();
        return submission;
    }

    private static async Task EnsureFilingSlaAsync(
        MetadataDbContext db,
        Guid tenantId,
        int moduleId,
        int periodId,
        int submissionId,
        DateTime deadlineDate,
        DateTime submittedDate,
        bool onTime)
    {
        if (await db.FilingSlaRecords.AnyAsync(x => x.TenantId == tenantId && x.PeriodId == periodId && x.ModuleId == moduleId))
        {
            return;
        }

        db.FilingSlaRecords.Add(new FilingSlaRecord
        {
            TenantId = tenantId,
            ModuleId = moduleId,
            PeriodId = periodId,
            SubmissionId = submissionId,
            PeriodEndDate = deadlineDate.AddDays(-30),
            DeadlineDate = deadlineDate,
            SubmittedDate = submittedDate,
            DaysToDeadline = (deadlineDate.Date - submittedDate.Date).Days,
            OnTime = onTime
        });

        await db.SaveChangesAsync();
    }

    private static async Task EnsureAnomalyReportAsync(
        MetadataDbContext db,
        Guid tenantId,
        int institutionId,
        string institutionName,
        int submissionId,
        string moduleCode,
        int modelVersionId,
        string periodCode,
        decimal qualityScore,
        string trafficLight,
        int alertCount,
        int totalFindings,
        string narrativeSummary)
    {
        if (await db.AnomalyReports.AnyAsync(x => x.SubmissionId == submissionId && x.ModelVersionId == modelVersionId))
        {
            return;
        }

        db.AnomalyReports.Add(new AnomalyReport
        {
            TenantId = tenantId,
            InstitutionId = institutionId,
            InstitutionName = institutionName,
            SubmissionId = submissionId,
            ModuleCode = moduleCode,
            RegulatorCode = "CBN",
            PeriodCode = periodCode,
            ModelVersionId = modelVersionId,
            OverallQualityScore = qualityScore,
            TotalFieldsAnalysed = 20,
            TotalFindings = totalFindings,
            AlertCount = alertCount,
            WarningCount = Math.Max(0, totalFindings - alertCount),
            InfoCount = 0,
            RelationshipFindings = 1,
            TemporalFindings = 1,
            PeerFindings = 1,
            TrafficLight = trafficLight,
            NarrativeSummary = narrativeSummary,
            AnalysedAt = new DateTime(2026, 3, 11, 8, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 3, 11, 8, 0, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();
    }

    private static async Task<ForeSightModelVersion> EnsureForeSightModelVersionAsync(
        MetadataDbContext db,
        string modelCode,
        int versionNumber)
    {
        var version = await db.ForeSightModelVersions.FirstOrDefaultAsync(x => x.ModelCode == modelCode && x.VersionNumber == versionNumber);
        if (version is not null)
        {
            return version;
        }

        version = new ForeSightModelVersion
        {
            ModelCode = modelCode,
            VersionNumber = versionNumber,
            Status = "ACTIVE",
            TrainedAt = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
            ObservationsCount = 100,
            AccuracyMetric = 0.86m,
            AccuracyMetricName = "AUC",
            Notes = "Integration test seed.",
            CreatedAt = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc)
        };

        db.ForeSightModelVersions.Add(version);
        await db.SaveChangesAsync();
        return version;
    }

    private static async Task<ForeSightPrediction> EnsureForeSightPredictionAsync(
        MetadataDbContext db,
        Guid tenantId,
        int modelVersionId,
        string modelCode,
        decimal predictedValue,
        string riskBand,
        string targetPeriodCode,
        string targetMetric,
        string targetLabel,
        string explanation,
        string recommendation)
    {
        var existing = await db.ForeSightPredictions.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId
            && x.ModelCode == modelCode
            && x.TargetPeriodCode == targetPeriodCode
            && x.TargetMetric == targetMetric);

        if (existing is not null)
        {
            return existing;
        }

        var prediction = new ForeSightPrediction
        {
            TenantId = tenantId,
            ModelCode = modelCode,
            ModelVersionId = modelVersionId,
            PredictionDate = new DateTime(2026, 3, 12),
            HorizonLabel = "Next Quarter",
            HorizonDate = new DateTime(2026, 6, 30),
            PredictedValue = predictedValue,
            ConfidenceLower = predictedValue * 0.9m,
            ConfidenceUpper = predictedValue * 1.1m,
            ConfidenceScore = 0.83m,
            RiskBand = riskBand,
            TargetModuleCode = "CBN_PRUDENTIAL",
            TargetPeriodCode = targetPeriodCode,
            TargetMetric = targetMetric,
            TargetLabel = targetLabel,
            Explanation = explanation,
            RootCauseNarrative = explanation,
            Recommendation = recommendation,
            RootCausePillar = "RegulatoryCapital",
            FeatureImportanceJson = """[{"featureName":"critical_ewi_count","featureLabel":"Critical EWI Count","rawValue":2,"normalizedValue":0.7,"weight":0.26,"contributionScore":0.41,"impactDirection":"INCREASES_RISK"}]""",
            IsSuppressed = false,
            CreatedAt = new DateTime(2026, 3, 12, 6, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 12, 6, 0, 0, DateTimeKind.Utc)
        };

        db.ForeSightPredictions.Add(prediction);
        await db.SaveChangesAsync();
        return prediction;
    }
    private sealed class TestMetadataDbContextFactory : IDbContextFactory<MetadataDbContext>
    {
        private readonly DbContextOptions<MetadataDbContext> _options;

        public TestMetadataDbContextFactory(DbContextOptions<MetadataDbContext> options) => _options = options;

        public MetadataDbContext CreateDbContext() => new(_options);

        public Task<MetadataDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public TestTenantContext(Guid currentTenantId) => CurrentTenantId = currentTenantId;

        public Guid? CurrentTenantId { get; }
        public bool IsPlatformAdmin => false;
        public Guid? ImpersonatingTenantId => null;
    }

    private sealed class TestSqlConnectionFactory : IDbConnectionFactory
    {
        private readonly string _connectionString;

        public TestSqlConnectionFactory(string connectionString) => _connectionString = connectionString;

        public async Task<IDbConnection> CreateConnectionAsync(Guid? tenantId, CancellationToken ct = default)
        {
            var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);
            return connection;
        }
    }

    private sealed class NoopAuditLogger : IAuditLogger
    {
        public Task Log(string entityType, int entityId, string action, object? oldValues, object? newValues, string performedBy, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopLlmService : ILlmService
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LlmResponse
            {
                Success = false,
                ErrorMessage = "No LLM response configured for this integration test."
            });

        public Task<T> CompleteStructuredAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class =>
            throw new InvalidOperationException("No structured LLM response configured for this integration test.");
    }

    private sealed class NoopAnomalyModelTrainingService : IAnomalyModelTrainingService
    {
        public Task<AnomalyModelVersion> TrainModuleModelAsync(string moduleCode, string initiatedBy, bool promoteImmediately = false, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task PromoteModelAsync(int modelVersionId, string promotedBy, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RollbackModelAsync(string moduleCode, string rolledBackBy, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<List<AnomalyModelTrainingSummary>> GetModelHistoryAsync(string moduleCode, CancellationToken ct = default)
            => Task.FromResult(new List<AnomalyModelTrainingSummary>());
    }

    private sealed class StubStressTestService : IStressTestService
    {
        public Task<StressTestReport> RunStressTestAsync(string regulatorCode, StressTestRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new StressTestReport
            {
                ScenarioName = request.ScenarioType.ToString(),
                ScenarioType = request.ScenarioType,
                RegulatorCode = regulatorCode,
                ResilienceRating = SectorResilienceRating.Amber,
                ResilienceRationale = "Stub stress test result.",
                EntityResults = new List<StressTestEntityResult>(),
                ContagionResults = new List<StressTestContagionResult>(),
                Recommendations = new List<string> { "Maintain heightened monitoring." }
            });
        }

        public List<StressScenarioInfo> GetAvailableScenarios() =>
            new()
            {
                new StressScenarioInfo
                {
                    Type = StressScenarioType.OilPriceCollapse,
                    Name = "Oil Price Collapse",
                    Category = "Macro",
                    Description = "Stub macroeconomic oil price shock.",
                    DefaultParameters = new StressTestShockParameters
                    {
                        ScenarioName = "Oil Price Collapse",
                        Description = "Stub macroeconomic oil price shock."
                    }
                }
            };

        public Task<byte[]> GenerateReportPdfAsync(string regulatorCode, StressTestReport report, CancellationToken ct = default) =>
            Task.FromResult(Array.Empty<byte>());
    }

    private sealed class StubPanAfricanDashboardService : IPanAfricanDashboardService
    {
        public Task<GroupComplianceOverview?> GetGroupOverviewAsync(int groupId, CancellationToken ct = default) =>
            Task.FromResult<GroupComplianceOverview?>(null);

        public Task<IReadOnlyList<SubsidiaryComplianceSnapshot>> GetSubsidiarySnapshotsAsync(int groupId, string? reportingPeriod, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SubsidiaryComplianceSnapshot>>(Array.Empty<SubsidiaryComplianceSnapshot>());

        public Task<IReadOnlyList<RegulatoryDeadlineDto>> GetDeadlineCalendarAsync(int groupId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RegulatoryDeadlineDto>>(Array.Empty<RegulatoryDeadlineDto>());

        public Task<CrossBorderRiskMetrics?> GetConsolidatedRiskMetricsAsync(int groupId, string reportingPeriod, CancellationToken ct = default) =>
            Task.FromResult<CrossBorderRiskMetrics?>(null);
    }

    private sealed class StubPolicyScenarioService : IPolicyScenarioService
    {
        public Task<long> CreateScenarioAsync(int regulatorId, string title, string? description, PolicyDomain domain, string targetEntityTypes, DateOnly baselineDate, int createdByUserId, CancellationToken ct = default)
            => Task.FromResult(1L);

        public Task AddParameterAsync(long scenarioId, int regulatorId, string parameterCode, decimal proposedValue, string? applicableEntityTypes, int userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateParameterAsync(long scenarioId, int regulatorId, string parameterCode, decimal newProposedValue, int userId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<PolicyScenarioDetail> GetScenarioAsync(long scenarioId, int regulatorId, CancellationToken ct = default)
            => Task.FromResult(new PolicyScenarioDetail(
                scenarioId,
                regulatorId,
                "Stub Capital Scenario",
                "Stub scenario",
                PolicyDomain.CapitalAdequacy,
                "DMB",
                new DateOnly(2026, 3, 1),
                PolicyStatus.Draft,
                1,
                Array.Empty<PolicyParameterChange>(),
                Array.Empty<PolicyScenarioRunSummary>()));

        public Task<PagedResult<PolicyScenarioSummary>> ListScenariosAsync(int regulatorId, PolicyDomain? domain, PolicyStatus? status, int page, int pageSize, CancellationToken ct = default)
        {
            var items = new List<PolicyScenarioSummary>
            {
                new(
                    1,
                    "Stub Capital Scenario",
                    domain ?? PolicyDomain.CapitalAdequacy,
                    status ?? PolicyStatus.Draft,
                    "DMB",
                    new DateOnly(2026, 3, 1),
                    1,
                    0,
                    DateTime.UtcNow)
            };

            return Task.FromResult(new PagedResult<PolicyScenarioSummary>(items, items.Count, page, pageSize));
        }

        public Task<long> CloneScenarioAsync(long sourceScenarioId, int regulatorId, string newTitle, int userId, CancellationToken ct = default)
            => Task.FromResult(sourceScenarioId + 1);

        public Task TransitionStatusAsync(long scenarioId, int regulatorId, PolicyStatus newStatus, int userId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
