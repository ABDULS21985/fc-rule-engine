using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class ModuleMarketplaceBootstrapService
{
    private static readonly IReadOnlyDictionary<string, ModulePricingDefinition> PricingByModuleCode =
        new Dictionary<string, ModulePricingDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["OPS_RESILIENCE"] = new ModulePricingDefinition(
                "OPS_RESILIENCE",
                StarterMonthly: 70000m,
                ProfessionalMonthly: 60000m,
                EnterpriseMonthly: 50000m,
                GroupMonthly: 40000m),
            ["MODEL_RISK"] = new ModulePricingDefinition(
                "MODEL_RISK",
                StarterMonthly: 80000m,
                ProfessionalMonthly: 70000m,
                EnterpriseMonthly: 60000m,
                GroupMonthly: 50000m)
        };

    private readonly MetadataDbContext _db;
    private readonly ILogger<ModuleMarketplaceBootstrapService> _logger;

    public ModuleMarketplaceBootstrapService(
        MetadataDbContext db,
        ILogger<ModuleMarketplaceBootstrapService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ModuleMarketplaceBootstrapResult> EnsurePricingAsync(CancellationToken ct = default)
    {
        var targetModuleCodes = PricingByModuleCode.Keys.ToList();
        var modules = await _db.Modules
            .Where(x => targetModuleCodes.Contains(x.ModuleCode))
            .ToDictionaryAsync(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase, ct);

        var plans = await _db.SubscriptionPlans
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var moduleIds = modules.Values.Select(x => x.Id).ToList();
        var existing = moduleIds.Count == 0
            ? []
            : await _db.PlanModulePricing
                .Where(x => moduleIds.Contains(x.ModuleId))
                .ToListAsync(ct);

        var created = 0;
        var updated = 0;

        foreach (var moduleCode in targetModuleCodes)
        {
            if (!modules.TryGetValue(moduleCode, out var module))
            {
                continue;
            }

            var pricingDefinition = PricingByModuleCode[moduleCode];
            foreach (var plan in plans)
            {
                var target = ResolvePricing(plan, pricingDefinition);
                var row = existing.FirstOrDefault(x => x.PlanId == plan.Id && x.ModuleId == module.Id);
                if (row is null)
                {
                    _db.PlanModulePricing.Add(new PlanModulePricing
                    {
                        PlanId = plan.Id,
                        ModuleId = module.Id,
                        PriceMonthly = target.PriceMonthly,
                        PriceAnnual = target.PriceAnnual,
                        IsIncludedInBase = target.IsIncludedInBase
                    });
                    created++;
                    continue;
                }

                if (row.PriceMonthly == target.PriceMonthly
                    && row.PriceAnnual == target.PriceAnnual
                    && row.IsIncludedInBase == target.IsIncludedInBase)
                {
                    continue;
                }

                row.PriceMonthly = target.PriceMonthly;
                row.PriceAnnual = target.PriceAnnual;
                row.IsIncludedInBase = target.IsIncludedInBase;
                updated++;
            }
        }

        if (created > 0 || updated > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Module marketplace bootstrap completed. PricingCreated={Created} PricingUpdated={Updated}",
            created,
            updated);

        return new ModuleMarketplaceBootstrapResult
        {
            PricingCreated = created,
            PricingUpdated = updated
        };
    }

    private static ModulePlanPricingTarget ResolvePricing(SubscriptionPlan plan, ModulePricingDefinition definition)
    {
        if (plan.PlanCode.Equals("REGULATOR", StringComparison.OrdinalIgnoreCase)
            || plan.PlanCode.Equals("WHITE_LABEL", StringComparison.OrdinalIgnoreCase)
            || plan.HasFeature("all_features"))
        {
            return new ModulePlanPricingTarget(0m, 0m, true);
        }

        var monthly = plan.PlanCode.ToUpperInvariant() switch
        {
            "STARTER" => definition.StarterMonthly,
            "PROFESSIONAL" => definition.ProfessionalMonthly,
            "ENTERPRISE" => definition.EnterpriseMonthly,
            "GROUP" => definition.GroupMonthly,
            _ => plan.Tier switch
            {
                >= 50 => definition.GroupMonthly,
                >= 10 => definition.EnterpriseMonthly,
                >= 3 => definition.ProfessionalMonthly,
                _ => definition.StarterMonthly
            }
        };

        return new ModulePlanPricingTarget(monthly, monthly * 10m, false);
    }
}

public sealed class ModuleMarketplaceBootstrapResult
{
    public int PricingCreated { get; init; }
    public int PricingUpdated { get; init; }
}

internal sealed record ModulePricingDefinition(
    string ModuleCode,
    decimal StarterMonthly,
    decimal ProfessionalMonthly,
    decimal EnterpriseMonthly,
    decimal GroupMonthly);

internal sealed record ModulePlanPricingTarget(
    decimal PriceMonthly,
    decimal PriceAnnual,
    bool IsIncludedInBase);
