using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.ValueObjects;

public class TenantEntitlement
{
    public Guid TenantId { get; init; }
    public TenantStatus TenantStatus { get; init; }
    public IReadOnlyList<string> LicenceTypeCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<EntitledModule> EligibleModules { get; init; } = Array.Empty<EntitledModule>();
    public IReadOnlyList<EntitledModule> ActiveModules { get; init; } = Array.Empty<EntitledModule>();
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();
    public string PlanCode { get; init; } = "DEFAULT";
    public DateTime ResolvedAt { get; init; }
}
