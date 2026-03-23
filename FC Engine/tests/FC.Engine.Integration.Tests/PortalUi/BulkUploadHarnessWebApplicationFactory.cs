extern alias PortalApp;

using System.Security.Claims;
using System.Text;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Portal.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FC.Engine.Integration.Tests.PortalUi;

public sealed class BulkUploadHarnessWebApplicationFactory : WebApplicationFactory<PortalApp::Program>
{
    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public const int InstitutionId = 41;
    public const int UserId = 4201;
    public const int ModuleId = 7;
    public const int ReturnPeriodId = 202603;
    public const string ReturnCode = "CAP_BUF";
    public Uri RootUri { get; private set; } = new("http://127.0.0.1");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:FcEngine"] = "Server=127.0.0.1;Database=bulk_upload_harness;User Id=sa;Password=Pass@word1;TrustServerCertificate=True;",
                ["RabbitMQ:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                         .Where(x => x.ServiceType == typeof(IHostedService)
                            && x.ImplementationType?.Namespace?.StartsWith("FC.Engine.", StringComparison.Ordinal) == true)
                         .ToList())
            {
                services.Remove(descriptor);
            }

            var tenantContext = new HarnessTenantContext(TenantId);
            var dbOptions = new DbContextOptionsBuilder<MetadataDbContext>()
                .UseInMemoryDatabase($"bulk-upload-harness-{Guid.NewGuid():N}")
                .Options;

            SeedHarnessMetadata(dbOptions, tenantContext);

            services.RemoveAll<ITenantContext>();
            services.AddSingleton<ITenantContext>(tenantContext);

            services.RemoveAll<DbContextOptions<MetadataDbContext>>();
            services.RemoveAll<MetadataDbContext>();
            services.RemoveAll<IDbContextFactory<MetadataDbContext>>();
            services.AddSingleton(dbOptions);
            services.AddScoped(_ => new MetadataDbContext(dbOptions, tenantContext));
            services.AddScoped<IDbContextFactory<MetadataDbContext>>(_ => new HarnessMetadataDbContextFactory(dbOptions, tenantContext));

            services.RemoveAll<AuthenticationStateProvider>();
            services.AddScoped<AuthenticationStateProvider>(_ => new HarnessAuthenticationStateProvider(CreatePrincipal()));

            services.RemoveAll<ITenantBrandingService>();
            services.AddScoped<ITenantBrandingService, HarnessTenantBrandingService>();

            services.RemoveAll<IConsentService>();
            services.AddScoped<IConsentService, HarnessConsentService>();

            services.RemoveAll<ISubscriptionService>();
            services.AddScoped<ISubscriptionService, HarnessSubscriptionService>();

            services.RemoveAll<SubmissionService>();
            services.AddScoped(_ => CreateSubmissionService(tenantContext, dbOptions));

            services.RemoveAll<ITemplateDownloadService>();
            services.AddScoped<ITemplateDownloadService, HarnessTemplateDownloadService>();

            services.RemoveAll<IBulkUploadService>();
            services.AddScoped<IBulkUploadService, HarnessBulkUploadService>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webHost =>
        {
            webHost.UseKestrel();
            webHost.UseUrls("http://127.0.0.1:0");
        });

        var host = builder.Build();
        host.Start();

        var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addresses?.Addresses.Single() ?? throw new InvalidOperationException("Failed to resolve harness server address.");
        RootUri = new Uri(address);

        return host;
    }

    private static ClaimsPrincipal CreatePrincipal()
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, UserId.ToString()),
            new Claim(ClaimTypes.Name, "bulk.harness"),
            new Claim(ClaimTypes.Role, "Maker"),
            new Claim("DisplayName", "Bulk Harness"),
            new Claim("InstitutionId", InstitutionId.ToString()),
            new Claim("InstitutionName", "Harness Institution"),
            new Claim("TenantId", TenantId.ToString("D"))
        ], "Harness");

        return new ClaimsPrincipal(identity);
    }

    private static void SeedHarnessMetadata(
        DbContextOptions<MetadataDbContext> options,
        ITenantContext tenantContext)
    {
        using var db = new MetadataDbContext(options, tenantContext);
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();

        db.Modules.Add(new Module
        {
            Id = ModuleId,
            ModuleCode = "CAPITAL_SUPERVISION",
            ModuleName = "Capital Supervision",
            RegulatorCode = "CBN",
            DefaultFrequency = "Monthly",
            IsActive = true,
            SheetCount = 1,
            CreatedAt = DateTime.UtcNow
        });

        db.ReturnPeriods.Add(new ReturnPeriod
        {
            Id = ReturnPeriodId,
            TenantId = TenantId,
            ModuleId = ModuleId,
            Year = 2026,
            Month = 3,
            Frequency = "Monthly",
            ReportingDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            DeadlineDate = new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            IsOpen = true,
            Status = "Open",
            CreatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
    }

    private static SubmissionService CreateSubmissionService(
        ITenantContext tenantContext,
        DbContextOptions<MetadataDbContext> dbOptions)
    {
        var template = CreatePublishedTemplate();
        var templateRepository = new Mock<ITemplateRepository>();
        templateRepository
            .Setup(x => x.GetByModuleIds(
                It.Is<IEnumerable<int>>(ids => ids.Contains(ModuleId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([template]);

        var entitlementService = new Mock<IEntitlementService>();
        entitlementService
            .Setup(x => x.ResolveEntitlements(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantEntitlement
            {
                TenantId = TenantId,
                TenantStatus = TenantStatus.Active,
                ActiveModules =
                [
                    new EntitledModule
                    {
                        ModuleId = ModuleId,
                        ModuleCode = "CAPITAL_SUPERVISION",
                        ModuleName = "Capital Supervision",
                        RegulatorCode = "CBN",
                        DefaultFrequency = "Monthly",
                        IsActive = true,
                        SheetCount = 1
                    }
                ]
            });

        var templateCache = new Mock<ITemplateMetadataCache>();
        var templateService = new TemplateService(
            templateRepository.Object,
            Mock.Of<IAuditLogger>(),
            templateCache.Object,
            Mock.Of<ISqlTypeMapper>(),
            entitlementService.Object,
            tenantContext);

        var submissionRepository = new Mock<ISubmissionRepository>();
        submissionRepository
            .Setup(x => x.GetByInstitution(InstitutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FC.Engine.Domain.Entities.Submission>());

        var validationOrchestrator = new ValidationOrchestrator(
            templateCache.Object,
            Mock.Of<IFormulaEvaluator>(),
            Mock.Of<ICrossSheetValidator>(),
            Mock.Of<IBusinessRuleEvaluator>());

        var orchestrator = new IngestionOrchestrator(
            templateCache.Object,
            Mock.Of<IXsdGenerator>(),
            Mock.Of<IGenericXmlParser>(),
            Mock.Of<IGenericDataRepository>(),
            submissionRepository.Object,
            validationOrchestrator,
            entitlementService.Object,
            tenantContext);

        var notificationService = new NotificationService(
            Mock.Of<IPortalNotificationRepository>(),
            Mock.Of<INotificationOrchestrator>(),
            Mock.Of<IInstitutionUserRepository>(),
            Mock.Of<IInstitutionRepository>(),
            tenantContext,
            new HarnessTenantBrandingService(),
            templateCache.Object);

        return new SubmissionService(
            templateService,
            entitlementService.Object,
            tenantContext,
            submissionRepository.Object,
            Mock.Of<ISubmissionApprovalRepository>(),
            orchestrator,
            new HarnessMetadataDbContextFactory(dbOptions, tenantContext),
            notificationService,
            Mock.Of<IFilingCalendarService>(),
            templateCache.Object,
            NullLogger<SubmissionService>.Instance,
            Mock.Of<Microsoft.AspNetCore.Http.IHttpContextAccessor>());
    }

    private static ReturnTemplate CreatePublishedTemplate()
    {
        var template = new ReturnTemplate
        {
            Id = 1001,
            TenantId = TenantId,
            ModuleId = ModuleId,
            ReturnCode = ReturnCode,
            Name = "Capital Buffer Register",
            Frequency = ReturnFrequency.Monthly,
            StructuralCategory = StructuralCategory.FixedRow,
            PhysicalTableName = "cap_buf",
            XmlRootElement = ReturnCode,
            XmlNamespace = "urn:harness:cap-buf",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "harness",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "harness"
        };

        var version = template.CreateDraftVersion("harness");
        version.Id = 2001;
        version.AddField(new TemplateField
        {
            Id = 3001,
            FieldName = "amount",
            DisplayName = "Amount",
            FieldOrder = 1,
            DataType = FieldDataType.Decimal,
            MinValue = "10"
        });
        version.SubmitForReview();
        version.Publish(DateTime.UtcNow, "harness");

        return template;
    }
}

internal sealed class HarnessMetadataDbContextFactory : IDbContextFactory<MetadataDbContext>
{
    private readonly DbContextOptions<MetadataDbContext> _options;
    private readonly ITenantContext _tenantContext;

    public HarnessMetadataDbContextFactory(DbContextOptions<MetadataDbContext> options, ITenantContext tenantContext)
    {
        _options = options;
        _tenantContext = tenantContext;
    }

    public MetadataDbContext CreateDbContext()
    {
        return new MetadataDbContext(_options, _tenantContext);
    }

    public Task<MetadataDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CreateDbContext());
    }
}

internal sealed class HarnessAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly AuthenticationState _state;

    public HarnessAuthenticationStateProvider(ClaimsPrincipal principal)
    {
        _state = new AuthenticationState(principal);
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return Task.FromResult(_state);
    }
}

internal sealed class HarnessTenantContext : ITenantContext
{
    public HarnessTenantContext(Guid tenantId)
    {
        CurrentTenantId = tenantId;
    }

    public Guid? CurrentTenantId { get; }
    public bool IsPlatformAdmin => false;
    public Guid? ImpersonatingTenantId => null;
}

internal sealed class HarnessTenantBrandingService : ITenantBrandingService
{
    public Task<BrandingConfig> GetBrandingConfig(Guid tenantId, CancellationToken ct = default)
    {
        return Task.FromResult(BrandingConfig.WithDefaults(new BrandingConfig
        {
            CompanyName = "Harness Portal",
            FaviconUrl = "/favicon.svg",
            LogoSmallUrl = "/images/cbn-logo.svg"
        }));
    }

    public Task UpdateBrandingConfig(Guid tenantId, BrandingConfig config, CancellationToken ct = default) => Task.CompletedTask;
    public Task<string> UploadLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> UploadCompactLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string> UploadFavicon(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default) => throw new NotSupportedException();
    public Task InvalidateCache(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class HarnessConsentService : IConsentService
{
    public string GetCurrentPolicyVersion() => "test";
    public Task RecordConsent(ConsentCaptureRequest request, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> HasCurrentRequiredConsent(Guid tenantId, int userId, string userType, CancellationToken ct = default) => Task.FromResult(true);
    public Task<IReadOnlyList<ConsentRecord>> GetConsentHistory(Guid tenantId, int userId, string userType, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConsentRecord>>(Array.Empty<ConsentRecord>());
    public Task WithdrawConsent(Guid tenantId, int userId, string userType, ConsentType consentType, string? ipAddress, string? userAgent, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class HarnessSubscriptionService : ISubscriptionService
{
    public Task<bool> HasFeature(Guid tenantId, string featureCode, CancellationToken ct = default) => Task.FromResult(false);

    public Task<Subscription> CreateSubscription(Guid tenantId, string planCode, BillingFrequency frequency, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Subscription> CreateSubscription(Guid tenantId, string planCode, BillingFrequency frequency, object? sharedDbContext, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Subscription> UpgradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Subscription> DowngradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CancelSubscription(Guid tenantId, string reason, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SubscriptionModule> ActivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SubscriptionModule> ActivateModule(Guid tenantId, string moduleCode, object? sharedDbContext, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeactivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<ModuleAvailability>> GetAvailableModules(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Invoice> GenerateInvoice(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Invoice> IssueInvoice(int invoiceId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Payment> RecordPayment(int invoiceId, RecordPaymentRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task VoidInvoice(int invoiceId, string reason, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<UsageSummary> GetUsageSummary(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> CheckLimit(Guid tenantId, string limitType, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Subscription> GetActiveSubscription(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<Invoice>> GetInvoices(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> GetInvoiceCount(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<InvoiceStats> GetInvoiceStats(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<Payment>> GetPayments(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> GetPaymentCount(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PaymentStats> GetPaymentStats(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<List<SubscriptionPlan>> GetAvailablePlans(Guid tenantId, CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class HarnessTemplateDownloadService : ITemplateDownloadService
{
    public Task<byte[]> GenerateTemplateExcel(Guid tenantId, string returnCode, CancellationToken ct = default)
    {
        return Task.FromResult(Array.Empty<byte>());
    }

    public Task<string> GenerateTemplateCsv(Guid tenantId, string returnCode, CancellationToken ct = default)
    {
        return Task.FromResult("Amount\r\n");
    }
}

internal sealed class HarnessBulkUploadService : IBulkUploadService
{
    public Task<BulkUploadResult> ProcessExcelUpload(Stream fileStream, Guid tenantId, string returnCode, int institutionId, int returnPeriodId, int? requestedByUserId = null, CancellationToken ct = default)
    {
        return ProcessCsvUpload(fileStream, tenantId, returnCode, institutionId, returnPeriodId, requestedByUserId, ct);
    }

    public async Task<BulkUploadResult> ProcessCsvUpload(Stream fileStream, Guid tenantId, string returnCode, int institutionId, int returnPeriodId, int? requestedByUserId = null, CancellationToken ct = default)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8, leaveOpen: true);
        var content = await reader.ReadToEndAsync(ct);
        fileStream.Position = 0;

        if (!content.Contains("5", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Harness upload expected a failing CSV value of 5.");
        }

        return new BulkUploadResult
        {
            Success = false,
            SubmissionId = 501,
            Status = nameof(SubmissionStatus.Rejected),
            Message = "Upload validation failed.",
            Errors =
            [
                new BulkUploadError
                {
                    RowNumber = 2,
                    FieldCode = "amount",
                    Message = "'Amount' value 5 is below minimum 10",
                    Severity = "Error",
                    Category = BulkUploadErrorCategories.TypeRange,
                    ExpectedValue = ">= 10"
                }
            ]
        };
    }
}
