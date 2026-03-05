using FC.Engine.Domain.Enums;
using System.Text.Json;

namespace FC.Engine.Domain.Entities;

public class Tenant
{
    public Guid TenantId { get; private set; }
    public string TenantName { get; private set; } = string.Empty;
    public string TenantSlug { get; private set; } = string.Empty;
    public TenantType TenantType { get; private set; }
    public TenantStatus Status { get; private set; }

    // Contact & registration
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public string? RcNumber { get; set; }

    // Configuration
    public int FiscalYearStartMonth { get; set; } = 1;
    public string Timezone { get; set; } = "Africa/Lagos";
    public string DefaultCurrency { get; set; } = "NGN";
    public string? BrandingConfig { get; set; }
    public string? CustomDomain { get; set; }

    // Plan limits
    public int MaxInstitutions { get; set; } = 1;
    public int MaxUsersPerEntity { get; set; } = 10;

    // Timestamps
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? DeactivatedAt { get; private set; }

    // Navigation
    public List<Institution> Institutions { get; set; } = new();
    public List<TenantLicenceType> TenantLicenceTypes { get; set; } = new();
    public List<Subscription> Subscriptions { get; set; } = new();
    public TenantSsoConfig? SsoConfig { get; set; }

    // Required by EF Core
    private Tenant() { }

    public static Tenant Create(string name, string slug, TenantType type, string? contactEmail = null)
    {
        return new Tenant
        {
            TenantId = Guid.NewGuid(),
            TenantName = name,
            TenantSlug = slug,
            TenantType = type,
            Status = TenantStatus.PendingActivation,
            ContactEmail = contactEmail,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    // ── State Machine ──

    public void Activate()
    {
        if (Status != TenantStatus.PendingActivation)
            throw new InvalidOperationException($"Cannot activate tenant in {Status} state");
        Status = TenantStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Suspend(string reason)
    {
        if (Status != TenantStatus.Active)
            throw new InvalidOperationException($"Cannot suspend tenant in {Status} state");
        Status = TenantStatus.Suspended;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        if (Status != TenantStatus.Suspended)
            throw new InvalidOperationException($"Cannot reactivate tenant in {Status} state");
        Status = TenantStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (Status is not (TenantStatus.Active or TenantStatus.Suspended))
            throw new InvalidOperationException($"Cannot deactivate tenant in {Status} state");
        Status = TenantStatus.Deactivated;
        DeactivatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Archive()
    {
        if (Status != TenantStatus.Deactivated)
            throw new InvalidOperationException($"Cannot archive tenant in {Status} state");
        Status = TenantStatus.Archived;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDetails(string name, string? contactEmail, string? contactPhone, string? address)
    {
        TenantName = name;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        Address = address;
        UpdatedAt = DateTime.UtcNow;
    }

    public Domain.ValueObjects.BrandingConfig GetBrandingConfig()
    {
        Domain.ValueObjects.BrandingConfig? custom = null;
        if (!string.IsNullOrWhiteSpace(BrandingConfig))
        {
            try
            {
                custom = JsonSerializer.Deserialize<Domain.ValueObjects.BrandingConfig>(BrandingConfig);
            }
            catch
            {
                // Fall back to defaults if persisted JSON is invalid.
            }
        }

        return Domain.ValueObjects.BrandingConfig.WithDefaults(custom);
    }

    public void SetBrandingConfig(Domain.ValueObjects.BrandingConfig config)
    {
        var merged = Domain.ValueObjects.BrandingConfig.WithDefaults(config);
        BrandingConfig = JsonSerializer.Serialize(merged);
        UpdatedAt = DateTime.UtcNow;
    }
}
