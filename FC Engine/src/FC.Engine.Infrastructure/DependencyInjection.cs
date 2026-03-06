using FC.Engine.Domain.Abstractions;
using FC.Engine.Application.Services;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Caching;
using FC.Engine.Infrastructure.Charts;
using FC.Engine.Infrastructure.DynamicSchema;
using FC.Engine.Infrastructure.Export;
using FC.Engine.Infrastructure.Export.Adapters;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Metadata.Repositories;
using FC.Engine.Infrastructure.Importing.Parsers;
using FC.Engine.Infrastructure.Services;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Persistence;
using FC.Engine.Infrastructure.Persistence.Interceptors;
using FC.Engine.Infrastructure.Persistence.Repositories;
using FC.Engine.Infrastructure.Notifications;
using FC.Engine.Infrastructure.Storage;
using FC.Engine.Infrastructure.Validation;
using FC.Engine.Infrastructure.Webhooks;
using FC.Engine.Infrastructure.Xml;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FcEngine")
            ?? throw new InvalidOperationException("Connection string 'FcEngine' not found");

        services.AddHttpContextAccessor();
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<PrivacyComplianceOptions>(configuration.GetSection(PrivacyComplianceOptions.SectionName));

        // ── Multi-Tenancy ──
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddScoped<IDataResidencyRouter, DataResidencyRouter>();
        services.AddScoped<IDbConnectionFactory, TenantAwareConnectionFactory>();
        services.AddScoped<TenantSessionContextInterceptor>();

        // EF Core for metadata + operational tables
        // Using AddDbContext (not pool) to support per-request interceptor injection
        services.AddDbContext<MetadataDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.CommandTimeout(30);
                sql.EnableRetryOnFailure(3);
            });

            // Add tenant session context interceptor for RLS
            var interceptor = sp.GetRequiredService<TenantSessionContextInterceptor>();
            options.AddInterceptors(interceptor);
        });

        // Repositories
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IFormulaRepository, FormulaRepository>();
        services.AddScoped<ISubmissionRepository, SubmissionRepository>();
        services.AddScoped<IGenericDataRepository, GenericDataRepository>();
        services.AddScoped<IPortalUserRepository, PortalUserRepository>();
        services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IInstitutionUserRepository, InstitutionUserRepository>();
        services.AddScoped<IInstitutionRepository, InstitutionRepository>();
        services.AddScoped<ISubmissionApprovalRepository, SubmissionApprovalRepository>();
        services.AddScoped<IPortalNotificationRepository, PortalNotificationRepository>();
        services.AddScoped<IExportRequestRepository, ExportRequestRepository>();
        services.AddScoped<IEmailTemplateRepository, EmailTemplateRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<INotificationDeliveryRepository, NotificationDeliveryRepository>();
        services.AddScoped<IFieldLocalisationService, FieldLocalisationService>();
        services.AddScoped<IUserLanguagePreferenceService, UserLanguagePreferenceService>();
        services.AddScoped<IJurisdictionConsolidationService, JurisdictionConsolidationService>();
        services.AddScoped<IFeatureFlagService, FeatureFlagService>();

        // Dynamic SQL
        services.AddSingleton<DynamicSqlBuilder>();

        // DDL Engine
        services.AddScoped<IDdlEngine, DdlEngine>();
        services.AddScoped<IDdlMigrationExecutor, DdlMigrationExecutor>();
        services.AddSingleton<ISqlTypeMapper, SqlTypeMapper>();

        // XML
        services.AddScoped<IGenericXmlParser, GenericXmlParser>();
        services.AddScoped<IXsdGenerator, XsdGenerator>();
        services.AddScoped<ITemplateDownloadService, TemplateDownloadService>();
        services.AddScoped<IBulkUploadService, BulkUploadService>();
        services.AddScoped<ICarryForwardService, CarryForwardService>();
        services.AddScoped<IReturnLockService, ReturnLockService>();
        services.AddScoped<IDataFeedService, DataFeedService>();
        services.AddScoped<IDraftDataService, DraftDataService>();
        services.AddScoped<IConsentService, ConsentService>();
        services.AddScoped<IDsarService, DsarService>();
        services.AddScoped<IDataBreachService, DataBreachService>();
        services.AddScoped<IPrivacyDashboardService, PrivacyDashboardService>();
        services.AddScoped<IHistoricalMigrationService, HistoricalMigrationService>();
        services.AddScoped<IFileParser, ExcelFileParser>();
        services.AddScoped<IFileParser, CsvFileParser>();
        services.AddScoped<IFileParser, PdfTableFileParser>();
        services.AddScoped<IExportEngine, ExportEngine>();
        services.AddScoped<IExportGenerator, ExcelExportGenerator>();
        services.AddScoped<IExportGenerator, PdfExportGenerator>();
        services.AddScoped<IExportGenerator, XmlExportGenerator>();
        services.AddScoped<IExportGenerator, XbrlExportGenerator>();
        services.AddScoped<XmlExportCoverageValidator>();

        // Regulator adapters
        services.AddScoped<IRegulatorSubmissionAdapter, CbnSubmissionAdapter>();
        services.AddScoped<IRegulatorSubmissionAdapter, NdicSubmissionAdapter>();
        services.AddScoped<IRegulatorSubmissionAdapter, SecSubmissionAdapter>();
        services.AddScoped<IRegulatorSubmissionAdapter, NaicomSubmissionAdapter>();
        services.AddScoped<IRegulatorSubmissionAdapter, PencomSubmissionAdapter>();
        services.AddScoped<IRegulatorSubmissionAdapter, NfiuSubmissionAdapter>();
        services.AddScoped<IRegulatorSubmissionAdapter, InternalSubmissionAdapter>();

        // Caching — singleton so the in-memory ConcurrentDictionary lives across requests
        services.AddSingleton<ITemplateMetadataCache, TemplateMetadataCache>();
        services.AddHostedService<CacheWarmupService>();

        // ── Rate Limiting (RG-15) ──
        services.AddSingleton<IRateLimitResolver, RateLimitResolver>();

        // ── Entitlement & Onboarding (RG-02) ──
        services.AddMemoryCache();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();
        services.AddScoped<IPartnerManagementService, PartnerManagementService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IModuleImportService, ModuleImportService>();
        services.AddScoped<IInterModuleDataFlowEngine, InterModuleDataFlowEngine>();

        // ── Authentication evolution (RG-05) ──
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IMfaService, MfaService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddSingleton<IMfaChallengeStore, MfaChallengeStore>();
        services.AddScoped<ITenantBrandingService, TenantBrandingService>();
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();

        // ── Notification evolution (RG-06) ──
        services.Configure<NotificationSettings>(configuration.GetSection(NotificationSettings.SectionName));
        services.AddScoped<INotificationOrchestrator, NotificationOrchestrator>();
        services.AddScoped<INotificationPusher, SignalRNotificationPusher>();

        services.AddHttpClient<AfricasTalkingSmsSender>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<NotificationSettings>>().Value;
            var baseUrl = options.Sms.AfricasTalking.BaseUrl;
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/'));
            }
        });

        var notificationOptions = configuration.GetSection(NotificationSettings.SectionName).Get<NotificationSettings>()
            ?? new NotificationSettings();

        if (string.Equals(notificationOptions.Email.Provider, "SendGrid", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailSender, SendGridEmailSender>();
        }
        else if (string.Equals(notificationOptions.Email.Provider, "AwsSes", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IEmailSender, AwsSesEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, NoopEmailSender>();
        }

        if (string.Equals(notificationOptions.Sms.Provider, "AfricasTalking", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<ISmsSender>(sp => sp.GetRequiredService<AfricasTalkingSmsSender>());
        }
        else
        {
            services.AddScoped<ISmsSender, NoopSmsSender>();
        }

        // Audit & Evidence (RG-14)
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IEvidencePackageService, EvidencePackageService>();
        services.AddScoped<IReturnTimelineService, ReturnTimelineService>();

        // Validation
        services.AddSingleton<ExpressionParser>();
        services.AddSingleton<ExpressionTokenizer>();
        services.AddScoped<IFormulaEvaluator, FormulaEvaluator>();
        services.AddScoped<ICrossSheetValidator, CrossSheetValidator>();
        services.AddScoped<IBusinessRuleEvaluator, BusinessRuleEvaluator>();

        // ── Filing Calendar (RG-12) ──
        services.AddScoped<DeadlineComputationService>();
        services.AddScoped<IFilingCalendarService, FilingCalendarService>();

        // ── Dashboard & Analytics (RG-17) ──
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IBenchmarkingService, BenchmarkingService>();
        services.AddScoped<ChartJsInterop>();
        services.AddScoped<IRegulatorInboxService, RegulatorInboxService>();
        services.AddScoped<ISectorAnalyticsService, SectorAnalyticsService>();
        services.AddScoped<IEntityBenchmarkingService, EntityBenchmarkingService>();
        services.AddScoped<IEarlyWarningService, EarlyWarningService>();
        services.AddScoped<IExaminationWorkspaceService, ExaminationWorkspaceService>();

        // ── Report Builder (RG-18) ──
        services.AddScoped<ISavedReportRepository, SavedReportRepository>();
        services.AddScoped<IReportQueryEngine, ReportQueryEngine>();
        services.AddScoped<IBoardPackGenerator, BoardPackGenerator>();

        // ── Webhook Engine & Event Bus (RG-30) ──
        services.AddScoped<IWebhookService, WebhookDeliveryService>();
        services.AddScoped<IDomainEventPublisher, MassTransitDomainEventPublisher>();
        services.AddHostedService<WebhookRetryJob>();
        services.AddHttpClient("WebhookClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent", "RegOS-Webhook/1.0");
        });

        var rabbitMqEnabled = configuration.GetValue<bool>("RabbitMQ:Enabled");
        services.AddMassTransit(x =>
        {
            x.AddConsumer<WebhookDeliveryConsumer>();

            if (rabbitMqEnabled)
            {
                x.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(
                        configuration["RabbitMQ:Host"] ?? "localhost",
                        configuration["RabbitMQ:VirtualHost"] ?? "/",
                        h =>
                        {
                            h.Username(configuration["RabbitMQ:Username"] ?? "guest");
                            h.Password(configuration["RabbitMQ:Password"] ?? "guest");
                        });
                    cfg.ConfigureEndpoints(context);
                });
            }
            else
            {
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
            }
        });

        // Billing & subscription background jobs
        services.AddHostedService<UsageTrackingJob>();
        services.AddHostedService<OverdueInvoiceJob>();
        services.AddHostedService<NotificationRetryJob>();
        services.AddHostedService<FilingCalendarJob>();
        services.AddHostedService<ExportProcessingJob>();
        services.AddHostedService<ExportCleanupJob>();
        services.AddHostedService<AuditIntegrityVerificationJob>();
        services.AddHostedService<DataBreachEscalationJob>();
        services.AddHostedService<RetentionEnforcementJob>();
        services.AddHostedService<ScheduledReportJob>();

        return services;
    }
}
