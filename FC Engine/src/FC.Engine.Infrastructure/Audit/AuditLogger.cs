using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Audit;

public class AuditLogger : IAuditLogger
{
    private const int AuditActionMaxLength = 64;
    private static readonly SemaphoreSlim SchemaEnsureLock = new(1, 1);
    private static volatile bool _auditLogSchemaEnsured;

    private readonly MetadataDbContext _db;
    private readonly ITenantContext _tenantContext;

    public AuditLogger(MetadataDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task Log(
        string entityType,
        int entityId,
        string action,
        object? oldValues,
        object? newValues,
        string performedBy,
        CancellationToken ct = default)
    {
        await EnsureAuditLogSchemaAsync(ct);

        var tenantId = _tenantContext.CurrentTenantId;
        var now = DateTime.UtcNow;

        var oldJson = oldValues != null ? JsonSerializer.Serialize(oldValues) : null;
        var newJson = newValues != null ? JsonSerializer.Serialize(newValues) : null;

        // Get previous entry for this tenant to build the chain
        var previous = await _db.AuditLog
            .Where(a => a.TenantId == tenantId && a.SequenceNumber > 0)
            .OrderByDescending(a => a.SequenceNumber)
            .Select(a => new { a.Hash, a.SequenceNumber })
            .FirstOrDefaultAsync(ct);

        var previousHash = previous?.Hash ?? "GENESIS";
        var sequenceNumber = (previous?.SequenceNumber ?? 0) + 1;
        var normalizedAction = Normalize(action, AuditActionMaxLength);

        // Compute hash: sequence|eventType|timestamp|tenantId|userId|entityType|entityId|action|before|after|previousHash
        var hash = ComputeHash(
            sequenceNumber, entityType, now, tenantId, performedBy,
            entityType, entityId, normalizedAction, oldJson, newJson, previousHash);

        var entry = new AuditLogEntry
        {
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = normalizedAction,
            OldValues = oldJson,
            NewValues = newJson,
            PerformedBy = performedBy,
            PerformedAt = now,
            Hash = hash,
            PreviousHash = previousHash,
            SequenceNumber = sequenceNumber
        };

        _db.AuditLog.Add(entry);
        await _db.SaveChangesAsync(ct);
    }

    private async Task EnsureAuditLogSchemaAsync(CancellationToken ct)
    {
        if (_auditLogSchemaEnsured || !_db.Database.IsSqlServer())
        {
            return;
        }

        await SchemaEnsureLock.WaitAsync(ct);
        try
        {
            if (_auditLogSchemaEnsured)
            {
                return;
            }

            await _db.Database.ExecuteSqlRawAsync(
                """
                IF COL_LENGTH('meta.audit_log', 'Action') IS NOT NULL
                BEGIN
                    DECLARE @CurrentLength INT;
                    SELECT @CurrentLength = c.max_length / CASE WHEN t.name IN ('nvarchar', 'nchar') THEN 2 ELSE 1 END
                    FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    WHERE c.object_id = OBJECT_ID(N'meta.audit_log')
                      AND c.name = N'Action';

                    IF @CurrentLength IS NOT NULL AND @CurrentLength < 64
                    BEGIN
                        ALTER TABLE meta.audit_log ALTER COLUMN [Action] NVARCHAR(64) NOT NULL;
                    END
                END
                """,
                ct);

            _auditLogSchemaEnsured = true;
        }
        finally
        {
            SchemaEnsureLock.Release();
        }
    }

    private static string Normalize(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    internal static string ComputeHash(
        long sequenceNumber,
        string eventType,
        DateTime timestamp,
        Guid? tenantId,
        string userId,
        string entityType,
        int entityId,
        string action,
        string? before,
        string? after,
        string previousHash)
    {
        var canonical = string.Join("|",
            sequenceNumber,
            eventType,
            timestamp.ToString("O"),
            tenantId?.ToString() ?? "",
            userId,
            entityType,
            entityId,
            action,
            before ?? "",
            after ?? "",
            previousHash);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes);
    }
}
