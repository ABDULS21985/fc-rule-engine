using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Events;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class SubscriptionService : ISubscriptionService
{
    private const decimal VatRate = 0.0750m;

    private readonly MetadataDbContext _db;
    private readonly IEntitlementService _entitlementService;
    private readonly SubscriptionModuleEntitlementBootstrapService? _subscriptionModuleEntitlementBootstrapService;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly IDomainEventPublisher? _domainEventPublisher;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        MetadataDbContext db,
        IEntitlementService entitlementService,
        ILogger<SubscriptionService> logger,
        INotificationOrchestrator? notificationOrchestrator = null,
        IDomainEventPublisher? domainEventPublisher = null,
        SubscriptionModuleEntitlementBootstrapService? subscriptionModuleEntitlementBootstrapService = null)
    {
        _db = db;
        _entitlementService = entitlementService;
        _logger = logger;
        _notificationOrchestrator = notificationOrchestrator;
        _domainEventPublisher = domainEventPublisher;
        _subscriptionModuleEntitlementBootstrapService = subscriptionModuleEntitlementBootstrapService;
    }

    public async Task<Subscription> CreateSubscription(
        Guid tenantId,
        string planCode,
        BillingFrequency frequency,
        CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        var existing = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired, ct);

        if (existing is not null)
            throw new InvalidOperationException($"Tenant {tenantId} already has an active/trial subscription");

        var plan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.PlanCode == planCode && p.IsActive, ct)
            ?? throw new InvalidOperationException($"Plan {planCode} not found or inactive");

        var now = DateTime.UtcNow;
        var sub = new Subscription
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            BillingFrequency = frequency,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = frequency == BillingFrequency.Monthly ? now.AddMonths(1) : now.AddYears(1),
            TrialEndsAt = now.AddDays(plan.TrialDays),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created {Status} subscription for tenant {TenantId} on plan {PlanCode}",
            sub.Status, tenantId, planCode);

        if (_subscriptionModuleEntitlementBootstrapService is not null)
        {
            await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForSubscriptionAsync(sub.Id, ct);
        }

        await _entitlementService.InvalidateCache(tenantId);
        sub.Plan = plan;
        sub.Tenant = tenant;
        return sub;
    }

    public Task<Subscription> UpgradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default)
        => ChangePlan(tenantId, newPlanCode, isUpgrade: true, ct);

    public Task<Subscription> DowngradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default)
        => ChangePlan(tenantId, newPlanCode, isUpgrade: false, ct);

    public async Task CancelSubscription(Guid tenantId, string reason, CancellationToken ct = default)
    {
        var subscription = await GetActiveSubscription(tenantId, ct);
        subscription.Cancel(reason);
        await _db.SaveChangesAsync(ct);
        await _entitlementService.InvalidateCache(tenantId);
    }

    public async Task<SubscriptionModule> ActivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default)
    {
        var subscription = await GetActiveSubscriptionInternal(tenantId, includeModules: true, ct);
        var plan = subscription.Plan ?? await _db.SubscriptionPlans.FindAsync(new object[] { subscription.PlanId }, ct)
            ?? throw new InvalidOperationException("Subscription plan not found");

        var module = await _db.Modules
            .FirstOrDefaultAsync(m => m.ModuleCode == moduleCode && m.IsActive, ct)
            ?? throw new InvalidOperationException($"Module {moduleCode} not found");

        // GATE 1: Licence eligibility
        var licenceIds = await _db.TenantLicenceTypes
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .Select(t => t.LicenceTypeId)
            .ToListAsync(ct);

        var eligible = await _db.LicenceModuleMatrix
            .AnyAsync(m => licenceIds.Contains(m.LicenceTypeId) && m.ModuleId == module.Id, ct);

        if (!eligible)
            throw new InvalidOperationException($"Module {moduleCode} not eligible for tenant licence type(s)");

        // GATE 2: Plan availability
        var pricing = await _db.PlanModulePricing
            .FirstOrDefaultAsync(p => p.PlanId == subscription.PlanId && p.ModuleId == module.Id, ct);

        if (pricing is null)
            throw new InvalidOperationException($"Module {moduleCode} not available on plan {plan.PlanName}");

        // GATE 3: Plan module limit
        var activeCount = await _db.SubscriptionModules
            .CountAsync(sm => sm.SubscriptionId == subscription.Id && sm.IsActive, ct);

        var existing = await _db.SubscriptionModules
            .FirstOrDefaultAsync(sm => sm.SubscriptionId == subscription.Id && sm.ModuleId == module.Id, ct);

        if (existing?.IsActive == true)
            throw new InvalidOperationException($"Module {moduleCode} already active");

        if (existing is null && activeCount >= plan.MaxModules)
            throw new InvalidOperationException($"Module limit reached ({activeCount}/{plan.MaxModules})");

        if (existing is not null)
        {
            existing.Reactivate(pricing.PriceMonthly, pricing.PriceAnnual);
        }
        else
        {
            existing = new SubscriptionModule
            {
                SubscriptionId = subscription.Id,
                ModuleId = module.Id,
                PriceMonthly = pricing.PriceMonthly,
                PriceAnnual = pricing.PriceAnnual,
                IsActive = true
            };
            _db.SubscriptionModules.Add(existing);
        }

        await _db.SaveChangesAsync(ct);
        await _entitlementService.InvalidateCache(tenantId);

        if (_notificationOrchestrator is not null)
        {
            try
            {
                await _notificationOrchestrator.Notify(new NotificationRequest
                {
                    TenantId = tenantId,
                    EventType = NotificationEvents.ModuleActivated,
                    Title = $"Module activated: {module.ModuleName}",
                    Message = $"{module.ModuleName} ({module.ModuleCode}) is now active for your subscription.",
                    Priority = NotificationPriority.Normal,
                    RecipientRoles = new List<string> { "Admin" },
                    ActionUrl = "/subscription/modules",
                    Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ModuleName"] = module.ModuleName,
                        ["ModuleCode"] = module.ModuleCode
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to emit module activation notification for tenant {TenantId}", tenantId);
            }
        }

        // Publish domain event for webhook/event bus (RG-30)
        if (_domainEventPublisher is not null)
        {
            try
            {
                await _domainEventPublisher.PublishAsync(new ModuleActivatedEvent(
                    tenantId, module.ModuleCode, module.ModuleName,
                    DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid()), ct);
            }
            catch { }
        }

        return existing;
    }

    public async Task DeactivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default)
    {
        var subscription = await GetActiveSubscriptionInternal(tenantId, includeModules: true, ct);

        var module = await _db.Modules
            .FirstOrDefaultAsync(m => m.ModuleCode == moduleCode && m.IsActive, ct)
            ?? throw new InvalidOperationException($"Module {moduleCode} not found");

        var requiredModuleIds = await GetRequiredModuleIdsForTenant(tenantId, ct);
        if (requiredModuleIds.Contains(module.Id))
            throw new InvalidOperationException($"Module {moduleCode} is required for tenant licence and cannot be deactivated");

        var subscriptionModule = await _db.SubscriptionModules
            .FirstOrDefaultAsync(sm => sm.SubscriptionId == subscription.Id
                && sm.ModuleId == module.Id
                && sm.IsActive, ct)
            ?? throw new InvalidOperationException($"Module {moduleCode} is not active");

        subscriptionModule.Deactivate();
        await _db.SaveChangesAsync(ct);
        await _entitlementService.InvalidateCache(tenantId);
    }

    public async Task<List<ModuleAvailability>> GetAvailableModules(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await GetActiveSubscriptionInternal(tenantId, includeModules: true, ct);

        var eligibility = await GetEligibilityMap(tenantId, ct);
        var pricing = await _db.PlanModulePricing
            .Where(p => p.PlanId == subscription.PlanId)
            .ToDictionaryAsync(p => p.ModuleId, ct);

        var activeModuleIds = subscription.Modules
            .Where(m => m.IsActive)
            .Select(m => m.ModuleId)
            .ToHashSet();

        var modules = await _db.Modules
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.ModuleCode)
            .ToListAsync(ct);

        var result = new List<ModuleAvailability>(modules.Count);
        foreach (var module in modules)
        {
            var isEligible = eligibility.TryGetValue(module.Id, out var isRequired);
            var isOnPlan = pricing.TryGetValue(module.Id, out var planPrice);
            var isActive = activeModuleIds.Contains(module.Id);

            result.Add(new ModuleAvailability
            {
                ModuleId = module.Id,
                ModuleCode = module.ModuleCode,
                ModuleName = module.ModuleName,
                IsEligible = isEligible,
                IsAvailableOnPlan = isOnPlan,
                IsActive = isActive,
                IsRequired = isRequired,
                PriceMonthly = isOnPlan ? planPrice!.PriceMonthly : null,
                PriceAnnual = isOnPlan ? planPrice!.PriceAnnual : null,
                Message = ResolveAvailabilityMessage(isEligible, isOnPlan, isActive)
            });
        }

        return result;
    }

    public async Task<Invoice> GenerateInvoice(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await GetActiveSubscriptionInternal(tenantId, includeModules: true, ct);
        var plan = subscription.Plan ?? throw new InvalidOperationException("Subscription plan not found");
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);

        PartnerConfig? partnerConfig = null;
        Guid? partnerTenantId = null;
        if (tenant?.ParentTenantId is Guid parentTenantId)
        {
            partnerTenantId = parentTenantId;
            partnerConfig = await _db.PartnerConfigs
                .AsNoTracking()
                .FirstOrDefaultAsync(pc => pc.TenantId == parentTenantId, ct);
        }

        var periodStart = DateOnly.FromDateTime(subscription.CurrentPeriodStart.Date);
        var periodEnd = DateOnly.FromDateTime(subscription.CurrentPeriodEnd.Date);

        var invoice = new Invoice
        {
            TenantId = tenantId,
            SubscriptionId = subscription.Id,
            InvoiceNumber = await GenerateInvoiceNumber(tenantId, subscription.CurrentPeriodStart, ct),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            VatRate = VatRate,
            Status = InvoiceStatus.Draft,
            Currency = "NGN"
        };

        var lineItems = new List<InvoiceLineItem>
        {
            new()
            {
                LineType = "BasePlan",
                Description = $"{plan.PlanName} Plan ({subscription.BillingFrequency})",
                Quantity = 1,
                UnitPrice = subscription.BillingFrequency == BillingFrequency.Monthly
                    ? plan.BasePriceMonthly
                    : plan.BasePriceAnnual,
                DisplayOrder = 1
            }
        };

        var activeModules = subscription.Modules
            .Where(m => m.IsActive)
            .ToList();

        var pricingMap = await _db.PlanModulePricing
            .Where(p => p.PlanId == subscription.PlanId)
            .ToDictionaryAsync(p => p.ModuleId, ct);

        var moduleLookup = await _db.Modules
            .Where(m => activeModules.Select(sm => sm.ModuleId).Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var order = 2;
        foreach (var sm in activeModules.OrderBy(m => m.ModuleId))
        {
            if (!pricingMap.TryGetValue(sm.ModuleId, out var pricing))
            {
                continue;
            }

            if (pricing.IsIncludedInBase)
            {
                continue;
            }

            if (!moduleLookup.TryGetValue(sm.ModuleId, out var module))
            {
                continue;
            }

            var unitPrice = subscription.BillingFrequency == BillingFrequency.Monthly
                ? sm.PriceMonthly
                : sm.PriceAnnual;

            lineItems.Add(new InvoiceLineItem
            {
                ModuleId = sm.ModuleId,
                LineType = "Module",
                Description = $"{module.ModuleName} ({module.ModuleCode})",
                Quantity = 1,
                UnitPrice = unitPrice,
                DisplayOrder = order++
            });
        }

        var grossSubtotal = lineItems.Sum(x => x.UnitPrice * x.Quantity);
        decimal? wholesaleDiscountRate = null;
        decimal wholesaleDiscountAmount = 0m;

        if (partnerConfig?.BillingModel == PartnerBillingModel.Reseller)
        {
            wholesaleDiscountRate = NormalizeRate(
                partnerConfig.WholesaleDiscount ?? GetDefaultWholesaleDiscount(partnerConfig.PartnerTier));

            if (wholesaleDiscountRate > 0m)
            {
                wholesaleDiscountAmount = decimal.Round(
                    grossSubtotal * wholesaleDiscountRate.Value,
                    2,
                    MidpointRounding.AwayFromZero);

                if (wholesaleDiscountAmount > 0m)
                {
                    lineItems.Add(new InvoiceLineItem
                    {
                        LineType = "PartnerDiscount",
                        Description = $"Partner wholesale discount ({wholesaleDiscountRate:P0})",
                        Quantity = 1,
                        UnitPrice = -wholesaleDiscountAmount,
                        DisplayOrder = order++
                    });
                }
            }
        }

        foreach (var li in lineItems)
        {
            li.LineTotal = li.UnitPrice * li.Quantity;
            invoice.LineItems.Add(li);
        }

        invoice.RecalculateTotals();
        _db.Invoices.Add(invoice);
        await _db.SaveChangesAsync(ct);

        if (partnerConfig is not null && partnerTenantId.HasValue)
        {
            decimal? commissionRate = null;
            var commissionAmount = 0m;
            if (partnerConfig.BillingModel == PartnerBillingModel.Direct)
            {
                commissionRate = NormalizeRate(
                    partnerConfig.CommissionRate ?? GetDefaultCommissionRate(partnerConfig.PartnerTier));
                commissionAmount = decimal.Round(
                    grossSubtotal * commissionRate.Value,
                    2,
                    MidpointRounding.AwayFromZero);
            }

            _db.PartnerRevenueRecords.Add(new PartnerRevenueRecord
            {
                TenantId = tenantId,
                PartnerTenantId = partnerTenantId.Value,
                InvoiceId = invoice.Id,
                BillingModel = partnerConfig.BillingModel,
                GrossAmount = decimal.Round(grossSubtotal, 2, MidpointRounding.AwayFromZero),
                NetAmount = decimal.Round(invoice.Subtotal, 2, MidpointRounding.AwayFromZero),
                CommissionRate = commissionRate,
                CommissionAmount = commissionAmount,
                WholesaleDiscountRate = wholesaleDiscountRate,
                WholesaleDiscountAmount = wholesaleDiscountAmount,
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }

        return invoice;
    }

    public async Task<Invoice> IssueInvoice(int invoiceId, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices
            .Include(i => i.LineItems)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");

        var dueDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(14));
        invoice.Issue(dueDate);

        await _db.SaveChangesAsync(ct);
        return invoice;
    }

    public async Task<Payment> RecordPayment(int invoiceId, RecordPaymentRequest request, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Subscription)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");

        var payment = new Payment
        {
            TenantId = invoice.TenantId,
            InvoiceId = invoiceId,
            Amount = request.Amount,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "NGN" : request.Currency,
            PaymentMethod = request.PaymentMethod,
            PaymentReference = request.PaymentReference,
            ProviderTransactionId = request.ProviderTransactionId,
            ProviderName = request.ProviderName,
            Status = PaymentStatus.Pending
        };

        if (request.IsSuccessful)
        {
            payment.MarkConfirmed();
            invoice.MarkPaid();

            var subscription = invoice.Subscription
                ?? throw new InvalidOperationException("Invoice subscription not found");

            if (subscription.Status == SubscriptionStatus.Trial)
            {
                subscription.Activate();
            }
            else if (subscription.Status is SubscriptionStatus.PastDue or SubscriptionStatus.Suspended)
            {
                subscription.Reactivate();
            }

            subscription.AdvancePeriod();
            await _entitlementService.InvalidateCache(invoice.TenantId);
        }
        else
        {
            payment.MarkFailed(request.FailureReason ?? "Payment was not successful");
        }

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return payment;
    }

    public async Task VoidInvoice(int invoiceId, string reason, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found");

        invoice.Void(reason);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<UsageSummary> GetUsageSummary(Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await GetActiveSubscriptionInternal(tenantId, includeModules: false, ct);
        var plan = subscription.Plan ?? throw new InvalidOperationException("Subscription plan not found");

        var usage = await _db.UsageRecords
            .Where(u => u.TenantId == tenantId)
            .OrderByDescending(u => u.RecordDate)
            .FirstOrDefaultAsync(ct);

        usage ??= new UsageRecord
        {
            TenantId = tenantId,
            RecordDate = DateOnly.FromDateTime(DateTime.UtcNow.Date)
        };

        return new UsageSummary
        {
            TenantId = tenantId,
            AsOfDate = usage.RecordDate,
            ActiveUsers = usage.ActiveUsers,
            ActiveUsersLimit = plan.MaxUsersPerEntity,
            ActiveEntities = usage.ActiveEntities,
            ActiveEntitiesLimit = plan.MaxEntities,
            ActiveModules = usage.ActiveModules,
            ActiveModulesLimit = plan.MaxModules,
            ApiCallCount = usage.ApiCallCount,
            ApiCallLimit = plan.MaxApiCallsPerMonth,
            StorageUsedMb = usage.StorageUsedMb,
            StorageLimitMb = plan.MaxStorageMb,
            ReturnsSubmitted = usage.ReturnsSubmitted
        };
    }

    public async Task<bool> CheckLimit(Guid tenantId, string limitType, CancellationToken ct = default)
    {
        var usage = await GetUsageSummary(tenantId, ct);

        return limitType.ToLowerInvariant() switch
        {
            "users" => usage.ActiveUsers <= usage.ActiveUsersLimit,
            "entities" => usage.ActiveEntities <= usage.ActiveEntitiesLimit,
            "modules" => usage.ActiveModules <= usage.ActiveModulesLimit,
            "api" => usage.ApiCallLimit <= 0 || usage.ApiCallCount <= usage.ApiCallLimit,
            "storage" => usage.StorageUsedMb <= usage.StorageLimitMb,
            _ => true
        };
    }

    public async Task<bool> HasFeature(Guid tenantId, string featureCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(featureCode))
        {
            return false;
        }

        var subscription = await _db.Subscriptions
            .Where(s => s.TenantId == tenantId)
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired)
            .OrderByDescending(s => s.UpdatedAt)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(ct);

        if (subscription?.Plan is null)
        {
            return false;
        }

        return subscription.Plan.HasFeature(featureCode);
    }

    public async Task<Subscription> GetActiveSubscription(Guid tenantId, CancellationToken ct = default)
    {
        return await GetActiveSubscriptionInternal(tenantId, includeModules: true, ct);
    }

    public async Task<List<Invoice>> GetInvoices(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return await _db.Invoices
            .Include(i => i.LineItems)
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> GetInvoiceCount(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.Invoices.CountAsync(i => i.TenantId == tenantId, ct);
    }

    public async Task<List<Payment>> GetPayments(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        return await _db.Payments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> GetPaymentCount(Guid tenantId, CancellationToken ct = default)
    {
        return await _db.Payments.CountAsync(p => p.TenantId == tenantId, ct);
    }

    public async Task<string> GenerateInvoiceNumber(Guid tenantId, DateTime periodStart, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found");

        var yearMonth = periodStart.ToString("yyyyMM");
        var slugUpper = tenant.TenantSlug.Replace("_", "-").ToUpperInvariant();
        var prefix = $"INV-{slugUpper}-{yearMonth}";

        var lastNumber = await _db.Invoices
            .Where(i => i.TenantId == tenantId && i.InvoiceNumber.StartsWith(prefix))
            .OrderByDescending(i => i.InvoiceNumber)
            .Select(i => i.InvoiceNumber)
            .FirstOrDefaultAsync(ct);

        var seq = 1;
        if (!string.IsNullOrWhiteSpace(lastNumber))
        {
            var suffix = lastNumber.Split('-').LastOrDefault();
            if (int.TryParse(suffix, out var parsed))
            {
                seq = parsed + 1;
            }
        }

        return $"{prefix}-{seq:D4}";
    }

    private static decimal NormalizeRate(decimal value)
    {
        if (value < 0m) return 0m;
        if (value > 1m) return 1m;
        return decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private static decimal GetDefaultCommissionRate(FC.Engine.Domain.Enums.PartnerTier tier) => tier switch
    {
        FC.Engine.Domain.Enums.PartnerTier.Platinum => 0.20m,
        FC.Engine.Domain.Enums.PartnerTier.Gold => 0.15m,
        _ => 0.10m
    };

    private static decimal GetDefaultWholesaleDiscount(FC.Engine.Domain.Enums.PartnerTier tier) => tier switch
    {
        FC.Engine.Domain.Enums.PartnerTier.Platinum => 0.40m,
        FC.Engine.Domain.Enums.PartnerTier.Gold => 0.30m,
        _ => 0.20m
    };

    private async Task<Subscription> ChangePlan(
        Guid tenantId,
        string newPlanCode,
        bool isUpgrade,
        CancellationToken ct)
    {
        var subscription = await GetActiveSubscriptionInternal(tenantId, includeModules: true, ct);
        var currentPlan = subscription.Plan ?? throw new InvalidOperationException("Current plan not found");

        var newPlan = await _db.SubscriptionPlans
            .FirstOrDefaultAsync(p => p.PlanCode == newPlanCode && p.IsActive, ct)
            ?? throw new InvalidOperationException($"Plan {newPlanCode} not found or inactive");

        if (subscription.PlanId == newPlan.Id)
            return subscription;

        if (isUpgrade && newPlan.Tier < currentPlan.Tier)
            throw new InvalidOperationException($"Plan {newPlanCode} is not an upgrade from {currentPlan.PlanCode}");

        if (!isUpgrade && newPlan.Tier > currentPlan.Tier)
            throw new InvalidOperationException($"Plan {newPlanCode} is not a downgrade from {currentPlan.PlanCode}");

        var activeModules = subscription.Modules.Where(m => m.IsActive).ToList();
        if (activeModules.Count > newPlan.MaxModules)
        {
            throw new InvalidOperationException(
                $"Cannot move to {newPlan.PlanCode}: active modules {activeModules.Count} exceed new limit {newPlan.MaxModules}");
        }

        var newPlanModuleIds = await _db.PlanModulePricing
            .Where(p => p.PlanId == newPlan.Id)
            .Select(p => p.ModuleId)
            .ToListAsync(ct);

        var unavailable = activeModules
            .Where(m => !newPlanModuleIds.Contains(m.ModuleId))
            .Select(m => m.ModuleId)
            .ToList();

        if (unavailable.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot move to {newPlan.PlanCode}: one or more active modules are unavailable on target plan");
        }

        subscription.PlanId = newPlan.Id;
        subscription.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (_subscriptionModuleEntitlementBootstrapService is not null)
        {
            await _subscriptionModuleEntitlementBootstrapService.EnsureIncludedModulesForSubscriptionAsync(subscription.Id, ct);
        }

        await _entitlementService.InvalidateCache(tenantId);

        // Publish domain event for webhook/event bus (RG-30)
        if (_domainEventPublisher is not null)
        {
            try
            {
                var changeType = isUpgrade ? "Upgraded" : "Downgraded";
                await _domainEventPublisher.PublishAsync(new SubscriptionChangedEvent(
                    tenantId, changeType, currentPlan.PlanCode, newPlan.PlanCode,
                    DateTime.UtcNow, DateTime.UtcNow, Guid.NewGuid()), ct);
            }
            catch { }
        }

        subscription.Plan = newPlan;
        return subscription;
    }

    private async Task<Subscription> GetActiveSubscriptionInternal(
        Guid tenantId,
        bool includeModules,
        CancellationToken ct)
    {
        IQueryable<Subscription> query = _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.TenantId == tenantId
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired)
            .OrderByDescending(s => s.Id);

        if (includeModules)
        {
            query = query
                .Include(s => s.Modules)
                    .ThenInclude(sm => sm.Module);
        }

        var sub = await query.FirstOrDefaultAsync(ct);
        if (sub is null)
            throw new InvalidOperationException($"Tenant {tenantId} has no active subscription");

        return sub;
    }

    private async Task<HashSet<int>> GetRequiredModuleIdsForTenant(Guid tenantId, CancellationToken ct)
    {
        var licenceIds = await _db.TenantLicenceTypes
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .Select(t => t.LicenceTypeId)
            .ToListAsync(ct);

        var requiredModuleIds = await _db.LicenceModuleMatrix
            .Where(m => licenceIds.Contains(m.LicenceTypeId) && m.IsRequired)
            .Select(m => m.ModuleId)
            .ToListAsync(ct);

        return requiredModuleIds.ToHashSet();
    }

    private async Task<Dictionary<int, bool>> GetEligibilityMap(Guid tenantId, CancellationToken ct)
    {
        var licenceIds = await _db.TenantLicenceTypes
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .Select(t => t.LicenceTypeId)
            .ToListAsync(ct);

        var matrix = await _db.LicenceModuleMatrix
            .Where(m => licenceIds.Contains(m.LicenceTypeId))
            .ToListAsync(ct);

        return matrix
            .GroupBy(m => m.ModuleId)
            .ToDictionary(g => g.Key, g => g.Any(x => x.IsRequired));
    }

    public async Task<List<SubscriptionPlan>> GetAvailablePlans(Guid tenantId, CancellationToken ct = default)
    {
        var sub = await _db.Subscriptions
            .Include(s => s.Plan)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired, ct);

        var currentTier = sub?.Plan?.Tier ?? 0;

        return await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive && p.Tier > currentTier)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Tier)
            .ToListAsync(ct);
    }

    private static string ResolveAvailabilityMessage(bool isEligible, bool isOnPlan, bool isActive)
    {
        if (isActive)
            return "Active";
        if (!isEligible)
            return "Not eligible for tenant licence type";
        if (!isOnPlan)
            return "Not available on current plan";
        return "Available";
    }
}
