using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Domain.Abstractions;

public interface IPartnerManagementService
{
    Task<TenantOnboardingResult> OnboardPartner(PartnerOnboardingRequest request, CancellationToken ct = default);
    Task<TenantOnboardingResult> CreateSubTenant(Guid partnerTenantId, SubTenantCreateRequest request, CancellationToken ct = default);
    Task<List<PartnerSubTenantSummary>> GetSubTenants(Guid partnerTenantId, CancellationToken ct = default);
    Task<PartnerConfig?> GetPartnerConfig(Guid partnerTenantId, CancellationToken ct = default);
    Task<PartnerConfig> UpdatePartnerConfig(Guid partnerTenantId, UpdatePartnerConfigRequest request, CancellationToken ct = default);
    Task<bool> IsPartnerTenant(Guid tenantId, CancellationToken ct = default);
    Task<List<Guid>> GetPartnerSubTenantIds(Guid partnerTenantId, CancellationToken ct = default);
    Task<List<PartnerSubTenantUserSummary>> GetSubTenantUsers(Guid partnerTenantId, Guid subTenantId, CancellationToken ct = default);
    Task<PartnerSubTenantUserSummary> CreateSubTenantUser(Guid partnerTenantId, Guid subTenantId, PartnerSubTenantUserCreateRequest request, CancellationToken ct = default);
    Task SetSubTenantUserStatus(Guid partnerTenantId, Guid subTenantId, int userId, bool isActive, CancellationToken ct = default);
    Task<List<PartnerSubTenantSubmissionSummary>> GetSubTenantSubmissions(Guid partnerTenantId, Guid subTenantId, int take = 20, CancellationToken ct = default);
    Task UpdateSubTenantBranding(Guid partnerTenantId, Guid subTenantId, BrandingConfig config, CancellationToken ct = default);

    Task<PartnerSupportTicket> CreateSupportTicket(
        Guid tenantId,
        int raisedByUserId,
        string raisedByUserName,
        string title,
        string description,
        PartnerSupportTicketPriority priority,
        CancellationToken ct = default);

    Task<List<PartnerSupportTicket>> GetSupportTicketsForPartner(Guid partnerTenantId, CancellationToken ct = default);
    Task<List<PartnerSupportTicket>> GetSupportTicketsForTenant(Guid tenantId, CancellationToken ct = default);
    Task<PartnerSupportTicket> EscalateSupportTicket(
        Guid partnerTenantId,
        int ticketId,
        int escalatedByUserId,
        CancellationToken ct = default);
}

public class PartnerOnboardingRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string? TenantSlug { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? RcNumber { get; set; }
    public string? TaxId { get; set; }
    public string SubscriptionPlanCode { get; set; } = "ENTERPRISE";

    public string AdminEmail { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = string.Empty;

    public Enums.PartnerTier PartnerTier { get; set; } = Enums.PartnerTier.Silver;
    public PartnerBillingModel BillingModel { get; set; } = PartnerBillingModel.Direct;
    public decimal? CommissionRate { get; set; }
    public decimal? WholesaleDiscount { get; set; }
    public int MaxSubTenants { get; set; } = 10;
    public DateTime? AgreementSignedAt { get; set; }
    public string? AgreementVersion { get; set; }
}

public class SubTenantCreateRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string? TenantSlug { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? RcNumber { get; set; }
    public string? TaxId { get; set; }
    public List<string> LicenceTypeCodes { get; set; } = new();
    public string SubscriptionPlanCode { get; set; } = "STARTER";

    public string AdminEmail { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = string.Empty;

    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? InstitutionType { get; set; }
    public string? JurisdictionCode { get; set; } = "NG";
}

public class PartnerSubTenantSummary
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ContactEmail { get; set; }
    public string? PlanCode { get; set; }
    public string? PlanName { get; set; }
    public int ActiveUsers { get; set; }
    public int ReturnsSubmittedThisMonth { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdatePartnerConfigRequest
{
    public Enums.PartnerTier PartnerTier { get; set; }
    public PartnerBillingModel BillingModel { get; set; }
    public decimal? CommissionRate { get; set; }
    public decimal? WholesaleDiscount { get; set; }
    public int MaxSubTenants { get; set; }
    public DateTime? AgreementSignedAt { get; set; }
    public string? AgreementVersion { get; set; }
}

public class PartnerSubTenantUserSummary
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class PartnerSubTenantUserCreateRequest
{
    public int? InstitutionId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = InstitutionRole.Maker.ToString();
    public string TemporaryPassword { get; set; } = string.Empty;
}

public class PartnerSubTenantSubmissionSummary
{
    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? ReturnReference { get; set; }
}
