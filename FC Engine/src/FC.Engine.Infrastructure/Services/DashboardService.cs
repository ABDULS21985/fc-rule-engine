using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly MetadataDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        MetadataDbContext db,
        IMemoryCache cache,
        IEntitlementService entitlementService,
        ILogger<DashboardService> logger)
    {
        _db = db;
        _cache = cache;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    public Task<DashboardSummary> GetSummary(Guid tenantId, CancellationToken ct = default)
    {
        return GetOrCreateCached($"dashboard:summary:{tenantId}", async () =>
        {
            var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);

            var overdueReturns = await _db.ReturnPeriods
                .AsNoTracking()
                .Where(rp => rp.TenantId == tenantId && rp.Status == "Overdue")
                .CountAsync(ct);

            var pendingReturns = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId)
                .Where(s => s.Status == SubmissionStatus.Draft
                         || s.Status == SubmissionStatus.Parsing
                         || s.Status == SubmissionStatus.Validating
                         || s.Status == SubmissionStatus.PendingApproval)
                .CountAsync(ct);

            var compliance = await GetComplianceSummary(tenantId, ct);

            return new DashboardSummary
            {
                ActiveModules = entitlement.ActiveModules.Count,
                OverdueReturns = overdueReturns,
                PendingReturns = pendingReturns,
                ComplianceScore = decimal.Round(compliance.OverallScore, 2),
                GeneratedAt = DateTime.UtcNow
            };
        });
    }

    public Task<ModuleDashboardData> GetModuleDashboard(Guid tenantId, string moduleCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            throw new ArgumentException("Module code is required.", nameof(moduleCode));
        }

        var normalizedCode = moduleCode.Trim().ToUpperInvariant();
        return GetOrCreateCached($"dashboard:module:{tenantId}:{normalizedCode}", async () =>
        {
            var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
            var entitledModule = entitlement.ActiveModules
                .FirstOrDefault(m => string.Equals(m.ModuleCode, normalizedCode, StringComparison.OrdinalIgnoreCase));

            if (entitledModule is null)
            {
                throw new InvalidOperationException($"Tenant {tenantId} is not entitled to module {normalizedCode}");
            }

            var module = await _db.Modules
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == entitledModule.ModuleId, ct)
                ?? throw new InvalidOperationException($"Module {normalizedCode} not found");

            var periodRows = await _db.ReturnPeriods
                .AsNoTracking()
                .Where(rp => rp.TenantId == tenantId && rp.ModuleId == module.Id)
                .OrderByDescending(rp => rp.Year)
                .ThenByDescending(rp => rp.Month)
                .ThenByDescending(rp => rp.Quarter)
                .ThenByDescending(rp => rp.Id)
                .Take(6)
                .ToListAsync(ct);

            periodRows = periodRows
                .OrderBy(rp => rp.Year)
                .ThenBy(rp => rp.Month)
                .ThenBy(rp => rp.Quarter)
                .ThenBy(rp => rp.Id)
                .ToList();

            var periodIds = periodRows.Select(p => p.Id).ToList();
            var submissions = await LoadLatestSubmissionsByPeriod(tenantId, periodIds, ct);
            var errorCounts = await LoadValidationIssueCounts(submissions.Values.Select(v => v.Id), ct);
            var slaLookup = await LoadSlaLookup(tenantId, module.Id, periodIds, ct);

            var items = new List<ModulePeriodStatusItem>();
            foreach (var period in periodRows)
            {
                submissions.TryGetValue(period.Id, out var submission);
                var issues = submission is null
                    ? ValidationIssueCount.Empty
                    : errorCounts.GetValueOrDefault(submission.Id, ValidationIssueCount.Empty);

                var ragClass = ComputeRagClass(period, submission);
                var completion = ComputeCompletionPercent(submission?.Status, issues.ErrorCount, issues.WarningCount, period.Status);
                var onTime = slaLookup.TryGetValue(period.Id, out var sla) && sla.OnTime == true;

                items.Add(new ModulePeriodStatusItem
                {
                    PeriodId = period.Id,
                    Label = FormatPeriodLabel(period),
                    Status = MapSubmissionStatus(submission?.Status, period.Status),
                    RagClass = ragClass,
                    CompletionPercent = completion,
                    ValidationErrorCount = issues.ErrorCount,
                    ValidationWarningCount = issues.WarningCount,
                    Deadline = period.EffectiveDeadline,
                    OnTime = onTime,
                    SubmissionId = submission?.Id,
                    SubmissionStatus = submission?.Status
                });
            }

            var data = new ModuleDashboardData
            {
                ModuleCode = module.ModuleCode,
                ModuleName = module.ModuleName,
                Periods = items,
                ValidationErrorTrend = BuildValidationTrend(items),
                SubmissionTimelinessTrend = BuildTimelinessTrend(items),
                DataQualityTrend = BuildDataQualityTrend(items),
                FilingStatusBreakdown = BuildStatusBreakdown(items)
            };

            return data;
        });
    }

    public Task<ComplianceSummaryData> GetComplianceSummary(Guid tenantId, CancellationToken ct = default)
    {
        return GetOrCreateCached($"dashboard:compliance:{tenantId}", async () =>
        {
            var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
            var moduleIds = entitlement.ActiveModules.Select(m => m.ModuleId).Distinct().ToList();

            if (moduleIds.Count == 0)
            {
                return new ComplianceSummaryData
                {
                    OverallScore = 0,
                    GeneratedAt = DateTime.UtcNow
                };
            }

            var moduleMap = await _db.Modules
                .AsNoTracking()
                .Where(m => moduleIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, ct);

            var allPeriods = await _db.ReturnPeriods
                .AsNoTracking()
                .Where(rp => rp.TenantId == tenantId && rp.ModuleId != null && moduleIds.Contains(rp.ModuleId.Value))
                .OrderByDescending(rp => rp.Year)
                .ThenByDescending(rp => rp.Month)
                .ThenByDescending(rp => rp.Quarter)
                .ThenByDescending(rp => rp.Id)
                .ToListAsync(ct);

            var groupedPeriods = allPeriods
                .GroupBy(rp => rp.ModuleId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.EffectiveDeadline).Take(6).ToList());

            var periodIds = groupedPeriods.Values.SelectMany(v => v).Select(v => v.Id).Distinct().ToList();
            var submissions = await LoadLatestSubmissionsByPeriod(tenantId, periodIds, ct);
            var errorCounts = await LoadValidationIssueCounts(submissions.Values.Select(v => v.Id), ct);

            var slaRecords = await _db.FilingSlaRecords
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId && moduleIds.Contains(s.ModuleId))
                .ToListAsync(ct);

            var rows = new List<ComplianceModuleRow>();
            foreach (var moduleId in moduleIds)
            {
                if (!moduleMap.TryGetValue(moduleId, out var module) || !groupedPeriods.TryGetValue(moduleId, out var periods))
                {
                    continue;
                }

                var periodItems = periods
                    .OrderBy(p => p.EffectiveDeadline)
                    .Select(period =>
                    {
                        submissions.TryGetValue(period.Id, out var submission);
                        var issues = submission is null
                            ? ValidationIssueCount.Empty
                            : errorCounts.GetValueOrDefault(submission.Id, ValidationIssueCount.Empty);

                        return new ModulePeriodStatusItem
                        {
                            PeriodId = period.Id,
                            Label = FormatPeriodLabel(period),
                            Status = MapSubmissionStatus(submission?.Status, period.Status),
                            RagClass = ComputeRagClass(period, submission),
                            CompletionPercent = ComputeCompletionPercent(submission?.Status, issues.ErrorCount, issues.WarningCount, period.Status),
                            ValidationErrorCount = issues.ErrorCount,
                            ValidationWarningCount = issues.WarningCount,
                            Deadline = period.EffectiveDeadline,
                            OnTime = slaRecords.Any(s => s.PeriodId == period.Id && s.OnTime == true),
                            SubmissionId = submission?.Id,
                            SubmissionStatus = submission?.Status
                        };
                    })
                    .ToList();

                var score = ComputeComplianceScore(periodItems);
                var currentItem = periodItems.LastOrDefault();
                var previousItem = periodItems.Count > 1 ? periodItems[^2] : null;

                rows.Add(new ComplianceModuleRow
                {
                    ModuleCode = module.ModuleCode,
                    ModuleName = module.ModuleName,
                    CurrentRag = MapRagName(currentItem?.RagClass),
                    PreviousRag = MapRagName(previousItem?.RagClass ?? currentItem?.RagClass),
                    Trend = ComputeTrend(score.OverallScore, score.PreviousScore),
                    Score = score.OverallScore,
                    TimelinessScore = score.TimelinessScore,
                    DataQualityScore = score.DataQualityScore,
                    ValidationPassRate = score.ValidationPassRate
                });
            }

            var orderedRows = rows.OrderBy(r => r.ModuleName).ToList();
            var overallScore = orderedRows.Count == 0
                ? 0
                : decimal.Round(orderedRows.Average(r => r.Score), 2);

            return new ComplianceSummaryData
            {
                OverallScore = overallScore,
                GeneratedAt = DateTime.UtcNow,
                Modules = orderedRows
            };
        });
    }

    public Task<TrendData> GetSubmissionTrend(Guid tenantId, string moduleCode, int periods = 6, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            throw new ArgumentException("Module code is required.", nameof(moduleCode));
        }

        var normalizedCode = moduleCode.Trim().ToUpperInvariant();
        return GetOrCreateCached($"dashboard:trend:submission:{tenantId}:{normalizedCode}:{periods}", async () =>
        {
            var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
            var hasAccess = entitlement.ActiveModules.Any(m =>
                string.Equals(m.ModuleCode, normalizedCode, StringComparison.OrdinalIgnoreCase));
            if (!hasAccess)
            {
                throw new InvalidOperationException($"Tenant {tenantId} is not entitled to module {normalizedCode}");
            }

            var module = await _db.Modules
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ModuleCode == normalizedCode, ct)
                ?? throw new InvalidOperationException($"Module {normalizedCode} not found");

            var periodRows = await _db.ReturnPeriods
                .AsNoTracking()
                .Where(rp => rp.TenantId == tenantId && rp.ModuleId == module.Id)
                .OrderByDescending(rp => rp.EffectiveDeadline)
                .Take(periods)
                .ToListAsync(ct);

            periodRows = periodRows.OrderBy(p => p.EffectiveDeadline).ToList();
            var periodIds = periodRows.Select(p => p.Id).ToList();

            var slaLookup = await LoadSlaLookup(tenantId, module.Id, periodIds, ct);

            var labels = new List<string>();
            var onTimeData = new List<decimal>();
            var lateData = new List<decimal>();

            foreach (var period in periodRows)
            {
                labels.Add(FormatPeriodLabel(period));
                if (slaLookup.TryGetValue(period.Id, out var sla) && sla.SubmittedDate.HasValue)
                {
                    onTimeData.Add(sla.OnTime == true ? 1 : 0);
                    lateData.Add(sla.OnTime == true ? 0 : 1);
                }
                else
                {
                    onTimeData.Add(0);
                    lateData.Add(0);
                }
            }

            return new TrendData
            {
                Title = "Submission Timeliness",
                Labels = labels,
                Datasets =
                {
                    new TrendDataset
                    {
                        Label = "On-Time",
                        Data = onTimeData,
                        BackgroundColor = "#0f766e",
                        BorderColor = "#0f766e"
                    },
                    new TrendDataset
                    {
                        Label = "Late",
                        Data = lateData,
                        BackgroundColor = "#b91c1c",
                        BorderColor = "#b91c1c"
                    }
                }
            };
        });
    }

    public Task<TrendData> GetValidationErrorTrend(Guid tenantId, string moduleCode, int periods = 6, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(moduleCode))
        {
            throw new ArgumentException("Module code is required.", nameof(moduleCode));
        }

        var normalizedCode = moduleCode.Trim().ToUpperInvariant();
        return GetOrCreateCached($"dashboard:trend:validation:{tenantId}:{normalizedCode}:{periods}", async () =>
        {
            var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
            var hasAccess = entitlement.ActiveModules.Any(m =>
                string.Equals(m.ModuleCode, normalizedCode, StringComparison.OrdinalIgnoreCase));
            if (!hasAccess)
            {
                throw new InvalidOperationException($"Tenant {tenantId} is not entitled to module {normalizedCode}");
            }

            var module = await _db.Modules
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.ModuleCode == normalizedCode, ct)
                ?? throw new InvalidOperationException($"Module {normalizedCode} not found");

            var periodRows = await _db.ReturnPeriods
                .AsNoTracking()
                .Where(rp => rp.TenantId == tenantId && rp.ModuleId == module.Id)
                .OrderByDescending(rp => rp.EffectiveDeadline)
                .Take(periods)
                .ToListAsync(ct);

            periodRows = periodRows.OrderBy(p => p.EffectiveDeadline).ToList();
            var periodIds = periodRows.Select(p => p.Id).ToList();

            var submissions = await LoadLatestSubmissionsByPeriod(tenantId, periodIds, ct);
            var issueCounts = await LoadValidationIssueCounts(submissions.Values.Select(v => v.Id), ct);

            var labels = new List<string>();
            var errorData = new List<decimal>();
            var warningData = new List<decimal>();

            foreach (var period in periodRows)
            {
                labels.Add(FormatPeriodLabel(period));
                if (submissions.TryGetValue(period.Id, out var submission)
                    && issueCounts.TryGetValue(submission.Id, out var issues))
                {
                    errorData.Add(issues.ErrorCount);
                    warningData.Add(issues.WarningCount);
                }
                else
                {
                    errorData.Add(0);
                    warningData.Add(0);
                }
            }

            return new TrendData
            {
                Title = "Validation Error Trend",
                Labels = labels,
                Datasets =
                {
                    new TrendDataset
                    {
                        Label = "Errors",
                        Data = errorData,
                        BackgroundColor = "#dc2626",
                        BorderColor = "#dc2626"
                    },
                    new TrendDataset
                    {
                        Label = "Warnings",
                        Data = warningData,
                        BackgroundColor = "#d97706",
                        BorderColor = "#d97706"
                    }
                }
            };
        });
    }

    public Task<AdminDashboardData> GetAdminDashboard(Guid tenantId, CancellationToken ct = default)
    {
        return GetOrCreateCached($"dashboard:admin:{tenantId}", async () =>
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1);

            var activeSubscription = await _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Where(s => s.TenantId == tenantId)
                .Where(s => s.Status == SubscriptionStatus.Active
                         || s.Status == SubscriptionStatus.Trial
                         || s.Status == SubscriptionStatus.PastDue
                         || s.Status == SubscriptionStatus.Suspended)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync(ct);

            var latestUsage = await _db.UsageRecords
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId)
                .OrderByDescending(u => u.RecordDate)
                .FirstOrDefaultAsync(ct);

            var activeUsersThisMonth = await _db.InstitutionUsers
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.IsActive && u.LastLoginAt >= monthStart)
                .CountAsync(ct);

            var successfulLogins = await _db.LoginAttempts
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId && a.Succeeded && a.AttemptedAt >= monthStart)
                .CountAsync(ct);

            var averageLoginsPerUser = activeUsersThisMonth > 0
                ? decimal.Round((decimal)successfulLogins / activeUsersThisMonth, 2)
                : 0m;

            var contributorRows = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.SubmittedAt >= monthStart && s.SubmittedByUserId != null)
                .GroupBy(s => s.SubmittedByUserId!.Value)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync(ct);

            var contributorNames = await _db.InstitutionUsers
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId && contributorRows.Select(c => c.UserId).Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

            var entitiesUsed = await _db.Institutions
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId && i.IsActive)
                .CountAsync(ct);

            var usersUsed = await _db.InstitutionUsers
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.IsActive)
                .CountAsync(ct);

            var modulesUsed = await _db.SubscriptionModules
                .AsNoTracking()
                .Where(sm => sm.Subscription != null
                          && sm.Subscription.TenantId == tenantId
                          && sm.IsActive)
                .CountAsync(ct);

            var outstandingBalance = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue)
                .SumAsync(i => (decimal?)i.TotalAmount, ct) ?? 0m;

            var nextInvoiceDate = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .Where(i => i.Status == InvoiceStatus.Issued || i.Status == InvoiceStatus.Overdue)
                .Where(i => i.DueDate != null)
                .OrderBy(i => i.DueDate)
                .Select(i => i.DueDate)
                .FirstOrDefaultAsync(ct);

            var notificationCounts = await _db.NotificationDeliveries
                .AsNoTracking()
                .Where(d => d.TenantId == tenantId)
                .GroupBy(d => d.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var qualityTrend = await BuildTenantDataQualityTrend(tenantId, 6, ct);

            var validationPassRate = qualityTrend.Datasets.FirstOrDefault(d => d.Label == "Validation Pass %")?.Data.DefaultIfEmpty(0).Average() ?? 0;
            var completenessScore = qualityTrend.Datasets.FirstOrDefault(d => d.Label == "Completeness %")?.Data.DefaultIfEmpty(0).Average() ?? 0;

            var usage = new SubscriptionUsageMetrics
            {
                UsersUsed = usersUsed,
                UsersLimit = activeSubscription?.Plan?.MaxUsersPerEntity ?? 0,
                UsersUsagePercent = Percent(usersUsed, activeSubscription?.Plan?.MaxUsersPerEntity ?? 0),
                EntitiesUsed = entitiesUsed,
                EntitiesLimit = activeSubscription?.Plan?.MaxEntities ?? 0,
                EntitiesUsagePercent = Percent(entitiesUsed, activeSubscription?.Plan?.MaxEntities ?? 0),
                ModulesUsed = modulesUsed,
                ModulesLimit = activeSubscription?.Plan?.MaxModules ?? 0,
                ModulesUsagePercent = Percent(modulesUsed, activeSubscription?.Plan?.MaxModules ?? 0),
                StorageUsedMb = latestUsage?.StorageUsedMb ?? 0,
                StorageLimitMb = activeSubscription?.Plan?.MaxStorageMb ?? 0,
                StorageUsagePercent = Percent(latestUsage?.StorageUsedMb ?? 0, activeSubscription?.Plan?.MaxStorageMb ?? 0),
                ApiCallsUsed = latestUsage?.ApiCallCount ?? 0,
                ApiCallsLimit = activeSubscription?.Plan?.MaxApiCallsPerMonth ?? 0,
                ApiUsagePercent = Percent(latestUsage?.ApiCallCount ?? 0, activeSubscription?.Plan?.MaxApiCallsPerMonth ?? 0)
            };

            return new AdminDashboardData
            {
                UserActivity = new UserActivityMetrics
                {
                    ActiveUsersThisMonth = activeUsersThisMonth,
                    AverageLoginsPerUser = averageLoginsPerUser,
                    TopContributors = contributorRows.Select(c => new UserContributionItem
                    {
                        UserId = c.UserId,
                        DisplayName = contributorNames.GetValueOrDefault(c.UserId, $"User {c.UserId}"),
                        SubmissionCount = c.Count
                    }).ToList()
                },
                Usage = usage,
                Billing = new BillingSummaryMetrics
                {
                    PlanName = activeSubscription?.Plan?.PlanName ?? "N/A",
                    PlanCode = activeSubscription?.Plan?.PlanCode ?? "N/A",
                    BillingFrequency = activeSubscription?.BillingFrequency.ToString() ?? "N/A",
                    NextInvoiceDate = nextInvoiceDate?.ToDateTime(TimeOnly.MinValue) ?? activeSubscription?.CurrentPeriodEnd ?? DateTime.UtcNow,
                    OutstandingBalance = decimal.Round(outstandingBalance, 2),
                    Currency = "NGN"
                },
                NotificationStats = new NotificationStats
                {
                    Sent = notificationCounts.Where(x => x.Status == DeliveryStatus.Sent).Sum(x => x.Count),
                    Delivered = notificationCounts.Where(x => x.Status == DeliveryStatus.Delivered).Sum(x => x.Count),
                    Failed = notificationCounts.Where(x => x.Status == DeliveryStatus.Failed || x.Status == DeliveryStatus.Bounced).Sum(x => x.Count),
                    Queued = notificationCounts.Where(x => x.Status == DeliveryStatus.Queued).Sum(x => x.Count)
                },
                DataQualityTrend = qualityTrend,
                ValidationPassRate = decimal.Round(validationPassRate, 2),
                CompletenessScore = decimal.Round(completenessScore, 2),
                GeneratedAt = DateTime.UtcNow
            };
        });
    }

    public Task<PlatformDashboardData> GetPlatformDashboard(CancellationToken ct = default)
    {
        return GetOrCreateCached("dashboard:platform", async () =>
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var lookback30 = now.AddDays(-30);

            var activeTenants = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Status == TenantStatus.Active)
                .Select(t => new { t.TenantId, t.TenantName, t.CreatedAt, t.DeactivatedAt })
                .ToListAsync(ct);

            var totalActiveTenants = activeTenants.Count;
            var newThisMonth = activeTenants.Count(t => t.CreatedAt >= monthStart);

            var churnedThisMonth = await _db.Tenants
                .AsNoTracking()
                .Where(t => (t.Status == TenantStatus.Deactivated || t.Status == TenantStatus.Archived)
                         && t.DeactivatedAt != null
                         && t.DeactivatedAt >= monthStart)
                .CountAsync(ct);

            var subscriptions = await _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.Plan)
                .Include(s => s.Modules)
                .Where(s => s.Status == SubscriptionStatus.Active
                         || s.Status == SubscriptionStatus.PastDue
                         || s.Status == SubscriptionStatus.Suspended)
                .ToListAsync(ct);

            var moduleMap = await _db.Modules
                .AsNoTracking()
                .ToDictionaryAsync(m => m.Id, ct);

            decimal ComputeNormalizedMonthly(Subscription s)
            {
                var plan = s.Plan;
                var baseAmount = plan is null
                    ? 0m
                    : s.BillingFrequency == BillingFrequency.Annual
                        ? plan.BasePriceAnnual / 12m
                        : plan.BasePriceMonthly;

                var moduleAmount = s.Modules
                    .Where(sm => sm.IsActive)
                    .Sum(sm => s.BillingFrequency == BillingFrequency.Annual
                        ? sm.PriceAnnual / 12m
                        : sm.PriceMonthly);

                return baseAmount + moduleAmount;
            }

            var mrr = subscriptions.Sum(ComputeNormalizedMonthly);

            var revenueByPlan = subscriptions
                .GroupBy(s => s.Plan?.PlanName ?? "Unknown")
                .Select(g => new RevenueBreakdownItem
                {
                    Label = g.Key,
                    Amount = decimal.Round(g.Sum(ComputeNormalizedMonthly), 2)
                })
                .OrderByDescending(r => r.Amount)
                .ToList();

            var revenueByModule = subscriptions
                .SelectMany(s => s.Modules.Where(sm => sm.IsActive).Select(sm => new
                {
                    sm.ModuleId,
                    Amount = s.BillingFrequency == BillingFrequency.Annual ? sm.PriceAnnual / 12m : sm.PriceMonthly
                }))
                .GroupBy(x => x.ModuleId)
                .Select(g => new RevenueBreakdownItem
                {
                    Label = moduleMap.GetValueOrDefault(g.Key)?.ModuleCode ?? $"Module {g.Key}",
                    Amount = decimal.Round(g.Sum(x => x.Amount), 2)
                })
                .OrderByDescending(r => r.Amount)
                .ToList();

            var adoptionRows = await _db.SubscriptionModules
                .AsNoTracking()
                .Where(sm => sm.IsActive)
                .Where(sm => sm.Subscription != null
                          && (sm.Subscription.Status == SubscriptionStatus.Active
                           || sm.Subscription.Status == SubscriptionStatus.PastDue
                           || sm.Subscription.Status == SubscriptionStatus.Suspended))
                .GroupBy(sm => sm.ModuleId)
                .Select(g => new { ModuleId = g.Key, TenantCount = g.Select(x => x.Subscription!.TenantId).Distinct().Count() })
                .ToListAsync(ct);

            var adoption = adoptionRows
                .Select(a => new ModuleAdoptionItem
                {
                    ModuleCode = moduleMap.GetValueOrDefault(a.ModuleId)?.ModuleCode ?? $"M-{a.ModuleId}",
                    ModuleName = moduleMap.GetValueOrDefault(a.ModuleId)?.ModuleName ?? "Unknown",
                    ActiveTenants = a.TenantCount,
                    AdoptionRate = totalActiveTenants > 0
                        ? decimal.Round((decimal)a.TenantCount * 100m / totalActiveTenants, 2)
                        : 0m
                })
                .OrderByDescending(a => a.ActiveTenants)
                .ThenBy(a => a.ModuleName)
                .ToList();

            var processingTimes = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.ProcessingDurationMs != null && s.SubmittedAt >= lookback30)
                .Select(s => s.ProcessingDurationMs!.Value)
                .OrderBy(x => x)
                .ToListAsync(ct);

            var totalProcessed = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.SubmittedAt >= lookback30)
                .CountAsync(ct);

            var failedProcessed = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.SubmittedAt >= lookback30)
                .Where(s => s.Status == SubmissionStatus.Rejected || s.Status == SubmissionStatus.ApprovalRejected)
                .CountAsync(ct);

            var activeInstitutionSessions = await _db.InstitutionUsers
                .AsNoTracking()
                .Where(u => u.LastLoginAt != null && u.LastLoginAt >= now.AddMinutes(-30))
                .CountAsync(ct);

            var activePortalSessions = await _db.PortalUsers
                .AsNoTracking()
                .Where(u => u.LastLoginAt != null && u.LastLoginAt >= now.AddMinutes(-30))
                .CountAsync(ct);

            var submissionsThisPeriod = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.SubmittedAt >= monthStart)
                .Where(s => s.Status != SubmissionStatus.Draft)
                .CountAsync(ct);

            var onTimeRecords = await _db.FilingSlaRecords
                .AsNoTracking()
                .Where(r => r.SubmittedDate != null && r.SubmittedDate >= monthStart)
                .Select(r => r.OnTime)
                .ToListAsync(ct);

            var validationErrorRows = await _db.ValidationErrors
                .AsNoTracking()
                .Join(_db.ValidationReports.AsNoTracking(),
                    err => err.ValidationReportId,
                    report => report.Id,
                    (err, report) => new { err, report })
                .Where(x => x.report.CreatedAt >= now.AddDays(-90))
                .GroupBy(x => new { x.err.RuleId, x.err.Field })
                .Select(g => new ValidationErrorAggregate
                {
                    RuleId = g.Key.RuleId,
                    Field = g.Key.Field,
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .ToListAsync(ct);

            var topTenantUsageRows = await _db.Submissions
                .AsNoTracking()
                .Where(s => s.SubmittedAt >= monthStart)
                .Where(s => s.Status != SubmissionStatus.Draft)
                .GroupBy(s => s.TenantId)
                .Select(g => new { TenantId = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(10)
                .ToListAsync(ct);

            var tenantNames = await _db.Tenants
                .AsNoTracking()
                .Where(t => topTenantUsageRows.Select(x => x.TenantId).Contains(t.TenantId))
                .ToDictionaryAsync(t => t.TenantId, t => t.TenantName, ct);

            return new PlatformDashboardData
            {
                TenantStats = new PlatformTenantStats
                {
                    TotalActiveTenants = totalActiveTenants,
                    NewThisMonth = newThisMonth,
                    ChurnedThisMonth = churnedThisMonth
                },
                Revenue = new RevenueMetrics
                {
                    Mrr = decimal.Round(mrr, 2),
                    Arr = decimal.Round(mrr * 12m, 2),
                    RevenueByPlan = revenueByPlan,
                    RevenueByModule = revenueByModule
                },
                ModuleAdoption = adoption,
                PlatformHealth = new PlatformHealthMetrics
                {
                    ApiLatencyP50Ms = Percentile(processingTimes, 50),
                    ApiLatencyP95Ms = Percentile(processingTimes, 95),
                    ApiLatencyP99Ms = Percentile(processingTimes, 99),
                    ErrorRatePercent = totalProcessed > 0
                        ? decimal.Round((decimal)failedProcessed * 100m / totalProcessed, 2)
                        : 0m,
                    ActiveSessions = activeInstitutionSessions + activePortalSessions
                },
                FilingAnalytics = new FilingAnalyticsMetrics
                {
                    TotalReturnsSubmittedThisPeriod = submissionsThisPeriod,
                    OnTimeRatePercent = onTimeRecords.Count > 0
                        ? decimal.Round(onTimeRecords.Count(x => x == true) * 100m / onTimeRecords.Count, 2)
                        : 0m,
                    TopValidationErrors = validationErrorRows
                },
                TopTenantsByUsage = topTenantUsageRows.Select(x => new TopTenantUsageItem
                {
                    TenantId = x.TenantId,
                    TenantName = tenantNames.GetValueOrDefault(x.TenantId, x.TenantId.ToString()),
                    ReturnsSubmitted = x.Count
                }).ToList(),
                GeneratedAt = DateTime.UtcNow
            };
        });
    }

    private async Task<TrendData> BuildTenantDataQualityTrend(Guid tenantId, int months, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var monthStarts = Enumerable.Range(0, months)
            .Select(offset => new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1 - offset)))
            .ToList();

        var labels = monthStarts.Select(m => m.ToString("MMM yy")).ToList();

        var submissions = await _db.Submissions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.SubmittedAt >= monthStarts.First())
            .Select(s => new { s.Id, s.SubmittedAt })
            .ToListAsync(ct);

        var submissionIds = submissions.Select(s => s.Id).ToList();
        var reportBySubmission = await _db.ValidationReports
            .AsNoTracking()
            .Where(r => submissionIds.Contains(r.SubmissionId))
            .Select(r => new { r.SubmissionId, r.Id })
            .ToListAsync(ct);

        var errorCounts = await _db.ValidationErrors
            .AsNoTracking()
            .Where(e => reportBySubmission.Select(r => r.Id).Contains(e.ValidationReportId))
            .GroupBy(e => e.ValidationReportId)
            .Select(g => new
            {
                ValidationReportId = g.Key,
                Errors = g.Count(x => x.Severity == ValidationSeverity.Error),
                Warnings = g.Count(x => x.Severity == ValidationSeverity.Warning)
            })
            .ToDictionaryAsync(x => x.ValidationReportId, x => x, ct);

        var reportLookup = reportBySubmission.ToDictionary(x => x.SubmissionId, x => x.Id);

        var validationPass = new List<decimal>();
        var completeness = new List<decimal>();

        foreach (var monthStart in monthStarts)
        {
            var monthEnd = monthStart.AddMonths(1);
            var monthSubs = submissions
                .Where(s => s.SubmittedAt >= monthStart && s.SubmittedAt < monthEnd)
                .ToList();

            if (monthSubs.Count == 0)
            {
                validationPass.Add(0);
                completeness.Add(0);
                continue;
            }

            var passedCount = 0;
            var completenessScores = new List<decimal>();
            foreach (var sub in monthSubs)
            {
                if (reportLookup.TryGetValue(sub.Id, out var reportId)
                    && errorCounts.TryGetValue(reportId, out var issues))
                {
                    if (issues.Errors == 0)
                    {
                        passedCount++;
                    }

                    completenessScores.Add(decimal.Max(0, 100 - issues.Errors * 12 - issues.Warnings * 2));
                }
                else
                {
                    completenessScores.Add(75);
                }
            }

            validationPass.Add(decimal.Round((decimal)passedCount * 100m / monthSubs.Count, 2));
            completeness.Add(decimal.Round(completenessScores.Average(), 2));
        }

        return new TrendData
        {
            Title = "Data Quality Trend",
            Labels = labels,
            Datasets =
            {
                new TrendDataset
                {
                    Label = "Validation Pass %",
                    Data = validationPass,
                    BackgroundColor = "#0f766e",
                    BorderColor = "#0f766e"
                },
                new TrendDataset
                {
                    Label = "Completeness %",
                    Data = completeness,
                    BackgroundColor = "#1d4ed8",
                    BorderColor = "#1d4ed8"
                }
            }
        };
    }

    private async Task<Dictionary<int, SubmissionRow>> LoadLatestSubmissionsByPeriod(Guid tenantId, IReadOnlyCollection<int> periodIds, CancellationToken ct)
    {
        if (periodIds.Count == 0)
        {
            return new Dictionary<int, SubmissionRow>();
        }

        var rows = await _db.Submissions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && periodIds.Contains(s.ReturnPeriodId))
            .Select(s => new SubmissionRow
            {
                Id = s.Id,
                ReturnPeriodId = s.ReturnPeriodId,
                Status = s.Status,
                SubmittedAt = s.SubmittedAt,
                TemplateVersionId = s.TemplateVersionId
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(s => s.ReturnPeriodId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.Id).First());
    }

    private async Task<Dictionary<int, ValidationIssueCount>> LoadValidationIssueCounts(IEnumerable<int> submissionIds, CancellationToken ct)
    {
        var submissionIdList = submissionIds.Distinct().ToList();
        if (submissionIdList.Count == 0)
        {
            return new Dictionary<int, ValidationIssueCount>();
        }

        var reportRows = await _db.ValidationReports
            .AsNoTracking()
            .Where(r => submissionIdList.Contains(r.SubmissionId))
            .Select(r => new { r.Id, r.SubmissionId })
            .ToListAsync(ct);

        if (reportRows.Count == 0)
        {
            return new Dictionary<int, ValidationIssueCount>();
        }

        var reportIds = reportRows.Select(r => r.Id).ToList();
        var issueRows = await _db.ValidationErrors
            .AsNoTracking()
            .Where(e => reportIds.Contains(e.ValidationReportId))
            .GroupBy(e => e.ValidationReportId)
            .Select(g => new
            {
                ValidationReportId = g.Key,
                ErrorCount = g.Count(x => x.Severity == ValidationSeverity.Error),
                WarningCount = g.Count(x => x.Severity == ValidationSeverity.Warning)
            })
            .ToListAsync(ct);

        var issuesByReport = issueRows.ToDictionary(
            r => r.ValidationReportId,
            r => new ValidationIssueCount { ErrorCount = r.ErrorCount, WarningCount = r.WarningCount });

        return reportRows.ToDictionary(
            r => r.SubmissionId,
            r => issuesByReport.GetValueOrDefault(r.Id, ValidationIssueCount.Empty));
    }

    private async Task<Dictionary<int, FilingSlaRecord>> LoadSlaLookup(Guid tenantId, int moduleId, IReadOnlyCollection<int> periodIds, CancellationToken ct)
    {
        if (periodIds.Count == 0)
        {
            return new Dictionary<int, FilingSlaRecord>();
        }

        return await _db.FilingSlaRecords
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ModuleId == moduleId && periodIds.Contains(s.PeriodId))
            .ToDictionaryAsync(s => s.PeriodId, ct);
    }

    private static string ComputeRagClass(ReturnPeriod period, SubmissionRow? submission)
    {
        var hasSubmitted = submission is { Status: SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings };
        var inReview = submission is { Status: SubmissionStatus.PendingApproval or SubmissionStatus.Validating or SubmissionStatus.Parsing };

        var color = FilingCalendarService.ComputeRagColor(
            DateTime.UtcNow.Date,
            period.EffectiveDeadline.Date,
            period.ReportingDate.Date,
            hasSubmitted,
            inReview,
            period.Status);

        return color switch
        {
            RagColor.Green => "rag-green",
            RagColor.Amber => "rag-amber",
            RagColor.Red => "rag-red",
            _ => "rag-green"
        };
    }

    private static decimal ComputeCompletionPercent(SubmissionStatus? status, int errors, int warnings, string periodStatus)
    {
        if (status is null)
        {
            return string.Equals(periodStatus, "Overdue", StringComparison.OrdinalIgnoreCase) ? 0 : 10;
        }

        if (status is SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings)
        {
            return 100;
        }

        if (status == SubmissionStatus.PendingApproval)
        {
            return decimal.Max(80, 100 - errors * 6 - warnings * 2);
        }

        if (status == SubmissionStatus.Draft)
        {
            return decimal.Max(20, 70 - errors * 8 - warnings * 2);
        }

        if (status == SubmissionStatus.Rejected || status == SubmissionStatus.ApprovalRejected)
        {
            return decimal.Max(15, 75 - errors * 10 - warnings * 2);
        }

        return decimal.Max(25, 75 - errors * 8 - warnings * 2);
    }

    private static string MapSubmissionStatus(SubmissionStatus? status, string periodStatus)
    {
        if (status is null)
        {
            return periodStatus;
        }

        return status.Value switch
        {
            SubmissionStatus.Draft => "Draft",
            SubmissionStatus.Parsing => "Parsing",
            SubmissionStatus.Validating => "Validating",
            SubmissionStatus.PendingApproval => "InReview",
            SubmissionStatus.Accepted => "Submitted",
            SubmissionStatus.AcceptedWithWarnings => "Submitted",
            SubmissionStatus.Rejected => "Rejected",
            SubmissionStatus.ApprovalRejected => "ApprovalRejected",
            _ => status.Value.ToString()
        };
    }

    private static string FormatPeriodLabel(ReturnPeriod period)
    {
        return period.Frequency switch
        {
            "Quarterly" => $"Q{period.Quarter} {period.Year}",
            "SemiAnnual" => period.Month <= 6 ? $"H1 {period.Year}" : $"H2 {period.Year}",
            "Annual" => $"FY {period.Year}",
            _ => new DateTime(period.Year, Math.Max(1, period.Month), 1).ToString("MMM yyyy")
        };
    }

    private static TrendData BuildValidationTrend(List<ModulePeriodStatusItem> items)
    {
        return new TrendData
        {
            Title = "Validation Error Trend",
            Labels = items.Select(i => i.Label).ToList(),
            Datasets =
            {
                new TrendDataset
                {
                    Label = "Errors",
                    Data = items.Select(i => (decimal)i.ValidationErrorCount).ToList(),
                    BackgroundColor = "#dc2626",
                    BorderColor = "#dc2626"
                },
                new TrendDataset
                {
                    Label = "Warnings",
                    Data = items.Select(i => (decimal)i.ValidationWarningCount).ToList(),
                    BackgroundColor = "#d97706",
                    BorderColor = "#d97706"
                }
            }
        };
    }

    private static TrendData BuildTimelinessTrend(List<ModulePeriodStatusItem> items)
    {
        return new TrendData
        {
            Title = "Submission Timeliness",
            Labels = items.Select(i => i.Label).ToList(),
            Datasets =
            {
                new TrendDataset
                {
                    Label = "On-Time",
                    Data = items.Select(i => i.OnTime ? 1m : 0m).ToList(),
                    BackgroundColor = "#0f766e",
                    BorderColor = "#0f766e"
                },
                new TrendDataset
                {
                    Label = "Late",
                    Data = items.Select(i => i.OnTime || i.SubmissionId == null ? 0m : 1m).ToList(),
                    BackgroundColor = "#b91c1c",
                    BorderColor = "#b91c1c"
                }
            }
        };
    }

    private static TrendData BuildDataQualityTrend(List<ModulePeriodStatusItem> items)
    {
        var validationScores = items
            .Select(i => decimal.Max(0, 100 - i.ValidationErrorCount * 12 - i.ValidationWarningCount * 2))
            .ToList();

        return new TrendData
        {
            Title = "Data Quality Trend",
            Labels = items.Select(i => i.Label).ToList(),
            Datasets =
            {
                new TrendDataset
                {
                    Label = "Completeness %",
                    Data = items.Select(i => i.CompletionPercent).ToList(),
                    BackgroundColor = "#1d4ed8",
                    BorderColor = "#1d4ed8"
                },
                new TrendDataset
                {
                    Label = "Validation Pass %",
                    Data = validationScores,
                    BackgroundColor = "#0f766e",
                    BorderColor = "#0f766e"
                }
            }
        };
    }

    private static TrendData BuildStatusBreakdown(List<ModulePeriodStatusItem> items)
    {
        var submitted = items.Count(i => i.SubmissionStatus is SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings);
        var overdue = items.Count(i => string.Equals(i.Status, "Overdue", StringComparison.OrdinalIgnoreCase));
        var pending = Math.Max(0, items.Count - submitted - overdue);

        return new TrendData
        {
            Title = "Module Filing Status",
            Labels = new List<string> { "Submitted", "Pending", "Overdue" },
            Datasets =
            {
                new TrendDataset
                {
                    Label = "Status",
                    Data = new List<decimal> { submitted, pending, overdue },
                    BackgroundColor = "#0f766e",
                    BorderColor = "#0f766e"
                }
            }
        };
    }

    private static ComplianceScore ComputeComplianceScore(List<ModulePeriodStatusItem> periodItems)
    {
        if (periodItems.Count == 0)
        {
            return new ComplianceScore(0, 0, 0, 0, 0);
        }

        var currentWindow = periodItems.TakeLast(4).ToList();
        var previousWindow = periodItems.Take(Math.Max(0, periodItems.Count - 1)).TakeLast(4).ToList();

        var timeliness = currentWindow.Count > 0
            ? decimal.Round(currentWindow.Count(x => x.OnTime) * 100m / currentWindow.Count, 2)
            : 0;

        var dataQuality = currentWindow.Count > 0
            ? decimal.Round(currentWindow.Average(x => x.CompletionPercent), 2)
            : 0;

        var validationPassRate = currentWindow.Count > 0
            ? decimal.Round(currentWindow.Average(x => decimal.Max(0, 100 - x.ValidationErrorCount * 12 - x.ValidationWarningCount * 2)), 2)
            : 0;

        var overall = decimal.Round((timeliness * 0.4m) + (dataQuality * 0.3m) + (validationPassRate * 0.3m), 2);

        var previousScore = previousWindow.Count > 0
            ? decimal.Round(
                (previousWindow.Count(x => x.OnTime) * 100m / previousWindow.Count) * 0.4m
                + (previousWindow.Average(x => x.CompletionPercent) * 0.3m)
                + (previousWindow.Average(x => decimal.Max(0, 100 - x.ValidationErrorCount * 12 - x.ValidationWarningCount * 2)) * 0.3m), 2)
            : overall;

        return new ComplianceScore(overall, timeliness, dataQuality, validationPassRate, previousScore);
    }

    private static string MapRagName(string? ragClass)
    {
        return ragClass switch
        {
            "rag-red" => "Red",
            "rag-amber" => "Amber",
            _ => "Green"
        };
    }

    private static string ComputeTrend(decimal currentScore, decimal previousScore)
    {
        if (currentScore >= previousScore + 2)
        {
            return "Up";
        }

        if (currentScore <= previousScore - 2)
        {
            return "Down";
        }

        return "Stable";
    }

    private static decimal Percent(decimal numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return decimal.Round(numerator * 100m / denominator, 2);
    }

    private static decimal Percent(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0;
        }

        return decimal.Round((decimal)numerator * 100m / denominator, 2);
    }

    private static decimal Percentile(IReadOnlyList<int> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var ordered = sortedValues.OrderBy(v => v).ToList();
        var rank = (percentile / 100d) * (ordered.Count - 1);
        var lowerIndex = (int)Math.Floor(rank);
        var upperIndex = (int)Math.Ceiling(rank);
        if (lowerIndex == upperIndex)
        {
            return ordered[lowerIndex];
        }

        var weight = (decimal)(rank - lowerIndex);
        return decimal.Round(ordered[lowerIndex] * (1 - weight) + ordered[upperIndex] * weight, 2);
    }

    private Task<T> GetOrCreateCached<T>(string key, Func<Task<T>> factory)
    {
        return _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            try
            {
                return await factory();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard query failed for cache key {CacheKey}", key);
                throw;
            }
        })!;
    }

    private sealed class SubmissionRow
    {
        public int Id { get; init; }
        public int ReturnPeriodId { get; init; }
        public SubmissionStatus Status { get; init; }
        public DateTime SubmittedAt { get; init; }
        public int? TemplateVersionId { get; init; }
    }

    private sealed class ValidationIssueCount
    {
        public static ValidationIssueCount Empty { get; } = new();

        public int ErrorCount { get; init; }
        public int WarningCount { get; init; }
    }

    private sealed record ComplianceScore(
        decimal OverallScore,
        decimal TimelinessScore,
        decimal DataQualityScore,
        decimal ValidationPassRate,
        decimal PreviousScore);
}
