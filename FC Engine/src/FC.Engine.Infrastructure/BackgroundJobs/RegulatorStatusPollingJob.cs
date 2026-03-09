using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Export;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public class RegulatorStatusPollingJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RegulatorStatusPollingJob> _logger;

    public RegulatorStatusPollingJob(
        IServiceProvider serviceProvider,
        ILogger<RegulatorStatusPollingJob> logger)
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
                var settings = scope.ServiceProvider.GetRequiredService<IOptions<RegulatoryApiSettings>>().Value;
                intervalSeconds = settings.StatusPolling.IntervalSeconds;

                if (settings.Enabled)
                {
                    await PollStatuses(scope.ServiceProvider, settings, stoppingToken);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Regulator status polling cycle failed");
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

    private async Task PollStatuses(
        IServiceProvider sp, RegulatoryApiSettings settings, CancellationToken ct)
    {
        var repo = sp.GetRequiredService<IDirectSubmissionRepository>();
        var clients = sp.GetServices<IRegulatorApiClient>()
            .ToDictionary(c => c.RegulatorCode, StringComparer.OrdinalIgnoreCase);
        var submissionRepo = sp.GetRequiredService<ISubmissionRepository>();
        var auditLogger = sp.GetRequiredService<IAuditLogger>();
        var notifier = sp.GetService<INotificationOrchestrator>();
        var db = sp.GetRequiredService<MetadataDbContext>();

        var awaitingStatus = await repo.GetSubmittedAwaitingStatus(settings.StatusPolling.BatchSize, ct);
        if (awaitingStatus.Count == 0) return;

        _logger.LogInformation("Polling status for {Count} direct submissions", awaitingStatus.Count);

        foreach (var ds in awaitingStatus)
        {
            if (string.IsNullOrWhiteSpace(ds.RegulatorReference)) continue;
            if (!clients.TryGetValue(ds.RegulatorCode, out var client)) continue;

            try
            {
                var statusResponse = await client.CheckStatusAsync(ds.RegulatorReference, ct);
                var previousStatus = ds.Status;
                ds.Status = MapRegulatorStatus(statusResponse.Status);

                if (ds.Status == DirectSubmissionStatus.Acknowledged
                    && previousStatus != DirectSubmissionStatus.Acknowledged)
                {
                    ds.AcknowledgedAt = DateTime.UtcNow;
                    var submission = await submissionRepo.GetById(ds.SubmissionId, ct);
                    if (submission is not null)
                    {
                        submission.MarkRegulatorAcknowledged();
                        await submissionRepo.Update(submission, ct);
                    }
                }

                if (ds.Status == DirectSubmissionStatus.Accepted
                    && previousStatus != DirectSubmissionStatus.Accepted)
                {
                    var submission = await submissionRepo.GetById(ds.SubmissionId, ct);
                    if (submission is not null)
                    {
                        submission.MarkRegulatorAccepted();
                        await submissionRepo.Update(submission, ct);
                    }
                }

                // Route queries from regulator
                if (statusResponse.Queries.Count > 0)
                {
                    await RouteQueriesAsync(ds, statusResponse.Queries, db, submissionRepo, notifier, ct);
                }

                await repo.Update(ds, ct);

                if (previousStatus != ds.Status)
                {
                    await auditLogger.Log("DirectSubmission", ds.Id, "StatusPoll",
                        new { PreviousStatus = previousStatus.ToString() },
                        new { NewStatus = ds.Status.ToString() },
                        "system", ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Status poll failed for direct submission {Id}", ds.Id);
            }
        }
    }

    private async Task RouteQueriesAsync(
        DirectSubmission ds,
        List<RegulatorQueryInfo> queries,
        MetadataDbContext db,
        ISubmissionRepository submissionRepo,
        INotificationOrchestrator? notifier,
        CancellationToken ct)
    {
        foreach (var query in queries)
        {
            // Idempotency: check if we already routed this query
            var exists = await db.ExaminerQueries
                .AnyAsync(q => q.SubmissionId == ds.SubmissionId
                    && q.QueryText == query.QueryText, ct);

            if (exists) continue;

            var examinerQuery = new ExaminerQuery
            {
                TenantId = ds.TenantId,
                RegulatorTenantId = Guid.Empty, // System-routed from regulator API
                SubmissionId = ds.SubmissionId,
                QueryText = query.QueryText,
                RaisedBy = 0,
                RaisedAt = query.RaisedAt,
                Status = ExaminerQueryStatus.Open,
                Priority = MapPriority(query.Priority)
            };

            db.ExaminerQueries.Add(examinerQuery);
            await db.SaveChangesAsync(ct);

            // Update submission status
            var submission = await submissionRepo.GetById(ds.SubmissionId, ct);
            if (submission is not null && submission.Status != SubmissionStatus.RegulatorQueriesRaised)
            {
                submission.MarkRegulatorQueriesRaised();
                await submissionRepo.Update(submission, ct);
            }

            // Notify institution
            if (notifier is not null)
            {
                try
                {
                    await notifier.Notify(new NotificationRequest
                    {
                        TenantId = ds.TenantId,
                        EventType = "return.regulator_query_routed",
                        Title = $"Regulator Query — {ds.RegulatorCode}",
                        Message = $"A query has been raised by {ds.RegulatorCode} for submission #{ds.SubmissionId}.",
                        Priority = NotificationPriority.High,
                        ActionUrl = $"/submissions/{ds.SubmissionId}",
                        RecipientInstitutionId = submission?.InstitutionId,
                        Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["RegulatorCode"] = ds.RegulatorCode,
                            ["QueryText"] = query.QueryText
                        }
                    }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to notify institution about routed query");
                }
            }
        }
    }

    private static DirectSubmissionStatus MapRegulatorStatus(string status) =>
        status.ToUpperInvariant() switch
        {
            "RECEIVED" or "ACKNOWLEDGED" => DirectSubmissionStatus.Acknowledged,
            "UNDER_REVIEW" or "PROCESSING" => DirectSubmissionStatus.Acknowledged,
            "ACCEPTED" or "FINAL_ACCEPTED" => DirectSubmissionStatus.Accepted,
            "REJECTED" => DirectSubmissionStatus.Rejected,
            "QUALITY_FEEDBACK" => DirectSubmissionStatus.QualityFeedback,
            _ => DirectSubmissionStatus.Submitted
        };

    private static ExaminerQueryPriority MapPriority(string priority) =>
        priority.ToUpperInvariant() switch
        {
            "CRITICAL" => ExaminerQueryPriority.Critical,
            "HIGH" => ExaminerQueryPriority.High,
            "LOW" => ExaminerQueryPriority.Low,
            _ => ExaminerQueryPriority.Normal
        };
}
