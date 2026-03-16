using System.Security.Cryptography;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using QRCoder;

namespace FC.Engine.Infrastructure.Services;

public class MfaService : IMfaService
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ITenantContext _tenantContext;
    private readonly INotificationOrchestrator? _notificationOrchestrator;

    public MfaService(
        IDbContextFactory<MetadataDbContext> dbFactory,
        ITenantContext tenantContext,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _dbFactory = dbFactory;
        _tenantContext = tenantContext;
        _notificationOrchestrator = notificationOrchestrator;
    }

    public async Task<MfaSetupResult> InitiateSetup(int userId, string userType, string email)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);
        var tenantId = await ResolveTenantId(db, userId, userType);

        var config = await db.UserMfaConfigs
            .FirstOrDefaultAsync(c => c.UserId == userId && c.UserType == userType);

        if (config is null)
        {
            config = new UserMfaConfig
            {
                TenantId = tenantId,
                UserId = userId,
                UserType = userType,
                SecretKey = secretBase32,
                BackupCodes = "[]",
                IsEnabled = false
            };
            db.UserMfaConfigs.Add(config);
        }
        else
        {
            config.SecretKey = secretBase32;
            config.BackupCodes = "[]";
            config.IsEnabled = false;
            config.EnabledAt = null;
            config.LastUsedAt = null;
        }

        await db.SaveChangesAsync();

        var issuer = "RegOS";
        var escapedIssuer = Uri.EscapeDataString(issuer);
        var escapedEmail = Uri.EscapeDataString(email);
        var otpUri = $"otpauth://totp/{escapedIssuer}:{escapedEmail}?secret={secretBase32}&issuer={escapedIssuer}&digits=6&period=30";

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(otpUri, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrCodeData);
        var pngBytes = qrCode.GetGraphic(5);
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";

        return new MfaSetupResult
        {
            SecretKey = secretBase32,
            QrCodeDataUri = dataUri,
            Issuer = issuer
        };
    }

    public async Task<MfaActivationResult> ActivateWithVerification(int userId, string userType, string code)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var config = await db.UserMfaConfigs
            .FirstOrDefaultAsync(c => c.UserId == userId && c.UserType == userType);

        if (config is null)
        {
            throw new InvalidOperationException("MFA setup has not been initiated.");
        }

        var totp = new Totp(Base32Encoding.ToBytes(config.SecretKey));
        if (!totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1)))
        {
            return new MfaActivationResult { Success = false };
        }

        var backupCodes = Enumerable.Range(0, 10)
            .Select(_ => GenerateBackupCode())
            .ToList();

        var hashes = backupCodes
            .Select(BCrypt.Net.BCrypt.HashPassword)
            .ToList();

        config.BackupCodes = JsonSerializer.Serialize(hashes);
        config.IsEnabled = true;
        config.EnabledAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return new MfaActivationResult
        {
            Success = true,
            BackupCodes = backupCodes
        };
    }

    public async Task<bool> VerifyCode(int userId, string code, string? userType = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.UserMfaConfigs.Where(c => c.UserId == userId && c.IsEnabled);
        if (!string.IsNullOrWhiteSpace(userType))
        {
            query = query.Where(c => c.UserType == userType);
        }

        var config = await query.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (config is null)
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(config.SecretKey));
        var valid = totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        if (valid)
        {
            config.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return valid;
    }

    public async Task<bool> VerifyBackupCode(int userId, string backupCode, string? userType = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.UserMfaConfigs.Where(c => c.UserId == userId && c.IsEnabled);
        if (!string.IsNullOrWhiteSpace(userType))
        {
            query = query.Where(c => c.UserType == userType);
        }

        var config = await query.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (config is null)
        {
            return false;
        }

        var hashes = JsonSerializer.Deserialize<List<string>>(config.BackupCodes) ?? new List<string>();
        for (var i = 0; i < hashes.Count; i++)
        {
            if (!BCrypt.Net.BCrypt.Verify(backupCode, hashes[i]))
            {
                continue;
            }

            hashes.RemoveAt(i);
            config.BackupCodes = JsonSerializer.Serialize(hashes);
            config.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> SendMfaCodeSms(int userId, string userType, CancellationToken ct = default)
    {
        if (_notificationOrchestrator is null)
        {
            return false;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var config = await db.UserMfaConfigs
            .FirstOrDefaultAsync(c => c.UserId == userId && c.UserType == userType && c.IsEnabled, ct);
        if (config is null)
        {
            return false;
        }

        var phone = await ResolveUserPhone(db, userId, userType, ct);
        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(config.SecretKey));
        var code = totp.ComputeTotp(DateTime.UtcNow);

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = config.TenantId,
            EventType = NotificationEvents.MfaCodeSms,
            Title = "Your verification code",
            Message = $"Your RegOS verification code is {code}. Valid for 5 minutes.",
            Priority = NotificationPriority.Critical,
            IsMandatory = true,
            RecipientUserIds = new List<int> { userId },
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = code
            }
        }, ct);

        return true;
    }

    public async Task Disable(int userId, string userType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var config = await db.UserMfaConfigs
            .FirstOrDefaultAsync(c => c.UserId == userId && c.UserType == userType);
        if (config is null)
        {
            return;
        }

        config.IsEnabled = false;
        config.EnabledAt = null;
        config.LastUsedAt = null;
        config.BackupCodes = "[]";
        await db.SaveChangesAsync();
    }

    public async Task<bool> IsMfaEnabled(int userId, string userType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.UserMfaConfigs.AnyAsync(c =>
            c.UserId == userId &&
            c.UserType == userType &&
            c.IsEnabled);
    }

    public async Task<bool> IsMfaRequired(Guid tenantId, string role)
    {
        if (string.Equals(role, "Checker", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "Approver", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var brandingConfig = await db.Tenants
            .Where(t => t.TenantId == tenantId)
            .Select(t => t.BrandingConfig)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(brandingConfig))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(brandingConfig);
            if (doc.RootElement.TryGetProperty("mfaRequired", out var mfaRequired))
            {
                return mfaRequired.ValueKind == JsonValueKind.True;
            }

            if (doc.RootElement.TryGetProperty("MfaRequired", out var mfaRequiredPascal))
            {
                return mfaRequiredPascal.ValueKind == JsonValueKind.True;
            }
        }
        catch
        {
            // Ignore malformed branding payload and treat as not required.
        }

        return false;
    }

    private async Task<Guid> ResolveTenantId(MetadataDbContext db, int userId, string userType)
    {
        if (_tenantContext.CurrentTenantId.HasValue)
        {
            return _tenantContext.CurrentTenantId.Value;
        }

        if (string.Equals(userType, "InstitutionUser", StringComparison.OrdinalIgnoreCase))
        {
            var tenantId = await db.InstitutionUsers
                .Where(u => u.Id == userId)
                .Select(u => (Guid?)u.TenantId)
                .FirstOrDefaultAsync();
            if (tenantId.HasValue)
            {
                return tenantId.Value;
            }
        }

        if (string.Equals(userType, "PortalUser", StringComparison.OrdinalIgnoreCase))
        {
            var tenantId = await db.PortalUsers
                .Where(u => u.Id == userId)
                .Select(u => u.TenantId)
                .FirstOrDefaultAsync();
            if (tenantId.HasValue)
            {
                return tenantId.Value;
            }
        }

        throw new InvalidOperationException($"Could not resolve TenantId for {userType}:{userId}.");
    }

    private static string GenerateBackupCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(5);
        var code = Convert.ToHexString(bytes).ToUpperInvariant()[..8];
        return $"{code[..4]}-{code[4..]}";
    }

    private async Task<string?> ResolveUserPhone(MetadataDbContext db, int userId, string userType, CancellationToken ct)
    {
        if (string.Equals(userType, "InstitutionUser", StringComparison.OrdinalIgnoreCase))
        {
            var user = await db.InstitutionUsers
                .Include(u => u.Institution)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);
            return user?.PhoneNumber ?? user?.Institution?.ContactPhone;
        }

        if (string.Equals(userType, "PortalUser", StringComparison.OrdinalIgnoreCase))
        {
            var user = await db.PortalUsers.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user?.TenantId is null)
            {
                return null;
            }

            var tenantPhone = await db.Tenants
                .Where(t => t.TenantId == user.TenantId.Value)
                .Select(t => t.ContactPhone)
                .FirstOrDefaultAsync(ct);

            return tenantPhone;
        }

        return null;
    }
}
