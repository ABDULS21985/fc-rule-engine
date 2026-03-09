using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.BackgroundJobs;
using FC.Engine.Infrastructure.Services;
using FC.Engine.Infrastructure.Services.CoreBanking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using StackExchange.Redis;

namespace FC.Engine.Infrastructure;

public static class CaaSServiceExtensions
{
    public static IServiceCollection AddCaaSEngine(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Azure Key Vault ──────────────────────────────────────────────
        var kvUri = configuration["KeyVault:Uri"]
            ?? throw new InvalidOperationException("KeyVault:Uri is required.");
        services.AddSingleton(new SecretClient(new Uri(kvUri), new DefaultAzureCredential()));

        // ── Redis (rate limiter) ─────────────────────────────────────────
        var redisConn = configuration["Redis:ConnectionString"]
            ?? throw new InvalidOperationException("Redis:ConnectionString is required.");
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConn));

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

        // ── Background services ──────────────────────────────────────────
        services.AddHostedService<WebhookDispatcherBackgroundService>();
        services.AddHostedService<AutoFilingSchedulerBackgroundService>();

        return services;
    }
}
