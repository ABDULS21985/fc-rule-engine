using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class KnowledgeGraphDossierCatalogService
{
    private readonly MetadataDbContext _db;

    public KnowledgeGraphDossierCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<KnowledgeGraphDossierCatalogState> MaterializeAsync(
        IReadOnlyList<KnowledgeGraphDossierSectionInput> rows,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var records = rows
            .Select(x => new KnowledgeGraphDossierSectionRecord
            {
                SectionCode = x.SectionCode,
                SectionName = x.SectionName,
                RowCount = x.RowCount,
                Signal = x.Signal,
                Coverage = x.Coverage,
                Commentary = x.Commentary,
                RecommendedAction = x.RecommendedAction,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        await ClearExistingCatalogAsync(ct);

        _db.KnowledgeGraphDossierSections.AddRange(records);
        await _db.SaveChangesAsync(ct);

        return new KnowledgeGraphDossierCatalogState
        {
            MaterializedAt = materializedAt,
            Sections = records
                .OrderBy(x => x.SectionCode, StringComparer.OrdinalIgnoreCase)
                .Select(MapState)
                .ToList()
        };
    }

    public async Task<KnowledgeGraphDossierCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var records = await _db.KnowledgeGraphDossierSections
            .AsNoTracking()
            .OrderBy(x => x.SectionCode)
            .ToListAsync(ct);

        return new KnowledgeGraphDossierCatalogState
        {
            MaterializedAt = records
                .OrderByDescending(x => x.MaterializedAt)
                .Select(x => (DateTime?)x.MaterializedAt)
                .FirstOrDefault(),
            Sections = records.Select(MapState).ToList()
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

            IF OBJECT_ID(N'[meta].[knowledge_graph_dossier_sections]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[knowledge_graph_dossier_sections]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [SectionCode] NVARCHAR(40) NOT NULL,
                    [SectionName] NVARCHAR(240) NOT NULL,
                    [RowCount] INT NOT NULL,
                    [Signal] NVARCHAR(30) NOT NULL,
                    [Coverage] NVARCHAR(600) NOT NULL,
                    [Commentary] NVARCHAR(1200) NOT NULL,
                    [RecommendedAction] NVARCHAR(1200) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_knowledge_graph_dossier_sections_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_knowledge_graph_dossier_sections_SectionCode]
                    ON [meta].[knowledge_graph_dossier_sections]([SectionCode]);
                CREATE INDEX [IX_knowledge_graph_dossier_sections_Signal]
                    ON [meta].[knowledge_graph_dossier_sections]([Signal]);
                CREATE INDEX [IX_knowledge_graph_dossier_sections_MaterializedAt]
                    ON [meta].[knowledge_graph_dossier_sections]([MaterializedAt]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[knowledge_graph_dossier_sections];", ct);
            return;
        }

        var existing = await _db.KnowledgeGraphDossierSections.ToListAsync(ct);
        if (existing.Count == 0)
        {
            return;
        }

        _db.KnowledgeGraphDossierSections.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);
    }

    private static KnowledgeGraphDossierSectionState MapState(KnowledgeGraphDossierSectionRecord record) =>
        new()
        {
            SectionCode = record.SectionCode,
            SectionName = record.SectionName,
            RowCount = record.RowCount,
            Signal = record.Signal,
            Coverage = record.Coverage,
            Commentary = record.Commentary,
            RecommendedAction = record.RecommendedAction,
            MaterializedAt = record.MaterializedAt
        };
}

public sealed class KnowledgeGraphDossierCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public List<KnowledgeGraphDossierSectionState> Sections { get; init; } = [];
}

public sealed class KnowledgeGraphDossierSectionInput
{
    public string SectionCode { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public string Signal { get; init; } = string.Empty;
    public string Coverage { get; init; } = string.Empty;
    public string Commentary { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
}

public sealed class KnowledgeGraphDossierSectionState
{
    public string SectionCode { get; init; } = string.Empty;
    public string SectionName { get; init; } = string.Empty;
    public int RowCount { get; init; }
    public string Signal { get; init; } = string.Empty;
    public string Coverage { get; init; } = string.Empty;
    public string Commentary { get; init; } = string.Empty;
    public string RecommendedAction { get; init; } = string.Empty;
    public DateTime MaterializedAt { get; init; }
}
