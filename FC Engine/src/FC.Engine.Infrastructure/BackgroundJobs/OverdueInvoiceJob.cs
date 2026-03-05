using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.BackgroundJobs;

public class OverdueInvoiceJob : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OverdueInvoiceJob> _logger;

    public OverdueInvoiceJob(
        IServiceProvider serviceProvider,
        ILogger<OverdueInvoiceJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessOverdueFlows(stoppingToken);

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private async Task ProcessOverdueFlows(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
            var entitlement = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var now = DateTime.UtcNow;

            var changedTenantIds = new HashSet<Guid>();

            var overdueInvoices = await db.Invoices
                .Include(i => i.Subscription)
                .Where(i => i.Status == InvoiceStatus.Issued)
                .Where(i => i.DueDate.HasValue && i.DueDate.Value < today)
                .ToListAsync(ct);

            foreach (var invoice in overdueInvoices)
            {
                invoice.MarkOverdue();

                var sub = invoice.Subscription;
                if (sub is not null && sub.Status == SubscriptionStatus.Active)
                {
                    sub.MarkPastDue();
                    changedTenantIds.Add(sub.TenantId);
                }

                changedTenantIds.Add(invoice.TenantId);
            }

            var graceExpired = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.PastDue)
                .Where(s => s.GracePeriodEndsAt.HasValue && s.GracePeriodEndsAt.Value < now)
                .ToListAsync(ct);

            foreach (var subscription in graceExpired)
            {
                subscription.Suspend("Grace period expired");
                changedTenantIds.Add(subscription.TenantId);
            }

            var expiredTrials = await db.Subscriptions
                .Where(s => s.Status == SubscriptionStatus.Trial)
                .Where(s => s.TrialEndsAt.HasValue && s.TrialEndsAt.Value < now)
                .ToListAsync(ct);

            foreach (var subscription in expiredTrials)
            {
                subscription.Expire();
                changedTenantIds.Add(subscription.TenantId);
            }

            if (changedTenantIds.Count > 0)
            {
                await db.SaveChangesAsync(ct);
                foreach (var tenantId in changedTenantIds)
                {
                    await entitlement.InvalidateCache(tenantId);
                }
            }

            _logger.LogInformation(
                "OverdueInvoiceJob processed {OverdueInvoices} overdue invoices, {GraceSuspended} suspended subscriptions, {TrialExpired} expired trials",
                overdueInvoices.Count,
                graceExpired.Count,
                expiredTrials.Count);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "OverdueInvoiceJob failed while processing overdue and trial-expiry flows");
        }
    }
}
