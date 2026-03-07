namespace FC.Engine.Portal.Services;

/// <summary>
/// Scoped service that tracks when a partner admin is "viewing as" a sub-tenant institution.
/// State is per-circuit (Blazor Server) — no persistence needed.
/// </summary>
public class PartnerViewAsService
{
    public Guid? ViewingAsTenantId { get; private set; }
    public string? ViewingAsTenantName { get; private set; }
    public bool IsViewingAs => ViewingAsTenantId.HasValue;

    public event Action? OnChange;

    public void StartViewing(Guid tenantId, string tenantName)
    {
        ViewingAsTenantId = tenantId;
        ViewingAsTenantName = tenantName;
        OnChange?.Invoke();
    }

    public void StopViewing()
    {
        ViewingAsTenantId = null;
        ViewingAsTenantName = null;
        OnChange?.Invoke();
    }
}
