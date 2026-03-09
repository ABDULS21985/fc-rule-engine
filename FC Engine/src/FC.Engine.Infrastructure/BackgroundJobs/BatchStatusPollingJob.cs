using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Models.BatchSubmission;
using FC.Engine.Infrastructure.Export.ChannelAdapters;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.BackgroundJobs;

/// <summary>
/// Polls regulatory APIs for status updates on submitted batches (RG-34).
/// Runs on the configured interval; processes up to BatchSize batches per cycle.
/// R-07: All status changes are structured-logged with CorrelationId.
/// </summary>
public sealed class BatchStatusPollingJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BatchStatusPollingJob> _logger;

    public BatchStatusPollingJob(
        IServiceProvider serviceProvider,
        ILogger<BatchStatusPollingJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int intervalSeconds = 300;
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var settings = scope.ServiceProvider
                    .GetRequiredService<IOptions<RegulatoryApiSettings>>().Value;
                intervalSeconds = settings.StatusPolling.IntervalSeconds;

                if (settings.Enabled)
                    await PollBatchStatusesAsync(scope.ServiceProvider, settings, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Batch status polling cycle failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task PollBatchStatusesAsync(
        IServiceProvider sp, RegulatoryApiSettings settings, CancellationToken ct)
    {
        var db = sp.GetRequiredService<MetadataDbContext>();
        var adapters = sp.GetServices<IRegulatoryChannelAdapter>()
            .ToDictionary(a => a.RegulatorCode, StringComparer.OrdinalIgnoreCase);

        // Fetch batches that are SUBMITTED or ACKNOWLEDGED (still awaiting final status)
        var pendingBatches = await db.SubmissionBatches
            .Where(b => b.Status == "SUBMITTED" || b.Status == "ACKNOWLEDGED" || b.Status == "PROCESSING")
            .Include(b => b.Receipts)
            .OrderBy(b => b.SubmittedAt)
            .Take(settings.StatusPolling.BatchSize)
            .ToListAsync(ct);

        if (pendingBatches.Count == 0) return;

        _logger.LogInformation(
            "Polling status for {Count} submission batches", pendingBatches.Count);

        foreach (var batch in pendingBatches)
        {
            if (!adapters.TryGetValue(batch.RegulatorCode, out var adapter)) continue;

            var receiptRef = batch.Receipts
                .OrderByDescending(r => r.ReceivedAt)
                .FirstOrDefault()?.ReceiptReference;

            if (string.IsNullOrWhiteSpace(receiptRef)) continue;

            try
            {
                var statusResponse = await adapter.CheckStatusAsync(receiptRef, ct);
                var previousStatus = batch.Status;
                batch.Status = MapStatus(statusResponse.MappedStatus);
                batch.UpdatedAt = DateTime.UtcNow;

                if (batch.Status == "ACKNOWLEDGED" && previousStatus != "ACKNOWLEDGED")
                    batch.AcknowledgedAt = DateTime.UtcNow;

                if (batch.Status is "ACCEPTED" or "FINAL_ACCEPTED" or "REJECTED" or "FAILED")
                    batch.FinalStatusAt = DateTime.UtcNow;

                await db.SaveChangesAsync(ct);

                if (previousStatus != batch.Status)
                {
                    _logger.LogInformation(
                        "Batch {BatchRef} [{CorrelationId}] status changed: {OldStatus} → {NewStatus}",
                        batch.BatchReference, batch.CorrelationId, previousStatus, batch.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Status poll failed for batch {BatchId} ({RegulatorCode})",
                    batch.Id, batch.RegulatorCode);
            }
        }
    }

    private static string MapStatus(BatchSubmissionStatusValue value) =>
        value switch
        {
            BatchSubmissionStatusValue.Acknowledged  => "ACKNOWLEDGED",
            BatchSubmissionStatusValue.Processing    => "PROCESSING",
            BatchSubmissionStatusValue.Accepted      => "ACCEPTED",
            BatchSubmissionStatusValue.FinalAccepted => "FINAL_ACCEPTED",
            BatchSubmissionStatusValue.QueriesRaised => "QUERIES_RAISED",
            BatchSubmissionStatusValue.Rejected      => "REJECTED",
            BatchSubmissionStatusValue.Failed        => "FAILED",
            _                                        => "SUBMITTED"
        };
}
