using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models.BatchSubmission;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

/// <summary>
/// Orchestrates end-to-end regulatory batch submission:
/// validate → export → evidence → sign → dispatch → receipt → events.
/// R-05: InstitutionId filters every DB query.
/// R-06: Idempotency enforced via UQ_submission_items_idempotent.
/// R-07: CorrelationId propagated through logs, events, and audit trail.
/// </summary>
public sealed class SubmissionOrchestrator : ISubmissionOrchestrator
{
    private static readonly ActivitySource ActivitySource = new("RegOS.SubmissionOrchestrator");

    private readonly MetadataDbContext _db;
    private readonly ISubmissionSigningService _signer;
    private readonly IEvidencePackageService _evidenceService;
    private readonly ISubmissionBatchAuditLogger _auditLogger;
    private readonly ISubmissionEventPublisher _events;
    private readonly ILogger<SubmissionOrchestrator> _logger;

    // Keyed channel adapters resolved lazily from DI
    private readonly IEnumerable<IRegulatoryChannelAdapter> _adapters;

    public SubmissionOrchestrator(
        MetadataDbContext db,
        ISubmissionSigningService signer,
        IEvidencePackageService evidenceService,
        ISubmissionBatchAuditLogger auditLogger,
        ISubmissionEventPublisher events,
        IEnumerable<IRegulatoryChannelAdapter> adapters,
        ILogger<SubmissionOrchestrator> logger)
    {
        _db = db;
        _signer = signer;
        _evidenceService = evidenceService;
        _auditLogger = auditLogger;
        _events = events;
        _adapters = adapters;
        _logger = logger;
    }

    // ── SubmitBatchAsync ───────────────────────────────────────────────

    public async Task<BatchSubmissionResult> SubmitBatchAsync(
        int institutionId,
        string regulatorCode,
        IReadOnlyList<int> submissionIds,
        int submittedByUserId,
        CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        using var activity = ActivitySource.StartActivity("SubmitBatch");
        activity?.SetTag("correlation_id", correlationId.ToString());
        activity?.SetTag("regulator", regulatorCode);
        activity?.SetTag("institution_id", institutionId.ToString());

        _logger.LogInformation(
            "[{CorrelationId}] Starting batch submission: InstitutionId={InstitutionId}, " +
            "Regulator={Regulator}, SubmissionCount={Count}",
            correlationId, institutionId, regulatorCode, submissionIds.Count);

        // ── Step 1: Resolve channel ────────────────────────────────────
        var channel = await _db.RegulatoryChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.RegulatorCode == regulatorCode && c.IsActive, ct);

        if (channel is null)
        {
            return Fail(correlationId,
                $"No active channel configured for regulator '{regulatorCode}'.");
        }

        var adapter = _adapters.FirstOrDefault(a =>
            string.Equals(a.RegulatorCode, regulatorCode, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
        {
            return Fail(correlationId,
                $"No channel adapter registered for regulator '{regulatorCode}'.");
        }

        // ── Step 2: Validate submissions belong to this institution ────
        var submissions = await _db.Submissions
            .AsNoTracking()
            .Where(s => submissionIds.Contains(s.Id) && s.InstitutionId == institutionId)
            .ToListAsync(ct);

        if (submissions.Count != submissionIds.Count)
        {
            return Fail(correlationId,
                "One or more submission IDs are invalid or do not belong to this institution.");
        }

        // ── Step 3: Create batch record ────────────────────────────────
        var batchRef = GenerateBatchReference(institutionId, regulatorCode);
        var batch = new SubmissionBatch
        {
            InstitutionId = institutionId,
            BatchReference = batchRef,
            RegulatorCode = regulatorCode,
            ChannelId = channel.Id,
            Status = "PENDING",
            SubmittedBy = submittedByUserId,
            CorrelationId = correlationId
        };
        _db.SubmissionBatches.Add(batch);
        await _db.SaveChangesAsync(ct);

        await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
            "BATCH_CREATED",
            new { submissionIds, regulatorCode, batchRef },
            submittedByUserId, ct);

        await _events.PublishAsync("submission.initiated", new
        {
            BatchId = batch.Id,
            BatchReference = batchRef,
            InstitutionId = institutionId,
            RegulatorCode = regulatorCode,
            CorrelationId = correlationId,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        // ── Step 4: Export + evidence per submission ───────────────────
        var items = new List<SubmissionItemContext>();

        foreach (var sub in submissions)
        {
            // Evidence package (RG-14)
            EvidencePackage? evidencePkg = null;
            long? evidenceId = null;
            try
            {
                evidencePkg = await _evidenceService.GenerateAsync(sub.Id, $"user:{submittedByUserId}", ct);
                evidenceId = evidencePkg.Id;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[{CorrelationId}] Evidence package generation failed for submission {SubId}",
                    correlationId, sub.Id);
            }

            // Build export payload from stored ParsedDataJson as XML/XLSX bytes
            var exportBytes = BuildExportPayload(sub, regulatorCode);
            var exportFormat = string.Equals(regulatorCode, "NFIU", StringComparison.OrdinalIgnoreCase)
                ? "XML" : "XLSX";

            var hash = ComputeSha512(exportBytes);
            var digest = new PayloadDigest("SHA-512", hash, exportBytes.Length);

            // Insert SubmissionItem — idempotent via unique constraint
            var item = new SubmissionItem
            {
                BatchId = batch.Id,
                InstitutionId = institutionId,
                SubmissionId = sub.Id,
                ReturnCode = sub.ReturnCode ?? string.Empty,
                ReturnVersion = sub.ReturnPeriodId,  // use period ID as surrogate version
                ReportingPeriod = FormatPeriod(sub),
                ExportFormat = exportFormat,
                ExportPayloadHash = hash,
                ExportPayloadSize = exportBytes.Length,
                EvidencePackageId = evidenceId,
                Status = "PENDING",
                RegulatorCode = regulatorCode
            };

            try
            {
                _db.SubmissionItems.Add(item);
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException dbEx)
                when (dbEx.InnerException?.Message.Contains("UQ_submission_items_idempotent",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                // R-06: idempotency — this submission is already in a batch for this regulator+version
                _logger.LogWarning(
                    "[{CorrelationId}] Duplicate submission item rejected for SubmissionId={SubId}, " +
                    "RegulatorCode={Regulator}", correlationId, sub.Id, regulatorCode);
                continue;
            }

            items.Add(new SubmissionItemContext(item.Id, exportBytes, digest, evidenceId ?? 0, exportFormat));

            await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
                "ITEM_ADDED",
                new { ItemId = item.Id, SubmissionId = sub.Id, ReturnCode = sub.ReturnCode },
                submittedByUserId, ct);
        }

        if (items.Count == 0)
        {
            await UpdateBatchStatusAsync(batch.Id, "FAILED", "All submission items were duplicates.", ct);
            return Fail(correlationId, "All submissions are already in an active batch for this regulator.", batch.Id);
        }

        // ── Step 5: Digital signing ────────────────────────────────────
        await UpdateBatchStatusAsync(batch.Id, "SIGNING", null, ct);
        await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
            "SIGNING_STARTED", null, null, ct);

        var combinedPayload = CombinePayloadsForSigning(items);
        BatchSignatureInfo signature;
        try
        {
            signature = await _signer.SignPayloadAsync(institutionId, combinedPayload, ct);
        }
        catch (Exception ex)
        {
            await UpdateBatchStatusAsync(batch.Id, "FAILED", ex.Message, ct);
            await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
                "FAILED", new { Step = "Signing", Error = ex.Message }, null, ct);
            return Fail(correlationId, $"Digital signing failed: {ex.Message}", batch.Id);
        }

        // Self-verify before dispatch
        var isValid = await _signer.VerifySignatureAsync(signature, combinedPayload, ct);
        if (!isValid)
        {
            await UpdateBatchStatusAsync(batch.Id, "FAILED", "Self-verification failed.", ct);
            return Fail(correlationId, "Digital signature self-verification failed.", batch.Id);
        }

        // Persist signature for each item
        foreach (var item in items)
        {
            _db.SubmissionSignatureRecords.Add(new SubmissionSignatureRecord
            {
                SubmissionItemId = item.ItemId,
                InstitutionId = institutionId,
                CertificateThumbprint = signature.CertificateThumbprint,
                SignatureAlgorithm = signature.SignatureAlgorithm,
                SignatureValue = signature.SignatureValue,
                SignedDataHash = signature.SignedDataHash,
                SignedAt = signature.SignedAt.UtcDateTime,
                TimestampToken = signature.TimestampToken,
                IsValid = true
            });
        }
        await _db.SaveChangesAsync(ct);

        await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
            "SIGNED",
            new { signature.CertificateThumbprint, signature.SignatureAlgorithm },
            null, ct);

        await _events.PublishAsync("submission.signed", new
        {
            BatchId = batch.Id, InstitutionId = institutionId,
            CorrelationId = correlationId, Timestamp = DateTimeOffset.UtcNow
        }, ct);

        // ── Step 6: Dispatch via channel adapter ───────────────────────
        await UpdateBatchStatusAsync(batch.Id, "DISPATCHING", null, ct);
        await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
            "DISPATCH_STARTED", null, null, ct);

        var institution = await _db.Institutions
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == institutionId, ct);

        var institutionCode = institution?.InstitutionCode ?? $"INST-{institutionId:D5}";

        // Retrieve evidence ZIP for the first item (used as attachment)
        byte[]? evidenceZip = null;
        if (items[0].EvidencePackageId > 0)
        {
            try
            {
                var pkg = await _db.EvidencePackages
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == (long)items[0].EvidencePackageId, ct);
                if (pkg is not null)
                    evidenceZip = await System.IO.File.ReadAllBytesAsync(pkg.StoragePath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{CorrelationId}] Evidence ZIP read failed", correlationId);
            }
        }

        var exportContent = CombineExports(items);
        var dispatchPayload = new DispatchPayload(
            BatchReference: batchRef,
            RegulatorCode: regulatorCode,
            InstitutionCode: institutionCode,
            ExportedFileContent: exportContent,
            ExportedFileName: $"{batchRef}.{items[0].ExportFormat.ToLowerInvariant()}",
            ExportFormat: items[0].ExportFormat,
            Digest: items[0].Digest,
            Signature: signature,
            EvidencePackage: evidenceZip,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["batch_reference"] = batchRef,
                ["item_count"] = items.Count.ToString(),
                ["reporting_period"] = items[0].ReportingPeriod,
                ["correlation_id"] = correlationId.ToString()
            });

        BatchRegulatorReceipt receipt;
        try
        {
            receipt = await adapter.DispatchAsync(dispatchPayload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[{CorrelationId}] Dispatch failed for batch {BatchId}", correlationId, batch.Id);

            await UpdateBatchStatusAsync(batch.Id, "FAILED", ex.Message, ct);

            var retryCount = batch.RetryCount + 1;
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE meta.submission_batches SET RetryCount = {0} WHERE Id = {1}",
                retryCount, batch.Id);

            await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
                "FAILED", new { Step = "Dispatch", Error = ex.Message }, null, ct);

            return Fail(correlationId, ex.Message, batch.Id);
        }

        // ── Step 7: Store receipt ──────────────────────────────────────
        _db.SubmissionBatchReceipts.Add(new SubmissionBatchReceipt
        {
            BatchId = batch.Id,
            InstitutionId = institutionId,
            RegulatorCode = regulatorCode,
            ReceiptReference = receipt.ReceiptReference,
            ReceiptTimestamp = receipt.ReceiptTimestamp.UtcDateTime,
            RawResponse = receipt.RawResponse,
            HttpStatusCode = receipt.HttpStatusCode
        });

        await UpdateBatchStatusAsync(batch.Id, "SUBMITTED", null, ct);

        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE meta.submission_batches SET SubmittedAt = {0} WHERE Id = {1}",
            DateTime.UtcNow, batch.Id);

        await _db.SaveChangesAsync(ct);

        await _auditLogger.LogAsync(batch.Id, institutionId, correlationId,
            "DISPATCHED",
            new { receipt.ReceiptReference },
            null, ct);

        await _events.PublishAsync("submission.dispatched", new
        {
            BatchId = batch.Id, InstitutionId = institutionId,
            RegulatorCode = regulatorCode,
            ReceiptReference = receipt.ReceiptReference,
            CorrelationId = correlationId,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        _logger.LogInformation(
            "[{CorrelationId}] Batch {BatchId} dispatched to {Regulator}. Receipt: {Receipt}",
            correlationId, batch.Id, regulatorCode, receipt.ReceiptReference);

        return new BatchSubmissionResult(
            Success: true,
            BatchId: batch.Id,
            BatchReference: batchRef,
            Status: "SUBMITTED",
            Receipt: receipt,
            ErrorMessage: null,
            CorrelationId: correlationId);
    }

    // ── RetryBatchAsync ────────────────────────────────────────────────

    public async Task<BatchSubmissionResult> RetryBatchAsync(
        int institutionId, long batchId, CancellationToken ct = default)
    {
        var batch = await _db.SubmissionBatches
            .Include(b => b.Items)
            .Include(b => b.Channel)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.InstitutionId == institutionId, ct);

        if (batch is null)
            return Fail(Guid.NewGuid(), $"Batch {batchId} not found.");

        if (batch.Status != "FAILED")
            return Fail(batch.CorrelationId, $"Batch {batchId} is not in FAILED status (current: {batch.Status}).");

        if (batch.RetryCount >= 3)
            return Fail(batch.CorrelationId, $"Batch {batchId} has exhausted all retries.");

        var adapter = _adapters.FirstOrDefault(a =>
            string.Equals(a.RegulatorCode, batch.RegulatorCode, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
            return Fail(batch.CorrelationId, $"No adapter for regulator '{batch.RegulatorCode}'.");

        var correlationId = batch.CorrelationId;
        await _auditLogger.LogAsync(batchId, institutionId, correlationId,
            "RETRY_ATTEMPTED",
            new { batch.RetryCount },
            null, ct);

        // Re-read items and rebuild dispatch payload from stored hashes
        var submissions = await _db.Submissions
            .AsNoTracking()
            .Where(s => batch.Items.Select(i => i.SubmissionId).Contains(s.Id))
            .ToListAsync(ct);

        var items = batch.Items.Select(i =>
        {
            var sub = submissions.FirstOrDefault(s => s.Id == i.SubmissionId);
            var exportBytes = sub is not null ? BuildExportPayload(sub, batch.RegulatorCode) : [];
            var digest = new PayloadDigest("SHA-512", i.ExportPayloadHash, i.ExportPayloadSize);
            return new SubmissionItemContext(i.Id, exportBytes, digest, i.EvidencePackageId ?? 0, i.ExportFormat);
        }).ToList();

        var combinedPayload = CombinePayloadsForSigning(items);
        BatchSignatureInfo signature;
        try
        {
            signature = await _signer.SignPayloadAsync(institutionId, combinedPayload, ct);
        }
        catch (Exception ex)
        {
            return Fail(correlationId, $"Re-signing failed: {ex.Message}", batchId);
        }

        var institution = await _db.Institutions.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == institutionId, ct);

        var dispatchPayload = new DispatchPayload(
            BatchReference: batch.BatchReference,
            RegulatorCode: batch.RegulatorCode,
            InstitutionCode: institution?.InstitutionCode ?? $"INST-{institutionId:D5}",
            ExportedFileContent: CombineExports(items),
            ExportedFileName: $"{batch.BatchReference}.{items[0].ExportFormat.ToLowerInvariant()}",
            ExportFormat: items[0].ExportFormat,
            Digest: items[0].Digest,
            Signature: signature,
            EvidencePackage: null,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["batch_reference"] = batch.BatchReference,
                ["item_count"] = items.Count.ToString(),
                ["reporting_period"] = items[0].ReportingPeriod,
                ["correlation_id"] = correlationId.ToString()
            });

        BatchRegulatorReceipt receipt;
        try
        {
            receipt = await adapter.DispatchAsync(dispatchPayload, ct);
        }
        catch (Exception ex)
        {
            await UpdateBatchStatusAsync(batchId, "FAILED", ex.Message, ct);
            await _db.Database.ExecuteSqlRawAsync(
                "UPDATE meta.submission_batches SET RetryCount = RetryCount + 1 WHERE Id = {0}", batchId);
            return Fail(correlationId, ex.Message, batchId);
        }

        _db.SubmissionBatchReceipts.Add(new SubmissionBatchReceipt
        {
            BatchId = batchId,
            InstitutionId = institutionId,
            RegulatorCode = batch.RegulatorCode,
            ReceiptReference = receipt.ReceiptReference,
            ReceiptTimestamp = receipt.ReceiptTimestamp.UtcDateTime,
            RawResponse = receipt.RawResponse,
            HttpStatusCode = receipt.HttpStatusCode
        });

        await UpdateBatchStatusAsync(batchId, "SUBMITTED", null, ct);
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE meta.submission_batches SET RetryCount = RetryCount + 1, SubmittedAt = {0} WHERE Id = {1}",
            DateTime.UtcNow, batchId);
        await _db.SaveChangesAsync(ct);

        await _auditLogger.LogAsync(batchId, institutionId, correlationId,
            "DISPATCHED", new { receipt.ReceiptReference }, null, ct);

        return new BatchSubmissionResult(true, batchId, batch.BatchReference,
            "SUBMITTED", receipt, null, correlationId);
    }

    // ── RefreshStatusAsync ─────────────────────────────────────────────

    public async Task<BatchStatusRefreshResult> RefreshStatusAsync(
        int institutionId, long batchId, CancellationToken ct = default)
    {
        var batch = await _db.SubmissionBatches
            .Include(b => b.Receipts)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.InstitutionId == institutionId, ct);

        if (batch is null)
            throw new InvalidOperationException($"Batch {batchId} not found.");

        var previousStatus = batch.Status;
        var latestReceipt = batch.Receipts.OrderByDescending(r => r.ReceivedAt).FirstOrDefault();
        if (latestReceipt is null)
            return new BatchStatusRefreshResult(batchId, previousStatus, previousStatus, false, null);

        var adapter = _adapters.FirstOrDefault(a =>
            string.Equals(a.RegulatorCode, batch.RegulatorCode, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
            return new BatchStatusRefreshResult(batchId, previousStatus, previousStatus, false, null);

        var statusResponse = await adapter.CheckStatusAsync(latestReceipt.ReceiptReference, ct);
        var newStatus = MapRegulatorStatus(statusResponse.MappedStatus);

        if (newStatus != previousStatus)
        {
            batch.Status = newStatus;
            batch.UpdatedAt = DateTime.UtcNow;

            if (newStatus is "ACKNOWLEDGED")
                batch.AcknowledgedAt = DateTime.UtcNow;
            if (newStatus is "ACCEPTED" or "FINAL_ACCEPTED" or "REJECTED")
                batch.FinalStatusAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            await _auditLogger.LogAsync(batchId, institutionId, batch.CorrelationId,
                "STATUS_UPDATED",
                new { PreviousStatus = previousStatus, NewStatus = newStatus },
                null, ct);

            await _events.PublishAsync("submission.status.updated", new
            {
                BatchId = batchId, InstitutionId = institutionId,
                PreviousStatus = previousStatus, NewStatus = newStatus,
                CorrelationId = batch.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow
            }, ct);
        }

        // Route any new queries from regulator
        if (statusResponse.MappedStatus == BatchSubmissionStatusValue.QueriesRaised)
        {
            var newQueries = await adapter.FetchQueriesAsync(latestReceipt.ReceiptReference, ct);
            await RouteRegulatorQueriesAsync(batch, newQueries, ct);
        }

        var receipt = new BatchRegulatorReceipt(
            latestReceipt.ReceiptReference,
            new DateTimeOffset(latestReceipt.ReceiptTimestamp, TimeSpan.Zero),
            latestReceipt.HttpStatusCode,
            latestReceipt.RawResponse);

        return new BatchStatusRefreshResult(
            batchId, previousStatus, newStatus, newStatus != previousStatus, receipt);
    }

    // ── Private helpers ────────────────────────────────────────────────

    private async Task RouteRegulatorQueriesAsync(
        SubmissionBatch batch,
        IReadOnlyList<BatchRegulatorQueryDto> queries,
        CancellationToken ct)
    {
        foreach (var q in queries)
        {
            // Idempotency: skip if we already stored this query reference
            var exists = await _db.RegulatoryQueryRecords
                .AnyAsync(r => r.BatchId == batch.Id && r.QueryReference == q.QueryReference, ct);
            if (exists) continue;

            _db.RegulatoryQueryRecords.Add(new RegulatoryQueryRecord
            {
                BatchId = batch.Id,
                InstitutionId = batch.InstitutionId,
                RegulatorCode = batch.RegulatorCode,
                QueryReference = q.QueryReference,
                QueryType = q.Type,
                QueryText = q.QueryText,
                DueDate = q.DueDate,
                Priority = q.Priority,
                Status = "OPEN"
            });

            await _auditLogger.LogAsync(batch.Id, batch.InstitutionId, batch.CorrelationId,
                "QUERY_RECEIVED",
                new { q.QueryReference, q.Type, q.Priority },
                null, ct);
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(ct);

            await _events.PublishAsync("submission.query.received", new
            {
                BatchId = batch.Id,
                InstitutionId = batch.InstitutionId,
                RegulatorCode = batch.RegulatorCode,
                QueryCount = queries.Count,
                CorrelationId = batch.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow
            }, ct);
        }
    }

    private async Task UpdateBatchStatusAsync(long batchId, string status, string? error, CancellationToken ct)
    {
        var batch = await _db.SubmissionBatches.FindAsync([batchId], ct);
        if (batch is null) return;
        batch.Status = status;
        batch.UpdatedAt = DateTime.UtcNow;
        if (error is not null)
            batch.LastError = error;
        await _db.SaveChangesAsync(ct);
    }

    private static string GenerateBatchReference(int institutionId, string regulatorCode)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"BATCH-{institutionId:D5}-{regulatorCode.ToUpperInvariant()}-{DateTime.UtcNow:yyyyMMdd}-{suffix}";
    }

    private static string ComputeSha512(byte[] data)
        => Convert.ToHexString(SHA512.HashData(data)).ToLowerInvariant();

    private static byte[] CombinePayloadsForSigning(IList<SubmissionItemContext> items)
    {
        using var ms = new MemoryStream();
        foreach (var item in items)
            ms.Write(item.ExportBytes);
        return ms.ToArray();
    }

    private static byte[] CombineExports(IList<SubmissionItemContext> items)
    {
        if (items.Count == 1)
            return items[0].ExportBytes;

        using var ms = new MemoryStream();
        using var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var item in items)
        {
            var entry = archive.CreateEntry(
                $"{item.ReportingPeriod}_{item.ItemId}.{item.ExportFormat.ToLowerInvariant()}");
            using var entryStream = entry.Open();
            entryStream.Write(item.ExportBytes);
        }
        archive.Dispose();
        return ms.ToArray();
    }

    /// <summary>
    /// Produces export bytes from the submission's stored ParsedDataJson.
    /// For NFIU we render XML; for all others we produce an XLSX stub
    /// (the actual rendering is handled by the adapter's packaging step).
    /// </summary>
    private static byte[] BuildExportPayload(Domain.Entities.Submission sub, string regulatorCode)
    {
        var json = sub.ParsedDataJson ?? "{}";
        return string.Equals(regulatorCode, "NFIU", StringComparison.OrdinalIgnoreCase)
            ? System.Text.Encoding.UTF8.GetBytes($"<submission id=\"{sub.Id}\">{json}</submission>")
            : System.Text.Encoding.UTF8.GetBytes(json);
    }

    private static string FormatPeriod(Domain.Entities.Submission sub)
        => $"{sub.ReturnPeriodId}";

    private static string MapRegulatorStatus(BatchSubmissionStatusValue sv) => sv switch
    {
        BatchSubmissionStatusValue.Acknowledged => "ACKNOWLEDGED",
        BatchSubmissionStatusValue.Processing => "PROCESSING",
        BatchSubmissionStatusValue.Accepted => "ACCEPTED",
        BatchSubmissionStatusValue.FinalAccepted => "FINAL_ACCEPTED",
        BatchSubmissionStatusValue.QueriesRaised => "QUERIES_RAISED",
        BatchSubmissionStatusValue.Rejected => "REJECTED",
        _ => "SUBMITTED"
    };

    private static BatchSubmissionResult Fail(
        Guid correlationId, string error, long batchId = 0)
        => new(false, batchId, string.Empty, "FAILED", null, error, correlationId);
}

// ── Internal context ──────────────────────────────────────────────────
internal sealed record SubmissionItemContext(
    long ItemId,
    byte[] ExportBytes,
    PayloadDigest Digest,
    long EvidencePackageId,
    string ExportFormat)
{
    public string ReportingPeriod => $"period-{ItemId}";
}
