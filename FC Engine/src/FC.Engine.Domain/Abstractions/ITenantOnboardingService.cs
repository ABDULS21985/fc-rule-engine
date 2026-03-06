namespace FC.Engine.Domain.Abstractions;

public interface ITenantOnboardingService
{
    Task<TenantOnboardingResult> OnboardTenant(TenantOnboardingRequest request, CancellationToken ct = default);
}

public class TenantOnboardingRequest
{
    public string TenantName { get; set; } = string.Empty;
    public string? TenantSlug { get; set; }
    public Enums.TenantType TenantType { get; set; }
    public Guid? ParentTenantId { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? RcNumber { get; set; }
    public string? TaxId { get; set; }
    public List<string> LicenceTypeCodes { get; set; } = new();
    public string? SubscriptionPlanCode { get; set; }

    // First admin user
    public string AdminEmail { get; set; } = string.Empty;
    public string AdminFullName { get; set; } = string.Empty;
    public string? AdminPhone { get; set; }

    // First institution
    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string? InstitutionType { get; set; }
    public string? JurisdictionCode { get; set; } = "NG";
}

public class TenantOnboardingResult
{
    public bool Success { get; set; }
    public Guid TenantId { get; set; }
    public string TenantSlug { get; set; } = string.Empty;
    public int InstitutionId { get; set; }
    public string AdminTemporaryPassword { get; set; } = string.Empty;
    public List<string> ActivatedModules { get; set; } = new();
    public int ReturnPeriodsCreated { get; set; }
    public List<string> Errors { get; set; } = new();
}
