using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Services;
using FC.Engine.Infrastructure.Services.CoreBanking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using StackExchange.Redis;

namespace FC.Engine.Infrastructure;

public static class CaaSServiceExtensions
{
    public static IServiceCollection AddCaaSEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var kvUri = configuration["KeyVault:Uri"];
        var redisConn = configuration["Redis:ConnectionString"];
        var caasEnabled = !string.IsNullOrEmpty(kvUri) && !string.IsNullOrEmpty(redisConn);

        // ── Azure Key Vault ──────────────────────────────────────────────
        if (!string.IsNullOrEmpty(kvUri))
        {
            services.AddSingleton(new SecretClient(new Uri(kvUri), new DefaultAzureCredential()));
        }

        // ── Redis (rate limiter) ─────────────────────────────────────────
        if (!string.IsNullOrEmpty(redisConn))
        {
            services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect(redisConn));
        }

        // ── Core CaaS services ───────────────────────────────────────────
        services.AddScoped<ICaaSService, CaaSService>();
        services.AddScoped<ICaaSApiKeyService, CaaSApiKeyService>();
        services.AddSingleton<ICaaSRateLimiter, CaaSRedisRateLimiter>();
        services.AddScoped<ICaaSWebhookDispatcher, CaaSWebhookDispatcher>();
        services.AddScoped<ICaaSAutoFilingService, CaaSAutoFilingService>();

        // ── Adapter bridge services ──────────────────────────────────────
        services.AddScoped<IValidationPipeline, CaaSValidationPipelineAdapter>();
        services.AddScoped<ITemplateEngine, CaaSTemplateEngineAdapter>();

        // ── Core banking adapters ────────────────────────────────────────
        services.AddSingleton<ICoreBankingAdapter, FinacleCoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapter, T24CoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapter, BankOneCoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapter, FlexcubeCoreBankingAdapter>();
        services.AddSingleton<ICoreBankingAdapterFactory, CoreBankingAdapterFactory>();

        // ── HTTP clients ─────────────────────────────────────────────────
        services.AddHttpClient("Webhook")
            .ConfigureHttpClient(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(10);
            })
            .AddResilienceHandler("Webhook", pipeline =>
            {
                pipeline.AddTimeout(TimeSpan.FromSeconds(10));
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType      = DelayBackoffType.Exponential,
                    Delay            = TimeSpan.FromSeconds(1),
                    UseJitter        = true
                });
            });

        // ── Background services (only when fully configured) ─────────────
        if (caasEnabled)
        {
            services.AddHostedService<WebhookDispatcherBackgroundService>();
            services.AddHostedService<AutoFilingSchedulerBackgroundService>();
        }

        return services;
    }
}
