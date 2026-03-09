using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Export;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public class DirectSubmissionRetryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DirectSubmissionRetryJob> _logger;

    public DirectSubmissionRetryJob(
        IServiceProvider serviceProvider,
        ILogger<DirectSubmissionRetryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IOptions<RegulatoryApiSettings>>().Value;

                if (settings.Enabled)
                {
                    await RetryFailedSubmissions(scope.ServiceProvider, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Direct submission retry cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task RetryFailedSubmissions(IServiceProvider sp, CancellationToken ct)
    {
        var submissionService = sp.GetRequiredService<IRegulatorySubmissionService>();
        var repo = sp.GetRequiredService<IDirectSubmissionRepository>();

        var pendingRetries = await repo.GetPendingRetries(10, ct);
        if (pendingRetries.Count == 0) return;

        _logger.LogInformation("Retrying {Count} failed direct submissions", pendingRetries.Count);

        foreach (var ds in pendingRetries)
        {
            try
            {
                await submissionService.RetrySubmissionAsync(ds.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retry failed for direct submission {Id}", ds.Id);
            }
        }
    }
}
