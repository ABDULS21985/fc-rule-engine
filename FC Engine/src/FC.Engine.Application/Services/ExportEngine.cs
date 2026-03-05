using System.Security.Cryptography;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Application.Services;

public class ExportEngine : IExportEngine
{
    private readonly IExportRequestRepository _exportRequestRepository;
    private readonly ISubmissionRepository _submissionRepository;
    private readonly ITenantBrandingService _brandingService;
    private readonly IFileStorageService _fileStorageService;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly IReadOnlyDictionary<ExportFormat, IExportGenerator> _generatorMap;
    private readonly ILogger<ExportEngine> _logger;

    public ExportEngine(
        IExportRequestRepository exportRequestRepository,
        ISubmissionRepository submissionRepository,
        ITenantBrandingService brandingService,
        IFileStorageService fileStorageService,
        IEnumerable<IExportGenerator> generators,
        ILogger<ExportEngine> logger,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _exportRequestRepository = exportRequestRepository;
        _submissionRepository = submissionRepository;
        _brandingService = brandingService;
        _fileStorageService = fileStorageService;
        _notificationOrchestrator = notificationOrchestrator;
        _logger = logger;
        _generatorMap = generators.ToDictionary(x => x.Format);
    }

    public async Task<int> QueueExport(
        Guid tenantId,
        int submissionId,
        ExportFormat format,
        int requestedByUserId,
        CancellationToken ct = default)
    {
        var submission = await _submissionRepository.GetById(submissionId, ct);
        if (submission is null)
        {
            throw new InvalidOperationException($"Submission {submissionId} was not found.");
        }

        if (submission.TenantId != tenantId)
        {
            throw new UnauthorizedAccessException("Submission does not belong to the requesting tenant.");
        }

        var request = new ExportRequest
        {
            TenantId = tenantId,
            SubmissionId = submissionId,
            Format = format,
            Status = ExportRequestStatus.Queued,
            RequestedBy = requestedByUserId,
            RequestedAt = DateTime.UtcNow
        };

        var created = await _exportRequestRepository.Add(request, ct);
        return created.Id;
    }

    public async Task<ExportResult> GenerateExport(int exportRequestId, CancellationToken ct = default)
    {
        var request = await _exportRequestRepository.GetById(exportRequestId, ct);
        if (request is null)
        {
            return new ExportResult
            {
                Success = false,
                ErrorMessage = $"Export request {exportRequestId} was not found."
            };
        }

        request.Status = ExportRequestStatus.Processing;
        await _exportRequestRepository.Update(request, ct);

        try
        {
            var submission = request.Submission ?? await _submissionRepository.GetByIdWithReport(request.SubmissionId, ct);
            if (submission is null)
            {
                throw new InvalidOperationException($"Submission {request.SubmissionId} was not found.");
            }

            if (!_generatorMap.TryGetValue(request.Format, out var generator))
            {
                throw new InvalidOperationException($"No export generator is registered for format {request.Format}.");
            }

            var branding = await _brandingService.GetBrandingConfig(request.TenantId, ct);
            var fileBytes = await generator.Generate(new ExportGenerationContext
            {
                TenantId = request.TenantId,
                Submission = submission,
                Branding = branding
            }, ct);

            var hash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
            var path = $"tenants/{request.TenantId}/exports/{request.SubmissionId}/{request.Id}.{generator.FileExtension}";

            await using (var stream = new MemoryStream(fileBytes))
            {
                await _fileStorageService.UploadAsync(path, stream, generator.ContentType, ct);
            }

            request.Status = ExportRequestStatus.Completed;
            request.CompletedAt = DateTime.UtcNow;
            request.FilePath = path;
            request.FileSize = fileBytes.LongLength;
            request.Sha256Hash = hash;
            request.ErrorMessage = null;
            request.ExpiresAt = DateTime.UtcNow.AddDays(7);
            await _exportRequestRepository.Update(request, ct);

            await NotifyExportReady(request, submission.ReturnCode, ct);

            return new ExportResult
            {
                Success = true,
                FilePath = path,
                FileSize = fileBytes.LongLength,
                Sha256Hash = hash
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export generation failed for request {RequestId}", exportRequestId);
            request.Status = ExportRequestStatus.Failed;
            request.ErrorMessage = ex.Message;
            request.CompletedAt = DateTime.UtcNow;
            await _exportRequestRepository.Update(request, ct);

            return new ExportResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<Stream> DownloadExport(int exportRequestId, Guid tenantId, CancellationToken ct = default)
    {
        var request = await _exportRequestRepository.GetById(exportRequestId, ct);
        if (request is null)
        {
            throw new FileNotFoundException("Export request was not found.");
        }

        if (request.TenantId != tenantId)
        {
            throw new UnauthorizedAccessException("This export belongs to another tenant.");
        }

        if (request.Status != ExportRequestStatus.Completed || string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new InvalidOperationException("Export is not available for download yet.");
        }

        return await _fileStorageService.DownloadAsync(request.FilePath, ct);
    }

    public async Task<List<ExportRequest>> GetExportHistory(Guid tenantId, int submissionId, CancellationToken ct = default)
    {
        var history = await _exportRequestRepository.GetBySubmission(tenantId, submissionId, ct);
        return history.ToList();
    }

    private async Task NotifyExportReady(ExportRequest request, string returnCode, CancellationToken ct)
    {
        if (_notificationOrchestrator is null)
        {
            return;
        }

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = request.TenantId,
            EventType = NotificationEvents.ExportReady,
            Title = $"Export Ready - {returnCode}",
            Message = $"Your {request.Format} export is ready for download.",
            Priority = NotificationPriority.Normal,
            RecipientUserIds = new List<int> { request.RequestedBy },
            ActionUrl = $"/exports/{request.Id}/download",
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExportName"] = request.Format.ToString()
            }
        }, ct);
    }
}
