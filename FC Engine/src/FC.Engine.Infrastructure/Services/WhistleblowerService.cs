using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class WhistleblowerService : IWhistleblowerService
{
    private readonly IDbConnectionFactory _db;
    private readonly IRegulatorTenantResolver _tenantResolver;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhistleblowerService> _log;

    public WhistleblowerService(
        IDbConnectionFactory db,
        IRegulatorTenantResolver tenantResolver,
        IConfiguration configuration,
        ILogger<WhistleblowerService> log)
    {
        _db = db;
        _tenantResolver = tenantResolver;
        _configuration = configuration;
        _log = log;
    }

    public async Task<WhistleblowerSubmissionReceipt> SubmitAsync(
        WhistleblowerSubmission submission,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (string.IsNullOrWhiteSpace(submission.Summary))
        {
            throw new InvalidOperationException("Submission summary is required.");
        }

        var context = await _tenantResolver.ResolveAsync(submission.RegulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);
        using var tx = conn.BeginTransaction();

        var caseReference = $"WB-{DateTime.UtcNow:yyyy}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var placeholderToken = Guid.NewGuid().ToString("N");
        var priorityScore = ComputePriority(submission.Category, submission.Summary);

        var reportId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO dbo.WhistleblowerReports
                (TenantId, CaseReference, AnonymousToken, RegulatorCode, AllegedInstitutionId,
                 AllegedInstitutionName, Category, Summary, EvidenceDescription, EvidenceS3Keys,
                 Status, PriorityScore)
            OUTPUT INSERTED.Id
            VALUES
                (@TenantId, @CaseReference, @PlaceholderToken, @RegulatorCode, @AllegedInstitutionId,
                 @AllegedInstitutionName, @Category, @Summary, @EvidenceDescription, @EvidenceS3Keys,
                 'RECEIVED', @PriorityScore)
            """,
            new
            {
                TenantId = context.TenantId,
                CaseReference = caseReference,
                PlaceholderToken = placeholderToken,
                RegulatorCode = context.RegulatorCode,
                submission.AllegedInstitutionId,
                submission.AllegedInstitutionName,
                Category = submission.Category.ToString(),
                submission.Summary,
                submission.EvidenceDescription,
                EvidenceS3Keys = submission.EvidenceFileKeys.Count > 0
                    ? JsonSerializer.Serialize(submission.EvidenceFileKeys)
                    : null,
                PriorityScore = priorityScore
            },
            tx);

        var salt = _configuration["WhistleblowerService:TokenSalt"];
        if (string.IsNullOrWhiteSpace(salt))
        {
            throw new InvalidOperationException("WhistleblowerService:TokenSalt is not configured.");
        }

        var anonymousToken = ComputeToken(salt, reportId, caseReference);
        await conn.ExecuteAsync(
            """
            UPDATE dbo.WhistleblowerReports
            SET AnonymousToken = @AnonymousToken
            WHERE Id = @ReportId
              AND AnonymousToken = @PlaceholderToken
            """,
            new
            {
                AnonymousToken = anonymousToken,
                ReportId = reportId,
                PlaceholderToken = placeholderToken
            },
            tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO dbo.WhistleblowerCaseEvents
                (TenantId, RegulatorCode, WhistleblowerReportId, EventType, Note, PerformedByUserId)
            VALUES
                (@TenantId, @RegulatorCode, @ReportId, 'RECEIVED', @Note, NULL)
            """,
            new
            {
                TenantId = context.TenantId,
                RegulatorCode = context.RegulatorCode,
                ReportId = reportId,
                Note = $"Case received. Category={submission.Category}. Priority={priorityScore}."
            },
            tx);

        tx.Commit();

        var baseUrl = (_configuration["RegOS:BaseUrl"] ?? "https://regos.cbn.gov.ng").TrimEnd('/');
        _log.LogInformation(
            "Whistleblower report received. Regulator={RegulatorCode} CaseReference={CaseReference}",
            context.RegulatorCode,
            caseReference);

        return new WhistleblowerSubmissionReceipt(
            caseReference,
            anonymousToken,
            DateTimeOffset.UtcNow,
            $"{baseUrl}/wb/status/{anonymousToken}");
    }

    public async Task<WhistleblowerStatusView?> CheckStatusAsync(
        string anonymousToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(anonymousToken))
        {
            return null;
        }

        using var conn = await _db.CreateConnectionAsync(null, ct);
        return await conn.QuerySingleOrDefaultAsync<WhistleblowerStatusView>(
            """
            SELECT CaseReference,
                   Status,
                   ReceivedAt,
                   UpdatedAt
            FROM dbo.WhistleblowerReports
            WHERE AnonymousToken = @AnonymousToken
            """,
            new { AnonymousToken = anonymousToken.Trim().ToLowerInvariant() });
    }

    public async Task<IReadOnlyList<WhistleblowerCaseSummary>> GetOpenCasesAsync(
        string regulatorCode,
        CancellationToken ct = default)
    {
        var context = await _tenantResolver.ResolveAsync(regulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);
        try
        {
            return (await conn.QueryAsync<WhistleblowerCaseSummary>(
                """
                SELECT r.Id AS ReportId,
                       r.CaseReference,
                       r.Category,
                       COALESCE(r.AllegedInstitutionName, i.InstitutionName) AS AllegedInstitutionName,
                       r.Status,
                       r.PriorityScore,
                       u.DisplayName AS AssignedToUserName,
                       r.ReceivedAt
                FROM dbo.WhistleblowerReports r
                LEFT JOIN dbo.institutions i ON i.Id = r.AllegedInstitutionId
                LEFT JOIN meta.portal_users u ON u.Id = r.AssignedToUserId
                WHERE r.TenantId = @TenantId
                  AND r.RegulatorCode = @RegulatorCode
                  AND r.Status NOT IN ('CONCLUDED', 'CLOSED')
                ORDER BY r.PriorityScore DESC, r.ReceivedAt ASC
                """,
                new { TenantId = context.TenantId, RegulatorCode = context.RegulatorCode })).ToList();
        }
        catch (Exception ex) when (ex.IsMissingSchemaObject())
        {
            return [];
        }
    }

    public async Task AssignCaseAsync(
        string caseReference,
        int assignedUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(caseReference))
        {
            throw new InvalidOperationException("Case reference is required.");
        }

        using var conn = await _db.CreateConnectionAsync(null, ct);
        var target = await conn.QuerySingleOrDefaultAsync<CaseContextRow>(
            """
            SELECT Id,
                   TenantId,
                   RegulatorCode
            FROM dbo.WhistleblowerReports
            WHERE CaseReference = @CaseReference
            """,
            new { CaseReference = caseReference.Trim().ToUpperInvariant() });

        if (target is null)
        {
            throw new KeyNotFoundException($"Whistleblower case '{caseReference}' was not found.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE dbo.WhistleblowerReports
            SET AssignedToUserId = @AssignedUserId,
                Status = CASE WHEN Status = 'RECEIVED' THEN 'UNDER_REVIEW' ELSE Status END,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            new { AssignedUserId = assignedUserId, target.Id });

        await conn.ExecuteAsync(
            """
            INSERT INTO dbo.WhistleblowerCaseEvents
                (TenantId, RegulatorCode, WhistleblowerReportId, EventType, Note, PerformedByUserId)
            VALUES
                (@TenantId, @RegulatorCode, @ReportId, 'ASSIGNED', @Note, @PerformedByUserId)
            """,
            new
            {
                target.TenantId,
                target.RegulatorCode,
                ReportId = target.Id,
                Note = $"Assigned to regulator user {assignedUserId}.",
                PerformedByUserId = assignedUserId
            });
    }

    public async Task UpdateStatusAsync(
        string caseReference,
        WhistleblowerStatus newStatus,
        string note,
        int performedByUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(caseReference))
        {
            throw new InvalidOperationException("Case reference is required.");
        }

        using var conn = await _db.CreateConnectionAsync(null, ct);
        var target = await conn.QuerySingleOrDefaultAsync<CaseContextRow>(
            """
            SELECT Id,
                   TenantId,
                   RegulatorCode
            FROM dbo.WhistleblowerReports
            WHERE CaseReference = @CaseReference
            """,
            new { CaseReference = caseReference.Trim().ToUpperInvariant() });

        if (target is null)
        {
            throw new KeyNotFoundException($"Whistleblower case '{caseReference}' was not found.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE dbo.WhistleblowerReports
            SET Status = @Status,
                UpdatedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """,
            new { Status = newStatus.ToString().ToUpperInvariant(), target.Id });

        await conn.ExecuteAsync(
            """
            INSERT INTO dbo.WhistleblowerCaseEvents
                (TenantId, RegulatorCode, WhistleblowerReportId, EventType, Note, PerformedByUserId)
            VALUES
                (@TenantId, @RegulatorCode, @ReportId, 'STATUS_CHANGED', @Note, @PerformedByUserId)
            """,
            new
            {
                target.TenantId,
                target.RegulatorCode,
                ReportId = target.Id,
                Note = string.IsNullOrWhiteSpace(note) ? $"Status changed to {newStatus}." : note.Trim(),
                PerformedByUserId = performedByUserId
            });
    }

    private static string ComputeToken(string salt, long reportId, string caseReference)
    {
        var key = Encoding.UTF8.GetBytes(salt);
        var payload = Encoding.UTF8.GetBytes($"{reportId}:{caseReference}");
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(payload)).ToLowerInvariant();
    }

    private static int ComputePriority(WhistleblowerCategory category, string summary)
    {
        var score = category switch
        {
            WhistleblowerCategory.InsiderTrading => 85,
            WhistleblowerCategory.FxManipulation => 80,
            WhistleblowerCategory.AmlFailure => 75,
            WhistleblowerCategory.RelatedPartyAbuse => 70,
            WhistleblowerCategory.PremiumFraud => 65,
            WhistleblowerCategory.ClaimsSuppression => 60,
            _ => 45
        };

        if (summary.Length > 500)
        {
            score += 10;
        }

        if (summary.Length > 1000)
        {
            score += 5;
        }

        return Math.Min(100, score);
    }

    private sealed record CaseContextRow(long Id, Guid TenantId, string RegulatorCode);
}
