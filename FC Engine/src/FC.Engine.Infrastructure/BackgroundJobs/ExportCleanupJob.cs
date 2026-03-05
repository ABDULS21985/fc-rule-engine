using FC.Engine.Domain.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public class ExportCleanupJob : BackgroundService
{
    private readonly IExportRequestRepository _exportRequestRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<ExportCleanupJob> _logger;

    public ExportCleanupJob(
        IExportRequestRepository exportRequestRepository,
        IFileStorageService fileStorageService,
        ILogger<ExportCleanupJob> logger)
    {
        _exportRequestRepository = exportRequestRepository;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await _exportRequestRepository.GetExpired(DateTime.UtcNow, 100, stoppingToken);
                foreach (var request in expired)
                {
                    await RemoveExpiredExport(request, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Export cleanup cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task RemoveExpiredExport(Domain.Entities.ExportRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            try
            {
                await _fileStorageService.DeleteAsync(request.FilePath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed deleting expired export file {Path}", request.FilePath);
            }
        }

        request.FilePath = null;
        request.FileSize = null;
        request.Sha256Hash = null;
        request.ErrorMessage = "Export expired and has been removed from storage.";
        await _exportRequestRepository.Update(request, ct);
    }
}
