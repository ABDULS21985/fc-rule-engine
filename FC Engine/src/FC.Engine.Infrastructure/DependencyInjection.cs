using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Auth;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Caching;
using FC.Engine.Infrastructure.DynamicSchema;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Metadata.Repositories;
using FC.Engine.Infrastructure.Services;
using FC.Engine.Infrastructure.MultiTenancy;
using FC.Engine.Infrastructure.Persistence;
using FC.Engine.Infrastructure.Persistence.Interceptors;
using FC.Engine.Infrastructure.Persistence.Repositories;
using FC.Engine.Infrastructure.Storage;
using FC.Engine.Infrastructure.Validation;
using FC.Engine.Infrastructure.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FC.Engine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FcEngine")
            ?? throw new InvalidOperationException("Connection string 'FcEngine' not found");

        services.AddHttpContextAccessor();
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

        // ── Multi-Tenancy ──
        services.AddScoped<ITenantContext, HttpTenantContext>();
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

        // Dynamic SQL
        services.AddSingleton<DynamicSqlBuilder>();

        // DDL Engine
        services.AddScoped<IDdlEngine, DdlEngine>();
        services.AddScoped<IDdlMigrationExecutor, DdlMigrationExecutor>();
        services.AddSingleton<ISqlTypeMapper, SqlTypeMapper>();

        // XML
        services.AddScoped<IGenericXmlParser, GenericXmlParser>();
        services.AddScoped<IXsdGenerator, XsdGenerator>();

        // Caching — singleton so the in-memory ConcurrentDictionary lives across requests
        services.AddSingleton<ITemplateMetadataCache, TemplateMetadataCache>();
        services.AddHostedService<CacheWarmupService>();

        // ── Entitlement & Onboarding (RG-02) ──
        services.AddMemoryCache();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();
        services.AddScoped<IPermissionService, PermissionService>();

        // ── Authentication evolution (RG-05) ──
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IMfaService, MfaService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddSingleton<IMfaChallengeStore, MfaChallengeStore>();
        services.AddScoped<ITenantBrandingService, TenantBrandingService>();
        services.AddSingleton<IFileStorageService, LocalFileStorageService>();

        // Audit
        services.AddScoped<IAuditLogger, AuditLogger>();

        // Validation
        services.AddSingleton<ExpressionParser>();
        services.AddSingleton<ExpressionTokenizer>();
        services.AddScoped<IFormulaEvaluator, FormulaEvaluator>();
        services.AddScoped<ICrossSheetValidator, CrossSheetValidator>();
        services.AddScoped<IBusinessRuleEvaluator, BusinessRuleEvaluator>();

        // Billing & subscription background jobs
        services.AddHostedService<UsageTrackingJob>();
        services.AddHostedService<OverdueInvoiceJob>();

        return services;
    }
}
