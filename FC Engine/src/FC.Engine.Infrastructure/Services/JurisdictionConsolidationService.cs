using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public class JurisdictionConsolidationService : IJurisdictionConsolidationService
{
    private readonly MetadataDbContext _db;

    public JurisdictionConsolidationService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<CrossJurisdictionConsolidation> GetConsolidation(
        Guid tenantId,
        string reportingCurrency = "NGN",
        CancellationToken ct = default)
    {
        var currency = string.IsNullOrWhiteSpace(reportingCurrency)
            ? "NGN"
            : reportingCurrency.Trim().ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // ── Tenant tree ──────────────────────────────────────────────────────
        var allTenants = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId || t.ParentTenantId == tenantId)
            .ToListAsync(ct);

        var relatedTenantIds = allTenants.Select(t => t.TenantId).ToList();
        if (!relatedTenantIds.Contains(tenantId))
            relatedTenantIds.Add(tenantId);

        var institutions = await _db.Institutions
            .AsNoTracking()
            .Where(i => relatedTenantIds.Contains(i.TenantId))
            .ToListAsync(ct);

        if (institutions.Count == 0)
        {
            return new CrossJurisdictionConsolidation
            {
                TenantId = tenantId,
                ReportingCurrency = currency,
                SubsidiaryCount = Math.Max(relatedTenantIds.Count - 1, 0),
                GeneratedAt = DateTime.UtcNow
            };
        }

        var jurisdictions = await _db.Jurisdictions
            .AsNoTracking()
            .Where(j => institutions.Select(i => i.JurisdictionId).Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, ct);

        var invoiceRows = await _db.Invoices
            .AsNoTracking()
            .Where(i => relatedTenantIds.Contains(i.TenantId) && i.Status != InvoiceStatus.Voided)
            .Select(i => new { i.TenantId, i.TotalAmount, i.Currency })
            .ToListAsync(ct);

        var submissionRows = await _db.Submissions
            .AsNoTracking()
            .Where(s => relatedTenantIds.Contains(s.TenantId))
            .Select(s => new
            {
                s.Id,
                s.TenantId,
                s.InstitutionId,
                s.ReturnCode,
                s.Status,
                s.SubmittedAt
            })
            .ToListAsync(ct);

        var overdueRows = await _db.ReturnPeriods
            .AsNoTracking()
            .Where(rp => relatedTenantIds.Contains(rp.TenantId) && rp.Status == "Overdue")
            .Select(rp => rp.TenantId)
            .ToListAsync(ct);

        var adjustments = await _db.ConsolidationAdjustments
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.EffectiveDate <= today)
            .Include(a => a.SourceInstitution)
            .Include(a => a.TargetInstitution)
            .ToListAsync(ct);

        // ── Jurisdictions ─────────────────────────────────────────────────────
        var grouped = institutions
            .GroupBy(i => i.JurisdictionId)
            .OrderBy(g => jurisdictions.TryGetValue(g.Key, out var j) ? j.CountryCode : "ZZ")
            .ToList();

        var result = new CrossJurisdictionConsolidation
        {
            TenantId = tenantId,
            ReportingCurrency = currency,
            SubsidiaryCount = Math.Max(relatedTenantIds.Count - 1, 0),
            GeneratedAt = DateTime.UtcNow
        };

        var institutionFxRates = new Dictionary<int, (decimal Rate, DateTime RateDate)>();

        foreach (var group in grouped)
        {
            if (!jurisdictions.TryGetValue(group.Key, out var jurisdiction))
                continue;

            var tenantIdsInJurisdiction = group.Select(i => i.TenantId).Distinct().ToHashSet();

            var invoiceAmount = invoiceRows
                .Where(i => tenantIdsInJurisdiction.Contains(i.TenantId))
                .Sum(i => i.TotalAmount);

            var (fxRate, fxRateDate) = await ResolveFxRateWithDate(jurisdiction.Currency, currency, today, ct);

            // Cache per jurisdiction for aggregation fields
            foreach (var inst in group)
                institutionFxRates[inst.Id] = (fxRate, fxRateDate);

            result.Jurisdictions.Add(new JurisdictionConsolidationItem
            {
                JurisdictionId = jurisdiction.Id,
                CountryCode = jurisdiction.CountryCode,
                CountryName = jurisdiction.CountryName,
                Currency = jurisdiction.Currency,
                FxRateToReportingCurrency = fxRate,
                FxRateDate = fxRateDate,
                InstitutionCount = group.Count(),
                SubmissionCount = submissionRows.Count(s =>
                    tenantIdsInJurisdiction.Contains(s.TenantId)
                    && group.Any(i => i.Id == s.InstitutionId)),
                OverdueSubmissionCount = overdueRows.Count(t => tenantIdsInJurisdiction.Contains(t)),
                GrossAmountLocal = decimal.Round(invoiceAmount, 2, MidpointRounding.AwayFromZero),
                GrossAmountReportingCurrency = decimal.Round(invoiceAmount * fxRate, 2, MidpointRounding.AwayFromZero)
            });
        }

        result.GrossAmount = result.Jurisdictions.Sum(x => x.GrossAmountReportingCurrency);

        // ── Elimination Adjustments ──────────────────────────────────────────
        decimal adjustmentTotal = 0;
        foreach (var adj in adjustments)
        {
            var (rate, _) = await ResolveFxRateWithDate(adj.Currency, currency, today, ct);
            var amountReporting = decimal.Round(adj.Amount * rate, 2, MidpointRounding.AwayFromZero);
            adjustmentTotal += amountReporting;

            result.Eliminations.Add(new EliminationEntry
            {
                Id = adj.Id,
                Description = adj.Description ?? $"{adj.AdjustmentType} adjustment",
                Category = adj.AdjustmentType,
                SourceEntityId = adj.SourceInstitutionId ?? 0,
                SourceEntityName = adj.SourceInstitution?.InstitutionName ?? "—",
                CounterpartyEntityId = adj.TargetInstitutionId ?? 0,
                CounterpartyEntityName = adj.TargetInstitution?.InstitutionName ?? "—",
                Amount = adj.Amount,
                Currency = adj.Currency,
                AmountReportingCurrency = amountReporting,
                FieldCode = adj.AdjustmentType.ToUpperInvariant().Replace(" ", "_"),
                IsMatched = adj.SourceInstitutionId.HasValue && adj.TargetInstitutionId.HasValue
            });
        }

        result.EliminationAdjustments = decimal.Round(adjustmentTotal, 2, MidpointRounding.AwayFromZero);
        result.NetAmount = decimal.Round(result.GrossAmount - result.EliminationAdjustments, 2, MidpointRounding.AwayFromZero);

        // ── Entity Hierarchy ─────────────────────────────────────────────────
        result.EntityHierarchy = BuildEntityHierarchy(allTenants, institutions, tenantId);

        // ── Consolidation Status Matrix ───────────────────────────────────────
        result.StatusMatrix = BuildStatusMatrix(institutions, submissionRows
            .Select(s => new SubmissionSummary
            {
                Id = s.Id,
                InstitutionId = s.InstitutionId,
                ReturnCode = s.ReturnCode,
                Status = s.Status,
                SubmittedAt = s.SubmittedAt ?? default
            }).ToList());

        // ── Aggregation Fields ────────────────────────────────────────────────
        result.AggregationFields = BuildAggregationFields(institutions, invoiceRows
            .Select(i => new InvoiceSummary { TenantId = i.TenantId, TotalAmount = i.TotalAmount, Currency = i.Currency })
            .ToList(), institutionFxRates, currency);

        // ── Reconciliation Alerts ─────────────────────────────────────────────
        result.ReconciliationAlerts = BuildReconciliationAlerts(result);

        return result;
    }

    // ── Entity Hierarchy Builder ─────────────────────────────────────────────

    private static List<EntityNode> BuildEntityHierarchy(List<Tenant> tenants, List<Institution> institutions, Guid rootTenantId)
    {
        var instByTenant = institutions.GroupBy(i => i.TenantId).ToDictionary(g => g.Key, g => g.ToList());
        var tenantById = tenants.ToDictionary(t => t.TenantId);

        var rootTenants = tenants.Where(t => t.TenantId == rootTenantId || t.ParentTenantId == null || t.ParentTenantId == Guid.Empty).ToList();
        if (rootTenants.Count == 0 && tenants.Count > 0)
            rootTenants = new List<Tenant> { tenants.First() };

        // For simplicity: treat each tenant as a group node, institutions as children
        var rootNodes = new List<EntityNode>();
        int nodeId = 1;

        foreach (var tenant in tenants.OrderBy(t => t.TenantId == rootTenantId ? 0 : 1))
        {
            var tenantInsts = instByTenant.GetValueOrDefault(tenant.TenantId, new List<Institution>());
            var entityType = tenant.TenantId == rootTenantId ? "HoldingCompany" : "Subsidiary";

            var tenantNode = new EntityNode
            {
                EntityId = nodeId++,
                Name = tenant.TenantName,
                Code = tenant.TenantSlug,
                EntityType = entityType,
                Jurisdiction = null,
                SubmissionCount = 0,
                PendingCount = 0,
                Children = tenantInsts.Select(inst => new EntityNode
                {
                    EntityId = inst.Id + 10000, // offset to avoid collision with tenant nodeIds
                    Name = inst.InstitutionName,
                    Code = inst.InstitutionCode,
                    EntityType = MapEntityType(inst.EntityType),
                    SubmissionCount = 0,
                    PendingCount = 0
                }).ToList()
            };

            if (tenant.TenantId == rootTenantId)
                rootNodes.Insert(0, tenantNode);
            else
                rootNodes.Add(tenantNode);
        }

        return rootNodes;
    }

    private static string MapEntityType(EntityType et) => et switch
    {
        EntityType.Subsidiary => "Subsidiary",
        EntityType.Branch or EntityType.RegionalOffice => "Branch",
        _ => "Subsidiary"
    };

    // ── Status Matrix Builder ─────────────────────────────────────────────────

    private static List<ConsolidationStatusEntry> BuildStatusMatrix(
        List<Institution> institutions,
        List<SubmissionSummary> submissions)
    {
        var entries = new List<ConsolidationStatusEntry>();
        var subsByInstitution = submissions.GroupBy(s => s.InstitutionId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var inst in institutions)
        {
            var instSubs = subsByInstitution.GetValueOrDefault(inst.Id, new List<SubmissionSummary>());

            // Group by return code, take the most recent per code
            var returnCells = instSubs
                .GroupBy(s => s.ReturnCode)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(s => s.SubmittedAt).First();
                    return new ReturnStatusCell
                    {
                        ReturnCode = g.Key,
                        ReturnName = g.Key,
                        SubmissionId = latest.Id,
                        SubmittedAt = latest.SubmittedAt,
                        Status = MapSubmissionStatus(latest.Status)
                    };
                })
                .ToList();

            entries.Add(new ConsolidationStatusEntry
            {
                EntityId = inst.Id,
                EntityName = inst.InstitutionName,
                EntityCode = inst.InstitutionCode,
                Returns = returnCells
            });
        }

        return entries;
    }

    private static ConsolidationSubmissionStatus MapSubmissionStatus(SubmissionStatus s) => s switch
    {
        SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings => ConsolidationSubmissionStatus.Accepted,
        SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected => ConsolidationSubmissionStatus.Rejected,
        SubmissionStatus.PendingApproval or SubmissionStatus.Validating or SubmissionStatus.Parsing => ConsolidationSubmissionStatus.Pending,
        _ => ConsolidationSubmissionStatus.Submitted
    };

    // ── Aggregation Fields Builder ────────────────────────────────────────────

    private static List<AggregationField> BuildAggregationFields(
        List<Institution> institutions,
        List<InvoiceSummary> invoices,
        Dictionary<int, (decimal Rate, DateTime RateDate)> institutionFxRates,
        string reportingCurrency)
    {
        if (institutions.Count == 0 || invoices.Count == 0)
            return new List<AggregationField>();

        var tenantByInst = institutions.ToDictionary(i => i.Id, i => i.TenantId);
        var instByTenant = institutions.GroupBy(i => i.TenantId).ToDictionary(g => g.Key, g => g.First());

        // Build one aggregation field per distinct currency (maps to a "Total Invoiced" per currency)
        var byCurrency = invoices.GroupBy(i => i.Currency).ToList();
        var fields = new List<AggregationField>();

        foreach (var currGroup in byCurrency)
        {
            var localCurrency = currGroup.Key ?? reportingCurrency;
            var entityValues = new List<EntityAggregationValue>();
            decimal fieldTotal = 0;

            foreach (var inst in institutions)
            {
                var tenantId = inst.TenantId;
                var tenantInvoices = invoices.Where(inv => inv.TenantId == tenantId && inv.Currency == localCurrency).ToList();
                if (tenantInvoices.Count == 0) continue;

                var localTotal = tenantInvoices.Sum(inv => inv.TotalAmount);
                institutionFxRates.TryGetValue(inst.Id, out var fxInfo);
                var rate = fxInfo.Rate > 0 ? fxInfo.Rate : 1m;
                var rateDate = fxInfo.RateDate != default ? fxInfo.RateDate : DateTime.UtcNow;
                var reportingValue = decimal.Round(localTotal * rate, 2, MidpointRounding.AwayFromZero);
                fieldTotal += reportingValue;

                entityValues.Add(new EntityAggregationValue
                {
                    EntityId = inst.Id,
                    EntityName = inst.InstitutionName,
                    Value = reportingValue,
                    LocalCurrency = localCurrency,
                    LocalValue = localTotal,
                    FxRate = rate,
                    FxRateDate = rateDate
                });
            }

            if (entityValues.Count == 0) continue;

            fields.Add(new AggregationField
            {
                FieldCode = $"INVOICE_{localCurrency}",
                FieldName = $"Total Invoiced ({localCurrency})",
                GroupTotal = fieldTotal,
                ExpectedTotal = fieldTotal, // no variance without a target — set to actual to indicate balanced
                Currency = reportingCurrency,
                EntityValues = entityValues
            });
        }

        return fields;
    }

    // ── Reconciliation Alerts Builder ─────────────────────────────────────────

    private static List<ReconciliationAlert> BuildReconciliationAlerts(CrossJurisdictionConsolidation result)
    {
        var alerts = new List<ReconciliationAlert>();

        // Alert if any elimination is unmatched
        var unmatchedEliminations = result.Eliminations.Where(e => !e.IsMatched).ToList();
        if (unmatchedEliminations.Count > 0)
        {
            var totalUnmatched = unmatchedEliminations.Sum(e => e.AmountReportingCurrency);
            alerts.Add(new ReconciliationAlert
            {
                AlertId = "UNMATCHED_ELIM",
                Severity = ReconciliationAlertSeverity.Warning,
                Title = $"{unmatchedEliminations.Count} unmatched elimination entr{(unmatchedEliminations.Count == 1 ? "y" : "ies")}",
                Message = $"Elimination entries totalling {totalUnmatched:N2} {result.ReportingCurrency} could not be matched to a counterparty entry. Review intercompany balances.",
                FieldCode = "ELIMINATIONS",
                ActualValue = totalUnmatched,
                ExpectedValue = 0,
                Difference = totalUnmatched,
                DrilldownItems = unmatchedEliminations.Select(e => new ReconciliationDrilldownItem
                {
                    EntityId = e.SourceEntityId,
                    EntityName = e.SourceEntityName,
                    FieldCode = e.FieldCode,
                    ReportedValue = e.AmountReportingCurrency,
                    ExpectedValue = 0,
                    Difference = e.AmountReportingCurrency
                }).ToList()
            });
        }

        // Alert if Net != Gross - Eliminations (floating point sanity)
        var expectedNet = decimal.Round(result.GrossAmount - result.EliminationAdjustments, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(result.NetAmount - expectedNet) > 0.05m)
        {
            alerts.Add(new ReconciliationAlert
            {
                AlertId = "NET_MISMATCH",
                Severity = ReconciliationAlertSeverity.Error,
                Title = "Net consolidated amount does not reconcile",
                Message = $"Expected net = Gross ({result.GrossAmount:N2}) − Eliminations ({result.EliminationAdjustments:N2}) = {expectedNet:N2}, but reported net is {result.NetAmount:N2}.",
                FieldCode = "NET_AMOUNT",
                ExpectedValue = expectedNet,
                ActualValue = result.NetAmount,
                Difference = result.NetAmount - expectedNet
            });
        }

        return alerts;
    }

    // ── FX Rate Helpers ───────────────────────────────────────────────────────

    private async Task<(decimal Rate, DateTime RateDate)> ResolveFxRateWithDate(
        string baseCurrency,
        string quoteCurrency,
        DateOnly rateDate,
        CancellationToken ct)
    {
        if (string.Equals(baseCurrency, quoteCurrency, StringComparison.OrdinalIgnoreCase))
            return (1m, DateTime.UtcNow);

        var exact = await _db.JurisdictionFxRates
            .AsNoTracking()
            .Where(r => r.BaseCurrency == baseCurrency && r.QuoteCurrency == quoteCurrency && r.RateDate <= rateDate)
            .OrderByDescending(r => r.RateDate)
            .Select(r => new { r.Rate, r.RateDate })
            .FirstOrDefaultAsync(ct);

        if (exact is { Rate: > 0 })
            return (exact.Rate, exact.RateDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        var inverse = await _db.JurisdictionFxRates
            .AsNoTracking()
            .Where(r => r.BaseCurrency == quoteCurrency && r.QuoteCurrency == baseCurrency && r.RateDate <= rateDate)
            .OrderByDescending(r => r.RateDate)
            .Select(r => new { r.Rate, r.RateDate })
            .FirstOrDefaultAsync(ct);

        if (inverse is { Rate: > 0 })
            return (decimal.Round(1m / inverse.Rate, 8, MidpointRounding.AwayFromZero),
                    inverse.RateDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        return (1m, DateTime.UtcNow);
    }

    // ── Private DTOs (used inside this service only) ──────────────────────────

    private sealed class SubmissionSummary
    {
        public int Id { get; init; }
        public int InstitutionId { get; init; }
        public string ReturnCode { get; init; } = "";
        public SubmissionStatus Status { get; init; }
        public DateTime SubmittedAt { get; init; }
    }

    private sealed class InvoiceSummary
    {
        public Guid TenantId { get; init; }
        public decimal TotalAmount { get; init; }
        public string Currency { get; init; } = "";
    }
}
