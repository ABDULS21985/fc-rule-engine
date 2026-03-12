using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class TenantOnboardingService : ITenantOnboardingService
{
    private readonly MetadataDbContext _db;
    private readonly IEntitlementService _entitlementService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly ILogger<TenantOnboardingService> _logger;

    public TenantOnboardingService(
        MetadataDbContext db,
        IEntitlementService entitlementService,
        ISubscriptionService subscriptionService,
        ILogger<TenantOnboardingService> logger,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _db = db;
        _entitlementService = entitlementService;
        _subscriptionService = subscriptionService;
        _notificationOrchestrator = notificationOrchestrator;
        _logger = logger;
    }

    public async Task<TenantOnboardingResult> OnboardTenant(TenantOnboardingRequest request, CancellationToken ct = default)
    {
        try
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            var execution = await strategy.ExecuteAsync(async () =>
            {
                var result = new TenantOnboardingResult();

                await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    var inviteNotification = await ExecuteOnboarding(request, result, ct);
                    if (result.Errors.Count > 0)
                    {
                        await transaction.RollbackAsync(ct);
                        return (Result: result, InviteNotification: (NotificationRequest?)null);
                    }

                    await transaction.CommitAsync(ct);
                    result.Success = true;
                    return (Result: result, InviteNotification: inviteNotification);
                }
                catch
                {
                    await transaction.RollbackAsync(ct);
                    throw;
                }
            });

            if (execution.InviteNotification is not null && _notificationOrchestrator is not null)
            {
                try
                {
                    await _notificationOrchestrator.Notify(execution.InviteNotification, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to emit onboarding invitation notification for tenant {TenantId}", execution.Result.TenantId);
                }
            }

            if (execution.Result.Success)
            {
                _logger.LogInformation(
                    "Onboarded tenant {TenantName} ({TenantId}) with {ModuleCount} modules",
                    request.TenantName,
                    execution.Result.TenantId,
                    execution.Result.ActivatedModules.Count);
            }

            return execution.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tenant onboarding failed for {TenantName}", request.TenantName);

            return new TenantOnboardingResult
            {
                Errors =
                {
                    $"Onboarding failed: {ex.Message} | Root cause: {ex.GetBaseException().Message}"
                }
            };
        }
    }

    private async Task<NotificationRequest?> ExecuteOnboarding(
        TenantOnboardingRequest request,
        TenantOnboardingResult result,
        CancellationToken ct)
    {
        // ── Step 1: Validate ──
        var slug = ResolveSlugCandidate(request);
        if (await _db.Tenants.AnyAsync(t => t.TenantSlug == slug, ct))
        {
            if (!string.IsNullOrWhiteSpace(request.TenantSlug))
            {
                result.Errors.Add($"Tenant slug '{slug}' is already in use");
                return null;
            }

            slug = await EnsureUniqueSlug(slug, ct);
        }

        if (await _db.Set<InstitutionUser>().AnyAsync(u => u.Email == request.AdminEmail, ct))
        {
            result.Errors.Add($"Email '{request.AdminEmail}' is already registered");
            return null;
        }

        var licenceTypes = await _db.LicenceTypes
            .Where(lt => request.LicenceTypeCodes.Contains(lt.Code) && lt.IsActive)
            .ToListAsync(ct);

        if (licenceTypes.Count != request.LicenceTypeCodes.Count)
        {
            var found = licenceTypes.Select(lt => lt.Code).ToHashSet();
            var missing = request.LicenceTypeCodes.Where(c => !found.Contains(c));
            result.Errors.Add($"Invalid licence type codes: {string.Join(", ", missing)}");
            return null;
        }

        var selectedPlanCode = string.IsNullOrWhiteSpace(request.SubscriptionPlanCode)
            ? "STARTER"
            : request.SubscriptionPlanCode.Trim().ToUpperInvariant();

        var selectedPlan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(
                p => p.PlanCode == selectedPlanCode && p.IsActive,
                ct);

        if (selectedPlan is null)
        {
            result.Errors.Add($"Invalid subscription plan code: {selectedPlanCode}");
            return null;
        }

        if (request.ParentTenantId.HasValue)
        {
            var parentTenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TenantId == request.ParentTenantId.Value, ct);

            if (parentTenant is null)
            {
                result.Errors.Add($"Parent tenant '{request.ParentTenantId}' not found.");
                return null;
            }

            if (parentTenant.TenantType != TenantType.WhiteLabelPartner)
            {
                result.Errors.Add("Parent tenant must be a white-label partner.");
                return null;
            }
        }

        var jurisdictionCode = string.IsNullOrWhiteSpace(request.JurisdictionCode)
            ? "NG"
            : request.JurisdictionCode.Trim().ToUpperInvariant();

        var jurisdiction = await _db.Jurisdictions
            .FirstOrDefaultAsync(j => j.CountryCode == jurisdictionCode, ct);

        if (jurisdiction is null
            && jurisdictionCode == "NG"
            && !await _db.Jurisdictions.AnyAsync(ct))
        {
            jurisdiction = new Jurisdiction
            {
                CountryCode = "NG",
                CountryName = "Nigeria",
                Currency = "NGN",
                Timezone = "Africa/Lagos",
                RegulatoryBodies = "[\"CBN\",\"NDIC\",\"SEC\",\"NAICOM\",\"PenCom\",\"NFIU\"]",
                DateFormat = "dd/MM/yyyy",
                DataProtectionLaw = "NDPR/NDPA 2023",
                DataResidencyRegion = "SouthAfricaNorth",
                IsActive = true
            };
            _db.Jurisdictions.Add(jurisdiction);
            await _db.SaveChangesAsync(ct);
        }

        if (jurisdiction is null)
        {
            result.Errors.Add($"Invalid jurisdiction code: {jurisdictionCode}");
            return null;
        }

        // ── Step 2: Create Tenant ──
        var tenant = Tenant.Create(request.TenantName, slug, request.TenantType, request.ContactEmail);
        tenant.ContactPhone = request.ContactPhone;
        tenant.Address = request.Address;
        tenant.RcNumber = request.RcNumber;
        tenant.TaxId = request.TaxId;
        tenant.MaxInstitutions = selectedPlan.MaxEntities;
        tenant.MaxUsersPerEntity = selectedPlan.MaxUsersPerEntity;
        tenant.Timezone = jurisdiction.Timezone;
        tenant.DefaultCurrency = jurisdiction.Currency;
        tenant.SetParentTenant(request.ParentTenantId);

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(ct);

        result.TenantId = tenant.TenantId;
        result.TenantSlug = slug;

        // Set SESSION_CONTEXT to the newly created tenant so that Row-Level Security
        // block predicates allow subsequent inserts into tenant-scoped tables
        // (tenant_licence_types, institutions, institution_users, etc.).
        await _db.Database.ExecuteSqlRawAsync(
            "EXEC sp_set_session_context @key=N'TenantId', @value={0};",
            new object[] { tenant.TenantId }, ct);

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
            JurisdictionId = jurisdiction.Id,
            InstitutionCode = request.InstitutionCode,
            InstitutionName = request.InstitutionName,
            LicenseType = request.InstitutionType,
            IsActive = true,
            EntityType = EntityType.HeadOffice,
            CreatedAt = DateTime.UtcNow,
            MaxUsersAllowed = selectedPlan.MaxUsersPerEntity
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
            PreferredLanguage = "en",
            Role = InstitutionRole.Admin,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Set<InstitutionUser>().Add(adminUser);
        await _db.SaveChangesAsync(ct);

        result.AdminTemporaryPassword = tempPassword;

        // ── Step 6: Create Subscription + auto-activate required modules ──
        await _subscriptionService.CreateSubscription(
            tenant.TenantId,
            selectedPlan.PlanCode,
            BillingFrequency.Monthly,
            ct);

        var baselineEntitlement = await _entitlementService.ResolveEntitlements(tenant.TenantId, ct);
        foreach (var requiredModule in baselineEntitlement.EligibleModules.Where(m => m.IsRequired))
        {
            try
            {
                await _subscriptionService.ActivateModule(tenant.TenantId, requiredModule.ModuleCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Auto-activate module {ModuleCode} failed for tenant {TenantId}",
                    requiredModule.ModuleCode,
                    tenant.TenantId);
            }
        }

        await _entitlementService.InvalidateCache(tenant.TenantId);

        // ── Step 7: Resolve Entitlements ──
        var entitlement = await _entitlementService.ResolveEntitlements(tenant.TenantId, ct);
        result.ActivatedModules = entitlement.ActiveModules.Select(m => m.ModuleCode).ToList();

        // ── Step 8: Create Return Periods & Filing Calendar ──
        var periodsCreated = await CreateInitialReturnPeriods(
            tenant.TenantId, tenant.FiscalYearStartMonth, entitlement.ActiveModules, ct);
        result.ReturnPeriodsCreated = periodsCreated;

        // ── Step 9: Activate Tenant ──
        tenant.Activate();
        await _db.SaveChangesAsync(ct);

        // ── Step 10: Create Welcome Notification ──
        var notification = new PortalNotification
        {
            TenantId = tenant.TenantId,
            InstitutionId = institution.Id,
            UserId = adminUser.Id,
            Type = NotificationType.SystemAnnouncement,
            Title = "Welcome to RegOS",
            Message = $"Your account has been set up with {result.ActivatedModules.Count} regulatory module(s) and {periodsCreated} filing period(s). Please change your password on first login.",
            CreatedAt = DateTime.UtcNow
        };
        _db.PortalNotifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        return _notificationOrchestrator is null
            ? null
            : new NotificationRequest
            {
                TenantId = tenant.TenantId,
                EventType = NotificationEvents.UserInvited,
                Title = $"You've been invited to {tenant.TenantName}",
                Message = $"Welcome to RegOS. Your account for {tenant.TenantName} is ready.",
                Priority = NotificationPriority.Normal,
                RecipientUserIds = new List<int> { adminUser.Id },
                ActionUrl = "/login",
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["InvitedBy"] = "RegOS Platform",
                    ["CompanyName"] = tenant.TenantName,
                    ["Role"] = "Admin",
                    ["SetupUrl"] = "https://portal.regos.app/login"
                }
            };
    }

    /// <summary>
    /// Creates initial return periods for each activated module based on its default frequency.
    /// Generates periods within the next 12 months.
    /// </summary>
    private async Task<int> CreateInitialReturnPeriods(
        Guid tenantId,
        int fiscalYearStartMonth,
        IReadOnlyList<Domain.ValueObjects.EntitledModule> activeModules,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var periodsCreated = 0;

        // Determine distinct frequencies from active modules.
        // Supports combined values like "Monthly/Quarterly".
        var frequencies = activeModules
            .SelectMany(m => ExpandFrequencies(m.DefaultFrequency))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var frequency in frequencies)
        {
            var periods = GeneratePeriodsForFrequency(frequency, now, fiscalYearStartMonth, monthsHorizon: 12);
            foreach (var (year, month) in periods)
            {
                // Check for duplicates (same tenant + year + month + frequency)
                var exists = await _db.ReturnPeriods.AnyAsync(
                    rp => rp.TenantId == tenantId && rp.Year == year && rp.Month == month && rp.Frequency == frequency, ct);
                if (exists) continue;

                _db.ReturnPeriods.Add(new ReturnPeriod
                {
                    TenantId = tenantId,
                    Year = year,
                    Month = month,
                    Frequency = frequency,
                    ReportingDate = new DateTime(year, month, DateTime.DaysInMonth(year, month)),
                    IsOpen = true,
                    CreatedAt = DateTime.UtcNow
                });
                periodsCreated++;
            }
        }

        if (periodsCreated > 0)
            await _db.SaveChangesAsync(ct);

        return periodsCreated;
    }

    /// <summary>
    /// Generates (year, month) tuples for a rolling horizon based on reporting frequency.
    /// </summary>
    internal static List<(int Year, int Month)> GeneratePeriodsForFrequency(
        string frequency, DateTime referenceDate, int fiscalYearStartMonth = 1, int monthsHorizon = 12)
    {
        if (monthsHorizon <= 0)
        {
            return new List<(int Year, int Month)>();
        }

        var normalizedFrequency = NormalizeFrequency(frequency);
        var monthStart = new DateTime(referenceDate.Year, referenceDate.Month, 1);
        var timeline = Enumerable.Range(0, monthsHorizon)
            .Select(i => monthStart.AddMonths(i))
            .ToList();

        return normalizedFrequency switch
        {
            "Monthly" => timeline.Select(d => (d.Year, d.Month)).ToList(),
            "Quarterly" => timeline
                .Where(d => d.Month is 3 or 6 or 9 or 12)
                .Select(d => (d.Year, d.Month))
                .ToList(),
            "SemiAnnual" => timeline
                .Where(d => d.Month is 6 or 12)
                .Select(d => (d.Year, d.Month))
                .ToList(),
            "Annual" => timeline
                .Where(d => d.Month == ResolveFiscalYearEndMonth(fiscalYearStartMonth))
                .Select(d => (d.Year, d.Month))
                .ToList(),
            _ => timeline.Select(d => (d.Year, d.Month)).ToList()
        };
    }

    private static IEnumerable<string> ExpandFrequencies(string? rawFrequency)
    {
        if (string.IsNullOrWhiteSpace(rawFrequency))
        {
            yield return "Monthly";
            yield break;
        }

        var parts = rawFrequency.Split(new[] { '/', ',', ';', '|' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            yield return "Monthly";
            yield break;
        }

        foreach (var part in parts)
        {
            yield return NormalizeFrequency(part);
        }
    }

    private static string NormalizeFrequency(string frequency)
    {
        return frequency.Trim().ToUpperInvariant() switch
        {
            "MONTHLY" => "Monthly",
            "QUARTERLY" => "Quarterly",
            "SEMIANNUAL" => "SemiAnnual",
            "ANNUAL" => "Annual",
            "ADHOC" => "AdHoc",
            _ => "Monthly"
        };
    }

    private static int ResolveFiscalYearEndMonth(int fiscalYearStartMonth)
    {
        if (fiscalYearStartMonth <= 1)
        {
            return 12;
        }

        return fiscalYearStartMonth - 1;
    }

    private static string ResolveSlugCandidate(TenantOnboardingRequest request)
    {
        var source = !string.IsNullOrWhiteSpace(request.TenantSlug)
            ? request.TenantSlug
            : request.TenantName;

        var slug = GenerateSlug(source);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = $"tenant-{Guid.NewGuid():N}"[..15];
        }

        return slug;
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
