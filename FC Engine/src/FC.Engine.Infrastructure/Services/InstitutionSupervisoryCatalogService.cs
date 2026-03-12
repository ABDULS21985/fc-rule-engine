using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class InstitutionSupervisoryCatalogService
{
    private readonly MetadataDbContext _db;

    public InstitutionSupervisoryCatalogService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task<InstitutionSupervisoryCatalogState> MaterializeAsync(
        InstitutionSupervisoryCatalogInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        await EnsureStoreAsync(ct);

        var materializedAt = DateTime.UtcNow;
        var scorecardRecords = input.Scorecards
            .Select(x => new InstitutionSupervisoryScorecardRecord
            {
                InstitutionId = x.InstitutionId,
                TenantId = x.TenantId,
                InstitutionName = x.InstitutionName,
                LicenceType = x.LicenceType,
                OverdueObligations = x.OverdueObligations,
                DueSoonObligations = x.DueSoonObligations,
                CapitalScore = x.CapitalScore,
                OpenResilienceIncidents = x.OpenResilienceIncidents,
                OpenSecurityAlerts = x.OpenSecurityAlerts,
                ModelReviewItems = x.ModelReviewItems,
                Priority = x.Priority,
                Summary = x.Summary,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        var detailRecords = input.Details
            .Select(x => new InstitutionSupervisoryDetailRecord
            {
                InstitutionId = x.InstitutionId,
                TenantId = x.TenantId,
                InstitutionName = x.InstitutionName,
                InstitutionCode = x.InstitutionCode,
                LicenceType = x.LicenceType,
                Priority = x.Priority,
                Summary = x.Summary,
                CapitalScore = x.CapitalScore,
                CapitalAlert = x.CapitalAlert,
                OverdueObligations = x.OverdueObligations,
                DueSoonObligations = x.DueSoonObligations,
                OpenResilienceIncidents = x.OpenResilienceIncidents,
                OpenSecurityAlerts = x.OpenSecurityAlerts,
                ModelReviewItems = x.ModelReviewItems,
                TopObligationsJson = x.TopObligationsJson,
                RecentSubmissionsJson = x.RecentSubmissionsJson,
                RecentActivityJson = x.RecentActivityJson,
                MaterializedAt = materializedAt,
                CreatedAt = materializedAt
            })
            .ToList();

        await ClearExistingCatalogAsync(ct);

        _db.InstitutionSupervisoryScorecards.AddRange(scorecardRecords);
        _db.InstitutionSupervisoryDetails.AddRange(detailRecords);
        await _db.SaveChangesAsync(ct);

        return new InstitutionSupervisoryCatalogState
        {
            MaterializedAt = materializedAt,
            Scorecards = scorecardRecords
                .OrderByDescending(x => PriorityRank(x.Priority))
                .ThenByDescending(x => x.OverdueObligations)
                .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
                .Select(MapScorecard)
                .ToList(),
            Details = detailRecords
                .OrderByDescending(x => PriorityRank(x.Priority))
                .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
                .Select(MapDetail)
                .ToList()
        };
    }

    public async Task<InstitutionSupervisoryCatalogState> LoadAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var scorecardRecords = await _db.InstitutionSupervisoryScorecards
            .AsNoTracking()
            .OrderByDescending(x => x.OverdueObligations)
            .ThenBy(x => x.InstitutionName)
            .ToListAsync(ct);

        var detailRecords = await _db.InstitutionSupervisoryDetails
            .AsNoTracking()
            .OrderBy(x => x.InstitutionName)
            .ToListAsync(ct);

        var materializedAt = scorecardRecords
            .Select(x => (DateTime?)x.MaterializedAt)
            .Concat(detailRecords.Select(x => (DateTime?)x.MaterializedAt))
            .OrderByDescending(x => x)
            .FirstOrDefault();

        return new InstitutionSupervisoryCatalogState
        {
            MaterializedAt = materializedAt,
            Scorecards = scorecardRecords
                .OrderByDescending(x => PriorityRank(x.Priority))
                .ThenByDescending(x => x.OverdueObligations)
                .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
                .Select(MapScorecard)
                .ToList(),
            Details = detailRecords
                .OrderByDescending(x => PriorityRank(x.Priority))
                .ThenBy(x => x.InstitutionName, StringComparer.OrdinalIgnoreCase)
                .Select(MapDetail)
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

            IF OBJECT_ID(N'[meta].[institution_supervisory_scorecards]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[institution_supervisory_scorecards]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [InstitutionId] INT NOT NULL,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [InstitutionName] NVARCHAR(240) NOT NULL,
                    [LicenceType] NVARCHAR(120) NOT NULL,
                    [OverdueObligations] INT NOT NULL,
                    [DueSoonObligations] INT NOT NULL,
                    [CapitalScore] DECIMAL(9,2) NULL,
                    [OpenResilienceIncidents] INT NOT NULL,
                    [OpenSecurityAlerts] INT NOT NULL,
                    [ModelReviewItems] INT NOT NULL,
                    [Priority] NVARCHAR(30) NOT NULL,
                    [Summary] NVARCHAR(1200) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_institution_supervisory_scorecards_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_institution_supervisory_scorecards_InstitutionId]
                    ON [meta].[institution_supervisory_scorecards]([InstitutionId]);
                CREATE INDEX [IX_institution_supervisory_scorecards_TenantId]
                    ON [meta].[institution_supervisory_scorecards]([TenantId]);
                CREATE INDEX [IX_institution_supervisory_scorecards_Priority]
                    ON [meta].[institution_supervisory_scorecards]([Priority]);
                CREATE INDEX [IX_institution_supervisory_scorecards_MaterializedAt]
                    ON [meta].[institution_supervisory_scorecards]([MaterializedAt]);
            END;

            IF OBJECT_ID(N'[meta].[institution_supervisory_details]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[institution_supervisory_details]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [InstitutionId] INT NOT NULL,
                    [TenantId] UNIQUEIDENTIFIER NOT NULL,
                    [InstitutionName] NVARCHAR(240) NOT NULL,
                    [InstitutionCode] NVARCHAR(80) NOT NULL,
                    [LicenceType] NVARCHAR(120) NOT NULL,
                    [Priority] NVARCHAR(30) NOT NULL,
                    [Summary] NVARCHAR(1200) NOT NULL,
                    [CapitalScore] DECIMAL(9,2) NULL,
                    [CapitalAlert] NVARCHAR(1200) NOT NULL,
                    [OverdueObligations] INT NOT NULL,
                    [DueSoonObligations] INT NOT NULL,
                    [OpenResilienceIncidents] INT NOT NULL,
                    [OpenSecurityAlerts] INT NOT NULL,
                    [ModelReviewItems] INT NOT NULL,
                    [TopObligationsJson] NVARCHAR(MAX) NOT NULL,
                    [RecentSubmissionsJson] NVARCHAR(MAX) NOT NULL,
                    [RecentActivityJson] NVARCHAR(MAX) NOT NULL,
                    [MaterializedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_institution_supervisory_details_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_institution_supervisory_details_InstitutionId]
                    ON [meta].[institution_supervisory_details]([InstitutionId]);
                CREATE INDEX [IX_institution_supervisory_details_TenantId]
                    ON [meta].[institution_supervisory_details]([TenantId]);
                CREATE INDEX [IX_institution_supervisory_details_Priority]
                    ON [meta].[institution_supervisory_details]([Priority]);
                CREATE INDEX [IX_institution_supervisory_details_MaterializedAt]
                    ON [meta].[institution_supervisory_details]([MaterializedAt]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task ClearExistingCatalogAsync(CancellationToken ct)
    {
        if (_db.Database.IsSqlServer())
        {
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[institution_supervisory_scorecards];", ct);
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM [meta].[institution_supervisory_details];", ct);
            return;
        }

        var scorecards = await _db.InstitutionSupervisoryScorecards.ToListAsync(ct);
        var details = await _db.InstitutionSupervisoryDetails.ToListAsync(ct);

        if (scorecards.Count > 0)
        {
            _db.InstitutionSupervisoryScorecards.RemoveRange(scorecards);
        }

        if (details.Count > 0)
        {
            _db.InstitutionSupervisoryDetails.RemoveRange(details);
        }

        if (scorecards.Count > 0 || details.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private static InstitutionSupervisoryScorecardState MapScorecard(InstitutionSupervisoryScorecardRecord record) =>
        new()
        {
            InstitutionId = record.InstitutionId,
            TenantId = record.TenantId,
            InstitutionName = record.InstitutionName,
            LicenceType = record.LicenceType,
            OverdueObligations = record.OverdueObligations,
            DueSoonObligations = record.DueSoonObligations,
            CapitalScore = record.CapitalScore,
            OpenResilienceIncidents = record.OpenResilienceIncidents,
            OpenSecurityAlerts = record.OpenSecurityAlerts,
            ModelReviewItems = record.ModelReviewItems,
            Priority = record.Priority,
            Summary = record.Summary,
            MaterializedAt = record.MaterializedAt
        };

    private static InstitutionSupervisoryDetailState MapDetail(InstitutionSupervisoryDetailRecord record) =>
        new()
        {
            InstitutionId = record.InstitutionId,
            TenantId = record.TenantId,
            InstitutionName = record.InstitutionName,
            InstitutionCode = record.InstitutionCode,
            LicenceType = record.LicenceType,
            Priority = record.Priority,
            Summary = record.Summary,
            CapitalScore = record.CapitalScore,
            CapitalAlert = record.CapitalAlert,
            OverdueObligations = record.OverdueObligations,
            DueSoonObligations = record.DueSoonObligations,
            OpenResilienceIncidents = record.OpenResilienceIncidents,
            OpenSecurityAlerts = record.OpenSecurityAlerts,
            ModelReviewItems = record.ModelReviewItems,
            TopObligationsJson = record.TopObligationsJson,
            RecentSubmissionsJson = record.RecentSubmissionsJson,
            RecentActivityJson = record.RecentActivityJson,
            MaterializedAt = record.MaterializedAt
        };

    private static int PriorityRank(string priority) => priority switch
    {
        "Critical" => 4,
        "High" => 3,
        "Watch" => 2,
        _ => 1
    };
}

public sealed class InstitutionSupervisoryCatalogInput
{
    public IReadOnlyList<InstitutionSupervisoryScorecardInput> Scorecards { get; init; } = [];
    public IReadOnlyList<InstitutionSupervisoryDetailInput> Details { get; init; } = [];
}

public sealed class InstitutionSupervisoryScorecardInput
{
    public int InstitutionId { get; init; }
    public Guid TenantId { get; init; }
    public string InstitutionName { get; init; } = string.Empty;
    public string LicenceType { get; init; } = string.Empty;
    public int OverdueObligations { get; init; }
    public int DueSoonObligations { get; init; }
    public decimal? CapitalScore { get; init; }
    public int OpenResilienceIncidents { get; init; }
    public int OpenSecurityAlerts { get; init; }
    public int ModelReviewItems { get; init; }
    public string Priority { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

public sealed class InstitutionSupervisoryDetailInput
{
    public int InstitutionId { get; init; }
    public Guid TenantId { get; init; }
    public string InstitutionName { get; init; } = string.Empty;
    public string InstitutionCode { get; init; } = string.Empty;
    public string LicenceType { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public decimal? CapitalScore { get; init; }
    public string CapitalAlert { get; init; } = string.Empty;
    public int OverdueObligations { get; init; }
    public int DueSoonObligations { get; init; }
    public int OpenResilienceIncidents { get; init; }
    public int OpenSecurityAlerts { get; init; }
    public int ModelReviewItems { get; init; }
    public string TopObligationsJson { get; init; } = "[]";
    public string RecentSubmissionsJson { get; init; } = "[]";
    public string RecentActivityJson { get; init; } = "[]";
}

public sealed class InstitutionSupervisoryCatalogState
{
    public DateTime? MaterializedAt { get; init; }
    public List<InstitutionSupervisoryScorecardState> Scorecards { get; init; } = [];
    public List<InstitutionSupervisoryDetailState> Details { get; init; } = [];
}

public sealed class InstitutionSupervisoryScorecardState
{
    public int InstitutionId { get; init; }
    public Guid TenantId { get; init; }
    public string InstitutionName { get; init; } = string.Empty;
    public string LicenceType { get; init; } = string.Empty;
    public int OverdueObligations { get; init; }
    public int DueSoonObligations { get; init; }
    public decimal? CapitalScore { get; init; }
    public int OpenResilienceIncidents { get; init; }
    public int OpenSecurityAlerts { get; init; }
    public int ModelReviewItems { get; init; }
    public string Priority { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DateTime MaterializedAt { get; init; }
}

public sealed class InstitutionSupervisoryDetailState
{
    public int InstitutionId { get; init; }
    public Guid TenantId { get; init; }
    public string InstitutionName { get; init; } = string.Empty;
    public string InstitutionCode { get; init; } = string.Empty;
    public string LicenceType { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public decimal? CapitalScore { get; init; }
    public string CapitalAlert { get; init; } = string.Empty;
    public int OverdueObligations { get; init; }
    public int DueSoonObligations { get; init; }
    public int OpenResilienceIncidents { get; init; }
    public int OpenSecurityAlerts { get; init; }
    public int ModelReviewItems { get; init; }
    public string TopObligationsJson { get; init; } = "[]";
    public string RecentSubmissionsJson { get; init; } = "[]";
    public string RecentActivityJson { get; init; } = "[]";
    public DateTime MaterializedAt { get; init; }
}
