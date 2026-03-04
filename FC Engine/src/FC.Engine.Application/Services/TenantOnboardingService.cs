using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Application.Services;

public class TenantOnboardingService : ITenantOnboardingService
{
    private readonly MetadataDbContext _db;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<TenantOnboardingService> _logger;

    public TenantOnboardingService(
        MetadataDbContext db,
        IEntitlementService entitlementService,
        ILogger<TenantOnboardingService> logger)
    {
        _db = db;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    public async Task<TenantOnboardingResult> OnboardTenant(TenantOnboardingRequest request, CancellationToken ct = default)
    {
        var result = new TenantOnboardingResult();

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // ── Step 1: Validate ──
            var slug = !string.IsNullOrWhiteSpace(request.TenantSlug)
                ? request.TenantSlug
                : GenerateSlug(request.TenantName);

            slug = await EnsureUniqueSlug(slug, ct);

            if (await _db.Set<InstitutionUser>().AnyAsync(u => u.Email == request.AdminEmail, ct))
            {
                result.Errors.Add($"Email '{request.AdminEmail}' is already registered");
                return result;
            }

            var licenceTypes = await _db.LicenceTypes
                .Where(lt => request.LicenceTypeCodes.Contains(lt.Code) && lt.IsActive)
                .ToListAsync(ct);

            if (licenceTypes.Count != request.LicenceTypeCodes.Count)
            {
                var found = licenceTypes.Select(lt => lt.Code).ToHashSet();
                var missing = request.LicenceTypeCodes.Where(c => !found.Contains(c));
                result.Errors.Add($"Invalid licence type codes: {string.Join(", ", missing)}");
                return result;
            }

            // ── Step 2: Create Tenant ──
            var tenant = Tenant.Create(request.TenantName, slug, request.TenantType, request.ContactEmail);
            tenant.ContactPhone = request.ContactPhone;
            tenant.Address = request.Address;
            tenant.RcNumber = request.RcNumber;
            tenant.TaxId = request.TaxId;

            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(ct);

            result.TenantId = tenant.TenantId;
            result.TenantSlug = slug;

            // ── Step 3: Assign Licence Types ──
            foreach (var lt in licenceTypes)
            {
                _db.TenantLicenceTypes.Add(new TenantLicenceType
                {
                    TenantId = tenant.TenantId,
                    LicenceTypeId = lt.Id,
                    EffectiveDate = DateTime.UtcNow.Date,
                    IsActive = true
                });
            }
            await _db.SaveChangesAsync(ct);

            // ── Step 4: Create Institution ──
            var institution = new Institution
            {
                TenantId = tenant.TenantId,
                InstitutionCode = request.InstitutionCode,
                InstitutionName = request.InstitutionName,
                LicenseType = request.InstitutionType,
                IsActive = true,
                EntityType = EntityType.HeadOffice,
                CreatedAt = DateTime.UtcNow
            };
            _db.Institutions.Add(institution);
            await _db.SaveChangesAsync(ct);

            result.InstitutionId = institution.Id;

            // ── Step 5: Create Admin User ──
            var tempPassword = GenerateTemporaryPassword();
            var passwordHash = AuthService.HashPassword(tempPassword);

            var adminUsername = GenerateUsername(request.AdminEmail);

            var adminUser = new InstitutionUser
            {
                TenantId = tenant.TenantId,
                InstitutionId = institution.Id,
                Username = adminUsername,
                Email = request.AdminEmail,
                DisplayName = request.AdminFullName,
                PasswordHash = passwordHash,
                Role = InstitutionRole.Admin,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Set<InstitutionUser>().Add(adminUser);
            await _db.SaveChangesAsync(ct);

            result.AdminTemporaryPassword = tempPassword;

            // ── Step 6: Activate Tenant ──
            tenant.Activate();
            await _db.SaveChangesAsync(ct);

            // ── Step 7: Resolve Entitlements ──
            var entitlement = await _entitlementService.ResolveEntitlements(tenant.TenantId, ct);
            result.ActivatedModules = entitlement.ActiveModules.Select(m => m.ModuleCode).ToList();

            // ── Step 8: Create Welcome Notification ──
            var notification = new PortalNotification
            {
                TenantId = tenant.TenantId,
                InstitutionId = institution.Id,
                UserId = adminUser.Id,
                Type = NotificationType.SystemAnnouncement,
                Title = "Welcome to RegOS",
                Message = $"Your account has been set up with {result.ActivatedModules.Count} regulatory module(s). Please change your password on first login.",
                CreatedAt = DateTime.UtcNow
            };
            _db.PortalNotifications.Add(notification);
            await _db.SaveChangesAsync(ct);

            // ── Step 9: Commit ──
            await transaction.CommitAsync(ct);

            result.Success = true;
            _logger.LogInformation(
                "Onboarded tenant {TenantName} ({TenantId}) with {ModuleCount} modules",
                tenant.TenantName, tenant.TenantId, result.ActivatedModules.Count);

            return result;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex, "Tenant onboarding failed for {TenantName}", request.TenantName);
            result.Errors.Add($"Onboarding failed: {ex.Message}");
            return result;
        }
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        return slug.Trim('-');
    }

    private async Task<string> EnsureUniqueSlug(string baseSlug, CancellationToken ct)
    {
        var slug = baseSlug;
        var counter = 1;
        while (await _db.Tenants.AnyAsync(t => t.TenantSlug == slug, ct))
        {
            slug = $"{baseSlug}-{counter}";
            counter++;
        }
        return slug;
    }

    private static string GenerateUsername(string email)
    {
        var parts = email.Split('@');
        return parts[0].ToLowerInvariant();
    }

    internal static string GenerateTemporaryPassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%&*";
        const string all = upper + lower + digits + special;

        var password = new char[16];
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[16];
        rng.GetBytes(bytes);

        // Ensure at least one of each type
        password[0] = upper[bytes[0] % upper.Length];
        password[1] = lower[bytes[1] % lower.Length];
        password[2] = digits[bytes[2] % digits.Length];
        password[3] = special[bytes[3] % special.Length];

        for (var i = 4; i < 16; i++)
            password[i] = all[bytes[i] % all.Length];

        // Shuffle
        var shuffleBytes = new byte[16];
        rng.GetBytes(shuffleBytes);
        return new string(password.Zip(shuffleBytes, (c, b) => (c, b))
            .OrderBy(x => x.b)
            .Select(x => x.c)
            .ToArray());
    }
}
