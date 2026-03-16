using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class RegulatorInboxService : IRegulatorInboxService
{
    private static readonly IReadOnlyDictionary<RegulatorReceiptStatus, HashSet<RegulatorReceiptStatus>> AllowedTransitions
        = new Dictionary<RegulatorReceiptStatus, HashSet<RegulatorReceiptStatus>>
        {
            [RegulatorReceiptStatus.Received] = new()
            {
                RegulatorReceiptStatus.UnderReview,
                RegulatorReceiptStatus.QueriesRaised
            },
            [RegulatorReceiptStatus.UnderReview] = new()
            {
                RegulatorReceiptStatus.Accepted,
                RegulatorReceiptStatus.FinalAccepted,
                RegulatorReceiptStatus.QueriesRaised
            },
            [RegulatorReceiptStatus.Accepted] = new() { RegulatorReceiptStatus.FinalAccepted },
            [RegulatorReceiptStatus.QueriesRaised] = new() { RegulatorReceiptStatus.ResponseReceived },
            [RegulatorReceiptStatus.ResponseReceived] = new() { RegulatorReceiptStatus.UnderReview },
            [RegulatorReceiptStatus.FinalAccepted] = new()
        };

    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly INotificationOrchestrator _notifications;
    private readonly ILogger<RegulatorInboxService> _logger;

    public RegulatorInboxService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        INotificationOrchestrator notifications,
        ILogger<RegulatorInboxService> logger)
    {
        _dbFactory = dbFactory;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RegulatorSubmissionInboxItem>> GetInbox(
        Guid regulatorTenantId,
        string regulatorCode,
        RegulatorInboxFilter? filter = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        filter ??= new RegulatorInboxFilter();

        var scoped = BuildScopedSubmissionQuery(db, regulatorCode);

        if (!string.IsNullOrWhiteSpace(filter.InstitutionName))
        {
            var q = filter.InstitutionName.Trim();
            scoped = scoped.Where(s => s.Institution != null && s.Institution.InstitutionName.Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(filter.LicenceType))
        {
            var q = filter.LicenceType.Trim();
            scoped = scoped.Where(s => s.Institution != null && s.Institution.LicenseType == q);
        }

        if (!string.IsNullOrWhiteSpace(filter.ModuleCode))
        {
            var q = filter.ModuleCode.Trim();
            scoped = scoped.Where(s => s.ReturnPeriod != null
                                       && s.ReturnPeriod.Module != null
                                       && s.ReturnPeriod.Module.ModuleCode == q);
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var statusText = filter.Status.Trim();
            if (Enum.TryParse<RegulatorReceiptStatus>(statusText, true, out var receiptStatus))
            {
                if (receiptStatus == RegulatorReceiptStatus.Received)
                {
                    var nonReceivedSubmissionIds = await db.RegulatorReceipts
                        .AsNoTracking()
                        .Where(r => r.RegulatorTenantId == regulatorTenantId && r.Status != RegulatorReceiptStatus.Received)
                        .Select(r => r.SubmissionId)
                        .ToListAsync(ct);

                    scoped = scoped.Where(s => !nonReceivedSubmissionIds.Contains(s.Id));
                }
                else
                {
                    var submissionIdsWithStatus = await db.RegulatorReceipts
                        .AsNoTracking()
                        .Where(r => r.RegulatorTenantId == regulatorTenantId && r.Status == receiptStatus)
                        .Select(r => r.SubmissionId)
                        .ToListAsync(ct);

                    scoped = scoped.Where(s => submissionIdsWithStatus.Contains(s.Id));
                }
            }
            else
            {
                scoped = scoped.Where(s => s.Status.ToString() == statusText);
            }
        }

        if (RegulatorAnalyticsSupport.TryParsePeriodCode(filter.PeriodCode, out var periodFilter) && periodFilter is not null)
        {
            scoped = scoped.Where(s => s.ReturnPeriod != null && s.ReturnPeriod.Year == periodFilter.Year);

            if (periodFilter.Quarter.HasValue)
            {
                var quarter = periodFilter.Quarter.Value;
                scoped = scoped.Where(s => s.ReturnPeriod != null && (s.ReturnPeriod.Quarter ?? ((s.ReturnPeriod.Month - 1) / 3 + 1)) == quarter);
            }
            else if (periodFilter.Month.HasValue)
            {
                var month = periodFilter.Month.Value;
                scoped = scoped.Where(s => s.ReturnPeriod != null && s.ReturnPeriod.Month == month);
            }
        }

        var rows = await scoped
            .OrderByDescending(s => s.SubmittedAt)
            .Take(1000)
            .Select(s => new
            {
                s.Id,
                s.TenantId,
                s.InstitutionId,
                InstitutionName = s.Institution != null ? s.Institution.InstitutionName : "Unknown",
                LicenceType = s.Institution != null ? s.Institution.LicenseType : null,
                ModuleCode = s.ReturnPeriod != null && s.ReturnPeriod.Module != null ? s.ReturnPeriod.Module.ModuleCode : "N/A",
                ModuleName = s.ReturnPeriod != null && s.ReturnPeriod.Module != null ? s.ReturnPeriod.Module.ModuleName : "Unknown",
                Year = s.ReturnPeriod != null ? s.ReturnPeriod.Year : 0,
                Month = s.ReturnPeriod != null ? s.ReturnPeriod.Month : 1,
                Quarter = s.ReturnPeriod != null ? s.ReturnPeriod.Quarter : null,
                s.SubmittedAt,
                SubmissionStatus = s.Status.ToString()
            })
            .ToListAsync(ct);

        var submissionIds = rows.Select(x => x.Id).ToList();

        var receipts = await db.RegulatorReceipts
            .AsNoTracking()
            .Where(r => r.RegulatorTenantId == regulatorTenantId && submissionIds.Contains(r.SubmissionId))
            .ToDictionaryAsync(x => x.SubmissionId, ct);

        var openQueryCounts = await db.ExaminerQueries
            .AsNoTracking()
            .Where(q => q.RegulatorTenantId == regulatorTenantId && submissionIds.Contains(q.SubmissionId))
            .Where(q => q.Status == ExaminerQueryStatus.Open || q.Status == ExaminerQueryStatus.Escalated)
            .GroupBy(q => q.SubmissionId)
            .Select(g => new { SubmissionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SubmissionId, x => x.Count, ct);

        var result = rows.Select(row =>
        {
            var period = new ReturnPeriod { Year = row.Year, Month = row.Month, Quarter = row.Quarter };
            return new RegulatorSubmissionInboxItem
            {
                SubmissionId = row.Id,
                TenantId = row.TenantId,
                InstitutionId = row.InstitutionId,
                InstitutionName = row.InstitutionName,
                LicenceType = row.LicenceType ?? "N/A",
                ModuleCode = row.ModuleCode,
                ModuleName = row.ModuleName,
                PeriodLabel = RegulatorAnalyticsSupport.FormatPeriodLabel(period),
                SubmittedAt = row.SubmittedAt ?? default,
                SubmissionStatus = row.SubmissionStatus,
                ReceiptStatus = receipts.TryGetValue(row.Id, out var receipt)
                    ? receipt.Status
                    : RegulatorReceiptStatus.Received,
                OpenQueryCount = openQueryCounts.GetValueOrDefault(row.Id)
            };
        }).ToList();

        return result;
    }

    public async Task<RegulatorSubmissionDetail?> GetSubmissionDetail(
        Guid regulatorTenantId,
        string regulatorCode,
        int submissionId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var submission = await BuildScopedSubmissionQuery(db, regulatorCode)
            .Include(s => s.ValidationReport)
                .ThenInclude(r => r!.Errors)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct);

        if (submission is null || submission.ReturnPeriod is null || submission.Institution is null)
        {
            return null;
        }

        var inboxItem = new RegulatorSubmissionInboxItem
        {
            SubmissionId = submission.Id,
            TenantId = submission.TenantId,
            InstitutionId = submission.InstitutionId,
            InstitutionName = submission.Institution.InstitutionName,
            LicenceType = submission.Institution.LicenseType ?? "N/A",
            ModuleCode = submission.ReturnPeriod.Module?.ModuleCode ?? "N/A",
            ModuleName = submission.ReturnPeriod.Module?.ModuleName ?? "Unknown",
            PeriodLabel = RegulatorAnalyticsSupport.FormatPeriodLabel(submission.ReturnPeriod),
            SubmittedAt = submission.SubmittedAt ?? default,
            SubmissionStatus = submission.Status.ToString(),
            ReceiptStatus = RegulatorReceiptStatus.Received
        };

        var receipt = await db.RegulatorReceipts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RegulatorTenantId == regulatorTenantId && x.SubmissionId == submissionId, ct);

        if (receipt is not null)
        {
            inboxItem.ReceiptStatus = receipt.Status;
        }

        var queries = await db.ExaminerQueries
            .AsNoTracking()
            .Where(q => q.RegulatorTenantId == regulatorTenantId && q.SubmissionId == submissionId)
            .OrderByDescending(q => q.RaisedAt)
            .ToListAsync(ct);

        var topErrors = submission.ValidationReport?.Errors
            .GroupBy(e => new { e.RuleId, e.Field })
            .Select(g => new ValidationErrorAggregate
            {
                RuleId = g.Key.RuleId,
                Field = g.Key.Field,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToList() ?? new List<ValidationErrorAggregate>();

        return new RegulatorSubmissionDetail
        {
            Header = inboxItem,
            Receipt = receipt,
            Queries = queries,
            TopValidationErrors = topErrors
        };
    }

    public async Task<RegulatorReceipt> UpdateReceiptStatus(
        Guid regulatorTenantId,
        int submissionId,
        RegulatorReceiptStatus status,
        int reviewedBy,
        string? notes,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var receipt = await db.RegulatorReceipts
            .FirstOrDefaultAsync(r => r.RegulatorTenantId == regulatorTenantId && r.SubmissionId == submissionId, ct);

        if (receipt is null)
        {
            var submissionTenantId = await db.Submissions
                .AsNoTracking()
                .Where(s => s.Id == submissionId)
                .Select(s => s.TenantId)
                .FirstOrDefaultAsync(ct);

            if (submissionTenantId == Guid.Empty)
            {
                throw new InvalidOperationException($"Submission {submissionId} was not found.");
            }

            receipt = new RegulatorReceipt
            {
                TenantId = submissionTenantId,
                RegulatorTenantId = regulatorTenantId,
                SubmissionId = submissionId,
                Status = RegulatorReceiptStatus.Received,
                ReceivedAt = DateTime.UtcNow
            };

            db.RegulatorReceipts.Add(receipt);
        }

        if (!IsTransitionAllowed(receipt.Status, status))
        {
            throw new InvalidOperationException($"Invalid receipt transition: {receipt.Status} -> {status}.");
        }

        receipt.Status = status;
        receipt.ReviewedBy = reviewedBy;
        receipt.ReviewedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
        {
            receipt.Notes = notes.Trim();
        }

        if (status == RegulatorReceiptStatus.Accepted)
        {
            receipt.AcceptedAt = DateTime.UtcNow;
        }

        if (status == RegulatorReceiptStatus.FinalAccepted)
        {
            receipt.FinalAcceptedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return receipt;
    }

    public async Task<IReadOnlyList<ExaminerQuery>> GetQueries(Guid regulatorTenantId, int submissionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ExaminerQueries
            .AsNoTracking()
            .Where(q => q.RegulatorTenantId == regulatorTenantId && q.SubmissionId == submissionId)
            .OrderByDescending(q => q.RaisedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExaminerQuery>> GetSubmissionQueries(int submissionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ExaminerQueries
            .AsNoTracking()
            .Where(q => q.SubmissionId == submissionId)
            .OrderByDescending(q => q.RaisedAt)
            .ToListAsync(ct);
    }

    public async Task<ExaminerQuery> RaiseQuery(
        Guid regulatorTenantId,
        int submissionId,
        string? fieldCode,
        string queryText,
        int raisedBy,
        ExaminerQueryPriority priority = ExaminerQueryPriority.Normal,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new ArgumentException("Query text is required.", nameof(queryText));
        }

        var submission = await db.Submissions
            .Include(s => s.Institution)
            .FirstOrDefaultAsync(s => s.Id == submissionId, ct)
            ?? throw new InvalidOperationException($"Submission {submissionId} not found.");

        var query = new ExaminerQuery
        {
            TenantId = submission.TenantId,
            RegulatorTenantId = regulatorTenantId,
            SubmissionId = submissionId,
            FieldCode = string.IsNullOrWhiteSpace(fieldCode) ? null : fieldCode.Trim(),
            QueryText = queryText.Trim(),
            RaisedBy = raisedBy,
            RaisedAt = DateTime.UtcNow,
            Status = ExaminerQueryStatus.Open,
            Priority = priority
        };

        db.ExaminerQueries.Add(query);
        await db.SaveChangesAsync(ct);

        await UpdateReceiptStatus(
            regulatorTenantId,
            submissionId,
            RegulatorReceiptStatus.QueriesRaised,
            raisedBy,
            "Examiner query raised.",
            ct);

        await _notifications.Notify(new NotificationRequest
        {
            TenantId = submission.TenantId,
            EventType = NotificationEvents.ReturnQueryRaised,
            Title = $"Regulator query raised for {submission.ReturnCode}",
            Message = $"A regulator examiner raised a query for submission #{submission.Id}.",
            Priority = NotificationPriority.High,
            RecipientInstitutionId = submission.InstitutionId,
            ActionUrl = $"/submissions/{submission.Id}"
        }, ct);

        return query;
    }

    public async Task<ExaminerQuery?> RespondToQuery(
        Guid regulatorTenantId,
        int queryId,
        int respondedBy,
        string responseText,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new ArgumentException("Response text is required.", nameof(responseText));
        }

        var query = await db.ExaminerQueries
            .Include(q => q.Submission)
            .FirstOrDefaultAsync(q => q.Id == queryId && q.RegulatorTenantId == regulatorTenantId, ct);

        if (query is null)
        {
            return null;
        }
        return await SaveQueryResponse(db, query, respondedBy, responseText, ct);
    }

    public async Task<ExaminerQuery?> RespondToQueryAsInstitution(
        int queryId,
        int respondedBy,
        string responseText,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new ArgumentException("Response text is required.", nameof(responseText));
        }

        var query = await db.ExaminerQueries
            .Include(q => q.Submission)
            .FirstOrDefaultAsync(q => q.Id == queryId, ct);

        if (query is null)
        {
            return null;
        }

        return await SaveQueryResponse(db, query, respondedBy, responseText, ct);
    }

    private static IQueryable<Submission> BuildScopedSubmissionQuery(MetadataDbContext db, string regulatorCode)
    {
        var code = regulatorCode.Trim();

        return db.Submissions
            .AsNoTracking()
            .Include(s => s.Institution)
            .Include(s => s.ReturnPeriod)
                .ThenInclude(rp => rp!.Module)
            .Where(s => s.ReturnPeriod != null
                        && s.ReturnPeriod.Module != null
                        && s.ReturnPeriod.Module.RegulatorCode == code);
    }

    private static bool IsTransitionAllowed(RegulatorReceiptStatus current, RegulatorReceiptStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return AllowedTransitions.TryGetValue(current, out var allowed)
               && allowed.Contains(next);
    }

    private async Task<ExaminerQuery> SaveQueryResponse(
        MetadataDbContext db,
        ExaminerQuery query,
        int respondedBy,
        string responseText,
        CancellationToken ct)
    {
        query.ResponseText = responseText.Trim();
        query.RespondedBy = respondedBy;
        query.RespondedAt = DateTime.UtcNow;
        query.Status = ExaminerQueryStatus.Responded;

        await db.SaveChangesAsync(ct);

        await UpdateReceiptStatus(
            query.RegulatorTenantId,
            query.SubmissionId,
            RegulatorReceiptStatus.ResponseReceived,
            respondedBy,
            "Institution response received.",
            ct);

        await _notifications.Notify(new NotificationRequest
        {
            TenantId = query.RegulatorTenantId,
            EventType = NotificationEvents.SystemAnnouncement,
            Title = $"Institution response received for submission #{query.SubmissionId}",
            Message = "An institution has responded to your examiner query.",
            Priority = NotificationPriority.High,
            RecipientPortalUserIds = new() { query.RaisedBy },
            ActionUrl = $"/inbox/{query.SubmissionId}"
        }, ct);

        _logger.LogInformation(
            "Submission response recorded for examiner query {QueryId} (submission {SubmissionId})",
            query.Id,
            query.SubmissionId);

        return query;
    }
}
