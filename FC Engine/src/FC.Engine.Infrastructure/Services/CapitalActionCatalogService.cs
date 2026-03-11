using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class CapitalActionCatalogService
{
    private readonly MetadataDbContext _db;

    public CapitalActionCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<CapitalActionCatalogState> MaterializeAsync(
        IReadOnlyList<CapitalActionTemplateInput> templates,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(templates);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var records = templates
            .Select(x => new CapitalActionTemplateRecord
            {
                Code = x.Code,
                Title = x.Title,
                Summary = x.Summary,
                PrimaryLever = x.PrimaryLever,
                CapitalActionBn = x.CapitalActionBn,
                RwaOptimisationPercent = x.RwaOptimisationPercent,
                QuarterlyRetainedEarningsDeltaBn = x.QuarterlyRetainedEarningsDeltaBn,
                EstimatedAnnualCostPercent = x.EstimatedAnnualCostPercent,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        await ClearExistingCatalogAsync(ct);

        _db.CapitalActionTemplates.AddRange(records);
        await _db.SaveChangesAsync(ct);

        return new CapitalActionCatalogState
        {
            MaterializedAt = materializedAt,
            Templates = records
                .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
                .Select(MapState)
                .ToList()
        };
    }

    public async Task<CapitalActionCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var records = await _db.CapitalActionTemplates
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .ToListAsync(ct);

        return new CapitalActionCatalogState
        {
            MaterializedAt = records
                .OrderByDescending(x => x.MaterializedAt)
                .Select(x => (DateTime?)x.MaterializedAt)
                .FirstOrDefault(),
            Templates = records.Select(MapState).ToList()
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

            IF OBJECT_ID(N'[meta].[capital_action_templates]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[capital_action_templates]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [Code] NVARCHAR(40) NOT NULL,
                    [Title] NVARCHAR(240) NOT NULL,
                    [Summary] NVARCHAR(1200) NOT NULL,
                    [PrimaryLever] NVARCHAR(40) NOT NULL,
                    [CapitalActionBn] DECIMAL(18,4) NOT NULL,
                    [RwaOptimisationPercent] DECIMAL(18,4) NOT NULL,
                    [QuarterlyRetainedEarningsDeltaBn] DECIMAL(18,4) NOT NULL,
                    [EstimatedAnnualCostPercent] DECIMAL(18,4) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_capital_action_templates_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_capital_action_templates_Code]
                    ON [meta].[capital_action_templates]([Code]);
                CREATE INDEX [IX_capital_action_templates_PrimaryLever]
                    ON [meta].[capital_action_templates]([PrimaryLever]);
                CREATE INDEX [IX_capital_action_templates_MaterializedAt]
                    ON [meta].[capital_action_templates]([MaterializedAt]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[capital_action_templates];", ct);
            return;
        }

        var existing = await _db.CapitalActionTemplates.ToListAsync(ct);
        if (existing.Count == 0)
        {
            return;
        }

        _db.CapitalActionTemplates.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);
    }

    private static CapitalActionTemplateState MapState(CapitalActionTemplateRecord record) =>
        new()
        {
            Code = record.Code,
            Title = record.Title,
            Summary = record.Summary,
            PrimaryLever = record.PrimaryLever,
            CapitalActionBn = record.CapitalActionBn,
            RwaOptimisationPercent = record.RwaOptimisationPercent,
            QuarterlyRetainedEarningsDeltaBn = record.QuarterlyRetainedEarningsDeltaBn,
            EstimatedAnnualCostPercent = record.EstimatedAnnualCostPercent,
            MaterializedAt = record.MaterializedAt
        };
}

public sealed class CapitalActionCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public List<CapitalActionTemplateState> Templates { get; init; } = [];
}

public sealed class CapitalActionTemplateInput
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string PrimaryLever { get; init; } = string.Empty;
    public decimal CapitalActionBn { get; init; }
    public decimal RwaOptimisationPercent { get; init; }
    public decimal QuarterlyRetainedEarningsDeltaBn { get; init; }
    public decimal EstimatedAnnualCostPercent { get; init; }
}

public sealed class CapitalActionTemplateState
{
    public string Code { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string PrimaryLever { get; init; } = string.Empty;
    public decimal CapitalActionBn { get; init; }
    public decimal RwaOptimisationPercent { get; init; }
    public decimal QuarterlyRetainedEarningsDeltaBn { get; init; }
    public decimal EstimatedAnnualCostPercent { get; init; }
    public DateTime MaterializedAt { get; init; }
}
