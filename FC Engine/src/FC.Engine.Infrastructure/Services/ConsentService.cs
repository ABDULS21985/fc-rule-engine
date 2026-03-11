using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.Services;

public class ConsentService : IConsentService
{
    private static readonly List<ConsentType> RequiredConsentTypes = [ConsentType.Registration, ConsentType.DataProcessing];

    private readonly MetadataDbContext _db;
    private readonly PrivacyComplianceOptions _options;

    public ConsentService(
        MetadataDbContext db,
        IOptions<PrivacyComplianceOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public string GetCurrentPolicyVersion() => _options.PolicyVersion;

    public async Task RecordConsent(ConsentCaptureRequest request, CancellationToken ct = default)
    {
        if (request.TenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required for consent capture.", nameof(request));
        }

        if (request.UserId <= 0)
        {
            throw new ArgumentException("UserId is required for consent capture.", nameof(request));
        }

        if (!request.ConsentGiven && IsCoreConsent(request.ConsentType))
        {
            throw new InvalidOperationException("Core consent cannot be denied.");
        }

        var consent = new ConsentRecord
        {
            TenantId = request.TenantId,
            UserId = request.UserId,
            UserType = request.UserType,
            ConsentType = request.ConsentType,
            PolicyVersion = string.IsNullOrWhiteSpace(request.PolicyVersion) ? _options.PolicyVersion : request.PolicyVersion.Trim(),
            ConsentGiven = request.ConsentGiven,
            ConsentMethod = string.IsNullOrWhiteSpace(request.ConsentMethod) ? "checkbox" : request.ConsentMethod.Trim(),
            IpAddress = request.IpAddress,
            UserAgent = request.UserAgent,
            ConsentedAt = DateTime.UtcNow,
            WithdrawnAt = request.ConsentGiven ? null : DateTime.UtcNow
        };

        _db.ConsentRecords.Add(consent);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasCurrentRequiredConsent(Guid tenantId, int userId, string userType, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || userId <= 0 || string.IsNullOrWhiteSpace(userType))
        {
            return false;
        }

        var latest = await _db.ConsentRecords
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.UserId == userId &&
                x.UserType == userType &&
                RequiredConsentTypes.Contains(x.ConsentType))
            .GroupBy(x => x.ConsentType)
            .Select(g => g
                .OrderByDescending(x => x.ConsentedAt)
                .FirstOrDefault())
            .ToListAsync(ct);

        foreach (var requiredType in RequiredConsentTypes)
        {
            var consent = latest.FirstOrDefault(x => x != null && x.ConsentType == requiredType);
            if (consent is null)
            {
                return false;
            }

            if (!consent.ConsentGiven || consent.WithdrawnAt.HasValue)
            {
                return false;
            }

            if (!string.Equals(consent.PolicyVersion, _options.PolicyVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetConsentHistory(
        Guid tenantId,
        int userId,
        string userType,
        CancellationToken ct = default)
    {
        return await _db.ConsentRecords
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.UserId == userId &&
                x.UserType == userType)
            .OrderByDescending(x => x.ConsentedAt)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);
    }

    public async Task WithdrawConsent(
        Guid tenantId,
        int userId,
        string userType,
        ConsentType consentType,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct = default)
    {
        if (IsCoreConsent(consentType))
        {
            throw new InvalidOperationException("Core consent cannot be withdrawn while service is active.");
        }

        var latestGiven = await _db.ConsentRecords
            .Where(x =>
                x.TenantId == tenantId &&
                x.UserId == userId &&
                x.UserType == userType &&
                x.ConsentType == consentType &&
                x.ConsentGiven &&
                !x.WithdrawnAt.HasValue)
            .OrderByDescending(x => x.ConsentedAt)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        if (latestGiven is not null)
        {
            latestGiven.WithdrawnAt = now;
        }

        _db.ConsentRecords.Add(new ConsentRecord
        {
            TenantId = tenantId,
            UserId = userId,
            UserType = userType,
            ConsentType = consentType,
            PolicyVersion = _options.PolicyVersion,
            ConsentGiven = false,
            ConsentMethod = "withdrawal",
            IpAddress = ipAddress,
            UserAgent = userAgent,
            ConsentedAt = now,
            WithdrawnAt = now
        });

        await _db.SaveChangesAsync(ct);
    }

    private static bool IsCoreConsent(ConsentType consentType)
        => consentType is ConsentType.Registration or ConsentType.DataProcessing;
}
