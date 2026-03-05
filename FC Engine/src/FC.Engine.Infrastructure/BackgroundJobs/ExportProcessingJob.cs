using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public class ExportProcessingJob : BackgroundService
{
    private readonly IExportRequestRepository _exportRequestRepository;
    private readonly IExportEngine _exportEngine;
    private readonly ILogger<ExportProcessingJob> _logger;

    public ExportProcessingJob(
        IExportRequestRepository exportRequestRepository,
        IExportEngine exportEngine,
        ILogger<ExportProcessingJob> logger)
    {
        _exportRequestRepository = exportRequestRepository;
        _exportEngine = exportEngine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var queued = await _exportRequestRepository.GetQueuedBatch(5, stoppingToken);
                foreach (var request in queued)
                {
                    var result = await _exportEngine.GenerateExport(request.Id, stoppingToken);
                    if (!result.Success)
                    {
                        _logger.LogWarning(
                            "Export request {RequestId} failed: {Error}",
                            request.Id,
                            result.ErrorMessage);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Export processing cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }
}
