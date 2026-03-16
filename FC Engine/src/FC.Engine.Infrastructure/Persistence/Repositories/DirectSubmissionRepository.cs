using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace FC.Engine.Infrastructure.Persistence.Repositories;

public class DirectSubmissionRepository : IDirectSubmissionRepository
{
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly ILogger<DirectSubmissionRepository> _logger;
    private bool? _isDirectSubmissionTableAvailable;

    public DirectSubmissionRepository(IDbContextFactory<MetadataDbContext> dbFactory, ILogger<DirectSubmissionRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<DirectSubmission> Add(DirectSubmission entity, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.DirectSubmissions.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task Update(DirectSubmission entity, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.DirectSubmissions.Update(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DirectSubmission?> GetById(int id, CancellationToken ct = default)
    {
        return await ExecuteReadAsync(
            async db => await db.DirectSubmissions.FindAsync(new object[] { id }, ct),
            default(DirectSubmission?),
            $"load direct submission {id}",
            ct);
    }

    public async Task<DirectSubmission?> GetByIdWithSubmission(int id, CancellationToken ct = default)
    {
        return await ExecuteReadAsync(
            db => db.DirectSubmissions
                .Include(d => d.Submission).ThenInclude(s => s!.Institution)
                .Include(d => d.Submission).ThenInclude(s => s!.ReturnPeriod)
                .FirstOrDefaultAsync(d => d.Id == id, ct),
            default(DirectSubmission?),
            $"load direct submission {id} with related submission data",
            ct);
    }

    public async Task<List<DirectSubmission>> GetBySubmission(int submissionId, CancellationToken ct = default)
    {
        return await ExecuteReadAsync(
            db => db.DirectSubmissions
                .Where(d => d.SubmissionId == submissionId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(ct),
            new List<DirectSubmission>(),
            $"load direct submissions for submission {submissionId}",
            ct);
    }

    public async Task<List<DirectSubmission>> GetByTenantAndSubmission(Guid tenantId, int submissionId, CancellationToken ct = default)
    {
        return await ExecuteReadAsync(
            db => db.DirectSubmissions
                .Where(d => d.TenantId == tenantId && d.SubmissionId == submissionId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync(ct),
            new List<DirectSubmission>(),
            $"load direct submissions for tenant {tenantId} and submission {submissionId}",
            ct);
    }

    public async Task<List<DirectSubmission>> GetPendingRetries(int batchSize, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await ExecuteReadAsync(
            db => db.DirectSubmissions
                .Include(d => d.Submission).ThenInclude(s => s!.Institution)
                .Where(d => d.Status == DirectSubmissionStatus.RetryScheduled
                    && d.NextRetryAt != null && d.NextRetryAt <= now
                    && d.AttemptCount < d.MaxAttempts)
                .OrderBy(d => d.NextRetryAt)
                .Take(batchSize)
                .ToListAsync(ct),
            new List<DirectSubmission>(),
            $"load up to {batchSize} retry-scheduled direct submissions",
            ct);
    }

    public async Task<List<DirectSubmission>> GetSubmittedAwaitingStatus(int batchSize, CancellationToken ct = default)
    {
        return await ExecuteReadAsync(
            db => db.DirectSubmissions
                .Where(d => d.Status == DirectSubmissionStatus.Submitted
                    || d.Status == DirectSubmissionStatus.Acknowledged)
                .OrderBy(d => d.SubmittedAt)
                .Take(batchSize)
                .ToListAsync(ct),
            new List<DirectSubmission>(),
            $"load up to {batchSize} submitted direct submissions awaiting status",
            ct);
    }

    private async Task<T> ExecuteReadAsync<T>(Func<MetadataDbContext, Task<T>> operation, T fallback, string description, CancellationToken ct)
    {
        if (!await IsDirectSubmissionTableAvailable(ct))
        {
            return fallback;
        }

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await operation(db);
        }
        catch (Exception ex) when (IsMissingDirectSubmissionSchema(ex))
        {
            _logger.LogWarning(ex,
                "Direct submission read skipped because the direct_submissions table is unavailable while attempting to {Description}. Returning fallback data.",
                description);
            return fallback;
        }
    }

    private async Task<bool> IsDirectSubmissionTableAvailable(CancellationToken ct)
    {
        if (_isDirectSubmissionTableAvailable.HasValue)
        {
            return _isDirectSubmissionTableAvailable.Value;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var connection = db.Database.GetDbConnection();
        var openedHere = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
                openedHere = true;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT CASE WHEN EXISTS (
                    SELECT 1
                    FROM sys.tables AS t
                    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
                    WHERE t.name = N'direct_submissions'
                      AND s.name = N'dbo'
                ) THEN 1 ELSE 0 END
                """;

            var result = await command.ExecuteScalarAsync(ct);
            _isDirectSubmissionTableAvailable = Convert.ToInt32(result ?? 0) == 1;

            if (!_isDirectSubmissionTableAvailable.Value)
            {
                _logger.LogWarning(
                    "Direct submission reads are disabled because the dbo.direct_submissions table is not present in the current metadata database.");
            }

            return _isDirectSubmissionTableAvailable.Value;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool IsMissingDirectSubmissionSchema(Exception ex)
    {
        if (ex is SqlException sqlException
            && sqlException.Number == 208
            && sqlException.Message.Contains("direct_submissions", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ex.InnerException is not null && IsMissingDirectSubmissionSchema(ex.InnerException);
    }
}
