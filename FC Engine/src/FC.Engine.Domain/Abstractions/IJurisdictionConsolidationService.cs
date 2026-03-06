using FC.Engine.Domain.Models;

namespace FC.Engine.Domain.Abstractions;

public interface IJurisdictionConsolidationService
{
    Task<CrossJurisdictionConsolidation> GetConsolidation(
        Guid tenantId,
        string reportingCurrency = "NGN",
        CancellationToken ct = default);
}
