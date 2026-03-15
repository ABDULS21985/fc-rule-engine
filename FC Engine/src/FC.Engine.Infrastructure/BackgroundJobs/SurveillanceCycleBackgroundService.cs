using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public sealed class SurveillanceCycleBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SurveillanceCycleBackgroundService> _log;

    public SurveillanceCycleBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SurveillanceCycleBackgroundService> log)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("SurveillanceCycle:Enabled", false))
        {
            _log.LogInformation("Surveillance cycle background service is disabled by configuration.");
            return;
        }

        var intervalHours = _configuration.GetValue("SurveillanceCycle:IntervalHours", 6);
        var regulatorCode = _configuration["SurveillanceCycle:RegulatorCode"];
        if (string.IsNullOrWhiteSpace(regulatorCode))
        {
            _log.LogWarning("Surveillance cycle background service is enabled but SurveillanceCycle:RegulatorCode is not configured.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(intervalHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<ISurveillanceOrchestrator>();
                var now = DateTime.UtcNow;
                var periodCode = $"{now.Year}-Q{((now.Month - 1) / 3) + 1}";

                var result = await orchestrator.RunCycleAsync(regulatorCode, periodCode, stoppingToken);
                _log.LogInformation(
                    "Scheduled conduct surveillance cycle completed. Regulator={RegulatorCode} Alerts={Alerts} Entities={Entities}",
                    regulatorCode,
                    result.AlertsRaised,
                    result.EntitiesScored);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Scheduled conduct surveillance cycle failed.");
            }

            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }
}
