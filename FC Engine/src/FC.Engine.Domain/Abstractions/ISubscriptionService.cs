using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Abstractions;

public interface ISubscriptionService
{
    Task<Subscription> CreateSubscription(Guid tenantId, string planCode, BillingFrequency frequency, CancellationToken ct = default);
    Task<Subscription> UpgradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default);
    Task<Subscription> DowngradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default);
    Task CancelSubscription(Guid tenantId, string reason, CancellationToken ct = default);

    Task<SubscriptionModule> ActivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default);
    Task DeactivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default);
    Task<List<ModuleAvailability>> GetAvailableModules(Guid tenantId, CancellationToken ct = default);

    Task<Invoice> GenerateInvoice(Guid tenantId, CancellationToken ct = default);
    Task<Invoice> IssueInvoice(int invoiceId, CancellationToken ct = default);
    Task<Payment> RecordPayment(int invoiceId, RecordPaymentRequest request, CancellationToken ct = default);
    Task VoidInvoice(int invoiceId, string reason, CancellationToken ct = default);

    Task<UsageSummary> GetUsageSummary(Guid tenantId, CancellationToken ct = default);
    Task<bool> CheckLimit(Guid tenantId, string limitType, CancellationToken ct = default);
    Task<bool> HasFeature(Guid tenantId, string featureCode, CancellationToken ct = default);

    Task<Subscription> GetActiveSubscription(Guid tenantId, CancellationToken ct = default);
    Task<List<Invoice>> GetInvoices(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<List<Payment>> GetPayments(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default);
}

public class RecordPaymentRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NGN";
    public string PaymentMethod { get; set; } = "Manual";
    public string? PaymentReference { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string? ProviderName { get; set; }
    public bool IsSuccessful { get; set; }
    public string? FailureReason { get; set; }
}

public class ModuleAvailability
{
    public int ModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public bool IsEligible { get; set; }
    public bool IsAvailableOnPlan { get; set; }
    public bool IsActive { get; set; }
    public bool IsRequired { get; set; }
    public decimal? PriceMonthly { get; set; }
    public decimal? PriceAnnual { get; set; }
    public string? Message { get; set; }
}

public class UsageSummary
{
    public Guid TenantId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public int ActiveUsers { get; set; }
    public int ActiveUsersLimit { get; set; }
    public int ActiveEntities { get; set; }
    public int ActiveEntitiesLimit { get; set; }
    public int ActiveModules { get; set; }
    public int ActiveModulesLimit { get; set; }
    public int ApiCallCount { get; set; }
    public int ApiCallLimit { get; set; }
    public decimal StorageUsedMb { get; set; }
    public int StorageLimitMb { get; set; }
    public int ReturnsSubmitted { get; set; }
}
