using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using ClosedXML.Excel;
using FC.Engine.Application.Services;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Models;
using FC.Engine.Domain.Notifications;
using FC.Engine.Infrastructure.Metadata;
using FC.Engine.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Admin.Services;

public class PlatformAdminService
{
    private readonly MetadataDbContext _db;
    private readonly IDashboardService _dashboardService;
    private readonly ISubscriptionService _subscriptionService;
    private readonly ITemplateRepository _templateRepository;
    private readonly TemplateVersioningService _templateVersioningService;
    private readonly IAuditLogger _auditLogger;
    private readonly INotificationOrchestrator _notificationOrchestrator;
    private readonly IFeatureFlagService _featureFlagService;

    public PlatformAdminService(
        MetadataDbContext db,
        IDashboardService dashboardService,
        ISubscriptionService subscriptionService,
        ITemplateRepository templateRepository,
        TemplateVersioningService templateVersioningService,
        IAuditLogger auditLogger,
        INotificationOrchestrator notificationOrchestrator,
        IFeatureFlagService featureFlagService)
    {
        _db = db;
        _dashboardService = dashboardService;
        _subscriptionService = subscriptionService;
        _templateRepository = templateRepository;
        _templateVersioningService = templateVersioningService;
        _auditLogger = auditLogger;
        _notificationOrchestrator = notificationOrchestrator;
        _featureFlagService = featureFlagService;
    }

    public async Task<PlatformTenantListResult> GetTenantList(PlatformTenantListQuery query, CancellationToken ct = default)
    {
        var tenants = await _db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.TenantName)
            .ToListAsync(ct);

        var subscriptionRows = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Include(s => s.Modules)
            .ToListAsync(ct);

        var userCounts = await _db.InstitutionUsers
            .AsNoTracking()
            .Where(u => u.IsActive)
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var entityCounts = await _db.Institutions
            .AsNoTracking()
            .Where(i => i.IsActive)
            .GroupBy(i => i.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var licenceNames = await _db.TenantLicenceTypes
            .AsNoTracking()
            .Include(t => t.LicenceType)
            .Where(t => t.IsActive)
            .GroupBy(t => t.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Names = g.Select(x => x.LicenceType != null ? x.LicenceType.Name : "Unknown")
            })
            .ToListAsync(ct);

        var licenceLookup = licenceNames.ToDictionary(
            x => x.TenantId,
            x => string.Join(", ", x.Names.Distinct().OrderBy(n => n)));

        var activeTenantLicenceRows = await _db.TenantLicenceTypes
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => new { x.TenantId, x.LicenceTypeId })
            .ToListAsync(ct);

        var activeLicenceIds = activeTenantLicenceRows
            .Select(x => x.LicenceTypeId)
            .Distinct()
            .ToList();

        var licenceModuleRows = activeLicenceIds.Count == 0
            ? new List<LicenceModuleSummaryRow>()
            : await _db.LicenceModuleMatrix
                .AsNoTracking()
                .Where(x => activeLicenceIds.Contains(x.LicenceTypeId))
                .Select(x => new LicenceModuleSummaryRow(x.LicenceTypeId, x.ModuleId))
                .ToListAsync(ct);

        var planIds = subscriptionRows
            .Select(x => x.PlanId)
            .Distinct()
            .ToList();

        var planModuleRows = planIds.Count == 0
            ? new List<PlanModuleSummaryRow>()
            : await (
                from pricing in _db.PlanModulePricing.AsNoTracking()
                join module in _db.Modules.AsNoTracking() on pricing.ModuleId equals module.Id
                where planIds.Contains(pricing.PlanId) && module.IsActive
                select new PlanModuleSummaryRow(pricing.PlanId, pricing.ModuleId, pricing.IsIncludedInBase))
                .ToListAsync(ct);

        var licenceIdsByTenant = activeTenantLicenceRows
            .GroupBy(x => x.TenantId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.LicenceTypeId).ToHashSet());

        var moduleIdsByLicenceType = licenceModuleRows
            .GroupBy(x => x.LicenceTypeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ModuleId).ToHashSet());

        var planModuleRowsByPlan = planModuleRows
            .GroupBy(x => x.PlanId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var entitlementAuditRows = await _db.AuditLog
            .AsNoTracking()
            .Where(x => x.Action == "TenantModulesReconciled"
                     || x.Action == "TenantLicenceAssigned"
                     || x.Action == "TenantLicenceRemoved")
            .OrderByDescending(x => x.PerformedAt)
            .ToListAsync(ct);

        var latestEntitlementAuditByTenant = entitlementAuditRows
            .Select(x => new
            {
                TenantId = ResolveEntitlementAuditTenantId(x),
                x.Action,
                x.PerformedAt
            })
            .Where(x => x.TenantId.HasValue)
            .GroupBy(x => x.TenantId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var tenantRows = tenants.Select(t =>
        {
            var activeSubscription = subscriptionRows
                .Where(s => s.TenantId == t.TenantId)
                .Where(s => s.Status == SubscriptionStatus.Active
                         || s.Status == SubscriptionStatus.Trial
                         || s.Status == SubscriptionStatus.PastDue
                         || s.Status == SubscriptionStatus.Suspended)
                .OrderByDescending(s => s.UpdatedAt)
                .FirstOrDefault();

            var activeModules = activeSubscription?.Modules.Count(m => m.IsActive) ?? 0;
            var mrr = activeSubscription != null ? ComputeMrr(activeSubscription) : 0m;
            var pendingReconciliationModules = 0;

            if (activeSubscription is not null
                && licenceIdsByTenant.TryGetValue(t.TenantId, out var tenantLicenceIds)
                && planModuleRowsByPlan.TryGetValue(activeSubscription.PlanId, out var tenantPlanRows))
            {
                var eligibleModuleIds = tenantLicenceIds
                    .SelectMany(licenceTypeId => moduleIdsByLicenceType.GetValueOrDefault(licenceTypeId) ?? Enumerable.Empty<int>())
                    .ToHashSet();

                var includedBaseModuleIds = tenantPlanRows
                    .Where(x => x.IsIncludedInBase && eligibleModuleIds.Contains(x.ModuleId))
                    .Select(x => x.ModuleId)
                    .ToHashSet();

                var activeModuleIds = activeSubscription.Modules
                    .Where(x => x.IsActive)
                    .Select(x => x.ModuleId)
                    .ToHashSet();

                pendingReconciliationModules = includedBaseModuleIds.Count(moduleId => !activeModuleIds.Contains(moduleId));
            }

            latestEntitlementAuditByTenant.TryGetValue(t.TenantId, out var latestEntitlementAudit);

            return new PlatformTenantListItem
            {
                TenantId = t.TenantId,
                TenantName = t.TenantName,
                TenantSlug = t.TenantSlug,
                Status = t.Status,
                PlanName = activeSubscription?.Plan?.PlanName ?? "N/A",
                PlanCode = activeSubscription?.Plan?.PlanCode ?? "N/A",
                LicenceType = licenceLookup.GetValueOrDefault(t.TenantId, "N/A"),
                ActiveModules = activeModules,
                PendingReconciliationModules = pendingReconciliationModules,
                LastEntitlementAction = latestEntitlementAudit is null
                    ? null
                    : DescribeEntitlementAction(latestEntitlementAudit.Action),
                LastEntitlementActionAt = latestEntitlementAudit?.PerformedAt,
                Users = userCounts.GetValueOrDefault(t.TenantId),
                Entities = entityCounts.GetValueOrDefault(t.TenantId),
                Mrr = decimal.Round(mrr, 2),
                ContactEmail = t.ContactEmail,
                CreatedAt = t.CreatedAt
            };
        });

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            tenantRows = tenantRows.Where(x =>
                x.TenantName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || x.TenantSlug.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(x.ContactEmail)
                    && x.ContactEmail.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<TenantStatus>(query.Status, true, out var status))
        {
            tenantRows = tenantRows.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.PlanCode) && !string.Equals(query.PlanCode, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            tenantRows = tenantRows.Where(x => string.Equals(x.PlanCode, query.PlanCode, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.LicenceType) && !string.Equals(query.LicenceType, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            tenantRows = tenantRows.Where(x => x.LicenceType.Contains(query.LicenceType, StringComparison.OrdinalIgnoreCase));
        }

        if (query.MinModuleCount.HasValue)
        {
            tenantRows = tenantRows.Where(x => x.ActiveModules >= query.MinModuleCount.Value);
        }

        if (query.OnlyNeedsReconciliation)
        {
            tenantRows = tenantRows.Where(x => x.PendingReconciliationModules > 0);
        }

        tenantRows = (query.SortBy ?? string.Empty).ToLowerInvariant() switch
        {
            "status" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.Status).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.Status).ThenBy(x => x.TenantName),
            "plan" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.PlanName).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.PlanName).ThenBy(x => x.TenantName),
            "reconciliation" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.PendingReconciliationModules).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.PendingReconciliationModules).ThenBy(x => x.TenantName),
            "modules" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.ActiveModules).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.ActiveModules).ThenBy(x => x.TenantName),
            "users" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.Users).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.Users).ThenBy(x => x.TenantName),
            "entities" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.Entities).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.Entities).ThenBy(x => x.TenantName),
            "mrr" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.Mrr).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.Mrr).ThenBy(x => x.TenantName),
            "created" => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.TenantName)
                : tenantRows.OrderBy(x => x.CreatedAt).ThenBy(x => x.TenantName),
            _ => query.SortDescending
                ? tenantRows.OrderByDescending(x => x.TenantName)
                : tenantRows.OrderBy(x => x.TenantName)
        };

        var rows = tenantRows.ToList();

        return new PlatformTenantListResult
        {
            Tenants = rows,
            PlanOptions = rows.Select(x => x.PlanCode)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "N/A", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList(),
            LicenceOptions = rows.Select(x => x.LicenceType)
                .Where(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, "N/A", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList()
        };
    }

    public async Task ActivateTenant(Guid tenantId, string actor, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        if (tenant.Status == TenantStatus.PendingActivation)
        {
            tenant.Activate();
        }
        else if (tenant.Status == TenantStatus.Suspended)
        {
            tenant.Reactivate();
        }
        else if (tenant.Status != TenantStatus.Active)
        {
            throw new InvalidOperationException($"Tenant cannot be activated from status {tenant.Status}.");
        }

        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "Tenant",
            0,
            "PlatformTenantActivated",
            null,
            new
            {
                TenantId = tenantId,
                IsPlatformAdmin = true,
                ImpersonatedTenantId = (Guid?)null
            },
            actor,
            ct);
    }

    public async Task SuspendTenant(Guid tenantId, string actor, string reason = "Platform admin action", CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException($"Tenant {tenantId} not found.");

        if (tenant.Status != TenantStatus.Active)
        {
            throw new InvalidOperationException($"Only active tenants can be suspended. Current status: {tenant.Status}");
        }

        tenant.Suspend(reason);
        await _db.SaveChangesAsync(ct);

        await _auditLogger.Log(
            "Tenant",
            0,
            "PlatformTenantSuspended",
            null,
            new
            {
                TenantId = tenantId,
                Reason = reason,
                IsPlatformAdmin = true,
                ImpersonatedTenantId = (Guid?)null
            },
            actor,
            ct);
    }

    public async Task<byte[]> ExportTenantListExcel(PlatformTenantListQuery query, CancellationToken ct = default)
    {
        var data = await GetTenantList(query, ct);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Tenants");

        var headers = new[]
        {
            "Tenant Name", "Slug", "Status", "Plan", "Licence Type", "Active Modules",
            "Pending Reconciliation", "Last Entitlement Action", "Last Entitlement At", "Users", "Entities", "MRR", "Created Date", "Contact Email"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var row = 2;
        foreach (var tenant in data.Tenants)
        {
            ws.Cell(row, 1).Value = tenant.TenantName;
            ws.Cell(row, 2).Value = tenant.TenantSlug;
            ws.Cell(row, 3).Value = tenant.Status.ToString();
            ws.Cell(row, 4).Value = tenant.PlanName;
            ws.Cell(row, 5).Value = tenant.LicenceType;
            ws.Cell(row, 6).Value = tenant.ActiveModules;
            ws.Cell(row, 7).Value = tenant.PendingReconciliationModules;
            ws.Cell(row, 8).Value = tenant.LastEntitlementAction ?? string.Empty;
            ws.Cell(row, 9).Value = tenant.LastEntitlementActionAt;
            ws.Cell(row, 10).Value = tenant.Users;
            ws.Cell(row, 11).Value = tenant.Entities;
            ws.Cell(row, 12).Value = tenant.Mrr;
            ws.Cell(row, 13).Value = tenant.CreatedAt;
            ws.Cell(row, 14).Value = tenant.ContactEmail ?? string.Empty;
            row++;
        }

        ws.Column(9).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Column(12).Style.NumberFormat.Format = "#,##0.00";
        ws.Column(13).Style.DateFormat.Format = "yyyy-mm-dd";
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public async Task<PlatformTenantDetailData?> GetTenantDetail(Guid tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        if (tenant is null)
        {
            return null;
        }

        var subscriptionRows = await _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Plan)
            .Include(s => s.Modules)
            .ThenInclude(m => m.Module)
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var currentSubscription = subscriptionRows
            .FirstOrDefault(s => s.Status == SubscriptionStatus.Active
                              || s.Status == SubscriptionStatus.Trial
                              || s.Status == SubscriptionStatus.PastDue
                              || s.Status == SubscriptionStatus.Suspended)
            ?? subscriptionRows.FirstOrDefault();

        var activeTenantLicences = await _db.TenantLicenceTypes
            .AsNoTracking()
            .Include(x => x.LicenceType)
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .ToListAsync(ct);

        var activeLicenceIds = activeTenantLicences
            .Select(x => x.LicenceTypeId)
            .Distinct()
            .ToList();

        var licenceModuleRows = activeLicenceIds.Count == 0
            ? new List<LicenceModuleMatrix>()
            : await _db.LicenceModuleMatrix
                .AsNoTracking()
                .Include(x => x.LicenceType)
                .Include(x => x.Module)
                .Where(x => activeLicenceIds.Contains(x.LicenceTypeId))
                .ToListAsync(ct);

        var currentPlanPricingRows = currentSubscription is null
            ? new List<PlanModulePricing>()
            : await _db.PlanModulePricing
                .AsNoTracking()
                .Include(x => x.Module)
                .Where(x => x.PlanId == currentSubscription.PlanId)
                .ToListAsync(ct);

        var entitlementActivity = (await _db.AuditLog
                .AsNoTracking()
                .Where(x => x.Action == "TenantModulesReconciled"
                         || x.Action == "TenantLicenceAssigned"
                         || x.Action == "TenantLicenceRemoved")
                .OrderByDescending(x => x.PerformedAt)
                .Take(500)
                .ToListAsync(ct))
            .Where(x => ResolveEntitlementAuditTenantId(x) == tenantId)
            .Take(20)
            .Select(x => new TenantEntitlementActivityItem
            {
                Action = DescribeEntitlementAction(x.Action),
                ActionCode = x.Action,
                PerformedAt = x.PerformedAt,
                PerformedBy = x.PerformedBy,
                Summary = BuildEntitlementAuditSummary(x.Action)
            })
            .ToList();

        var dashboard = await _dashboardService.GetAdminDashboard(tenantId, ct);

        var institutionUsers = await _db.InstitutionUsers
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderByDescending(u => u.LastLoginAt)
            .ToListAsync(ct);

        var entities = await _db.Institutions
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderBy(i => i.InstitutionName)
            .ToListAsync(ct);
        var entityNameById = entities.ToDictionary(e => e.Id, e => e.InstitutionName);
        var branchCountByParentId = entities
            .Where(e => e.ParentInstitutionId.HasValue && e.EntityType == EntityType.Branch)
            .GroupBy(e => e.ParentInstitutionId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var periods = await _db.ReturnPeriods
            .AsNoTracking()
            .Include(rp => rp.Module)
            .Where(rp => rp.TenantId == tenantId)
            .OrderByDescending(rp => rp.Year)
            .ThenByDescending(rp => rp.Month)
            .ThenByDescending(rp => rp.Quarter)
            .Take(40)
            .ToListAsync(ct);

        var periodIds = periods.Select(p => p.Id).ToList();
        var submissions = await _db.Submissions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && periodIds.Contains(s.ReturnPeriodId))
            .GroupBy(s => s.ReturnPeriodId)
            .Select(g => g.OrderByDescending(x => x.SubmittedAt).FirstOrDefault()!)
            .ToListAsync(ct);

        var submissionLookup = submissions.ToDictionary(s => s.ReturnPeriodId, s => s);

        var audits = await _db.AuditLog
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId)
            .OrderByDescending(a => a.PerformedAt)
            .Take(200)
            .ToListAsync(ct);

        var supportTickets = await _db.PartnerSupportTickets
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var invoices = await _db.Invoices
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var payments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return new PlatformTenantDetailData
        {
            TenantId = tenant.TenantId,
            TenantName = tenant.TenantName,
            TenantSlug = tenant.TenantSlug,
            Status = tenant.Status,
            ContactEmail = tenant.ContactEmail,
            ContactPhone = tenant.ContactPhone,
            Address = tenant.Address,
            CreatedAt = tenant.CreatedAt,
            DefaultCurrency = tenant.DefaultCurrency,
            Timezone = tenant.Timezone,
            CurrentPlanName = currentSubscription?.Plan?.PlanName ?? "N/A",
            CurrentPlanCode = currentSubscription?.Plan?.PlanCode ?? "N/A",
            CurrentModuleEntitlements = BuildModuleEntitlements(
                currentSubscription,
                currentPlanPricingRows,
                licenceModuleRows),
            LastReconciledAt = entitlementActivity
                .FirstOrDefault(x => string.Equals(x.ActionCode, "TenantModulesReconciled", StringComparison.Ordinal))?
                .PerformedAt,
            EntitlementActivity = entitlementActivity,
            Usage = dashboard.Usage,
            Billing = dashboard.Billing,
            SubscriptionHistory = subscriptionRows.Select(s => new TenantSubscriptionHistoryItem
            {
                SubscriptionId = s.Id,
                PlanName = s.Plan?.PlanName ?? "N/A",
                PlanCode = s.Plan?.PlanCode ?? "N/A",
                Status = s.Status.ToString(),
                BillingFrequency = s.BillingFrequency.ToString(),
                CurrentPeriodStart = s.CurrentPeriodStart,
                CurrentPeriodEnd = s.CurrentPeriodEnd,
                CreatedAt = s.CreatedAt,
                Modules = s.Modules
                    .Where(m => m.IsActive)
                    .Select(m => m.Module?.ModuleCode ?? $"M-{m.ModuleId}")
                    .OrderBy(x => x)
                    .ToList()
            }).ToList(),
            Invoices = invoices.Select(i => new TenantInvoiceItem
            {
                InvoiceId = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status.ToString(),
                TotalAmount = i.TotalAmount,
                Currency = i.Currency,
                DueDate = i.DueDate,
                CreatedAt = i.CreatedAt
            }).ToList(),
            Payments = payments.Select(p => new TenantPaymentItem
            {
                PaymentId = p.Id,
                InvoiceId = p.InvoiceId,
                Amount = p.Amount,
                Currency = p.Currency,
                Status = p.Status.ToString(),
                PaymentMethod = p.PaymentMethod,
                PaidAt = p.PaidAt,
                CreatedAt = p.CreatedAt
            }).ToList(),
            Users = institutionUsers.Select(u => new TenantUserItem
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Role = u.Role.ToString(),
                LastLoginAt = u.LastLoginAt,
                MfaEnabled = false
            }).ToList(),
            Entities = entities.Select(e => new TenantEntityItem
            {
                InstitutionId = e.Id,
                InstitutionCode = e.InstitutionCode,
                InstitutionName = e.InstitutionName,
                IsActive = e.IsActive,
                ContactEmail = e.ContactEmail,
                EntityType = e.EntityType.ToString(),
                ParentInstitutionName = e.ParentInstitutionId.HasValue
                    ? entityNameById.GetValueOrDefault(e.ParentInstitutionId.Value)
                    : null,
                BranchCount = branchCountByParentId.GetValueOrDefault(e.Id)
            }).ToList(),
            FilingMatrix = periods.Select(p =>
            {
                submissionLookup.TryGetValue(p.Id, out var sub);
                return new TenantFilingMatrixItem
                {
                    PeriodId = p.Id,
                    ModuleName = p.Module?.ModuleName ?? p.Module?.ModuleCode ?? "N/A",
                    ModuleCode = p.Module?.ModuleCode ?? "N/A",
                    PeriodLabel = BuildPeriodLabel(p),
                    Deadline = p.EffectiveDeadline,
                    RagStatus = p.Status,
                    SubmissionStatus = sub?.Status.ToString() ?? "NotStarted"
                };
            }).ToList(),
            AuditEntries = audits.Select(a => new TenantAuditItem
            {
                Id = a.Id,
                Action = a.Action,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                PerformedBy = a.PerformedBy,
                PerformedAt = a.PerformedAt
            }).ToList(),
            SupportTickets = supportTickets.Select(t => new TenantSupportTicketItem
            {
                TicketId = t.Id,
                Title = t.Title,
                Priority = t.Priority.ToString(),
                Status = t.Status.ToString(),
                CreatedAt = t.CreatedAt,
                SlaDueAt = t.SlaDueAt
            }).ToList()
        };
    }

    public async Task<TemplateConsoleData> GetTemplateConsole(CancellationToken ct = default)
    {
        var templates = await _db.ReturnTemplates
            .AsNoTracking()
            .Include(t => t.Module)
            .Include(t => t.Versions)
                .ThenInclude(v => v.Fields)
            .Include(t => t.Versions)
                .ThenInclude(v => v.IntraSheetFormulas)
            .OrderBy(t => t.Module != null ? t.Module.ModuleName : "ZZZ")
            .ThenBy(t => t.ReturnCode)
            .ToListAsync(ct);

        var rows = templates.Select(t =>
        {
            var published = t.Versions
                .Where(v => v.Status == TemplateStatus.Published)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();
            var draft = t.Versions
                .Where(v => v.Status == TemplateStatus.Draft || v.Status == TemplateStatus.Review)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();

            return new TemplateConsoleItem
            {
                TemplateId = t.Id,
                ReturnCode = t.ReturnCode,
                TemplateName = t.Name,
                ModuleCode = t.Module?.ModuleCode ?? "N/A",
                ModuleName = t.Module?.ModuleName ?? "Unassigned",
                CurrentVersionId = published?.Id,
                CurrentVersionNumber = published?.VersionNumber,
                DraftVersionId = draft?.Id,
                DraftVersionNumber = draft?.VersionNumber,
                DraftStatus = draft?.Status.ToString(),
                FieldCount = (draft ?? published)?.Fields.Count ?? 0,
                FormulaCount = (draft ?? published)?.IntraSheetFormulas.Count ?? 0
            };
        }).ToList();

        return new TemplateConsoleData
        {
            Items = rows
        };
    }

    public async Task PublishDraftVersion(int templateId, string approvedBy, CancellationToken ct = default)
    {
        var template = await _templateRepository.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found.");

        var draft = template.Versions
            .Where(v => v.Status == TemplateStatus.Draft || v.Status == TemplateStatus.Review)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No draft/review version available to publish.");

        if (draft.Status == TemplateStatus.Draft)
        {
            await _templateVersioningService.SubmitForReview(template.Id, draft.Id, approvedBy, ct);
        }

        await _templateVersioningService.Publish(template.Id, draft.Id, approvedBy, ct);

        await _auditLogger.Log(
            "TemplateVersion",
            draft.Id,
            "Published",
            null,
            new
            {
                TemplateId = template.Id,
                draft.VersionNumber,
                IsPlatformAdmin = true,
                ImpersonatedTenantId = (Guid?)null
            },
            approvedBy,
            ct);
    }

    public async Task DeprecateVersion(int templateId, int versionId, string actor, CancellationToken ct = default)
    {
        var template = await _templateRepository.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found.");

        var version = template.Versions.FirstOrDefault(v => v.Id == versionId)
            ?? throw new InvalidOperationException($"Version {versionId} not found.");

        if (version.Status == TemplateStatus.Deprecated)
        {
            return;
        }

        version.Deprecate();
        await _templateRepository.Update(template, ct);

        await _auditLogger.Log(
            "TemplateVersion",
            version.Id,
            "Deprecated",
            null,
            new
            {
                TemplateId = template.Id,
                version.VersionNumber,
                IsPlatformAdmin = true
            },
            actor,
            ct);
    }

    public async Task<TemplateVersionDiffData?> GetTemplateDiff(
        int templateId,
        int? fromVersionId,
        int? toVersionId,
        CancellationToken ct = default)
    {
        var template = await _templateRepository.GetById(templateId, ct);
        if (template is null)
        {
            return null;
        }

        var ordered = template.Versions.OrderBy(v => v.VersionNumber).ToList();
        if (ordered.Count < 2)
        {
            return null;
        }

        var toVersion = toVersionId.HasValue
            ? ordered.FirstOrDefault(v => v.Id == toVersionId.Value)
            : ordered.LastOrDefault();

        if (toVersion is null)
        {
            return null;
        }

        var fromVersion = fromVersionId.HasValue
            ? ordered.FirstOrDefault(v => v.Id == fromVersionId.Value)
            : ordered.LastOrDefault(v => v.VersionNumber < toVersion.VersionNumber);

        if (fromVersion is null)
        {
            return null;
        }

        var oldFields = fromVersion.Fields.ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase);
        var newFields = toVersion.Fields.ToDictionary(f => f.FieldName, StringComparer.OrdinalIgnoreCase);

        var added = newFields.Keys.Except(oldFields.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removed = oldFields.Keys.Except(newFields.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var changed = newFields.Values
            .Where(n => oldFields.TryGetValue(n.FieldName, out var old) && !FieldEquivalent(old, n))
            .Select(n => n.FieldName)
            .OrderBy(x => x)
            .ToList();

        return new TemplateVersionDiffData
        {
            TemplateId = template.Id,
            ReturnCode = template.ReturnCode,
            TemplateName = template.Name,
            FromVersionId = fromVersion.Id,
            FromVersionNumber = fromVersion.VersionNumber,
            ToVersionId = toVersion.Id,
            ToVersionNumber = toVersion.VersionNumber,
            AddedFields = added,
            RemovedFields = removed,
            ChangedFields = changed,
            FormulaCountFrom = fromVersion.IntraSheetFormulas.Count,
            FormulaCountTo = toVersion.IntraSheetFormulas.Count
        };
    }

    public async Task<PlatformBillingOpsData> GetBillingOps(CancellationToken ct = default)
    {
        // Compute all billing metrics directly from _db to avoid shared-DbContext
        // threading issues that arise when delegating to DashboardService (which also
        // uses the same scoped MetadataDbContext via IMemoryCache.GetOrCreateAsync).

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var rollingStart = monthStart.AddMonths(-2);

        // ── Subscriptions for MRR / ARR / revenue breakdown ──────────────────────
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

        decimal NormalizedMonthly(Subscription s)
        {
            var base_ = s.Plan is null ? 0m
                : s.BillingFrequency == BillingFrequency.Annual
                    ? s.Plan.BasePriceAnnual / 12m
                    : s.Plan.BasePriceMonthly;
            var mods = s.Modules
                .Where(sm => sm.IsActive)
                .Sum(sm => s.BillingFrequency == BillingFrequency.Annual
                    ? sm.PriceAnnual / 12m
                    : sm.PriceMonthly);
            return base_ + mods;
        }

        var mrr = subscriptions.Sum(NormalizedMonthly);

        var revenueByPlan = subscriptions
            .GroupBy(s => s.Plan?.PlanName ?? "Unknown")
            .Select(g => new RevenueBreakdownItem
            {
                Label = g.Key,
                Amount = decimal.Round(g.Sum(NormalizedMonthly), 2)
            })
            .OrderByDescending(r => r.Amount)
            .ToList();

        var revenueByModule = subscriptions
            .SelectMany(s => s.Modules.Where(sm => sm.IsActive)
                .Select(sm => new
                {
                    sm.ModuleId,
                    Amount = s.BillingFrequency == BillingFrequency.Annual
                        ? sm.PriceAnnual / 12m
                        : sm.PriceMonthly
                }))
            .GroupBy(x => x.ModuleId)
            .Select(g => new RevenueBreakdownItem
            {
                Label = moduleMap.GetValueOrDefault(g.Key)?.ModuleCode ?? $"Module {g.Key}",
                Amount = decimal.Round(g.Sum(x => x.Amount), 2)
            })
            .OrderByDescending(r => r.Amount)
            .ToList();

        // ── Tenant counts for ARPU / churn ────────────────────────────────────────
        var activeTenantCount = Math.Max(1,
            await _db.Tenants.AsNoTracking()
                .CountAsync(t => t.Status == TenantStatus.Active, ct));

        var churnNumerator = await _db.Tenants
            .AsNoTracking()
            .CountAsync(t => (t.Status == TenantStatus.Deactivated || t.Status == TenantStatus.Archived)
                             && t.DeactivatedAt != null
                             && t.DeactivatedAt >= monthStart, ct);

        var churnRolling = await _db.Tenants
            .AsNoTracking()
            .CountAsync(t => (t.Status == TenantStatus.Deactivated || t.Status == TenantStatus.Archived)
                             && t.DeactivatedAt != null
                             && t.DeactivatedAt >= rollingStart, ct);

        // ── Invoices ──────────────────────────────────────────────────────────────
        var invoices = await _db.Invoices
            .AsNoTracking()
            .OrderByDescending(i => i.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var tenants = await _db.Tenants
            .AsNoTracking()
            .ToDictionaryAsync(t => t.TenantId, t => t.TenantName, ct);

        return new PlatformBillingOpsData
        {
            Mrr = mrr,
            Arr = mrr * 12m,
            Arpu = decimal.Round(mrr / activeTenantCount, 2),
            ChurnRatePercent = decimal.Round(churnNumerator * 100m / activeTenantCount, 2),
            ChurnRateTrailing3MonthsPercent = decimal.Round(churnRolling * 100m / (activeTenantCount * 3m), 2),
            RevenueByPlan = revenueByPlan,
            RevenueByModule = revenueByModule,
            Invoices = invoices.Select(i => new PlatformBillingInvoiceRow
            {
                InvoiceId = i.Id,
                TenantId = i.TenantId,
                TenantName = tenants.GetValueOrDefault(i.TenantId, i.TenantId.ToString()),
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status,
                TotalAmount = i.TotalAmount,
                DueDate = i.DueDate,
                CreatedAt = i.CreatedAt
            }).ToList()
        };
    }

    public async Task RecordManualPayment(int invoiceId, string actor, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");

        await _subscriptionService.RecordPayment(invoiceId, new RecordPaymentRequest
        {
            Amount = invoice.TotalAmount,
            Currency = invoice.Currency,
            PaymentMethod = "Manual",
            PaymentReference = $"PLATFORM-{DateTime.UtcNow:yyyyMMddHHmmss}",
            ProviderName = "PlatformAdmin",
            IsSuccessful = true
        }, ct);

        await _auditLogger.Log(
            "Invoice",
            invoiceId,
            "ManualPaymentRecorded",
            null,
            new
            {
                IsPlatformAdmin = true,
                ImpersonatedTenantId = (Guid?)null
            },
            actor,
            ct);
    }

    public async Task SendDunningReminder(int invoiceId, string actor, CancellationToken ct = default)
    {
        var invoice = await _db.Invoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new InvalidOperationException($"Invoice {invoiceId} not found.");

        if (invoice.Status != InvoiceStatus.Overdue && invoice.Status != InvoiceStatus.Issued)
        {
            throw new InvalidOperationException("Dunning reminders can only be sent for issued/overdue invoices.");
        }

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = invoice.TenantId,
            EventType = NotificationEvents.PaymentOverdue,
            Title = $"Invoice {invoice.InvoiceNumber} is overdue",
            Message = $"Outstanding amount: {invoice.TotalAmount:N2} {invoice.Currency}. Please settle immediately.",
            Priority = NotificationPriority.Critical,
            IsMandatory = true,
            RecipientRoles = new List<string> { "Admin" },
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InvoiceNumber"] = invoice.InvoiceNumber,
                ["Amount"] = invoice.TotalAmount.ToString("N2"),
                ["DueDate"] = invoice.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty
            }
        }, ct);

        await _auditLogger.Log(
            "Invoice",
            invoiceId,
            "DunningReminderSent",
            null,
            new
            {
                IsPlatformAdmin = true,
                ImpersonatedTenantId = (Guid?)null
            },
            actor,
            ct);
    }

    public async Task<PlatformHealthData> GetPlatformHealth(CancellationToken ct = default)
    {
        var dashboard = await _dashboardService.GetPlatformDashboard(ct);

        var queuedNotifications = await _db.NotificationDeliveries
            .AsNoTracking()
            .CountAsync(d => d.Status == DeliveryStatus.Queued, ct);

        var queuedExports = await _db.ExportRequests
            .AsNoTracking()
            .CountAsync(e => e.Status == ExportRequestStatus.Queued, ct);

        var queuedImports = await _db.ImportJobs
            .AsNoTracking()
            .CountAsync(i => i.Status != ImportJobStatus.Committed && i.Status != ImportJobStatus.Failed, ct);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _db.Tenants.AsNoTracking().Select(t => t.TenantId).FirstOrDefaultAsync(ct);
        sw.Stop();

        var serviceProbes = await CheckServiceHealth(sw.ElapsedMilliseconds, queuedNotifications + queuedExports, ct);

        return new PlatformHealthData
        {
            ApiLatencyP50Ms = dashboard.PlatformHealth.ApiLatencyP50Ms,
            ApiLatencyP95Ms = dashboard.PlatformHealth.ApiLatencyP95Ms,
            ApiLatencyP99Ms = dashboard.PlatformHealth.ApiLatencyP99Ms,
            ErrorRatePercent = dashboard.PlatformHealth.ErrorRatePercent,
            ActiveSessions = dashboard.PlatformHealth.ActiveSessions,
            QueueDepth = queuedNotifications + queuedExports + queuedImports,
            RabbitMqPending = queuedNotifications + queuedExports,
            DatabaseQueryDurationMs = sw.ElapsedMilliseconds,
            DatabasePoolUtilisationPercent = 0m,
            RedisCacheHitRatioPercent = 0m,
            RedisMemoryUsageMb = 0m,
            // Operational summary (stub values)
            SystemUptimeSince = DateTime.UtcNow.Date.AddDays(-12).AddHours(9).AddMinutes(17),
            AvailabilityPercent = 99.94m,
            TotalRequestsToday = 84_321,
            ErrorsToday = 17,
            QueuedNotifications = queuedNotifications,
            QueuedExports = queuedExports,
            QueuedImports = queuedImports,
            PeakHourUtc = 14,
            ServiceProbes = serviceProbes,
        };
    }

    private static Task<List<ServiceProbe>> CheckServiceHealth(long sqlDbMs, int rabbitMqPending, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // API — lightweight stub (HttpClient not injected; real impl would GET /api/health)
        var apiMs = (long)Random.Shared.Next(12, 55);
        var apiStatus = apiMs >= 2000 ? ServiceStatus.Down : apiMs >= 500 ? ServiceStatus.Degraded : ServiceStatus.Healthy;
        ProbeHistoryStore.Record("API", new ProbeResult { Status = apiStatus, ResponseMs = apiMs, CheckedAt = now });

        // SQL Server — re-use Stopwatch result already captured
        var sqlStatus = sqlDbMs >= 2000 ? ServiceStatus.Down : sqlDbMs >= 500 ? ServiceStatus.Degraded : ServiceStatus.Healthy;
        ProbeHistoryStore.Record("SQL Server", new ProbeResult { Status = sqlStatus, ResponseMs = sqlDbMs, CheckedAt = now });

        // Redis — PING stub (IConnectionMultiplexer not injected)
        var redisMs = (long)Random.Shared.Next(1, 6);
        ProbeHistoryStore.Record("Redis", new ProbeResult { Status = ServiceStatus.Healthy, ResponseMs = redisMs, CheckedAt = now });

        // RabbitMQ — infer health from pending backlog depth
        var rabbitMs = (long)Random.Shared.Next(4, 18);
        var rabbitStatus = rabbitMqPending > 500 ? ServiceStatus.Down : rabbitMqPending > 100 ? ServiceStatus.Degraded : ServiceStatus.Healthy;
        ProbeHistoryStore.Record("RabbitMQ", new ProbeResult { Status = rabbitStatus, ResponseMs = rabbitMs, CheckedAt = now });

        // Background Jobs — lightweight heartbeat stub
        var bgMs = (long)Random.Shared.Next(2, 12);
        ProbeHistoryStore.Record("Background Jobs", new ProbeResult { Status = ServiceStatus.Healthy, ResponseMs = bgMs, CheckedAt = now });

        return Task.FromResult(new List<ServiceProbe>
        {
            new() { Name = "API",            Status = apiStatus,            ResponseMs = apiMs,   UptimePercent = 99.98m, LastChecked = now, History = ProbeHistoryStore.Get("API") },
            new() { Name = "SQL Server",     Status = sqlStatus,            ResponseMs = sqlDbMs, UptimePercent = 99.97m, LastChecked = now, History = ProbeHistoryStore.Get("SQL Server") },
            new() { Name = "Redis",          Status = ServiceStatus.Healthy, ResponseMs = redisMs, UptimePercent = 100.00m, LastChecked = now, History = ProbeHistoryStore.Get("Redis") },
            new() { Name = "RabbitMQ",       Status = rabbitStatus,         ResponseMs = rabbitMs, UptimePercent = 99.95m, LastChecked = now, History = ProbeHistoryStore.Get("RabbitMQ") },
            new() { Name = "Background Jobs",Status = ServiceStatus.Healthy, ResponseMs = bgMs,   UptimePercent = 99.99m, LastChecked = now, History = ProbeHistoryStore.Get("Background Jobs") },
        });
    }

    public async Task<ModuleAnalyticsData> GetModuleAnalytics(CancellationToken ct = default)
    {
        var modules = await _db.Modules
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.ModuleCode)
            .ToListAsync(ct);

        var moduleTemplates = await _db.ReturnTemplates
            .AsNoTracking()
            .Where(t => t.ModuleId != null)
            .Select(t => new { t.ModuleId, t.ReturnCode })
            .ToListAsync(ct);

        var templateMap = moduleTemplates
            .GroupBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ModuleId!.Value, StringComparer.OrdinalIgnoreCase);

        var activeModuleTenants = await _db.SubscriptionModules
            .AsNoTracking()
            .Where(sm => sm.IsActive)
            .Where(sm => sm.Subscription != null
                      && (sm.Subscription.Status == SubscriptionStatus.Active
                       || sm.Subscription.Status == SubscriptionStatus.Trial
                       || sm.Subscription.Status == SubscriptionStatus.PastDue
                       || sm.Subscription.Status == SubscriptionStatus.Suspended))
            .GroupBy(sm => sm.ModuleId)
            .Select(g => new
            {
                ModuleId = g.Key,
                Tenants = g.Select(x => x.Subscription!.TenantId).Distinct()
            })
            .ToListAsync(ct);

        var tenantLookup = activeModuleTenants.ToDictionary(
            x => x.ModuleId,
            x => x.Tenants.ToHashSet());

        var submissions = await _db.Submissions
            .AsNoTracking()
            .Where(s => s.Status != SubmissionStatus.Draft)
            .Select(s => new { s.Id, s.TenantId, s.ReturnCode })
            .ToListAsync(ct);

        var submissionByModule = submissions
            .Where(s => templateMap.ContainsKey(s.ReturnCode))
            .GroupBy(s => templateMap[s.ReturnCode])
            .ToDictionary(g => g.Key, g => g.ToList());

        var reportsBySubmission = await _db.ValidationReports
            .AsNoTracking()
            .Select(r => new { r.SubmissionId, r.Id })
            .ToListAsync(ct);

        var reportLookup = reportsBySubmission.ToDictionary(x => x.SubmissionId, x => x.Id);

        var validationErrors = await _db.ValidationErrors
            .AsNoTracking()
            .ToListAsync(ct);

        var errorsByReportId = validationErrors
            .GroupBy(e => e.ValidationReportId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = new List<ModuleAnalyticsItem>();
        foreach (var module in modules)
        {
            var tenants = tenantLookup.TryGetValue(module.Id, out var t) ? t : new HashSet<Guid>();
            var moduleSubs = submissionByModule.TryGetValue(module.Id, out var ms) ? (IList<dynamic>)ms : new List<dynamic>();

            var submissionCount = moduleSubs.Count;
            var uniqueTenants = tenants.Count;

            var activeUsers = await _db.InstitutionUsers
                .AsNoTracking()
                .Where(u => u.IsActive && tenants.Contains(u.TenantId))
                .CountAsync(ct);

            var moduleSubmissionIds = moduleSubs.Select(s => (int)s.Id).ToList();
            var moduleReportIds = moduleSubmissionIds
                .Where(reportLookup.ContainsKey)
                .Select(id => reportLookup[id])
                .ToList();

            var moduleErrorCount = moduleReportIds.Sum(id => errorsByReportId.GetValueOrDefault(id)?.Count ?? 0);
            var validationErrorRate = submissionCount > 0
                ? decimal.Round(moduleErrorCount * 100m / submissionCount, 2)
                : 0m;

            var topErrors = moduleReportIds
                .SelectMany(id => errorsByReportId.GetValueOrDefault(id) ?? new List<ValidationError>())
                .GroupBy(e => $"{e.RuleId}:{e.Field}")
                .Select(g =>
                {
                    var first = g.First();
                    return new ModuleValidationHotspot
                    {
                        RuleId = first.RuleId,
                        Field = first.Field,
                        Count = g.Count()
                    };
                })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            items.Add(new ModuleAnalyticsItem
            {
                ModuleId = module.Id,
                ModuleCode = module.ModuleCode,
                ModuleName = module.ModuleName,
                TenantCount = uniqueTenants,
                ActiveUsers = activeUsers,
                ReturnsSubmitted = submissionCount,
                ValidationErrorRate = validationErrorRate,
                Heat = ResolveHeat(validationErrorRate),
                Hotspots = topErrors
            });
        }

        return new ModuleAnalyticsData
        {
            Items = items
        };
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetFeatureFlags(CancellationToken ct = default)
        => await _featureFlagService.GetAll(ct);

    public async Task<FeatureFlag> UpsertFeatureFlag(UpdateFeatureFlagRequest request, string actor, CancellationToken ct = default)
    {
        var saved = await _featureFlagService.Upsert(
            request.FlagCode,
            request.Description,
            request.IsEnabled,
            request.RolloutPercent,
            request.AllowedTenants,
            request.AllowedPlans,
            ct);

        await _auditLogger.Log(
            "FeatureFlag",
            saved.Id,
            "Updated",
            null,
            new
            {
                saved.FlagCode,
                saved.IsEnabled,
                saved.RolloutPercent,
                saved.AllowedTenants,
                saved.AllowedPlans,
                IsPlatformAdmin = true
            },
            actor,
            ct);

        return saved;
    }

    private static decimal ComputeMrr(Subscription s)
    {
        var baseAmount = s.Plan is null
            ? 0m
            : s.BillingFrequency == BillingFrequency.Annual
                ? s.Plan.BasePriceAnnual / 12m
                : s.Plan.BasePriceMonthly;

        var moduleAmount = s.Modules
            .Where(m => m.IsActive)
            .Sum(m => s.BillingFrequency == BillingFrequency.Annual ? m.PriceAnnual / 12m : m.PriceMonthly);

        return baseAmount + moduleAmount;
    }

    private static string BuildPeriodLabel(ReturnPeriod period)
    {
        return period.Frequency switch
        {
            "Monthly" => $"{period.Year}-{period.Month:00}",
            "Quarterly" => $"{period.Year}-Q{period.Quarter}",
            "Annual" => $"{period.Year}",
            _ => $"{period.Year}-{period.Month:00}"
        };
    }

    private static List<TenantModuleEntitlementItem> BuildModuleEntitlements(
        Subscription? currentSubscription,
        IReadOnlyList<PlanModulePricing> planPricingRows,
        IReadOnlyList<LicenceModuleMatrix> licenceModuleRows)
    {
        if (currentSubscription is null)
        {
            return new List<TenantModuleEntitlementItem>();
        }

        var subscriptionModules = currentSubscription.Modules
            .OrderBy(x => x.Module?.DisplayOrder ?? int.MaxValue)
            .ThenBy(x => x.Module?.ModuleCode)
            .ToList();

        var moduleIds = subscriptionModules.Select(x => x.ModuleId)
            .Concat(planPricingRows.Select(x => x.ModuleId))
            .Concat(licenceModuleRows.Select(x => x.ModuleId))
            .Distinct()
            .ToList();

        var subscriptionModuleById = subscriptionModules
            .GroupBy(x => x.ModuleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.ActivatedAt).First());

        var planPricingById = planPricingRows
            .GroupBy(x => x.ModuleId)
            .ToDictionary(g => g.Key, g => g.First());

        var coverageByModuleId = licenceModuleRows
            .GroupBy(x => x.ModuleId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(x => x.LicenceType?.DisplayOrder ?? int.MaxValue)
                    .ThenBy(x => x.LicenceType?.Code)
                    .ToList());

        return moduleIds
            .Select(moduleId =>
            {
                subscriptionModuleById.TryGetValue(moduleId, out var subscriptionModule);
                planPricingById.TryGetValue(moduleId, out var pricing);
                coverageByModuleId.TryGetValue(moduleId, out var coverageRows);

                var module = subscriptionModule?.Module
                             ?? pricing?.Module
                             ?? coverageRows?.FirstOrDefault()?.Module;

                var isActive = subscriptionModule?.IsActive == true;
                var isIncludedInBase = pricing?.IsIncludedInBase == true;
                var isLicenceEligible = coverageRows is { Count: > 0 };
                var pricingMode = pricing is null
                    ? "Unpriced"
                    : pricing.IsIncludedInBase
                        ? "Included"
                        : "Add-on";
                var status = isActive
                    ? "Active"
                    : isLicenceEligible && isIncludedInBase
                        ? "Pending Reconciliation"
                        : subscriptionModule is not null
                            ? "Inactive"
                            : isLicenceEligible
                                ? "Eligible"
                                : "Unavailable";
                var nextAction = isActive && !isLicenceEligible
                    ? "Review licence coverage or deactivate the module."
                    : !isActive && isLicenceEligible && isIncludedInBase
                        ? "Run entitlement reconciliation to activate this included module."
                        : !isActive && isLicenceEligible && pricing is not null && !pricing.IsIncludedInBase
                            ? "Activate as a priced add-on if the tenant needs access."
                            : pricing is null && isLicenceEligible
                                ? "Configure plan pricing for this licence-eligible module."
                                : "No action required.";

                return new TenantModuleEntitlementItem
                {
                    ModuleId = moduleId,
                    ModuleCode = module?.ModuleCode ?? $"M-{moduleId}",
                    ModuleName = module?.ModuleName ?? "Unknown Module",
                    IsActive = isActive,
                    IsIncludedInBase = isIncludedInBase,
                    IsLicenceEligible = isLicenceEligible,
                    PricingMode = pricingMode,
                    MonthlyPrice = pricing?.PriceMonthly ?? subscriptionModule?.PriceMonthly ?? 0m,
                    AnnualPrice = pricing?.PriceAnnual ?? subscriptionModule?.PriceAnnual ?? 0m,
                    ActivatedAt = subscriptionModule?.ActivatedAt,
                    DeactivatedAt = subscriptionModule?.DeactivatedAt,
                    Status = status,
                    Coverage = coverageRows is { Count: > 0 }
                        ? string.Join(", ", coverageRows.Select(BuildCoverageLabel))
                        : "No active licence coverage",
                    NextAction = nextAction
                };
            })
            .OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.IsLicenceEligible)
            .ThenBy(x => x.ModuleCode)
            .ToList();
    }

    private static string BuildCoverageLabel(LicenceModuleMatrix row)
    {
        var coverageType = row.IsRequired ? "Required" : "Optional";
        return $"{row.LicenceType?.Code ?? $"LT-{row.LicenceTypeId}"} ({coverageType})";
    }

    private static Guid? ResolveEntitlementAuditTenantId(AuditLogEntry entry)
    {
        if (entry.TenantId.HasValue)
        {
            return entry.TenantId.Value;
        }

        if (string.IsNullOrWhiteSpace(entry.NewValues))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.NewValues);
            if (document.RootElement.TryGetProperty("TenantId", out var tenantIdProperty)
                && tenantIdProperty.ValueKind == JsonValueKind.String
                && Guid.TryParse(tenantIdProperty.GetString(), out var parsedTenantId))
            {
                return parsedTenantId;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string DescribeEntitlementAction(string action) => action switch
    {
        "TenantModulesReconciled" => "Modules Reconciled",
        "TenantLicenceAssigned" => "Licence Assigned",
        "TenantLicenceRemoved" => "Licence Removed",
        _ => action
    };

    private static string BuildEntitlementAuditSummary(string action) => action switch
    {
        "TenantModulesReconciled" => "Entitlement reconciliation executed for the tenant.",
        "TenantLicenceAssigned" => "A platform administrator assigned a tenant licence and reconciled access.",
        "TenantLicenceRemoved" => "A platform administrator removed a tenant licence and reconciled access.",
        _ => "Platform entitlement activity recorded."
    };

    private static bool FieldEquivalent(TemplateField oldField, TemplateField newField)
    {
        return string.Equals(oldField.DisplayName, newField.DisplayName, StringComparison.Ordinal)
               && oldField.DataType == newField.DataType
               && oldField.IsRequired == newField.IsRequired
               && oldField.IsComputed == newField.IsComputed
               && oldField.IsKeyField == newField.IsKeyField
               && string.Equals(oldField.AllowedValues, newField.AllowedValues, StringComparison.Ordinal)
               && string.Equals(oldField.MinValue, newField.MinValue, StringComparison.Ordinal)
               && string.Equals(oldField.MaxValue, newField.MaxValue, StringComparison.Ordinal);
    }

    private static string ResolveHeat(decimal validationErrorRate) => validationErrorRate switch
    {
        < 5m => "green",
        < 15m => "amber",
        _ => "red"
    };
}

public class PlatformTenantListQuery
{
    public string Search { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? PlanCode { get; set; }
    public string? LicenceType { get; set; }
    public int? MinModuleCount { get; set; }
    public bool OnlyNeedsReconciliation { get; set; }
    public string SortBy { get; set; } = "name";
    public bool SortDescending { get; set; }
}

public class PlatformTenantListResult
{
    public List<PlatformTenantListItem> Tenants { get; set; } = new();
    public List<string> PlanOptions { get; set; } = new();
    public List<string> LicenceOptions { get; set; } = new();
}

public class PlatformTenantListItem
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string PlanName { get; set; } = "N/A";
    public string PlanCode { get; set; } = "N/A";
    public string LicenceType { get; set; } = "N/A";
    public int ActiveModules { get; set; }
    public int PendingReconciliationModules { get; set; }
    public int Users { get; set; }
    public int Entities { get; set; }
    public decimal Mrr { get; set; }
    public string? LastEntitlementAction { get; set; }
    public DateTime? LastEntitlementActionAt { get; set; }
    public string? ContactEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

file sealed record PlanModuleSummaryRow(int PlanId, int ModuleId, bool IsIncludedInBase);
file sealed record LicenceModuleSummaryRow(int LicenceTypeId, int ModuleId);

public class PlatformTenantDetailData
{
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string TenantSlug { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Address { get; set; }
    public string DefaultCurrency { get; set; } = "NGN";
    public string Timezone { get; set; } = "Africa/Lagos";
    public DateTime CreatedAt { get; set; }
    public string CurrentPlanName { get; set; } = "N/A";
    public string CurrentPlanCode { get; set; } = "N/A";
    public List<TenantModuleEntitlementItem> CurrentModuleEntitlements { get; set; } = new();
    public SubscriptionUsageMetrics Usage { get; set; } = new();
    public BillingSummaryMetrics Billing { get; set; } = new();
    public List<TenantSubscriptionHistoryItem> SubscriptionHistory { get; set; } = new();
    public List<TenantInvoiceItem> Invoices { get; set; } = new();
    public List<TenantPaymentItem> Payments { get; set; } = new();
    public List<TenantUserItem> Users { get; set; } = new();
    public List<TenantEntityItem> Entities { get; set; } = new();
    public List<TenantFilingMatrixItem> FilingMatrix { get; set; } = new();
    public DateTime? LastReconciledAt { get; set; }
    public List<TenantEntitlementActivityItem> EntitlementActivity { get; set; } = new();
    public List<TenantAuditItem> AuditEntries { get; set; } = new();
    public List<TenantSupportTicketItem> SupportTickets { get; set; } = new();
}

public class TenantSubscriptionHistoryItem
{
    public int SubscriptionId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public string PlanCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string BillingFrequency { get; set; } = string.Empty;
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> Modules { get; set; } = new();
}

public class TenantModuleEntitlementItem
{
    public int ModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsIncludedInBase { get; set; }
    public bool IsLicenceEligible { get; set; }
    public string PricingMode { get; set; } = string.Empty;
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? DeactivatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Coverage { get; set; } = string.Empty;
    public string NextAction { get; set; } = string.Empty;
}

public class TenantInvoiceItem
{
    public int InvoiceId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "NGN";
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TenantPaymentItem
{
    public int PaymentId { get; set; }
    public int InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NGN";
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TenantUserItem
{
    public int UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime? LastLoginAt { get; set; }
    public bool MfaEnabled { get; set; }
}

public class TenantEntityItem
{
    public int InstitutionId { get; set; }
    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ContactEmail { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? ParentInstitutionName { get; set; }
    public int BranchCount { get; set; }
}

public class TenantFilingMatrixItem
{
    public int PeriodId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime Deadline { get; set; }
    public string RagStatus { get; set; } = string.Empty;
    public string SubmissionStatus { get; set; } = string.Empty;
}

public class TenantEntitlementActivityItem
{
    public string Action { get; set; } = string.Empty;
    public string ActionCode { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public string? Summary { get; set; }
}

public class TenantAuditItem
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
}

public class TenantSupportTicketItem
{
    public int TicketId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime SlaDueAt { get; set; }
}

public class TemplateConsoleData
{
    public List<TemplateConsoleItem> Items { get; set; } = new();
}

public class TemplateConsoleItem
{
    public int TemplateId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int? CurrentVersionId { get; set; }
    public int? CurrentVersionNumber { get; set; }
    public int? DraftVersionId { get; set; }
    public int? DraftVersionNumber { get; set; }
    public string? DraftStatus { get; set; }
    public int FieldCount { get; set; }
    public int FormulaCount { get; set; }
}

public class TemplateVersionDiffData
{
    public int TemplateId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public int FromVersionId { get; set; }
    public int FromVersionNumber { get; set; }
    public int ToVersionId { get; set; }
    public int ToVersionNumber { get; set; }
    public List<string> AddedFields { get; set; } = new();
    public List<string> RemovedFields { get; set; } = new();
    public List<string> ChangedFields { get; set; } = new();
    public int FormulaCountFrom { get; set; }
    public int FormulaCountTo { get; set; }
}

public class PlatformBillingOpsData
{
    public decimal Mrr { get; set; }
    public decimal Arr { get; set; }
    public decimal Arpu { get; set; }
    public decimal ChurnRatePercent { get; set; }
    public decimal ChurnRateTrailing3MonthsPercent { get; set; }
    public List<RevenueBreakdownItem> RevenueByPlan { get; set; } = new();
    public List<RevenueBreakdownItem> RevenueByModule { get; set; } = new();
    public List<PlatformBillingInvoiceRow> Invoices { get; set; } = new();
}

public class PlatformBillingInvoiceRow
{
    public int InvoiceId { get; set; }
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public InvoiceStatus Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PlatformHealthData
{
    public decimal ApiLatencyP50Ms { get; set; }
    public decimal ApiLatencyP95Ms { get; set; }
    public decimal ApiLatencyP99Ms { get; set; }
    public decimal ErrorRatePercent { get; set; }
    public int ActiveSessions { get; set; }
    public int QueueDepth { get; set; }
    public int RabbitMqPending { get; set; }
    public long DatabaseQueryDurationMs { get; set; }
    public decimal DatabasePoolUtilisationPercent { get; set; }
    public decimal RedisCacheHitRatioPercent { get; set; }
    public decimal RedisMemoryUsageMb { get; set; }
    // Operational summary
    public DateTime SystemUptimeSince { get; set; }
    public decimal AvailabilityPercent { get; set; }
    public long TotalRequestsToday { get; set; }
    public int ErrorsToday { get; set; }
    public int QueuedNotifications { get; set; }
    public int QueuedExports { get; set; }
    public int QueuedImports { get; set; }
    public int PeakHourUtc { get; set; }
    // Service dependency probes
    public List<ServiceProbe> ServiceProbes { get; set; } = new();
}

public enum ServiceStatus { Healthy, Degraded, Down }

public class ProbeResult
{
    public ServiceStatus Status { get; set; }
    public long ResponseMs { get; set; }
    public DateTime CheckedAt { get; set; }
}

public class ServiceProbe
{
    public string Name { get; set; } = string.Empty;
    public ServiceStatus Status { get; set; }
    public long ResponseMs { get; set; }
    public decimal UptimePercent { get; set; }
    public DateTime LastChecked { get; set; }
    public List<ProbeResult> History { get; set; } = new();
}

internal static class ProbeHistoryStore
{
    private static readonly ConcurrentDictionary<string, Queue<ProbeResult>> _store = new();

    public static void Record(string name, ProbeResult result)
    {
        var q = _store.GetOrAdd(name, _ => new Queue<ProbeResult>());
        lock (q)
        {
            q.Enqueue(result);
            while (q.Count > 10) q.Dequeue();
        }
    }

    public static List<ProbeResult> Get(string name)
    {
        if (_store.TryGetValue(name, out var q))
            lock (q) return q.ToList();
        return new List<ProbeResult>();
    }
}

public class ModuleAnalyticsData
{
    public List<ModuleAnalyticsItem> Items { get; set; } = new();
}

public class ModuleAnalyticsItem
{
    public int ModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int TenantCount { get; set; }
    public int ActiveUsers { get; set; }
    public int ReturnsSubmitted { get; set; }
    public decimal ValidationErrorRate { get; set; }
    public string Heat { get; set; } = "green";
    public List<ModuleValidationHotspot> Hotspots { get; set; } = new();
}

public class ModuleValidationHotspot
{
    public string RuleId { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class UpdateFeatureFlagRequest
{
    public string FlagCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int RolloutPercent { get; set; }
    public string? AllowedTenants { get; set; }
    public string? AllowedPlans { get; set; }
}
