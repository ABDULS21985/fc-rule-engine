using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class PlatformOperationsCatalogService
{
    private readonly MetadataDbContext _db;

    public PlatformOperationsCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<PlatformOperationsCatalogState> MaterializeAsync(
        PlatformOperationsCatalogInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var interventionRecords = input.Interventions
            .Select(x => new PlatformInterventionRecord
            {
                Domain = x.Domain,
                Subject = x.Subject,
                Signal = x.Signal,
                Priority = x.Priority,
                NextAction = x.NextAction,
                DueDate = x.DueDate,
                OwnerLane = x.OwnerLane,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        var timelineRecords = input.Timeline
            .Select(x => new PlatformActivityTimelineRecord
            {
                TenantId = x.TenantId,
                InstitutionId = x.InstitutionId,
                Domain = x.Domain,
                Title = x.Title,
                Detail = x.Detail,
                HappenedAt = x.HappenedAt,
                Severity = x.Severity,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        await ClearExistingCatalogAsync(ct);

        _db.PlatformInterventions.AddRange(interventionRecords);
        _db.PlatformActivityTimeline.AddRange(timelineRecords);
        await _db.SaveChangesAsync(ct);

        return new PlatformOperationsCatalogState
        {
            MaterializedAt = materializedAt,
            Interventions = interventionRecords
                .OrderByDescending(x => InterventionPriorityRank(x.Priority))
                .ThenBy(x => x.DueDate)
                .Select(MapIntervention)
                .ToList(),
            Timeline = timelineRecords
                .OrderByDescending(x => x.HappenedAt)
                .ThenByDescending(x => SeverityRank(x.Severity))
                .Select(MapTimeline)
                .ToList()
        };
    }

    public async Task<PlatformOperationsCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var interventions = await _db.PlatformInterventions
            .AsNoTracking()
            .OrderByDescending(x => x.DueDate)
            .ToListAsync(ct);

        var timeline = await _db.PlatformActivityTimeline
            .AsNoTracking()
            .OrderByDescending(x => x.HappenedAt)
            .ToListAsync(ct);

        var materializedAt = interventions
            .Select(x => (DateTime?)x.MaterializedAt)
            .Concat(timeline.Select(x => (DateTime?)x.MaterializedAt))
            .OrderByDescending(x => x)
            .FirstOrDefault();

        return new PlatformOperationsCatalogState
        {
            MaterializedAt = materializedAt,
            Interventions = interventions
                .OrderByDescending(x => InterventionPriorityRank(x.Priority))
                .ThenBy(x => x.DueDate)
                .Select(MapIntervention)
                .ToList(),
            Timeline = timeline
                .OrderByDescending(x => x.HappenedAt)
                .ThenByDescending(x => SeverityRank(x.Severity))
                .Select(MapTimeline)
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

            IF OBJECT_ID(N'[meta].[platform_interventions]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[platform_interventions]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [Domain] NVARCHAR(80) NOT NULL,
                    [Subject] NVARCHAR(240) NOT NULL,
                    [Signal] NVARCHAR(600) NOT NULL,
                    [Priority] NVARCHAR(30) NOT NULL,
                    [NextAction] NVARCHAR(1200) NOT NULL,
                    [DueDate] DATETIME2 NOT NULL,
                    [OwnerLane] NVARCHAR(120) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_platform_interventions_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX [IX_platform_interventions_Priority]
                    ON [meta].[platform_interventions]([Priority]);
                CREATE INDEX [IX_platform_interventions_DueDate]
                    ON [meta].[platform_interventions]([DueDate]);
                CREATE INDEX [IX_platform_interventions_MaterializedAt]
                    ON [meta].[platform_interventions]([MaterializedAt]);
            END;

            IF OBJECT_ID(N'[meta].[platform_activity_timeline]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[platform_activity_timeline]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [TenantId] UNIQUEIDENTIFIER NULL,
                    [InstitutionId] INT NULL,
                    [Domain] NVARCHAR(80) NOT NULL,
                    [Title] NVARCHAR(240) NOT NULL,
                    [Detail] NVARCHAR(1200) NOT NULL,
                    [HappenedAt] DATETIME2 NOT NULL,
                    [Severity] NVARCHAR(30) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_platform_activity_timeline_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX [IX_platform_activity_timeline_TenantId]
                    ON [meta].[platform_activity_timeline]([TenantId]);
                CREATE INDEX [IX_platform_activity_timeline_InstitutionId]
                    ON [meta].[platform_activity_timeline]([InstitutionId]);
                CREATE INDEX [IX_platform_activity_timeline_HappenedAt]
                    ON [meta].[platform_activity_timeline]([HappenedAt]);
                CREATE INDEX [IX_platform_activity_timeline_Severity]
                    ON [meta].[platform_activity_timeline]([Severity]);
                CREATE INDEX [IX_platform_activity_timeline_MaterializedAt]
                    ON [meta].[platform_activity_timeline]([MaterializedAt]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[platform_interventions];", ct);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[platform_activity_timeline];", ct);
            return;
        }

        var interventions = await _db.PlatformInterventions.ToListAsync(ct);
        var timeline = await _db.PlatformActivityTimeline.ToListAsync(ct);

        if (interventions.Count > 0)
        {
            _db.PlatformInterventions.RemoveRange(interventions);
        }

        if (timeline.Count > 0)
        {
            _db.PlatformActivityTimeline.RemoveRange(timeline);
        }

        if (interventions.Count > 0 || timeline.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private static PlatformInterventionState MapIntervention(PlatformInterventionRecord record) =>
        new()
        {
            Domain = record.Domain,
            Subject = record.Subject,
            Signal = record.Signal,
            Priority = record.Priority,
            NextAction = record.NextAction,
            DueDate = record.DueDate,
            OwnerLane = record.OwnerLane,
            MaterializedAt = record.MaterializedAt
        };

    private static PlatformActivityTimelineState MapTimeline(PlatformActivityTimelineRecord record) =>
        new()
        {
            TenantId = record.TenantId,
            InstitutionId = record.InstitutionId,
            Domain = record.Domain,
            Title = record.Title,
            Detail = record.Detail,
            HappenedAt = record.HappenedAt,
            Severity = record.Severity,
            MaterializedAt = record.MaterializedAt
        };

    private static int InterventionPriorityRank(string priority) => priority switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };

    private static int SeverityRank(string severity) => severity switch
    {
        "Critical" => 4,
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };
}

public sealed class PlatformOperationsCatalogInput
{
    public IReadOnlyList<PlatformInterventionInput> Interventions { get; init; } = [];
    public IReadOnlyList<PlatformActivityTimelineInput> Timeline { get; init; } = [];
}

public sealed class PlatformInterventionInput
{
    public string Domain { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Signal { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
    public DateTime DueDate { get; init; }
    public string OwnerLane { get; init; } = string.Empty;
}

public sealed class PlatformActivityTimelineInput
{
    public Guid? TenantId { get; init; }
    public int? InstitutionId { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public DateTime HappenedAt { get; init; }
    public string Severity { get; init; } = string.Empty;
}

public sealed class PlatformOperationsCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public List<PlatformInterventionState> Interventions { get; init; } = [];
    public List<PlatformActivityTimelineState> Timeline { get; init; } = [];
}

public sealed class PlatformInterventionState
{
    public string Domain { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Signal { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
    public DateTime DueDate { get; init; }
    public string OwnerLane { get; init; } = string.Empty;
    public DateTime MaterializedAt { get; init; }
}

public sealed class PlatformActivityTimelineState
{
    public Guid? TenantId { get; init; }
    public int? InstitutionId { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public DateTime HappenedAt { get; init; }
    public string Severity { get; init; } = string.Empty;
    public DateTime MaterializedAt { get; init; }
}
