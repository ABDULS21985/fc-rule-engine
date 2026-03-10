using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Audit;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FC.Engine.Infrastructure.Services;

public class DsarService : IDsarService
{
    private readonly MetadataDbContext _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogger _auditLogger;
    private readonly PrivacyComplianceOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DsarService(
        MetadataDbContext db,
        IFileStorageService fileStorage,
        IAuditLogger auditLogger,
        IOptions<PrivacyComplianceOptions> options)
    {
        _db = db;
        _fileStorage = fileStorage;
        _auditLogger = auditLogger;
        _options = options.Value;
    }

    public async Task<DataSubjectRequest> CreateRequest(
        Guid tenantId,
        DataSubjectRequestType requestType,
        int requestedBy,
        string requestedByUserType,
        string? description,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var request = new DataSubjectRequest
        {
            TenantId = tenantId,
            RequestType = requestType,
            RequestedBy = requestedBy,
            RequestedByUserType = string.IsNullOrWhiteSpace(requestedByUserType) ? "InstitutionUser" : requestedByUserType.Trim(),
            Status = requestType == DataSubjectRequestType.Erasure
                ? DataSubjectRequestStatus.PendingApproval
                : DataSubjectRequestStatus.Received,
            Description = description,
            DueDate = now.AddDays(Math.Max(1, _options.DsarDueDays)),
            CreatedAt = now
        };

        _db.DataSubjectRequests.Add(request);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<IReadOnlyList<DataSubjectRequest>> GetRequests(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.DataSubjectRequests
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task<string> GenerateAccessPackage(int dsarId, int processedByUserId, CancellationToken ct = default)
    {
        var dsar = await GetRequest(dsarId, ct);
        if (dsar.RequestType != DataSubjectRequestType.Access)
        {
            throw new InvalidOperationException("DSAR request type must be Access to generate an access package.");
        }

        dsar.Status = DataSubjectRequestStatus.InProgress;
        dsar.ProcessedBy = processedByUserId;
        await _db.SaveChangesAsync(ct);

        var profile = await ResolveProfile(dsar.RequestedBy, dsar.RequestedByUserType, dsar.TenantId, ct);
        var package = await BuildPackage(dsar, profile, ct);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);

        var basePath = $"tenants/{dsar.TenantId}/dsar/{dsar.Id}";
        var jsonPath = $"{basePath}/data_package.json";
        await _fileStorage.UploadAsync(jsonPath, new MemoryStream(jsonBytes), "application/json", ct);

        QuestPDF.Settings.License = LicenseType.Community;
        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Column(col =>
                {
                    col.Item().Text("Data Subject Access Request Package").FontSize(20).Bold();
                    col.Item().PaddingTop(5).Text($"Request ID: {dsar.Id}");
                    col.Item().Text($"Generated: {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC");
                    col.Item().Text($"User: {profile.DisplayName} ({profile.Email})");
                    col.Item().PaddingTop(15).Text("Package Summary").FontSize(14).Bold();
                    col.Item().Text($"Return Data Entries: {package.ReturnDataEntries.Count}");
                    col.Item().Text($"Workflow Actions: {package.WorkflowActions.Count}");
                    col.Item().Text($"Login Events: {package.LoginHistory.Count}");
                    col.Item().Text($"Notifications: {package.Notifications.Count}");
                    col.Item().Text($"Consent Records: {package.ConsentHistory.Count}");
                });
            });
        }).GeneratePdf();

        var pdfPath = $"{basePath}/data_package_summary.pdf";
        await _fileStorage.UploadAsync(pdfPath, new MemoryStream(pdfBytes), "application/pdf", ct);

        dsar.DataPackagePath = jsonPath;
        dsar.ResponseNotes = $"Summary PDF: {pdfPath}";
        dsar.Status = DataSubjectRequestStatus.Completed;
        dsar.CompletedAt = DateTime.UtcNow;
        dsar.ProcessedBy = processedByUserId;
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "DataSubjectRequest",
            dsar.Id,
            "DSAR_ACCESS_PACKAGE_GENERATED",
            null,
            new { dsar.DataPackagePath, SummaryPdf = pdfPath },
            processedByUserId.ToString(),
            ct);

        return jsonPath;
    }

    public async Task ProcessErasure(int dsarId, int approvedByDpoId, CancellationToken ct = default)
    {
        var dsar = await GetRequest(dsarId, ct);
        if (dsar.RequestType != DataSubjectRequestType.Erasure)
        {
            throw new InvalidOperationException("DSAR request type must be Erasure.");
        }

        if (dsar.Status != DataSubjectRequestStatus.PendingApproval)
        {
            throw new InvalidOperationException("Erasure requires DPO approval gate and pending approval status.");
        }

        var profile = await ResolveProfile(dsar.RequestedBy, dsar.RequestedByUserType, dsar.TenantId, ct);
        var aliases = BuildUserAliases(profile);
        var now = DateTime.UtcNow;
        var anonymisedEmail = $"anonymised_{dsar.RequestedBy}@deleted.regos.app";
        var anonymisedUsername = $"anonymised_{dsar.RequestedBy}";

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var portalUser = await _db.PortalUsers
            .FirstOrDefaultAsync(x =>
                x.Id == dsar.RequestedBy &&
                x.TenantId == dsar.TenantId,
                ct);

        if (portalUser is not null)
        {
            portalUser.Username = anonymisedUsername;
            portalUser.DisplayName = "ANONYMISED USER";
            portalUser.Email = anonymisedEmail;
            portalUser.IsActive = false;
            portalUser.DeletedAt = now;
            portalUser.DeletionReason = $"DSAR Erasure #{dsar.Id}";
        }

        var institutionUser = await _db.InstitutionUsers
            .FirstOrDefaultAsync(x =>
                x.Id == dsar.RequestedBy &&
                x.TenantId == dsar.TenantId,
                ct);

        if (institutionUser is not null)
        {
            institutionUser.Username = anonymisedUsername;
            institutionUser.DisplayName = "ANONYMISED USER";
            institutionUser.Email = anonymisedEmail;
            institutionUser.PhoneNumber = null;
            institutionUser.LastLoginIp = null;
            institutionUser.IsActive = false;
            institutionUser.DeletedAt = now;
            institutionUser.DeletionReason = $"DSAR Erasure #{dsar.Id}";
        }

        var loginAttempts = await _db.LoginAttempts
            .Where(x =>
                x.TenantId == dsar.TenantId &&
                ((x.UserId.HasValue && x.UserId == dsar.RequestedBy) ||
                 aliases.Contains(x.Username)))
            .ToListAsync(ct);

        foreach (var attempt in loginAttempts)
        {
            attempt.Username = anonymisedUsername;
            attempt.IpAddress = "0.0.0.0";
            attempt.UserAgent = "ANONYMISED";
            attempt.UserId = null;
        }

        var fieldChanges = await _db.FieldChangeHistory
            .Where(x => x.TenantId == dsar.TenantId && aliases.Contains(x.ChangedBy))
            .ToListAsync(ct);
        foreach (var fieldChange in fieldChanges)
        {
            fieldChange.ChangedBy = "ANONYMISED";
        }

        var auditEntries = await _db.AuditLog
            .Where(x => x.TenantId == dsar.TenantId && aliases.Contains(x.PerformedBy))
            .ToListAsync(ct);
        foreach (var auditEntry in auditEntries)
        {
            auditEntry.PerformedBy = "ANONYMISED";
            auditEntry.IpAddress = null;
        }

        var notifications = await _db.PortalNotifications
            .Where(x => x.TenantId == dsar.TenantId && x.UserId == dsar.RequestedBy)
            .ToListAsync(ct);
        foreach (var notification in notifications)
        {
            notification.RecipientEmail = null;
            notification.RecipientPhone = null;
            notification.Metadata = null;
        }

        await RehashTenantAuditTrail(dsar.TenantId, ct);

        dsar.Status = DataSubjectRequestStatus.Completed;
        dsar.ProcessedBy = approvedByDpoId;
        dsar.CompletedAt = now;
        dsar.ResponseNotes = $"PII anonymised per DSAR #{dsar.Id}; regulatory data retained.";

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        });

        await _auditLogger.Log(
            "DataSubjectRequest",
            dsar.Id,
            "DSAR_ERASURE",
            null,
            new { UserId = dsar.RequestedBy, dsar.RequestedByUserType, Action = "PII anonymised, regulatory data preserved" },
            approvedByDpoId.ToString(),
            ct);
    }

    public async Task<DataSubjectRequest> UpdateStatus(
        int dsarId,
        DataSubjectRequestStatus status,
        int processedByUserId,
        string? responseNotes,
        CancellationToken ct = default)
    {
        var dsar = await GetRequest(dsarId, ct);

        dsar.Status = status;
        dsar.ProcessedBy = processedByUserId;
        dsar.ResponseNotes = responseNotes;
        if (status is DataSubjectRequestStatus.Completed or DataSubjectRequestStatus.Rejected)
        {
            dsar.CompletedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return dsar;
    }

    private async Task<DataSubjectRequest> GetRequest(int dsarId, CancellationToken ct)
    {
        return await _db.DataSubjectRequests
            .FirstOrDefaultAsync(x => x.Id == dsarId, ct)
            ?? throw new InvalidOperationException($"DSAR request {dsarId} not found.");
    }

    private async Task<DataSubjectProfile> ResolveProfile(int userId, string userType, Guid tenantId, CancellationToken ct)
    {
        if (string.Equals(userType, "PortalUser", StringComparison.OrdinalIgnoreCase))
        {
            var portalUser = await _db.PortalUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId, ct);
            if (portalUser is null)
            {
                throw new InvalidOperationException($"Portal user {userId} not found for tenant {tenantId}.");
            }

            return new DataSubjectProfile
            {
                UserId = portalUser.Id,
                UserType = "PortalUser",
                Username = portalUser.Username,
                DisplayName = portalUser.DisplayName,
                Email = portalUser.Email,
                IsActive = portalUser.IsActive,
                LastLoginAt = portalUser.LastLoginAt,
                CreatedAt = portalUser.CreatedAt
            };
        }

        var institutionUser = await _db.InstitutionUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId && x.TenantId == tenantId, ct);
        if (institutionUser is null)
        {
            throw new InvalidOperationException($"Institution user {userId} not found for tenant {tenantId}.");
        }

        return new DataSubjectProfile
        {
            UserId = institutionUser.Id,
            UserType = "InstitutionUser",
            Username = institutionUser.Username,
            DisplayName = institutionUser.DisplayName,
            Email = institutionUser.Email,
            PhoneNumber = institutionUser.PhoneNumber,
            IsActive = institutionUser.IsActive,
            LastLoginAt = institutionUser.LastLoginAt,
            CreatedAt = institutionUser.CreatedAt
        };
    }

    private async Task<DataSubjectPackage> BuildPackage(DataSubjectRequest dsar, DataSubjectProfile profile, CancellationToken ct)
    {
        var aliases = BuildUserAliases(profile);

        var returnDataEntries = await _db.FieldChangeHistory
            .AsNoTracking()
            .Where(x => x.TenantId == dsar.TenantId && aliases.Contains(x.ChangedBy))
            .OrderByDescending(x => x.ChangedAt)
            .Take(2000)
            .Select(x => new DataEntryAuditItem
            {
                SubmissionId = x.SubmissionId,
                ReturnCode = x.ReturnCode,
                FieldName = x.FieldName,
                OldValue = x.OldValue,
                NewValue = x.NewValue,
                ChangeSource = x.ChangeSource,
                ChangedAt = x.ChangedAt
            })
            .ToListAsync(ct);

        var workflowActions = await _db.SubmissionApprovals
            .AsNoTracking()
            .Where(x =>
                x.TenantId == dsar.TenantId &&
                (x.RequestedByUserId == dsar.RequestedBy || x.ReviewedByUserId == dsar.RequestedBy))
            .OrderByDescending(x => x.RequestedAt)
            .Take(500)
            .Select(x => new WorkflowActionItem
            {
                SubmissionId = x.SubmissionId,
                ActionType = x.ReviewedByUserId.HasValue ? "ReviewAction" : "ApprovalRequest",
                Status = x.Status.ToString(),
                Notes = x.ReviewerComments ?? x.SubmitterNotes,
                ActionAt = x.ReviewedAt ?? x.RequestedAt
            })
            .ToListAsync(ct);

        var loginHistory = await _db.LoginAttempts
            .AsNoTracking()
            .Where(x =>
                x.TenantId == dsar.TenantId &&
                ((x.UserId.HasValue && x.UserId == dsar.RequestedBy) || aliases.Contains(x.Username)))
            .OrderByDescending(x => x.AttemptedAt)
            .Take(1000)
            .Select(x => new LoginHistoryItem
            {
                AttemptedAt = x.AttemptedAt,
                Succeeded = x.Succeeded,
                IpAddress = x.IpAddress,
                UserAgent = x.UserAgent,
                FailureReason = x.FailureReason
            })
            .ToListAsync(ct);

        var notifications = await _db.PortalNotifications
            .AsNoTracking()
            .Where(x =>
                x.TenantId == dsar.TenantId &&
                (x.UserId == dsar.RequestedBy ||
                 (profile.Email != string.Empty && x.RecipientEmail == profile.Email)))
            .OrderByDescending(x => x.CreatedAt)
            .Take(1000)
            .Select(x => new UserNotificationItem
            {
                EventType = x.EventType,
                Channel = x.Channel.ToString(),
                Title = x.Title,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(ct);

        var consentHistory = await _db.ConsentRecords
            .AsNoTracking()
            .Where(x =>
                x.TenantId == dsar.TenantId &&
                x.UserId == dsar.RequestedBy &&
                x.UserType == dsar.RequestedByUserType)
            .OrderByDescending(x => x.ConsentedAt)
            .Select(x => new ConsentHistoryItem
            {
                ConsentType = x.ConsentType,
                PolicyVersion = x.PolicyVersion,
                ConsentGiven = x.ConsentGiven,
                ConsentMethod = x.ConsentMethod,
                ConsentedAt = x.ConsentedAt,
                WithdrawnAt = x.WithdrawnAt
            })
            .ToListAsync(ct);

        return new DataSubjectPackage
        {
            GeneratedAt = DateTime.UtcNow,
            Profile = profile,
            ReturnDataEntries = returnDataEntries,
            WorkflowActions = workflowActions,
            LoginHistory = loginHistory,
            Notifications = notifications,
            ConsentHistory = consentHistory
        };
    }

    private static HashSet<string> BuildUserAliases(DataSubjectProfile profile)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.Username))
        {
            aliases.Add(profile.Username.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile.Email))
        {
            aliases.Add(profile.Email.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            aliases.Add(profile.DisplayName.Trim());
        }

        return aliases;
    }

    private async Task RehashTenantAuditTrail(Guid tenantId, CancellationToken ct)
    {
        var entries = await _db.AuditLog
            .Where(x => x.TenantId == tenantId && x.SequenceNumber > 0)
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync(ct);

        var previousHash = "GENESIS";
        foreach (var entry in entries)
        {
            entry.PreviousHash = previousHash;
            entry.Hash = AuditLogger.ComputeHash(
                entry.SequenceNumber,
                entry.EntityType,
                entry.PerformedAt,
                entry.TenantId,
                entry.PerformedBy,
                entry.EntityType,
                entry.EntityId,
                entry.Action,
                entry.OldValues,
                entry.NewValues,
                entry.PreviousHash);
            previousHash = entry.Hash;
        }
    }
}
