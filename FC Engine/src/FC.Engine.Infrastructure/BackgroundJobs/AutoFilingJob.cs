using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

/// <summary>
/// Background job for webhook-driven auto-filing.
/// Checks CaasAutoFilingConfig table for tenant schedules and executes:
///   1. Extract data from core banking (via adapter)
///   2. Validate the extracted data
///   3. If clean, auto-submit for approval
///   4. If errors, notify compliance officer with error details
/// </summary>
public class AutoFilingJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoFilingJob> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    public AutoFilingJob(IServiceScopeFactory scopeFactory, ILogger<AutoFilingJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AutoFilingJob started — checking every {Interval}", CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAutoFilingConfigs(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoFilingJob iteration failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ProcessAutoFilingConfigs(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var caaSService = scope.ServiceProvider.GetRequiredService<ICaaSService>();
        var adapters = scope.ServiceProvider.GetServices<ICoreBankingAdapter>().ToList();
        var webhookService = scope.ServiceProvider.GetRequiredService<IWebhookService>();

        var configs = await db.Set<CaasAutoFilingConfig>()
            .Where(c => c.IsActive)
            .ToListAsync(ct);

        foreach (var config in configs)
        {
            if (!ShouldRunNow(config))
                continue;

            _logger.LogInformation("Auto-filing triggered for tenant {TenantId}, module {Module}, adapter {Adapter}",
                config.TenantId, config.ModuleCode, config.AdapterName);

            var adapter = adapters.FirstOrDefault(a =>
                string.Equals(a.AdapterName, config.AdapterName, StringComparison.OrdinalIgnoreCase));

            if (adapter == null)
            {
                _logger.LogWarning("No adapter found for {AdapterName}", config.AdapterName);
                config.LastRunStatus = "AdapterNotFound";
                config.LastRunAt = DateTime.UtcNow;
                continue;
            }

            try
            {
                // Step 1: Extract data from core banking
                var connectionConfig = System.Text.Json.JsonSerializer
                    .Deserialize<CoreBankingConnectionConfig>(config.ConnectionConfig ?? "{}") ?? new();

                var periodCode = ComputeCurrentPeriod();
                var extractResult = await adapter.ExtractReturnData(
                    config.ModuleCode, periodCode, connectionConfig, ct);

                if (!extractResult.Success)
                {
                    config.LastRunStatus = "ExtractionFailed";
                    config.LastRunAt = DateTime.UtcNow;
                    _logger.LogWarning("Auto-filing extraction failed: {Error}", extractResult.ErrorMessage);
                    continue;
                }

                // Step 2: Validate the extracted data
                var records = new List<Dictionary<string, object?>>
                {
                    extractResult.FieldValues.ToDictionary(kv => kv.Key, kv => (object?)kv.Value)
                };

                var validateRequest = new CaaSValidateRequest
                {
                    ModuleCode = config.ModuleCode,
                    ReturnCode = config.ModuleCode,
                    Records = records
                };

                var validationResult = await caaSService.ValidateAsync(config.TenantId, validateRequest, ct);

                if (!validationResult.IsValid)
                {
                    // Step 3a: Hold and notify compliance officer
                    config.LastRunStatus = "ValidationFailed";
                    config.LastRunAt = DateTime.UtcNow;

                    _logger.LogWarning("Auto-filing validation failed for {Module}: {ErrorCount} errors",
                        config.ModuleCode, validationResult.ErrorCount);

                    // Dispatch webhook notification
                    var endpoints = await webhookService.GetEndpointsAsync(config.TenantId, ct);
                    foreach (var ep in endpoints.Where(e => e.IsActive && e.EventTypes.Contains("auto-filing.failed")))
                    {
                        await webhookService.DeliverAsync(ep, "auto-filing.failed", new
                        {
                            moduleCode = config.ModuleCode,
                            periodCode,
                            errorCount = validationResult.ErrorCount,
                            errors = validationResult.Errors.Take(10)
                        }, ct);
                    }

                    continue;
                }

                // Step 3b: Auto-submit clean data
                var submitRequest = new CaaSSubmitRequest
                {
                    ReturnCode = config.ModuleCode,
                    PeriodCode = periodCode,
                    Records = records,
                    AutoApprove = false // Requires compliance officer approval
                };

                var submitResult = await caaSService.SubmitReturnAsync(
                    config.TenantId, 0, submitRequest, ct);

                config.LastRunStatus = submitResult.Success ? "Submitted" : "SubmissionFailed";
                config.LastRunAt = DateTime.UtcNow;

                // Notify via webhook
                var successEndpoints = await webhookService.GetEndpointsAsync(config.TenantId, ct);
                foreach (var ep in successEndpoints.Where(e => e.IsActive && e.EventTypes.Contains("auto-filing.completed")))
                {
                    await webhookService.DeliverAsync(ep, "auto-filing.completed", new
                    {
                        moduleCode = config.ModuleCode,
                        periodCode,
                        submissionId = submitResult.SubmissionId,
                        status = submitResult.Status
                    }, ct);
                }

                _logger.LogInformation("Auto-filing completed for {Module}: SubmissionId={SubmissionId}",
                    config.ModuleCode, submitResult.SubmissionId);
            }
            catch (Exception ex)
            {
                config.LastRunStatus = "Error";
                config.LastRunAt = DateTime.UtcNow;
                _logger.LogError(ex, "Auto-filing failed for tenant {TenantId}, module {Module}",
                    config.TenantId, config.ModuleCode);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool ShouldRunNow(CaasAutoFilingConfig config)
    {
        if (config.LastRunAt == null)
            return true;

        // Simple daily check: run if last run was more than 23 hours ago
        // Full cron parsing would be added in production
        return (DateTime.UtcNow - config.LastRunAt.Value).TotalHours >= 23;
    }

    private static string ComputeCurrentPeriod()
    {
        var now = DateTime.UtcNow;
        // Previous month (most returns are for the prior period)
        var period = now.AddMonths(-1);
        return period.ToString("yyyy-MM");
    }
}

/// <summary>EF entity for auto-filing configuration.</summary>
public class CaasAutoFilingConfig
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public string? ConnectionConfig { get; set; }
    public string? CronSchedule { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
