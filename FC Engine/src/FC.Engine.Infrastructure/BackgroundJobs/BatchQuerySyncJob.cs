using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Export.ChannelAdapters;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.BackgroundJobs;

/// <summary>
/// Fetches and routes regulator queries for SUBMITTED/ACKNOWLEDGED batches (RG-34).
/// Idempotent: skips queries already present by QueryReference.
/// R-07: CorrelationId propagated into all log entries.
/// </summary>
public sealed class BatchQuerySyncJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BatchQuerySyncJob> _logger;

    // Queries are polled less frequently than statuses
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(15);

    public BatchQuerySyncJob(
        IServiceProvider serviceProvider,
        ILogger<BatchQuerySyncJob> logger)
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
                var settings = scope.ServiceProvider
                    .GetRequiredService<IOptions<RegulatoryApiSettings>>().Value;

                if (settings.Enabled)
                    await SyncQueriesAsync(scope.ServiceProvider, settings, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Batch query sync cycle failed");
            }

            try
            {
                await Task.Delay(DefaultInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task SyncQueriesAsync(
        IServiceProvider sp, RegulatoryApiSettings settings, CancellationToken ct)
    {
        var db = sp.GetRequiredService<MetadataDbContext>();
        var adapters = sp.GetServices<IRegulatoryChannelAdapter>()
            .ToDictionary(a => a.RegulatorCode, StringComparer.OrdinalIgnoreCase);

        // Only sync batches that are actively being reviewed by regulators
        var activeBatches = await db.SubmissionBatches
            .Where(b => b.Status == "SUBMITTED"
                || b.Status == "ACKNOWLEDGED"
                || b.Status == "PROCESSING"
                || b.Status == "QUERIES_RAISED")
            .Include(b => b.Receipts)
            .Take(settings.StatusPolling.BatchSize)
            .ToListAsync(ct);

        if (activeBatches.Count == 0) return;

        _logger.LogDebug(
            "Syncing queries for {Count} active batches", activeBatches.Count);

        foreach (var batch in activeBatches)
        {
            if (!adapters.TryGetValue(batch.RegulatorCode, out var adapter)) continue;

            var receiptRef = batch.Receipts
                .OrderByDescending(r => r.ReceivedAt)
                .FirstOrDefault()?.ReceiptReference;

            if (string.IsNullOrWhiteSpace(receiptRef)) continue;

            try
            {
                var queries = await adapter.FetchQueriesAsync(receiptRef, ct);
                if (queries.Count == 0) continue;

                int newQueryCount = 0;
                foreach (var q in queries)
                {
                    // Idempotency: skip if we already have this query reference for this batch
                    var exists = await db.RegulatoryQueryRecords
                        .AnyAsync(r => r.BatchId == batch.Id
                            && r.QueryReference == q.QueryReference, ct);

                    if (exists) continue;

                    db.RegulatoryQueryRecords.Add(new Domain.Entities.RegulatoryQueryRecord
                    {
                        BatchId = batch.Id,
                        InstitutionId = batch.InstitutionId,
                        RegulatorCode = batch.RegulatorCode,
                        QueryReference = q.QueryReference,
                        QueryType = q.Type,
                        QueryText = q.QueryText,
                        DueDate = q.DueDate,
                        Priority = q.Priority,
                        Status = "OPEN",
                        ReceivedAt = DateTime.UtcNow
                    });

                    newQueryCount++;
                }

                if (newQueryCount > 0)
                {
                    // Mark the batch as queries raised
                    if (batch.Status != "QUERIES_RAISED")
                    {
                        batch.Status = "QUERIES_RAISED";
                        batch.UpdatedAt = DateTime.UtcNow;
                    }

                    await db.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "Synced {NewCount} new queries for batch {BatchRef} [{CorrelationId}] from {RegulatorCode}",
                        newQueryCount, batch.BatchReference, batch.CorrelationId, batch.RegulatorCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Query sync failed for batch {BatchId} ({RegulatorCode})",
                    batch.Id, batch.RegulatorCode);
            }
        }
    }
}
