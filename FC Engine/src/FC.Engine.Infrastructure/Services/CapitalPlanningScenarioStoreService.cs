using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class CapitalPlanningScenarioStoreService
{
    private const string LatestScenarioKey = "LATEST";

    private readonly MetadataDbContext _db;

    public CapitalPlanningScenarioStoreService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<CapitalPlanningScenarioState?> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var record = await _db.CapitalPlanningScenarios
            .AsNoTracking()
            .OrderByDescending(x => x.SavedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (record is null)
        {
            return null;
        }

        return new CapitalPlanningScenarioState
        {
            CurrentCarPercent = record.CurrentCarPercent,
            CurrentRwaBn = record.CurrentRwaBn,
            QuarterlyRwaGrowthPercent = record.QuarterlyRwaGrowthPercent,
            QuarterlyRetainedEarningsBn = record.QuarterlyRetainedEarningsBn,
            CapitalActionBn = record.CapitalActionBn,
            MinimumRequirementPercent = record.MinimumRequirementPercent,
            ConservationBufferPercent = record.ConservationBufferPercent,
            CountercyclicalBufferPercent = record.CountercyclicalBufferPercent,
            DsibBufferPercent = record.DsibBufferPercent,
            RwaOptimisationPercent = record.RwaOptimisationPercent,
            TargetCarPercent = record.TargetCarPercent,
            Cet1CostPercent = record.Cet1CostPercent,
            At1CostPercent = record.At1CostPercent,
            Tier2CostPercent = record.Tier2CostPercent,
            MaxAt1SharePercent = record.MaxAt1SharePercent,
            MaxTier2SharePercent = record.MaxTier2SharePercent,
            StepPercent = record.StepPercent,
            SavedAtUtc = record.SavedAtUtc
        };
    }

    public async Task<IReadOnlyList<CapitalPlanningScenarioHistoryState>> LoadHistoryAsync(int take = 8, CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        return await _db.CapitalPlanningScenarioHistory
            .AsNoTracking()
            .OrderByDescending(x => x.SavedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(Math.Max(1, take))
            .Select(x => new CapitalPlanningScenarioHistoryState
            {
                HistoryId = x.Id,
                CurrentCarPercent = x.CurrentCarPercent,
                CurrentRwaBn = x.CurrentRwaBn,
                QuarterlyRwaGrowthPercent = x.QuarterlyRwaGrowthPercent,
                QuarterlyRetainedEarningsBn = x.QuarterlyRetainedEarningsBn,
                CapitalActionBn = x.CapitalActionBn,
                MinimumRequirementPercent = x.MinimumRequirementPercent,
                ConservationBufferPercent = x.ConservationBufferPercent,
                CountercyclicalBufferPercent = x.CountercyclicalBufferPercent,
                DsibBufferPercent = x.DsibBufferPercent,
                RwaOptimisationPercent = x.RwaOptimisationPercent,
                TargetCarPercent = x.TargetCarPercent,
                Cet1CostPercent = x.Cet1CostPercent,
                At1CostPercent = x.At1CostPercent,
                Tier2CostPercent = x.Tier2CostPercent,
                MaxAt1SharePercent = x.MaxAt1SharePercent,
                MaxTier2SharePercent = x.MaxTier2SharePercent,
                StepPercent = x.StepPercent,
                SavedAtUtc = x.SavedAtUtc
            })
            .ToListAsync(ct);
    }

    public async Task<CapitalPlanningScenarioState> SaveAsync(CapitalPlanningScenarioCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await EnsureStoreAsync(ct);

        var savedAtUtc = command.SavedAtUtc == default ? DateTime.UtcNow : command.SavedAtUtc;
        var existing = await _db.CapitalPlanningScenarios
            .FirstOrDefaultAsync(x => x.ScenarioKey == LatestScenarioKey, ct);

        if (existing is null)
        {
            existing = new CapitalPlanningScenarioRecord
            {
                ScenarioKey = LatestScenarioKey,
                CreatedAt = savedAtUtc
            };
            _db.CapitalPlanningScenarios.Add(existing);
        }

        existing.CurrentCarPercent = command.CurrentCarPercent;
        existing.CurrentRwaBn = command.CurrentRwaBn;
        existing.QuarterlyRwaGrowthPercent = command.QuarterlyRwaGrowthPercent;
        existing.QuarterlyRetainedEarningsBn = command.QuarterlyRetainedEarningsBn;
        existing.CapitalActionBn = command.CapitalActionBn;
        existing.MinimumRequirementPercent = command.MinimumRequirementPercent;
        existing.ConservationBufferPercent = command.ConservationBufferPercent;
        existing.CountercyclicalBufferPercent = command.CountercyclicalBufferPercent;
        existing.DsibBufferPercent = command.DsibBufferPercent;
        existing.RwaOptimisationPercent = command.RwaOptimisationPercent;
        existing.TargetCarPercent = command.TargetCarPercent;
        existing.Cet1CostPercent = command.Cet1CostPercent;
        existing.At1CostPercent = command.At1CostPercent;
        existing.Tier2CostPercent = command.Tier2CostPercent;
        existing.MaxAt1SharePercent = command.MaxAt1SharePercent;
        existing.MaxTier2SharePercent = command.MaxTier2SharePercent;
        existing.StepPercent = command.StepPercent;
        existing.SavedAtUtc = savedAtUtc;

        _db.CapitalPlanningScenarioHistory.Add(new CapitalPlanningScenarioHistoryRecord
        {
            CurrentCarPercent = command.CurrentCarPercent,
            CurrentRwaBn = command.CurrentRwaBn,
            QuarterlyRwaGrowthPercent = command.QuarterlyRwaGrowthPercent,
            QuarterlyRetainedEarningsBn = command.QuarterlyRetainedEarningsBn,
            CapitalActionBn = command.CapitalActionBn,
            MinimumRequirementPercent = command.MinimumRequirementPercent,
            ConservationBufferPercent = command.ConservationBufferPercent,
            CountercyclicalBufferPercent = command.CountercyclicalBufferPercent,
            DsibBufferPercent = command.DsibBufferPercent,
            RwaOptimisationPercent = command.RwaOptimisationPercent,
            TargetCarPercent = command.TargetCarPercent,
            Cet1CostPercent = command.Cet1CostPercent,
            At1CostPercent = command.At1CostPercent,
            Tier2CostPercent = command.Tier2CostPercent,
            MaxAt1SharePercent = command.MaxAt1SharePercent,
            MaxTier2SharePercent = command.MaxTier2SharePercent,
            StepPercent = command.StepPercent,
            SavedAtUtc = savedAtUtc,
            CreatedAt = savedAtUtc
        });

        await _db.SaveChangesAsync(ct);

        return new CapitalPlanningScenarioState
        {
            CurrentCarPercent = existing.CurrentCarPercent,
            CurrentRwaBn = existing.CurrentRwaBn,
            QuarterlyRwaGrowthPercent = existing.QuarterlyRwaGrowthPercent,
            QuarterlyRetainedEarningsBn = existing.QuarterlyRetainedEarningsBn,
            CapitalActionBn = existing.CapitalActionBn,
            MinimumRequirementPercent = existing.MinimumRequirementPercent,
            ConservationBufferPercent = existing.ConservationBufferPercent,
            CountercyclicalBufferPercent = existing.CountercyclicalBufferPercent,
            DsibBufferPercent = existing.DsibBufferPercent,
            RwaOptimisationPercent = existing.RwaOptimisationPercent,
            TargetCarPercent = existing.TargetCarPercent,
            Cet1CostPercent = existing.Cet1CostPercent,
            At1CostPercent = existing.At1CostPercent,
            Tier2CostPercent = existing.Tier2CostPercent,
            MaxAt1SharePercent = existing.MaxAt1SharePercent,
            MaxTier2SharePercent = existing.MaxTier2SharePercent,
            StepPercent = existing.StepPercent,
            SavedAtUtc = existing.SavedAtUtc
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

            IF OBJECT_ID(N'[meta].[capital_planning_scenarios]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[capital_planning_scenarios]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ScenarioKey] NVARCHAR(80) NOT NULL,
                    [CurrentCarPercent] DECIMAL(18,4) NOT NULL,
                    [CurrentRwaBn] DECIMAL(18,4) NOT NULL,
                    [QuarterlyRwaGrowthPercent] DECIMAL(18,4) NOT NULL,
                    [QuarterlyRetainedEarningsBn] DECIMAL(18,4) NOT NULL,
                    [CapitalActionBn] DECIMAL(18,4) NOT NULL,
                    [MinimumRequirementPercent] DECIMAL(18,4) NOT NULL,
                    [ConservationBufferPercent] DECIMAL(18,4) NOT NULL,
                    [CountercyclicalBufferPercent] DECIMAL(18,4) NOT NULL,
                    [DsibBufferPercent] DECIMAL(18,4) NOT NULL,
                    [RwaOptimisationPercent] DECIMAL(18,4) NOT NULL,
                    [TargetCarPercent] DECIMAL(18,4) NOT NULL,
                    [Cet1CostPercent] DECIMAL(18,4) NOT NULL,
                    [At1CostPercent] DECIMAL(18,4) NOT NULL,
                    [Tier2CostPercent] DECIMAL(18,4) NOT NULL,
                    [MaxAt1SharePercent] DECIMAL(18,4) NOT NULL,
                    [MaxTier2SharePercent] DECIMAL(18,4) NOT NULL,
                    [StepPercent] DECIMAL(18,4) NOT NULL,
                    [SavedAtUtc] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_capital_planning_scenarios_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_capital_planning_scenarios_ScenarioKey]
                    ON [meta].[capital_planning_scenarios]([ScenarioKey]);
                CREATE INDEX [IX_capital_planning_scenarios_SavedAtUtc]
                    ON [meta].[capital_planning_scenarios]([SavedAtUtc]);
            END;

            IF OBJECT_ID(N'[meta].[capital_planning_scenario_history]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[capital_planning_scenario_history]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [CurrentCarPercent] DECIMAL(18,4) NOT NULL,
                    [CurrentRwaBn] DECIMAL(18,4) NOT NULL,
                    [QuarterlyRwaGrowthPercent] DECIMAL(18,4) NOT NULL,
                    [QuarterlyRetainedEarningsBn] DECIMAL(18,4) NOT NULL,
                    [CapitalActionBn] DECIMAL(18,4) NOT NULL,
                    [MinimumRequirementPercent] DECIMAL(18,4) NOT NULL,
                    [ConservationBufferPercent] DECIMAL(18,4) NOT NULL,
                    [CountercyclicalBufferPercent] DECIMAL(18,4) NOT NULL,
                    [DsibBufferPercent] DECIMAL(18,4) NOT NULL,
                    [RwaOptimisationPercent] DECIMAL(18,4) NOT NULL,
                    [TargetCarPercent] DECIMAL(18,4) NOT NULL,
                    [Cet1CostPercent] DECIMAL(18,4) NOT NULL,
                    [At1CostPercent] DECIMAL(18,4) NOT NULL,
                    [Tier2CostPercent] DECIMAL(18,4) NOT NULL,
                    [MaxAt1SharePercent] DECIMAL(18,4) NOT NULL,
                    [MaxTier2SharePercent] DECIMAL(18,4) NOT NULL,
                    [StepPercent] DECIMAL(18,4) NOT NULL,
                    [SavedAtUtc] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_capital_planning_scenario_history_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX [IX_capital_planning_scenario_history_SavedAtUtc]
                    ON [meta].[capital_planning_scenario_history]([SavedAtUtc]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }
}

public sealed class CapitalPlanningScenarioState
{
    public decimal CurrentCarPercent { get; init; }
    public decimal CurrentRwaBn { get; init; }
    public decimal QuarterlyRwaGrowthPercent { get; init; }
    public decimal QuarterlyRetainedEarningsBn { get; init; }
    public decimal CapitalActionBn { get; init; }
    public decimal MinimumRequirementPercent { get; init; }
    public decimal ConservationBufferPercent { get; init; }
    public decimal CountercyclicalBufferPercent { get; init; }
    public decimal DsibBufferPercent { get; init; }
    public decimal RwaOptimisationPercent { get; init; }
    public decimal TargetCarPercent { get; init; }
    public decimal Cet1CostPercent { get; init; }
    public decimal At1CostPercent { get; init; }
    public decimal Tier2CostPercent { get; init; }
    public decimal MaxAt1SharePercent { get; init; }
    public decimal MaxTier2SharePercent { get; init; }
    public decimal StepPercent { get; init; }
    public DateTime SavedAtUtc { get; init; }
}

public sealed class CapitalPlanningScenarioHistoryState
{
    public int HistoryId { get; init; }
    public decimal CurrentCarPercent { get; init; }
    public decimal CurrentRwaBn { get; init; }
    public decimal QuarterlyRwaGrowthPercent { get; init; }
    public decimal QuarterlyRetainedEarningsBn { get; init; }
    public decimal CapitalActionBn { get; init; }
    public decimal MinimumRequirementPercent { get; init; }
    public decimal ConservationBufferPercent { get; init; }
    public decimal CountercyclicalBufferPercent { get; init; }
    public decimal DsibBufferPercent { get; init; }
    public decimal RwaOptimisationPercent { get; init; }
    public decimal TargetCarPercent { get; init; }
    public decimal Cet1CostPercent { get; init; }
    public decimal At1CostPercent { get; init; }
    public decimal Tier2CostPercent { get; init; }
    public decimal MaxAt1SharePercent { get; init; }
    public decimal MaxTier2SharePercent { get; init; }
    public decimal StepPercent { get; init; }
    public DateTime SavedAtUtc { get; init; }
}

public sealed class CapitalPlanningScenarioCommand
{
    public decimal CurrentCarPercent { get; init; }
    public decimal CurrentRwaBn { get; init; }
    public decimal QuarterlyRwaGrowthPercent { get; init; }
    public decimal QuarterlyRetainedEarningsBn { get; init; }
    public decimal CapitalActionBn { get; init; }
    public decimal MinimumRequirementPercent { get; init; }
    public decimal ConservationBufferPercent { get; init; }
    public decimal CountercyclicalBufferPercent { get; init; }
    public decimal DsibBufferPercent { get; init; }
    public decimal RwaOptimisationPercent { get; init; }
    public decimal TargetCarPercent { get; init; }
    public decimal Cet1CostPercent { get; init; }
    public decimal At1CostPercent { get; init; }
    public decimal Tier2CostPercent { get; init; }
    public decimal MaxAt1SharePercent { get; init; }
    public decimal MaxTier2SharePercent { get; init; }
    public decimal StepPercent { get; init; }
    public DateTime SavedAtUtc { get; init; }
}
