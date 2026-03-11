using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Services;

public sealed class SanctionsScreeningSessionStoreService
{
    private const int MaxBatchRuns = 20;
    private const int MaxTransactionChecks = 20;

    private readonly MetadataDbContext _db;

    public SanctionsScreeningSessionStoreService(MetadataDbContext db)
    {
        _db = db;
    }

    public async Task RecordBatchRunAsync(SanctionsStoredScreeningRun run, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);

        await EnsureStoreAsync(ct);

        var screeningKey = $"BATCH-{run.ScreenedAt:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..40];
        _db.SanctionsScreeningRuns.Add(new SanctionsScreeningRunRecord
        {
            ScreeningKey = screeningKey,
            ThresholdPercent = run.ThresholdPercent,
            ScreenedAt = run.ScreenedAt,
            TotalSubjects = run.TotalSubjects,
            MatchCount = run.MatchCount,
            CreatedAt = DateTime.UtcNow
        });

        _db.SanctionsScreeningResults.AddRange(run.Results.Select((result, index) => new SanctionsScreeningResultRecord
        {
            ScreeningKey = screeningKey,
            SortOrder = index,
            Subject = result.Subject,
            Disposition = result.Disposition,
            MatchScore = result.MatchScore,
            MatchedName = result.MatchedName,
            SourceCode = result.SourceCode,
            SourceName = result.SourceName,
            Category = result.Category,
            RiskLevel = result.RiskLevel,
            CreatedAt = DateTime.UtcNow
        }));

        await _db.SaveChangesAsync(ct);
        await TrimStoreAsync(ct);
    }

    public async Task RecordTransactionCheckAsync(SanctionsStoredTransactionCheck result, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await EnsureStoreAsync(ct);

        var transactionKey = $"TX-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}"[..40];
        var screenedAt = DateTime.UtcNow;

        _db.SanctionsTransactionChecks.Add(new SanctionsTransactionCheckRecord
        {
            TransactionKey = transactionKey,
            TransactionReference = result.TransactionReference,
            Amount = result.Amount,
            Currency = result.Currency,
            Channel = result.Channel,
            ThresholdPercent = result.ThresholdPercent,
            HighRisk = result.HighRisk,
            ControlDecision = result.ControlDecision,
            Narrative = result.Narrative,
            RequiresStrDraft = result.RequiresStrDraft,
            ScreenedAt = screenedAt,
            CreatedAt = screenedAt
        });

        _db.SanctionsTransactionPartyResults.AddRange(result.PartyResults.Select((party, index) => new SanctionsTransactionPartyResultRecord
        {
            TransactionKey = transactionKey,
            SortOrder = index,
            PartyRole = party.PartyRole,
            PartyName = party.PartyName,
            Disposition = party.Disposition,
            MatchScore = party.MatchScore,
            MatchedName = party.MatchedName,
            SourceCode = party.SourceCode,
            RiskLevel = party.RiskLevel,
            CreatedAt = screenedAt
        }));

        await _db.SaveChangesAsync(ct);
        await TrimStoreAsync(ct);
    }

    public async Task<SanctionsScreeningSessionState> LoadLatestAsync(CancellationToken ct = default)
    {
        await EnsureStoreAsync(ct);

        var latestBatch = await _db.SanctionsScreeningRuns
            .AsNoTracking()
            .OrderByDescending(x => x.ScreenedAt)
            .FirstOrDefaultAsync(ct);

        SanctionsStoredScreeningRun? screeningRun = null;
        if (latestBatch is not null)
        {
            var results = await _db.SanctionsScreeningResults
                .AsNoTracking()
                .Where(x => x.ScreeningKey == latestBatch.ScreeningKey)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            screeningRun = new SanctionsStoredScreeningRun
            {
                ThresholdPercent = latestBatch.ThresholdPercent,
                ScreenedAt = latestBatch.ScreenedAt,
                TotalSubjects = latestBatch.TotalSubjects,
                MatchCount = latestBatch.MatchCount,
                Results = results.Select(MapResult).ToList()
            };
        }

        var latestTransaction = await _db.SanctionsTransactionChecks
            .AsNoTracking()
            .OrderByDescending(x => x.ScreenedAt)
            .FirstOrDefaultAsync(ct);

        SanctionsStoredTransactionCheck? transactionResult = null;
        if (latestTransaction is not null)
        {
            var parties = await _db.SanctionsTransactionPartyResults
                .AsNoTracking()
                .Where(x => x.TransactionKey == latestTransaction.TransactionKey)
                .OrderBy(x => x.SortOrder)
                .ToListAsync(ct);

            transactionResult = new SanctionsStoredTransactionCheck
            {
                TransactionReference = latestTransaction.TransactionReference,
                Amount = latestTransaction.Amount,
                Currency = latestTransaction.Currency,
                Channel = latestTransaction.Channel,
                ThresholdPercent = latestTransaction.ThresholdPercent,
                HighRisk = latestTransaction.HighRisk,
                ControlDecision = latestTransaction.ControlDecision,
                Narrative = latestTransaction.Narrative,
                RequiresStrDraft = latestTransaction.RequiresStrDraft,
                PartyResults = parties.Select(MapPartyResult).ToList()
            };
        }

        return new SanctionsScreeningSessionState
        {
            LatestRun = screeningRun,
            LatestTransaction = transactionResult
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

            IF OBJECT_ID(N'[meta].[sanctions_screening_runs]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[sanctions_screening_runs]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ScreeningKey] NVARCHAR(80) NOT NULL,
                    [ThresholdPercent] FLOAT NOT NULL,
                    [ScreenedAt] DATETIME2 NOT NULL,
                    [TotalSubjects] INT NOT NULL,
                    [MatchCount] INT NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_sanctions_screening_runs_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_sanctions_screening_runs_ScreeningKey]
                    ON [meta].[sanctions_screening_runs]([ScreeningKey]);
                CREATE INDEX [IX_sanctions_screening_runs_ScreenedAt]
                    ON [meta].[sanctions_screening_runs]([ScreenedAt]);
            END;

            IF OBJECT_ID(N'[meta].[sanctions_screening_results]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[sanctions_screening_results]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ScreeningKey] NVARCHAR(80) NOT NULL,
                    [SortOrder] INT NOT NULL,
                    [Subject] NVARCHAR(240) NOT NULL,
                    [Disposition] NVARCHAR(40) NOT NULL,
                    [MatchScore] FLOAT NOT NULL,
                    [MatchedName] NVARCHAR(240) NOT NULL,
                    [SourceCode] NVARCHAR(40) NOT NULL,
                    [SourceName] NVARCHAR(240) NOT NULL,
                    [Category] NVARCHAR(40) NOT NULL,
                    [RiskLevel] NVARCHAR(30) NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_sanctions_screening_results_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX [IX_sanctions_screening_results_ScreeningKey]
                    ON [meta].[sanctions_screening_results]([ScreeningKey]);
                CREATE INDEX [IX_sanctions_screening_results_Subject]
                    ON [meta].[sanctions_screening_results]([Subject]);
            END;

            IF OBJECT_ID(N'[meta].[sanctions_transaction_checks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[sanctions_transaction_checks]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [TransactionKey] NVARCHAR(80) NOT NULL,
                    [TransactionReference] NVARCHAR(120) NOT NULL,
                    [Amount] DECIMAL(18,2) NOT NULL,
                    [Currency] NVARCHAR(16) NOT NULL,
                    [Channel] NVARCHAR(120) NOT NULL,
                    [ThresholdPercent] FLOAT NOT NULL,
                    [HighRisk] BIT NOT NULL,
                    [ControlDecision] NVARCHAR(40) NOT NULL,
                    [Narrative] NVARCHAR(1200) NOT NULL,
                    [RequiresStrDraft] BIT NOT NULL,
                    [ScreenedAt] DATETIME2 NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_sanctions_transaction_checks_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE UNIQUE INDEX [IX_sanctions_transaction_checks_TransactionKey]
                    ON [meta].[sanctions_transaction_checks]([TransactionKey]);
                CREATE INDEX [IX_sanctions_transaction_checks_ScreenedAt]
                    ON [meta].[sanctions_transaction_checks]([ScreenedAt]);
            END;

            IF OBJECT_ID(N'[meta].[sanctions_transaction_party_results]', N'U') IS NULL
            BEGIN
                CREATE TABLE [meta].[sanctions_transaction_party_results]
                (
                    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [TransactionKey] NVARCHAR(80) NOT NULL,
                    [SortOrder] INT NOT NULL,
                    [PartyRole] NVARCHAR(40) NOT NULL,
                    [PartyName] NVARCHAR(240) NOT NULL,
                    [Disposition] NVARCHAR(40) NOT NULL,
                    [MatchScore] FLOAT NOT NULL,
                    [MatchedName] NVARCHAR(240) NOT NULL,
                    [SourceCode] NVARCHAR(40) NOT NULL,
                    [RiskLevel] NVARCHAR(30) NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL CONSTRAINT [DF_sanctions_transaction_party_results_CreatedAt] DEFAULT SYSUTCDATETIME()
                );

                CREATE INDEX [IX_sanctions_transaction_party_results_TransactionKey]
                    ON [meta].[sanctions_transaction_party_results]([TransactionKey]);
                CREATE INDEX [IX_sanctions_transaction_party_results_PartyName]
                    ON [meta].[sanctions_transaction_party_results]([PartyName]);
            END;
            """;

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task TrimStoreAsync(CancellationToken ct)
    {
        var staleRunKeys = await _db.SanctionsScreeningRuns
            .OrderByDescending(x => x.ScreenedAt)
            .Skip(MaxBatchRuns)
            .Select(x => x.ScreeningKey)
            .ToListAsync(ct);

        if (staleRunKeys.Count > 0)
        {
            var staleRuns = await _db.SanctionsScreeningRuns
                .Where(x => staleRunKeys.Contains(x.ScreeningKey))
                .ToListAsync(ct);
            var staleResults = await _db.SanctionsScreeningResults
                .Where(x => staleRunKeys.Contains(x.ScreeningKey))
                .ToListAsync(ct);

            _db.SanctionsScreeningRuns.RemoveRange(staleRuns);
            _db.SanctionsScreeningResults.RemoveRange(staleResults);
        }

        var staleTransactionKeys = await _db.SanctionsTransactionChecks
            .OrderByDescending(x => x.ScreenedAt)
            .Skip(MaxTransactionChecks)
            .Select(x => x.TransactionKey)
            .ToListAsync(ct);

        if (staleTransactionKeys.Count > 0)
        {
            var staleChecks = await _db.SanctionsTransactionChecks
                .Where(x => staleTransactionKeys.Contains(x.TransactionKey))
                .ToListAsync(ct);
            var staleParties = await _db.SanctionsTransactionPartyResults
                .Where(x => staleTransactionKeys.Contains(x.TransactionKey))
                .ToListAsync(ct);

            _db.SanctionsTransactionChecks.RemoveRange(staleChecks);
            _db.SanctionsTransactionPartyResults.RemoveRange(staleParties);
        }

        if (staleRunKeys.Count > 0 || staleTransactionKeys.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private static SanctionsStoredScreeningResult MapResult(SanctionsScreeningResultRecord record) =>
        new()
        {
            Subject = record.Subject,
            Disposition = record.Disposition,
            MatchScore = record.MatchScore,
            MatchedName = record.MatchedName,
            SourceCode = record.SourceCode,
            SourceName = record.SourceName,
            Category = record.Category,
            RiskLevel = record.RiskLevel
        };

    private static SanctionsStoredTransactionPartyResult MapPartyResult(SanctionsTransactionPartyResultRecord record) =>
        new()
        {
            PartyRole = record.PartyRole,
            PartyName = record.PartyName,
            Disposition = record.Disposition,
            MatchScore = record.MatchScore,
            MatchedName = record.MatchedName,
            SourceCode = record.SourceCode,
            RiskLevel = record.RiskLevel
        };

}

public sealed class SanctionsScreeningSessionState
{
    public SanctionsStoredScreeningRun? LatestRun { get; init; }
    public SanctionsStoredTransactionCheck? LatestTransaction { get; init; }
}

public sealed class SanctionsStoredScreeningRun
{
    public double ThresholdPercent { get; init; }
    public DateTime ScreenedAt { get; init; }
    public int TotalSubjects { get; init; }
    public int MatchCount { get; init; }
    public List<SanctionsStoredScreeningResult> Results { get; init; } = [];
}

public sealed class SanctionsStoredScreeningResult
{
    public string Subject { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public double MatchScore { get; init; }
    public string MatchedName { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
}

public sealed class SanctionsStoredTransactionCheck
{
    public string TransactionReference { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public double ThresholdPercent { get; init; }
    public bool HighRisk { get; init; }
    public string ControlDecision { get; init; } = string.Empty;
    public string Narrative { get; init; } = string.Empty;
    public bool RequiresStrDraft { get; init; }
    public List<SanctionsStoredTransactionPartyResult> PartyResults { get; init; } = [];
}

public sealed class SanctionsStoredTransactionPartyResult
{
    public string PartyRole { get; init; } = string.Empty;
    public string PartyName { get; init; } = string.Empty;
    public string Disposition { get; init; } = string.Empty;
    public double MatchScore { get; init; }
    public string MatchedName { get; init; } = string.Empty;
    public string SourceCode { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = string.Empty;
}
