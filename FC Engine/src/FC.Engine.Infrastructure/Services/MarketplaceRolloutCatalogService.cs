using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class MarketplaceRolloutCatalogService
{
    private readonly MetadataDbContext _db;

    public MarketplaceRolloutCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<MarketplaceRolloutCatalogState> MaterializeAsync(
        MarketplaceRolloutCatalogInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var moduleRecords = input.Modules
            .Select(x => new MarketplaceRolloutModuleRecord
            {
                ModuleCode = x.ModuleCode,
                ModuleName = x.ModuleName,
                EligibleTenants = x.EligibleTenants,
                ActiveEntitlements = x.ActiveEntitlements,
                PendingEntitlements = x.PendingEntitlements,
                StaleTenants = x.StaleTenants,
                IncludedBasePlans = x.IncludedBasePlans,
                AddOnPlans = x.AddOnPlans,
                AdoptionRatePercent = x.AdoptionRatePercent,
                Signal = x.Signal,
                Commentary = x.Commentary,
                RecommendedAction = x.RecommendedAction,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        var planRecords = input.PlanCoverage
            .Select(x => new MarketplaceRolloutPlanCoverageRecord
            {
                ModuleCode = x.ModuleCode,
                ModuleName = x.ModuleName,
                PlanCode = x.PlanCode,
                PlanName = x.PlanName,
                CoverageMode = x.CoverageMode,
                EligibleTenants = x.EligibleTenants,
                ActiveEntitlements = x.ActiveEntitlements,
                PendingEntitlements = x.PendingEntitlements,
                PriceMonthly = x.PriceMonthly,
                PriceAnnual = x.PriceAnnual,
                Signal = x.Signal,
                Commentary = x.Commentary,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        var queueRecords = input.ReconciliationQueue
            .Select(x => new MarketplaceRolloutQueueRecord
            {
                TenantId = x.TenantId,
                TenantName = x.TenantName,
                PlanCode = x.PlanCode,
                PlanName = x.PlanName,
                PendingModuleCount = x.PendingModuleCount,
                PendingModules = x.PendingModules,
                State = x.State,
                Signal = x.Signal,
                LastEntitlementAction = x.LastEntitlementAction,
                LastEntitlementActionAt = x.LastEntitlementActionAt,
                RecommendedAction = x.RecommendedAction,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        await ClearExistingCatalogAsync(ct);

        _db.MarketplaceRolloutModules.AddRange(moduleRecords);
        _db.MarketplaceRolloutPlanCoverage.AddRange(planRecords);
        _db.MarketplaceRolloutReconciliationQueue.AddRange(queueRecords);
        await _db.SaveChangesAsync(ct);

        return new MarketplaceRolloutCatalogState
        {
            MaterializedAt = materializedAt,
            Modules = moduleRecords
                .OrderBy(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase)
                .Select(MapModule)
                .ToList(),
            PlanCoverage = planRecords
                .OrderBy(x => x.PlanCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase)
                .Select(MapPlanCoverage)
                .ToList(),
            ReconciliationQueue = queueRecords
                .OrderByDescending(x => string.Equals(x.State, "Stale", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.PendingModuleCount)
                .ThenBy(x => x.TenantName, StringComparer.OrdinalIgnoreCase)
                .Select(MapQueue)
                .ToList()
        };
    }

    public async Task<MarketplaceRolloutCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var moduleRecords = await _db.MarketplaceRolloutModules
            .AsNoTracking()
            .OrderBy(x => x.ModuleCode)
            .ToListAsync(ct);

        var planRecords = await _db.MarketplaceRolloutPlanCoverage
            .AsNoTracking()
            .OrderBy(x => x.PlanCode)
            .ThenBy(x => x.ModuleCode)
            .ToListAsync(ct);

        var queueRecords = await _db.MarketplaceRolloutReconciliationQueue
            .AsNoTracking()
            .OrderByDescending(x => x.State == "Stale")
            .ThenByDescending(x => x.PendingModuleCount)
            .ThenBy(x => x.TenantName)
            .ToListAsync(ct);

        var materializedAt = moduleRecords
            .Select(x => (DateTime?)x.MaterializedAt)
            .Concat(planRecords.Select(x => (DateTime?)x.MaterializedAt))
            .Concat(queueRecords.Select(x => (DateTime?)x.MaterializedAt))
            .OrderByDescending(x => x)
            .FirstOrDefault();

        return new MarketplaceRolloutCatalogState
        {
            MaterializedAt = materializedAt,
            Modules = moduleRecords.Select(MapModule).ToList(),
            PlanCoverage = planRecords.Select(MapPlanCoverage).ToList(),
            ReconciliationQueue = queueRecords.Select(MapQueue).ToList()
        };
    }

    private async Task EnsureStoreAsync(CancellationToken ct)
    {
        if (!_db.Database.IsSqlServer())
        {
            return;
        }

        const string sql = """
            IF SCHEMA_ID(N'meta') IS NULL
                EXEC(N'CREATE SCHEMA [meta]');

            IF OBJECT_ID(N'[meta].[marketplace_rollout_modules]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[marketplace_rollout_modules]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ModuleCode] NVARCHAR(80) NOT NULL,
                    [ModuleName] NVARCHAR(240) NOT NULL,
                    [EligibleTenants] INT NOT NULL,
                    [ActiveEntitlements] INT NOT NULL,
                    [PendingEntitlements] INT NOT NULL,
                    [StaleTenants] INT NOT NULL,
                    [IncludedBasePlans] INT NOT NULL,
                    [AddOnPlans] INT NOT NULL,
                    [AdoptionRatePercent] DECIMAL(9,2) NOT NULL,
                    [Signal] NVARCHAR(30) NOT NULL,
                    [Commentary] NVARCHAR(1200) NOT NULL,
                    [RecommendedAction] NVARCHAR(1200) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_marketplace_rollout_modules_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_marketplace_rollout_modules_ModuleCode]
                    ON [meta].[marketplace_rollout_modules]([ModuleCode]);
                CREATE INDEX [IX_marketplace_rollout_modules_Signal]
                    ON [meta].[marketplace_rollout_modules]([Signal]);
                CREATE INDEX [IX_marketplace_rollout_modules_MaterializedAt]
                    ON [meta].[marketplace_rollout_modules]([MaterializedAt]);
            END;

            IF OBJECT_ID(N'[meta].[marketplace_rollout_plan_coverage]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[marketplace_rollout_plan_coverage]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ModuleCode] NVARCHAR(80) NOT NULL,
                    [ModuleName] NVARCHAR(240) NOT NULL,
                    [PlanCode] NVARCHAR(80) NOT NULL,
                    [PlanName] NVARCHAR(240) NOT NULL,
                    [CoverageMode] NVARCHAR(30) NOT NULL,
                    [EligibleTenants] INT NOT NULL,
                    [ActiveEntitlements] INT NOT NULL,
                    [PendingEntitlements] INT NOT NULL,
                    [PriceMonthly] DECIMAL(18,2) NOT NULL,
                    [PriceAnnual] DECIMAL(18,2) NOT NULL,
                    [Signal] NVARCHAR(30) NOT NULL,
                    [Commentary] NVARCHAR(1200) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_marketplace_rollout_plan_coverage_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_marketplace_rollout_plan_coverage_ModuleCode_PlanCode]
                    ON [meta].[marketplace_rollout_plan_coverage]([ModuleCode], [PlanCode]);
                CREATE INDEX [IX_marketplace_rollout_plan_coverage_Signal]
                    ON [meta].[marketplace_rollout_plan_coverage]([Signal]);
                CREATE INDEX [IX_marketplace_rollout_plan_coverage_MaterializedAt]
                    ON [meta].[marketplace_rollout_plan_coverage]([MaterializedAt]);
            END;

            IF OBJECT_ID(N'[meta].[marketplace_rollout_reconciliation_queue]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[marketplace_rollout_reconciliation_queue]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [TenantName] NVARCHAR(240) NOT NULL,
                    [PlanCode] NVARCHAR(80) NOT NULL,
                    [PlanName] NVARCHAR(240) NOT NULL,
                    [PendingModuleCount] INT NOT NULL,
                    [PendingModules] NVARCHAR(600) NOT NULL,
                    [State] NVARCHAR(30) NOT NULL,
                    [Signal] NVARCHAR(30) NOT NULL,
                    [LastEntitlementAction] NVARCHAR(120) NULL,
                    [LastEntitlementActionAt] DATETIME2 NULL,
                    [RecommendedAction] NVARCHAR(1200) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_marketplace_rollout_reconciliation_queue_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_marketplace_rollout_reconciliation_queue_TenantId]
                    ON [meta].[marketplace_rollout_reconciliation_queue]([TenantId]);
                CREATE INDEX [IX_marketplace_rollout_reconciliation_queue_State]
                    ON [meta].[marketplace_rollout_reconciliation_queue]([State]);
                CREATE INDEX [IX_marketplace_rollout_reconciliation_queue_Signal]
                    ON [meta].[marketplace_rollout_reconciliation_queue]([Signal]);
                CREATE INDEX [IX_marketplace_rollout_reconciliation_queue_MaterializedAt]
                    ON [meta].[marketplace_rollout_reconciliation_queue]([MaterializedAt]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[marketplace_rollout_modules];", ct);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[marketplace_rollout_plan_coverage];", ct);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[marketplace_rollout_reconciliation_queue];", ct);
            return;
        }

        var moduleRecords = await _db.MarketplaceRolloutModules.ToListAsync(ct);
        var planRecords = await _db.MarketplaceRolloutPlanCoverage.ToListAsync(ct);
        var queueRecords = await _db.MarketplaceRolloutReconciliationQueue.ToListAsync(ct);

        if (moduleRecords.Count > 0)
        {
            _db.MarketplaceRolloutModules.RemoveRange(moduleRecords);
        }

        if (planRecords.Count > 0)
        {
            _db.MarketplaceRolloutPlanCoverage.RemoveRange(planRecords);
        }

        if (queueRecords.Count > 0)
        {
            _db.MarketplaceRolloutReconciliationQueue.RemoveRange(queueRecords);
        }

        if (moduleRecords.Count > 0 || planRecords.Count > 0 || queueRecords.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private static MarketplaceRolloutModuleState MapModule(MarketplaceRolloutModuleRecord record) =>
        new()
        {
            ModuleCode = record.ModuleCode,
            ModuleName = record.ModuleName,
            EligibleTenants = record.EligibleTenants,
            ActiveEntitlements = record.ActiveEntitlements,
            PendingEntitlements = record.PendingEntitlements,
            StaleTenants = record.StaleTenants,
            IncludedBasePlans = record.IncludedBasePlans,
            AddOnPlans = record.AddOnPlans,
            AdoptionRatePercent = record.AdoptionRatePercent,
            Signal = record.Signal,
            Commentary = record.Commentary,
            RecommendedAction = record.RecommendedAction,
            MaterializedAt = record.MaterializedAt
        };

    private static MarketplaceRolloutPlanCoverageState MapPlanCoverage(MarketplaceRolloutPlanCoverageRecord record) =>
        new()
        {
            ModuleCode = record.ModuleCode,
            ModuleName = record.ModuleName,
            PlanCode = record.PlanCode,
            PlanName = record.PlanName,
            CoverageMode = record.CoverageMode,
            EligibleTenants = record.EligibleTenants,
            ActiveEntitlements = record.ActiveEntitlements,
            PendingEntitlements = record.PendingEntitlements,
            PriceMonthly = record.PriceMonthly,
            PriceAnnual = record.PriceAnnual,
            Signal = record.Signal,
            Commentary = record.Commentary,
            MaterializedAt = record.MaterializedAt
        };

    private static MarketplaceRolloutQueueState MapQueue(MarketplaceRolloutQueueRecord record) =>
        new()
        {
            TenantId = record.TenantId,
            TenantName = record.TenantName,
            PlanCode = record.PlanCode,
            PlanName = record.PlanName,
            PendingModuleCount = record.PendingModuleCount,
            PendingModules = record.PendingModules,
            State = record.State,
            Signal = record.Signal,
            LastEntitlementAction = record.LastEntitlementAction,
            LastEntitlementActionAt = record.LastEntitlementActionAt,
            RecommendedAction = record.RecommendedAction,
            MaterializedAt = record.MaterializedAt
        };
}

public sealed class MarketplaceRolloutCatalogInput
{
    public IReadOnlyList<MarketplaceRolloutModuleInput> Modules { get; init; } = [];
    public IReadOnlyList<MarketplaceRolloutPlanCoverageInput> PlanCoverage { get; init; } = [];
    public IReadOnlyList<MarketplaceRolloutQueueInput> ReconciliationQueue { get; init; } = [];
}

public sealed class MarketplaceRolloutModuleInput
{
    public string ModuleCode { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public int EligibleTenants { get; init; }
    public int ActiveEntitlements { get; init; }
    public int PendingEntitlements { get; init; }
    public int StaleTenants { get; init; }
    public int IncludedBasePlans { get; init; }
    public int AddOnPlans { get; init; }
    public decimal AdoptionRatePercent { get; init; }
    public string Signal { get; init; } = string.Empty;
    public string Commentary { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
}

public sealed class MarketplaceRolloutPlanCoverageInput
{
    public string ModuleCode { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string CoverageMode { get; init; } = string.Empty;
    public int EligibleTenants { get; init; }
    public int ActiveEntitlements { get; init; }
    public int PendingEntitlements { get; init; }
    public decimal PriceMonthly { get; init; }
    public decimal PriceAnnual { get; init; }
    public string Signal { get; init; } = string.Empty;
    public string Commentary { get; init; } = string.Empty;
}

public sealed class MarketplaceRolloutQueueInput
{
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public int PendingModuleCount { get; init; }
    public string PendingModules { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Signal { get; init; } = string.Empty;
    public string? LastEntitlementAction { get; init; }
    public DateTime? LastEntitlementActionAt { get; init; }
    public string RecommendedAction { get; init; } = string.Empty;
}

public sealed class MarketplaceRolloutCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public List<MarketplaceRolloutModuleState> Modules { get; init; } = [];
    public List<MarketplaceRolloutPlanCoverageState> PlanCoverage { get; init; } = [];
    public List<MarketplaceRolloutQueueState> ReconciliationQueue { get; init; } = [];
}

public sealed class MarketplaceRolloutModuleState
{
    public string ModuleCode { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public int EligibleTenants { get; init; }
    public int ActiveEntitlements { get; init; }
    public int PendingEntitlements { get; init; }
    public int StaleTenants { get; init; }
    public int IncludedBasePlans { get; init; }
    public int AddOnPlans { get; init; }
    public decimal AdoptionRatePercent { get; init; }
    public string Signal { get; init; } = string.Empty;
    public string Commentary { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public DateTime MaterializedAt { get; init; }
}

public sealed class MarketplaceRolloutPlanCoverageState
{
    public string ModuleCode { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public string CoverageMode { get; init; } = string.Empty;
    public int EligibleTenants { get; init; }
    public int ActiveEntitlements { get; init; }
    public int PendingEntitlements { get; init; }
    public decimal PriceMonthly { get; init; }
    public decimal PriceAnnual { get; init; }
    public string Signal { get; init; } = string.Empty;
    public string Commentary { get; init; } = string.Empty;
    public DateTime MaterializedAt { get; init; }
}

public sealed class MarketplaceRolloutQueueState
{
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public int PendingModuleCount { get; init; }
    public string PendingModules { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Signal { get; init; } = string.Empty;
    public string? LastEntitlementAction { get; init; }
    public DateTime? LastEntitlementActionAt { get; init; }
    public string RecommendedAction { get; init; } = string.Empty;
    public DateTime MaterializedAt { get; init; }
}
