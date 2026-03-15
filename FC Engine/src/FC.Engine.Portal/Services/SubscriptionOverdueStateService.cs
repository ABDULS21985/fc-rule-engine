using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Scoped service that caches the overdue invoice total for the current Blazor circuit,
/// so the SubscriptionOverdueBanner doesn't re-query on every page navigation.
/// </summary>
public class SubscriptionOverdueStateService
{
    private readonly ISubscriptionService _subscriptionService;

    private bool _loaded;
    private decimal _overdueAmount;

    public decimal OverdueAmount => _overdueAmount;
    public event Action? OnChange;

    public SubscriptionOverdueStateService(ISubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    /// <summary>
    /// Loads the overdue total once per circuit. Subsequent calls return the cached value
    /// unless <paramref name="force"/> is true.
    /// </summary>
    public async Task<decimal> GetOverdueAmountAsync(Guid tenantId, bool force = false)
    {
        if (_loaded && !force)
            return _overdueAmount;

        try
        {
            var invoices = await _subscriptionService.GetInvoices(tenantId, 1, 100);
            _overdueAmount = invoices
                .Where(i => i.Status == InvoiceStatus.Overdue)
                .Sum(i => i.TotalAmount);
        }
        catch
        {
            // Non-critical — banner is informational
            _overdueAmount = 0;
        }

        _loaded = true;
        OnChange?.Invoke();
        return _overdueAmount;
    }

    /// <summary>
    /// Force-refreshes after a mutation (e.g. invoice issued, payment recorded).
    /// </summary>
    public async Task InvalidateAsync(Guid tenantId)
    {
        await GetOverdueAmountAsync(tenantId, force: true);
    }
}
