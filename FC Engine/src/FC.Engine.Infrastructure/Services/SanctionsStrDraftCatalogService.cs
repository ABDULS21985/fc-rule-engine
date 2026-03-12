using System.Text.Json;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class SanctionsStrDraftCatalogService
{
    private readonly MetadataDbContext _db;

    public SanctionsStrDraftCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<SanctionsStrDraftCatalogState> MaterializeAsync(
        IReadOnlyList<SanctionsStrDraftInput> drafts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var records = drafts
            .Select(x => new SanctionsStrDraftRecord
            {
                DraftId = x.DraftId,
                Subject = x.Subject,
                MatchedName = x.MatchedName,
                SourceCode = x.SourceCode,
                SourceName = x.SourceName,
                RiskLevel = x.RiskLevel,
                Decision = x.Decision,
                Status = x.Status,
                Priority = x.Priority,
                ScorePercent = x.ScorePercent,
                FreezeRecommended = x.FreezeRecommended,
                ScreenedAtUtc = x.ScreenedAtUtc,
                ReviewDueAtUtc = x.ReviewDueAtUtc,
                SuspicionBasis = x.SuspicionBasis,
                GoAmlPayloadSummary = x.GoAmlPayloadSummary,
                Narrative = x.Narrative,
                RecommendedActionsJson = JsonSerializer.Serialize(x.RecommendedActions ?? []),
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        await ClearExistingCatalogAsync(ct);

        _db.SanctionsStrDrafts.AddRange(records);
        await _db.SaveChangesAsync(ct);

        return new SanctionsStrDraftCatalogState
        {
            MaterializedAt = materializedAt,
            Drafts = records
                .OrderByDescending(x => PriorityRank(x.Priority))
                .ThenByDescending(x => x.ScorePercent)
                .ThenBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
                .Select(MapState)
                .ToList()
        };
    }

    public async Task<SanctionsStrDraftCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var records = await _db.SanctionsStrDrafts
            .AsNoTracking()
            .OrderByDescending(x => x.ScorePercent)
            .ThenBy(x => x.Subject)
            .ToListAsync(ct);

        return new SanctionsStrDraftCatalogState
        {
            MaterializedAt = records
                .OrderByDescending(x => x.MaterializedAt)
                .Select(x => (DateTime?)x.MaterializedAt)
                .FirstOrDefault(),
            Drafts = records
                .OrderByDescending(x => PriorityRank(x.Priority))
                .ThenByDescending(x => x.ScorePercent)
                .ThenBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
                .Select(MapState)
                .ToList()
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

            IF OBJECT_ID(N'[meta].[sanctions_str_drafts]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[sanctions_str_drafts]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [DraftId] NVARCHAR(80) NOT NULL,
                    [Subject] NVARCHAR(240) NOT NULL,
                    [MatchedName] NVARCHAR(240) NOT NULL,
                    [SourceCode] NVARCHAR(80) NOT NULL,
                    [SourceName] NVARCHAR(240) NOT NULL,
                    [RiskLevel] NVARCHAR(30) NOT NULL,
                    [Decision] NVARCHAR(40) NOT NULL,
                    [Status] NVARCHAR(40) NOT NULL,
                    [Priority] NVARCHAR(30) NOT NULL,
                    [ScorePercent] DECIMAL(9,2) NOT NULL,
                    [FreezeRecommended] BIT NOT NULL,
                    [ScreenedAtUtc] DATETIME2 NOT NULL,
                    [ReviewDueAtUtc] DATETIME2 NOT NULL,
                    [SuspicionBasis] NVARCHAR(1600) NOT NULL,
                    [GoAmlPayloadSummary] NVARCHAR(1600) NOT NULL,
                    [Narrative] NVARCHAR(MAX) NOT NULL,
                    [RecommendedActionsJson] NVARCHAR(MAX) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_sanctions_str_drafts_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_sanctions_str_drafts_DraftId]
                    ON [meta].[sanctions_str_drafts]([DraftId]);
                CREATE INDEX [IX_sanctions_str_drafts_Status]
                    ON [meta].[sanctions_str_drafts]([Status]);
                CREATE INDEX [IX_sanctions_str_drafts_Priority]
                    ON [meta].[sanctions_str_drafts]([Priority]);
                CREATE INDEX [IX_sanctions_str_drafts_MaterializedAt]
                    ON [meta].[sanctions_str_drafts]([MaterializedAt]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[sanctions_str_drafts];", ct);
            return;
        }

        var existing = await _db.SanctionsStrDrafts.ToListAsync(ct);
        if (existing.Count == 0)
        {
            return;
        }

        _db.SanctionsStrDrafts.RemoveRange(existing);
        await _db.SaveChangesAsync(ct);
    }

    private static SanctionsStrDraftState MapState(SanctionsStrDraftRecord record) =>
        new()
        {
            DraftId = record.DraftId,
            Subject = record.Subject,
            MatchedName = record.MatchedName,
            SourceCode = record.SourceCode,
            SourceName = record.SourceName,
            RiskLevel = record.RiskLevel,
            Decision = record.Decision,
            Status = record.Status,
            Priority = record.Priority,
            ScorePercent = record.ScorePercent,
            FreezeRecommended = record.FreezeRecommended,
            ScreenedAtUtc = record.ScreenedAtUtc,
            ReviewDueAtUtc = record.ReviewDueAtUtc,
            SuspicionBasis = record.SuspicionBasis,
            GoAmlPayloadSummary = record.GoAmlPayloadSummary,
            Narrative = record.Narrative,
            RecommendedActions = DeserializeActions(record.RecommendedActionsJson),
            MaterializedAt = record.MaterializedAt
        };

    private static List<string> DeserializeActions(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int PriorityRank(string priority) => priority switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };
}

public sealed class SanctionsStrDraftInput
{
    public string DraftId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string MatchedName { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public decimal ScorePercent { get; init; }
    public bool FreezeRecommended { get; init; }
    public DateTime ScreenedAtUtc { get; init; }
    public DateTime ReviewDueAtUtc { get; init; }
    public string SuspicionBasis { get; init; } = string.Empty;
    public string GoAmlPayloadSummary { get; init; } = string.Empty;
    public string Narrative { get; init; } = string.Empty;
    public IReadOnlyList<string> RecommendedActions { get; init; } = [];
}

public sealed class SanctionsStrDraftCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public List<SanctionsStrDraftState> Drafts { get; init; } = [];
}

public sealed class SanctionsStrDraftState
{
    public string DraftId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string MatchedName { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public decimal ScorePercent { get; init; }
    public bool FreezeRecommended { get; init; }
    public DateTime ScreenedAtUtc { get; init; }
    public DateTime ReviewDueAtUtc { get; init; }
    public string SuspicionBasis { get; init; } = string.Empty;
    public string GoAmlPayloadSummary { get; init; } = string.Empty;
    public string Narrative { get; init; } = string.Empty;
    public List<string> RecommendedActions { get; init; } = [];
    public DateTime MaterializedAt { get; init; }
}
