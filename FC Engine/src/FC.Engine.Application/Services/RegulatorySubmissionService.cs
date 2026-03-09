using System.Security.Cryptography;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FC.Engine.Domain.Models;

namespace FC.Engine.Application.Services;

public class RegulatorySubmissionService : IRegulatorySubmissionService
{
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IDirectSubmissionRepository _directSubmissionRepo;
    private readonly IReadOnlyDictionary<string, IRegulatorSubmissionAdapter> _adapterMap;
    private readonly IReadOnlyDictionary<string, IRegulatorApiClient> _clientMap;
    private readonly IDigitalSignatureService _signatureService;
    private readonly IEvidencePackageService _evidenceService;
    private readonly IAuditLogger _auditLogger;
    private readonly IFileStorageService _fileStorage;
    private readonly ITenantContext _tenantContext;
    private readonly RegulatoryApiSettings _settings;
    private readonly ILogger<RegulatorySubmissionService> _logger;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly IDomainEventPublisher? _domainEventPublisher;

    public RegulatorySubmissionService(
        ISubmissionRepository submissionRepo,
        IDirectSubmissionRepository directSubmissionRepo,
        IEnumerable<IRegulatorSubmissionAdapter> adapters,
        IEnumerable<IRegulatorApiClient> clients,
        IDigitalSignatureService signatureService,
        IEvidencePackageService evidenceService,
        IAuditLogger auditLogger,
        IFileStorageService fileStorage,
        ITenantContext tenantContext,
        IOptions<RegulatoryApiSettings> settings,
        ILogger<RegulatorySubmissionService> logger,
        INotificationOrchestrator? notificationOrchestrator = null,
        IDomainEventPublisher? domainEventPublisher = null)
    {
        _submissionRepo = submissionRepo;
        _directSubmissionRepo = directSubmissionRepo;
        _adapterMap = adapters.ToDictionary(a => a.RegulatorCode, StringComparer.OrdinalIgnoreCase);
        _clientMap = clients.ToDictionary(c => c.RegulatorCode, StringComparer.OrdinalIgnoreCase);
        _signatureService = signatureService;
        _evidenceService = evidenceService;
        _auditLogger = auditLogger;
        _fileStorage = fileStorage;
        _tenantContext = tenantContext;
        _settings = settings.Value;
        _logger = logger;
        _notificationOrchestrator = notificationOrchestrator;
        _domainEventPublisher = domainEventPublisher;
    }

    public async Task<DirectSubmissionResult> SubmitToRegulatorAsync(
        int submissionId, string regulatorCode, string submittedBy,
        CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            return new DirectSubmissionResult
            {
                Success = false,
                Message = "Direct regulatory submission is not enabled."
            };
        }

        // Step 1: Validate prerequisites
        var submission = await _submissionRepo.GetByIdWithReport(submissionId, ct)
            ?? throw new InvalidOperationException($"Submission {submissionId} not found.");

        if (submission.Status != SubmissionStatus.Accepted
            && submission.Status != SubmissionStatus.AcceptedWithWarnings)
        {
            throw new InvalidOperationException(
                $"Submission must be in Accepted or AcceptedWithWarnings status. Current: {submission.Status}");
        }

        if (!_adapterMap.TryGetValue(regulatorCode, out var adapter))
        {
            throw new InvalidOperationException($"No adapter registered for regulator '{regulatorCode}'.");
        }

        if (!_clientMap.TryGetValue(regulatorCode, out var client))
        {
            throw new InvalidOperationException($"No API client registered for regulator '{regulatorCode}'.");
        }

        // Step 2: Create tracking record
        var directSub = new DirectSubmission
        {
            TenantId = submission.TenantId,
            SubmissionId = submissionId,
            RegulatorCode = regulatorCode,
            Channel = SubmissionChannel.DirectApi,
            Status = DirectSubmissionStatus.Packaging,
            CreatedBy = submittedBy
        };
        await _directSubmissionRepo.Add(directSub, ct);

        try
        {
            // Step 3: Generate evidence package
            await _evidenceService.GenerateAsync(submissionId, submittedBy, ct);

            // Step 4: Package via existing adapter
            var preferredFormat = string.Equals(regulatorCode, "NFIU", StringComparison.OrdinalIgnoreCase)
                ? ExportFormat.XML
                : ExportFormat.Excel;
            var packageBytes = await adapter.Package(submission, preferredFormat, ct);

            // Step 5: Store package
            var storagePath = $"direct-submissions/{submission.TenantId}/{submissionId}/{directSub.Id}.pkg";
            await using (var stream = new MemoryStream(packageBytes))
            {
                await _fileStorage.UploadAsync(storagePath, stream, "application/octet-stream", ct);
            }

            directSub.PackageStoragePath = storagePath;
            directSub.PackageSizeBytes = packageBytes.LongLength;
            directSub.PackageSha256 = Convert.ToHexStringLower(SHA256.HashData(packageBytes));

            // Step 6: Digital signature
            directSub.Status = DirectSubmissionStatus.Signing;
            await _directSubmissionRepo.Update(directSub, ct);

            byte[]? signatureBytes = null;
            var sigResult = await _signatureService.SignPackageAsync(packageBytes, regulatorCode, ct);
            if (sigResult.Success)
            {
                signatureBytes = sigResult.Signature;
                directSub.SignatureAlgorithm = sigResult.Algorithm;
                directSub.SignatureHash = sigResult.Hash;
                directSub.CertificateThumbprint = sigResult.CertificateThumbprint;
                directSub.SignedAt = sigResult.SignedAt;
            }

            // Step 7: Submit via API client
            directSub.Status = DirectSubmissionStatus.Submitting;
            directSub.AttemptCount = 1;
            directSub.LastAttemptAt = DateTime.UtcNow;
            await _directSubmissionRepo.Update(directSub, ct);

            var context = BuildSubmissionContext(submission);
            var response = await client.SubmitAsync(packageBytes, signatureBytes, context, ct);

            // Step 8: Process response
            directSub.HttpStatusCode = response.HttpStatusCode;
            directSub.RegulatorResponseBody = response.RawResponseBody;

            if (response.Success)
            {
                directSub.Status = DirectSubmissionStatus.Submitted;
                directSub.RegulatorReference = response.Reference;
                directSub.SubmittedAt = DateTime.UtcNow;

                submission.MarkSubmittedToRegulator();
                await _submissionRepo.Update(submission, ct);

                _logger.LogInformation(
                    "Direct submission {DirectSubId} to {Regulator} succeeded. Reference: {Reference}",
                    directSub.Id, regulatorCode, response.Reference);
            }
            else
            {
                directSub.ErrorMessage = response.Message;
                ScheduleRetry(directSub);

                _logger.LogWarning(
                    "Direct submission {DirectSubId} to {Regulator} failed: {Message}",
                    directSub.Id, regulatorCode, response.Message);
            }

            await _directSubmissionRepo.Update(directSub, ct);

            // Step 9: Audit
            await _auditLogger.Log("DirectSubmission", directSub.Id, "Submit",
                null,
                new { directSub.RegulatorCode, Status = directSub.Status.ToString(), directSub.RegulatorReference },
                submittedBy, ct);

            // Step 10: Notifications & events
            if (response.Success)
            {
                await NotifySubmissionSuccessAsync(submission, directSub, ct);
                await PublishDirectSubmittedEventAsync(submission, directSub, ct);
            }

            return new DirectSubmissionResult
            {
                Success = response.Success,
                DirectSubmissionId = directSub.Id,
                RegulatorReference = response.Reference,
                Status = directSub.Status.ToString(),
                Message = response.Message,
                SubmittedAt = directSub.SubmittedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Direct submission {DirectSubId} to {Regulator} failed with exception",
                directSub.Id, regulatorCode);

            directSub.ErrorMessage = ex.Message;
            directSub.Status = DirectSubmissionStatus.Failed;
            ScheduleRetry(directSub);
            await _directSubmissionRepo.Update(directSub, ct);

            return new DirectSubmissionResult
            {
                Success = false,
                DirectSubmissionId = directSub.Id,
                Status = directSub.Status.ToString(),
                Message = ex.Message
            };
        }
    }

    public async Task<DirectSubmissionResult> RetrySubmissionAsync(
        int directSubmissionId, CancellationToken ct = default)
    {
        var directSub = await _directSubmissionRepo.GetByIdWithSubmission(directSubmissionId, ct)
            ?? throw new InvalidOperationException($"Direct submission {directSubmissionId} not found.");

        if (directSub.Status != DirectSubmissionStatus.RetryScheduled
            && directSub.Status != DirectSubmissionStatus.Failed
            && directSub.Status != DirectSubmissionStatus.Exhausted)
        {
            return new DirectSubmissionResult
            {
                Success = false,
                DirectSubmissionId = directSub.Id,
                Status = directSub.Status.ToString(),
                Message = "Submission is not in a retryable state."
            };
        }

        if (!_clientMap.TryGetValue(directSub.RegulatorCode, out var client))
        {
            return new DirectSubmissionResult
            {
                Success = false,
                DirectSubmissionId = directSub.Id,
                Message = $"No API client for regulator '{directSub.RegulatorCode}'."
            };
        }

        // Load the stored package
        byte[] packageBytes;
        try
        {
            var stream = await _fileStorage.DownloadAsync(directSub.PackageStoragePath!, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            packageBytes = ms.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load package for retry of direct submission {Id}", directSub.Id);
            return new DirectSubmissionResult
            {
                Success = false,
                DirectSubmissionId = directSub.Id,
                Message = "Failed to load stored package for retry."
            };
        }

        // Re-sign if needed
        byte[]? signatureBytes = null;
        if (!string.IsNullOrWhiteSpace(directSub.SignatureAlgorithm))
        {
            var sigResult = await _signatureService.SignPackageAsync(packageBytes, directSub.RegulatorCode, ct);
            if (sigResult.Success)
            {
                signatureBytes = sigResult.Signature;
            }
        }

        // Retry the submission
        directSub.Status = DirectSubmissionStatus.Submitting;
        directSub.AttemptCount++;
        directSub.LastAttemptAt = DateTime.UtcNow;
        await _directSubmissionRepo.Update(directSub, ct);

        var context = BuildSubmissionContext(directSub.Submission!);
        var response = await client.SubmitAsync(packageBytes, signatureBytes, context, ct);

        directSub.HttpStatusCode = response.HttpStatusCode;
        directSub.RegulatorResponseBody = response.RawResponseBody;

        if (response.Success)
        {
            directSub.Status = DirectSubmissionStatus.Submitted;
            directSub.RegulatorReference = response.Reference;
            directSub.SubmittedAt = DateTime.UtcNow;
            directSub.NextRetryAt = null;

            if (directSub.Submission is not null)
            {
                directSub.Submission.MarkSubmittedToRegulator();
                await _submissionRepo.Update(directSub.Submission, ct);
            }
        }
        else
        {
            directSub.ErrorMessage = response.Message;
            ScheduleRetry(directSub);
        }

        await _directSubmissionRepo.Update(directSub, ct);

        await _auditLogger.Log("DirectSubmission", directSub.Id, "Retry",
            null,
            new { directSub.AttemptCount, Status = directSub.Status.ToString(), directSub.RegulatorReference },
            "system", ct);

        return new DirectSubmissionResult
        {
            Success = response.Success,
            DirectSubmissionId = directSub.Id,
            RegulatorReference = response.Reference,
            Status = directSub.Status.ToString(),
            Message = response.Message,
            SubmittedAt = directSub.SubmittedAt
        };
    }

    public async Task<DirectSubmissionStatusResult> CheckStatusAsync(
        int directSubmissionId, CancellationToken ct = default)
    {
        var directSub = await _directSubmissionRepo.GetById(directSubmissionId, ct)
            ?? throw new InvalidOperationException($"Direct submission {directSubmissionId} not found.");

        var result = new DirectSubmissionStatusResult
        {
            DirectSubmissionId = directSub.Id,
            Status = directSub.Status.ToString(),
            RegulatorCode = directSub.RegulatorCode,
            RegulatorReference = directSub.RegulatorReference,
            LatestMessage = directSub.ErrorMessage
        };

        // If submitted and we have a reference, do a live status check
        if (!string.IsNullOrWhiteSpace(directSub.RegulatorReference)
            && _clientMap.TryGetValue(directSub.RegulatorCode, out var client)
            && (directSub.Status == DirectSubmissionStatus.Submitted
                || directSub.Status == DirectSubmissionStatus.Acknowledged))
        {
            try
            {
                var statusResponse = await client.CheckStatusAsync(directSub.RegulatorReference, ct);
                result.LatestMessage = statusResponse.Message;
                result.LastCheckedAt = DateTime.UtcNow;

                foreach (var q in statusResponse.Queries)
                {
                    result.Queries.Add(q);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live status check failed for direct submission {Id}", directSub.Id);
                result.LatestMessage = $"Live status check failed: {ex.Message}";
            }
        }

        return result;
    }

    public async Task<List<DirectSubmission>> GetSubmissionHistoryAsync(
        Guid tenantId, int submissionId, CancellationToken ct = default)
    {
        return await _directSubmissionRepo.GetByTenantAndSubmission(tenantId, submissionId, ct);
    }

    private void ScheduleRetry(DirectSubmission directSub)
    {
        if (directSub.AttemptCount >= directSub.MaxAttempts)
        {
            directSub.Status = DirectSubmissionStatus.Exhausted;
            directSub.NextRetryAt = null;
            return;
        }

        directSub.Status = DirectSubmissionStatus.RetryScheduled;
        directSub.NextRetryAt = DateTime.UtcNow.Add(directSub.AttemptCount switch
        {
            1 => TimeSpan.FromMinutes(5),
            2 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromHours(2)
        });
    }

    private static RegulatorSubmissionContext BuildSubmissionContext(Submission submission)
    {
        return new RegulatorSubmissionContext
        {
            SubmissionId = submission.Id,
            InstitutionCode = submission.Institution?.InstitutionCode ?? string.Empty,
            InstitutionName = submission.Institution?.InstitutionName ?? string.Empty,
            ReturnCode = submission.ReturnCode,
            PeriodLabel = submission.ReturnPeriod is not null
                ? $"{submission.ReturnPeriod.Year}-{submission.ReturnPeriod.Month:D2}"
                : string.Empty,
            TenantId = submission.TenantId
        };
    }

    private async Task NotifySubmissionSuccessAsync(
        Submission submission, DirectSubmission directSub, CancellationToken ct)
    {
        if (_notificationOrchestrator is null) return;

        try
        {
            await _notificationOrchestrator.Notify(new NotificationRequest
            {
                TenantId = submission.TenantId,
                EventType = "return.direct_submitted",
                Title = $"Return submitted to {directSub.RegulatorCode}",
                Message = $"Return {submission.ReturnCode} has been submitted to {directSub.RegulatorCode}. Reference: {directSub.RegulatorReference}",
                Priority = Domain.Enums.NotificationPriority.High,
                ActionUrl = $"/submissions/{submission.Id}",
                RecipientInstitutionId = submission.InstitutionId,
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RegulatorCode"] = directSub.RegulatorCode,
                    ["RegulatorReference"] = directSub.RegulatorReference ?? string.Empty,
                    ["SubmissionId"] = submission.Id.ToString()
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification for direct submission {Id}", directSub.Id);
        }
    }

    private async Task PublishDirectSubmittedEventAsync(
        Submission submission, DirectSubmission directSub, CancellationToken ct)
    {
        if (_domainEventPublisher is null) return;

        try
        {
            await _domainEventPublisher.PublishAsync(new ReturnDirectSubmittedEvent(
                TenantId: submission.TenantId,
                SubmissionId: submission.Id,
                ModuleCode: string.Empty,
                ReturnCode: submission.ReturnCode,
                RegulatorCode: directSub.RegulatorCode,
                RegulatorReference: directSub.RegulatorReference ?? string.Empty,
                SubmittedAt: directSub.SubmittedAt ?? DateTime.UtcNow,
                OccurredAt: DateTime.UtcNow,
                CorrelationId: Guid.NewGuid()
            ), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish domain event for direct submission {Id}", directSub.Id);
        }
    }
}
