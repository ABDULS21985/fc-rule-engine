using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.ValueObjects;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class PartnerManagementService : IPartnerManagementService
{
    private readonly MetadataDbContext _db;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly ITenantBrandingService _tenantBrandingService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ILogger<PartnerManagementService> _logger;
    private readonly INotificationOrchestrator? _notificationOrchestrator;

    public PartnerManagementService(
        MetadataDbContext db,
        ITenantOnboardingService tenantOnboardingService,
        ITenantBrandingService tenantBrandingService,
        ISubscriptionService subscriptionService,
        ILogger<PartnerManagementService> logger,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _db = db;
        _tenantOnboardingService = tenantOnboardingService;
        _tenantBrandingService = tenantBrandingService;
        _subscriptionService = subscriptionService;
        _logger = logger;
        _notificationOrchestrator = notificationOrchestrator;
    }

    public async Task<TenantOnboardingResult> OnboardPartner(PartnerOnboardingRequest request, CancellationToken ct = default)
    {
        var onboarding = new TenantOnboardingRequest
        {
            TenantName = request.TenantName,
            TenantSlug = request.TenantSlug,
            TenantType = TenantType.WhiteLabelPartner,
            ContactEmail = request.ContactEmail,
            ContactPhone = request.ContactPhone,
            Address = request.Address,
            RcNumber = request.RcNumber,
            TaxId = request.TaxId,
            LicenceTypeCodes = new List<string>(),
            SubscriptionPlanCode = request.SubscriptionPlanCode,
            AdminEmail = request.AdminEmail,
            AdminFullName = request.AdminFullName,
            JurisdictionCode = "NG",
            InstitutionCode = BuildInstitutionCode(request.TenantName),
            InstitutionName = $"{request.TenantName} Partner",
            InstitutionType = "Partner"
        };

        var result = await _tenantOnboardingService.OnboardTenant(onboarding, ct);
        if (!result.Success)
        {
            return result;
        }

        var config = new PartnerConfig
        {
            TenantId = result.TenantId,
            PartnerTier = request.PartnerTier,
            BillingModel = request.BillingModel,
            CommissionRate = request.BillingModel == PartnerBillingModel.Direct
                ? (request.CommissionRate ?? GetDefaultCommissionRate(request.PartnerTier))
                : null,
            WholesaleDiscount = request.BillingModel == PartnerBillingModel.Reseller
                ? (request.WholesaleDiscount ?? GetDefaultWholesaleDiscount(request.PartnerTier))
                : null,
            MaxSubTenants = Math.Max(1, request.MaxSubTenants),
            AgreementSignedAt = request.AgreementSignedAt ?? DateTime.UtcNow,
            AgreementVersion = string.IsNullOrWhiteSpace(request.AgreementVersion) ? "v1" : request.AgreementVersion.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.PartnerConfigs.Add(config);
        await _db.SaveChangesAsync(ct);

        return result;
    }

    public async Task<TenantOnboardingResult> CreateSubTenant(Guid partnerTenantId, SubTenantCreateRequest request, CancellationToken ct = default)
    {
        var partner = await EnsurePartnerTenant(partnerTenantId, ct);
        var config = await _db.PartnerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == partnerTenantId, ct)
            ?? throw new InvalidOperationException($"Partner config not found for tenant {partnerTenantId}.");

        var currentCount = await _db.Tenants
            .AsNoTracking()
            .CountAsync(t => t.ParentTenantId == partnerTenantId && t.Status != TenantStatus.Archived, ct);

        if (currentCount >= config.MaxSubTenants)
        {
            throw new InvalidOperationException($"Sub-tenant limit reached ({currentCount}/{config.MaxSubTenants}).");
        }

        var onboarding = new TenantOnboardingRequest
        {
            TenantName = request.TenantName,
            TenantSlug = request.TenantSlug,
            TenantType = TenantType.Institution,
            ParentTenantId = partnerTenantId,
            ContactEmail = request.ContactEmail,
            ContactPhone = request.ContactPhone,
            Address = request.Address,
            RcNumber = request.RcNumber,
            TaxId = request.TaxId,
            LicenceTypeCodes = request.LicenceTypeCodes,
            SubscriptionPlanCode = request.SubscriptionPlanCode,
            AdminEmail = request.AdminEmail,
            AdminFullName = request.AdminFullName,
            JurisdictionCode = request.JurisdictionCode,
            InstitutionCode = request.InstitutionCode,
            InstitutionName = request.InstitutionName,
            InstitutionType = request.InstitutionType
        };

        var result = await _tenantOnboardingService.OnboardTenant(onboarding, ct);
        if (!result.Success)
        {
            return result;
        }

        // Branding cascade: inherit partner branding unless the child tenant can override.
        var childHasCustomBranding = await _subscriptionService.HasFeature(result.TenantId, "custom_branding", ct);
        if (!childHasCustomBranding)
        {
            var partnerBranding = await _tenantBrandingService.GetBrandingConfig(partner.TenantId, ct);
            await _tenantBrandingService.UpdateBrandingConfig(result.TenantId, partnerBranding, ct);
        }

        return result;
    }

    public async Task<List<PartnerSubTenantSummary>> GetSubTenants(Guid partnerTenantId, CancellationToken ct = default)
    {
        await EnsurePartnerTenant(partnerTenantId, ct);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        var tenants = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.ParentTenantId == partnerTenantId)
            .OrderBy(t => t.TenantName)
            .ToListAsync(ct);

        var tenantIds = tenants.Select(t => t.TenantId).ToList();

        var activeUsers = await _db.InstitutionUsers
            .AsNoTracking()
            .Where(u => tenantIds.Contains(u.TenantId) && u.IsActive)
            .GroupBy(u => u.TenantId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var submittedThisMonth = await _db.Submissions
            .AsNoTracking()
            .Where(s => tenantIds.Contains(s.TenantId)
                        && s.SubmittedAt >= monthStart
                        && s.Status != SubmissionStatus.Draft)
            .GroupBy(s => s.TenantId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var subscriptions = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => tenantIds.Contains(s.TenantId)
                        && s.Status != SubscriptionStatus.Cancelled
                        && s.Status != SubscriptionStatus.Expired)
            .OrderByDescending(s => s.Id)
            .ToListAsync(ct);

        var latestSubscription = subscriptions
            .GroupBy(s => s.TenantId)
            .ToDictionary(g => g.Key, g => g.First());

        return tenants.Select(t =>
        {
            latestSubscription.TryGetValue(t.TenantId, out var sub);
            return new PartnerSubTenantSummary
            {
                TenantId = t.TenantId,
                TenantName = t.TenantName,
                TenantSlug = t.TenantSlug,
                Status = t.Status.ToString(),
                ContactEmail = t.ContactEmail,
                PlanCode = sub?.Plan?.PlanCode,
                PlanName = sub?.Plan?.PlanName,
                ActiveUsers = activeUsers.GetValueOrDefault(t.TenantId),
                ReturnsSubmittedThisMonth = submittedThisMonth.GetValueOrDefault(t.TenantId),
                CreatedAt = t.CreatedAt
            };
        }).ToList();
    }

    public async Task<PartnerConfig?> GetPartnerConfig(Guid partnerTenantId, CancellationToken ct = default)
    {
        var isPartner = await IsPartnerTenant(partnerTenantId, ct);
        if (!isPartner)
        {
            return null;
        }

        return await _db.PartnerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == partnerTenantId, ct);
    }

    public async Task<PartnerConfig> UpdatePartnerConfig(Guid partnerTenantId, UpdatePartnerConfigRequest request, CancellationToken ct = default)
    {
        await EnsurePartnerTenant(partnerTenantId, ct);

        var config = await _db.PartnerConfigs
            .FirstOrDefaultAsync(x => x.TenantId == partnerTenantId, ct);

        if (config is null)
        {
            config = new PartnerConfig
            {
                TenantId = partnerTenantId,
                CreatedAt = DateTime.UtcNow
            };
            _db.PartnerConfigs.Add(config);
        }

        config.PartnerTier = request.PartnerTier;
        config.BillingModel = request.BillingModel;
        config.CommissionRate = request.BillingModel == PartnerBillingModel.Direct
            ? (request.CommissionRate ?? GetDefaultCommissionRate(request.PartnerTier))
            : null;
        config.WholesaleDiscount = request.BillingModel == PartnerBillingModel.Reseller
            ? (request.WholesaleDiscount ?? GetDefaultWholesaleDiscount(request.PartnerTier))
            : null;
        config.MaxSubTenants = Math.Max(1, request.MaxSubTenants);
        config.AgreementSignedAt = request.AgreementSignedAt;
        config.AgreementVersion = request.AgreementVersion;
        config.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return config;
    }

    public async Task<bool> IsPartnerTenant(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.TenantId == tenantId && t.TenantType == TenantType.WhiteLabelPartner, ct);
    }

    public async Task<List<Guid>> GetPartnerSubTenantIds(Guid partnerTenantId, CancellationToken ct = default)
    {
        await EnsurePartnerTenant(partnerTenantId, ct);

        return await _db.Tenants
            .AsNoTracking()
            .Where(t => t.ParentTenantId == partnerTenantId)
            .Select(t => t.TenantId)
            .ToListAsync(ct);
    }

    public async Task<List<PartnerSubTenantUserSummary>> GetSubTenantUsers(Guid partnerTenantId, Guid subTenantId, CancellationToken ct = default)
    {
        await EnsureSubTenantBelongsToPartner(partnerTenantId, subTenantId, ct);

        return await _db.InstitutionUsers
            .AsNoTracking()
            .Where(u => u.TenantId == subTenantId)
            .OrderBy(u => u.DisplayName)
            .Select(u => new PartnerSubTenantUserSummary
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Role = u.Role.ToString(),
                IsActive = u.IsActive,
                LastLoginAt = u.LastLoginAt
            })
            .ToListAsync(ct);
    }

    public async Task<PartnerSubTenantUserSummary> CreateSubTenantUser(Guid partnerTenantId, Guid subTenantId, PartnerSubTenantUserCreateRequest request, CancellationToken ct = default)
    {
        await EnsureSubTenantBelongsToPartner(partnerTenantId, subTenantId, ct);

        if (string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.DisplayName)
            || string.IsNullOrWhiteSpace(request.TemporaryPassword))
        {
            throw new InvalidOperationException("Username, email, display name, and temporary password are required.");
        }

        if (request.TemporaryPassword.Trim().Length < 8)
        {
            throw new InvalidOperationException("Temporary password must be at least 8 characters.");
        }

        if (await _db.InstitutionUsers.AnyAsync(u => u.Username == request.Username, ct))
        {
            throw new InvalidOperationException($"Username '{request.Username}' is already taken.");
        }

        if (await _db.InstitutionUsers.AnyAsync(u => u.Email == request.Email, ct))
        {
            throw new InvalidOperationException($"Email '{request.Email}' is already registered.");
        }

        if (!Enum.TryParse<InstitutionRole>(request.Role, true, out var parsedRole))
        {
            throw new InvalidOperationException($"Invalid role '{request.Role}'.");
        }

        var institutionId = request.InstitutionId
            ?? await _db.Institutions
                .AsNoTracking()
                .Where(i => i.TenantId == subTenantId && i.IsActive)
                .OrderBy(i => i.Id)
                .Select(i => (int?)i.Id)
                .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No active institution found for sub-tenant.");

        var user = new InstitutionUser
        {
            TenantId = subTenantId,
            InstitutionId = institutionId,
            Username = request.Username.Trim(),
            Email = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = AuthService.HashPassword(request.TemporaryPassword.Trim()),
            PreferredLanguage = "en",
            Role = parsedRole,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.InstitutionUsers.Add(user);
        await _db.SaveChangesAsync(ct);

        return new PartnerSubTenantUserSummary
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            LastLoginAt = user.LastLoginAt
        };
    }

    public async Task SetSubTenantUserStatus(Guid partnerTenantId, Guid subTenantId, int userId, bool isActive, CancellationToken ct = default)
    {
        await EnsureSubTenantBelongsToPartner(partnerTenantId, subTenantId, ct);

        var user = await _db.InstitutionUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == subTenantId, ct)
            ?? throw new InvalidOperationException($"User {userId} not found for sub-tenant.");

        user.IsActive = isActive;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<PartnerSubTenantSubmissionSummary>> GetSubTenantSubmissions(Guid partnerTenantId, Guid subTenantId, int take = 20, CancellationToken ct = default)
    {
        await EnsureSubTenantBelongsToPartner(partnerTenantId, subTenantId, ct);
        take = Math.Clamp(take, 1, 100);

        return await _db.Submissions
            .AsNoTracking()
            .Where(s => s.TenantId == subTenantId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(take)
            .Select(s => new PartnerSubTenantSubmissionSummary
            {
                SubmissionId = s.Id,
                ReturnCode = s.ReturnCode,
                Status = s.Status.ToString(),
                CreatedAt = s.CreatedAt,
                SubmittedAt = s.SubmittedAt
            })
            .ToListAsync(ct);
    }

    public async Task UpdateSubTenantBranding(Guid partnerTenantId, Guid subTenantId, BrandingConfig config, CancellationToken ct = default)
    {
        await EnsureSubTenantBelongsToPartner(partnerTenantId, subTenantId, ct);
        await _tenantBrandingService.UpdateBrandingConfig(subTenantId, config, ct);
    }

    public async Task<PartnerSupportTicket> CreateSupportTicket(
        Guid tenantId,
        int raisedByUserId,
        string raisedByUserName,
        string title,
        string description,
        PartnerSupportTicketPriority priority,
        CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        var partnerTenantId = tenant.ParentTenantId ?? tenant.TenantId;
        var isPartnerManaged = tenant.ParentTenantId.HasValue;

        if (!isPartnerManaged && tenant.TenantType != TenantType.WhiteLabelPartner)
        {
            throw new InvalidOperationException("Tenant is not linked to a partner.");
        }

        var now = DateTime.UtcNow;
        var ticket = new PartnerSupportTicket
        {
            TenantId = tenantId,
            PartnerTenantId = partnerTenantId,
            RaisedByUserId = raisedByUserId,
            RaisedByUserName = string.IsNullOrWhiteSpace(raisedByUserName) ? "Unknown" : raisedByUserName.Trim(),
            Title = title.Trim(),
            Description = description.Trim(),
            Priority = priority,
            Status = PartnerSupportTicketStatus.Open,
            EscalationLevel = 0,
            SlaDueAt = now.Add(GetPartnerSla(priority)),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.PartnerSupportTickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        if (_notificationOrchestrator is not null)
        {
            await _notificationOrchestrator.Notify(new NotificationRequest
            {
                TenantId = partnerTenantId,
                EventType = NotificationEvents.SystemAnnouncement,
                Title = $"Partner Support Ticket #{ticket.Id}",
                Message = $"New ticket from {tenant.TenantName}: {ticket.Title}",
                Priority = priority is PartnerSupportTicketPriority.Critical or PartnerSupportTicketPriority.High
                    ? NotificationPriority.High
                    : NotificationPriority.Normal,
                RecipientRoles = new List<string> { "Admin" },
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TicketId"] = ticket.Id.ToString(),
                    ["TenantId"] = tenantId.ToString(),
                    ["Priority"] = priority.ToString()
                }
            }, ct);
        }

        return ticket;
    }

    public async Task<List<PartnerSupportTicket>> GetSupportTicketsForPartner(Guid partnerTenantId, CancellationToken ct = default)
    {
        await EnsurePartnerTenant(partnerTenantId, ct);

        return await _db.PartnerSupportTickets
            .AsNoTracking()
            .Where(t => t.PartnerTenantId == partnerTenantId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<PartnerSupportTicket>> GetSupportTicketsForTenant(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.PartnerSupportTickets
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<PartnerSupportTicket> EscalateSupportTicket(
        Guid partnerTenantId,
        int ticketId,
        int escalatedByUserId,
        CancellationToken ct = default)
    {
        await EnsurePartnerTenant(partnerTenantId, ct);

        var ticket = await _db.PartnerSupportTickets
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.PartnerTenantId == partnerTenantId, ct)
            ?? throw new InvalidOperationException($"Support ticket {ticketId} not found.");

        ticket.EscalationLevel = 1;
        ticket.EscalatedAt = DateTime.UtcNow;
        ticket.EscalatedByUserId = escalatedByUserId;
        if (ticket.Status == PartnerSupportTicketStatus.Open)
        {
            ticket.Status = PartnerSupportTicketStatus.InProgress;
        }

        ticket.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Partner support ticket escalated. PartnerTenantId={PartnerTenantId}, TicketId={TicketId}, EscalatedBy={EscalatedByUserId}",
            partnerTenantId,
            ticketId,
            escalatedByUserId);

        if (_notificationOrchestrator is not null)
        {
            await _notificationOrchestrator.Notify(new NotificationRequest
            {
                TenantId = partnerTenantId,
                EventType = NotificationEvents.SystemAnnouncement,
                Title = $"Ticket #{ticket.Id} Escalated",
                Message = $"Support ticket '{ticket.Title}' has been escalated to RegOS platform support.",
                Priority = NotificationPriority.High,
                RecipientRoles = new List<string> { "Admin" },
                Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TicketId"] = ticket.Id.ToString(),
                    ["EscalationLevel"] = ticket.EscalationLevel.ToString()
                }
            }, ct);
        }

        return ticket;
    }

    private async Task<Tenant> EnsurePartnerTenant(Guid partnerTenantId, CancellationToken ct)
    {
        var partner = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == partnerTenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {partnerTenantId} not found.");

        if (partner.TenantType != TenantType.WhiteLabelPartner)
        {
            throw new InvalidOperationException("Tenant is not a white-label partner.");
        }

        return partner;
    }

    private async Task EnsureSubTenantBelongsToPartner(Guid partnerTenantId, Guid subTenantId, CancellationToken ct)
    {
        await EnsurePartnerTenant(partnerTenantId, ct);

        var isOwned = await _db.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.TenantId == subTenantId && t.ParentTenantId == partnerTenantId, ct);

        if (!isOwned)
        {
            throw new InvalidOperationException("Sub-tenant does not belong to the current partner.");
        }
    }

    internal static decimal GetDefaultCommissionRate(FC.Engine.Domain.Enums.PartnerTier tier) => tier switch
    {
        FC.Engine.Domain.Enums.PartnerTier.Platinum => 0.20m,
        FC.Engine.Domain.Enums.PartnerTier.Gold => 0.15m,
        _ => 0.10m
    };

    internal static decimal GetDefaultWholesaleDiscount(FC.Engine.Domain.Enums.PartnerTier tier) => tier switch
    {
        FC.Engine.Domain.Enums.PartnerTier.Platinum => 0.40m,
        FC.Engine.Domain.Enums.PartnerTier.Gold => 0.30m,
        _ => 0.20m
    };

    private static TimeSpan GetPartnerSla(PartnerSupportTicketPriority priority) => priority switch
    {
        PartnerSupportTicketPriority.Critical => TimeSpan.FromHours(4),
        PartnerSupportTicketPriority.High => TimeSpan.FromHours(8),
        PartnerSupportTicketPriority.Normal => TimeSpan.FromHours(24),
        _ => TimeSpan.FromHours(48)
    };

    private static string BuildInstitutionCode(string tenantName)
    {
        var letters = new string((tenantName ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Take(8)
            .ToArray())
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(letters))
        {
            letters = "PARTNER";
        }

        return $"{letters}-HQ";
    }
}
