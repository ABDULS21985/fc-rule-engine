using System.Text.Json;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class ModelInventoryCatalogService
{
    private readonly MetadataDbContext _db;

    public ModelInventoryCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<ModelInventoryCatalogState> MaterializeAsync(
        IReadOnlyList<ModelInventoryDefinitionInput> definitions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var records = definitions
            .Select(x => new ModelInventoryDefinitionRecord
            {
                ModelCode = x.ModelCode,
                ModelName = x.ModelName,
                Tier = x.Tier,
                Owner = x.Owner,
                ReturnHint = x.ReturnHint,
                MatchTermsJson = JsonSerializer.Serialize(x.MatchTerms),
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        await ClearExistingCatalogAsync(ct);

        _db.ModelInventoryDefinitions.AddRange(records);
        await _db.SaveChangesAsync(ct);

        return new ModelInventoryCatalogState
        {
            MaterializedAt = materializedAt,
            Definitions = records
                .OrderBy(x => x.ModelCode, StringComparer.OrdinalIgnoreCase)
                .Select(MapState)
                .ToList()
        };
    }

    public async Task<ModelInventoryCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var records = await _db.ModelInventoryDefinitions
            .AsNoTracking()
            .OrderBy(x => x.ModelCode)
            .ToListAsync(ct);

        return new ModelInventoryCatalogState
        {
            MaterializedAt = records
                .OrderByDescending(x => x.MaterializedAt)
                .Select(x => (DateTime?)x.MaterializedAt)
                .FirstOrDefault(),
            Definitions = records.Select(MapState).ToList()
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

            IF OBJECT_ID(N'[meta].[model_inventory_definitions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[model_inventory_definitions]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ModelCode] NVARCHAR(40) NOT NULL,
                    [ModelName] NVARCHAR(240) NOT NULL,
                    [Tier] NVARCHAR(40) NOT NULL,
                    [Owner] NVARCHAR(120) NOT NULL,
                    [ReturnHint] NVARCHAR(120) NOT NULL,
                    [MatchTermsJson] NVARCHAR(4000) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_model_inventory_definitions_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_model_inventory_definitions_ModelCode]
                    ON [meta].[model_inventory_definitions]([ModelCode]);
                CREATE INDEX [IX_model_inventory_definitions_Tier]
                    ON [meta].[model_inventory_definitions]([Tier]);
                CREATE INDEX [IX_model_inventory_definitions_MaterializedAt]
                    ON [meta].[model_inventory_definitions]([MaterializedAt]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[model_inventory_definitions];", ct);
            return;
        }

        var existing = await _db.ModelInventoryDefinitions.ToListAsync(ct);
        if (existing.Count == 0)
        {
            return;
        }

        _db.ModelInventoryDefinitions.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);
    }

    private static ModelInventoryDefinitionState MapState(ModelInventoryDefinitionRecord record) =>
        new()
        {
            ModelCode = record.ModelCode,
            ModelName = record.ModelName,
            Tier = record.Tier,
            Owner = record.Owner,
            ReturnHint = record.ReturnHint,
            MatchTerms = JsonSerializer.Deserialize<List<string>>(record.MatchTermsJson) ?? [],
            MaterializedAt = record.MaterializedAt
        };
}

public sealed class ModelInventoryCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public List<ModelInventoryDefinitionState> Definitions { get; init; } = [];
}

public sealed class ModelInventoryDefinitionInput
{
    public string ModelCode { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string ReturnHint { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchTerms { get; init; } = [];
}

public sealed class ModelInventoryDefinitionState
{
    public string ModelCode { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string Tier { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string ReturnHint { get; init; } = string.Empty;
    public List<string> MatchTerms { get; init; } = [];
    public DateTime MaterializedAt { get; init; }
}
