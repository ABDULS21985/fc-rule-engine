using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Audit;

public class AuditLogger : IAuditLogger
{
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

        // Compute hash: sequence|eventType|timestamp|tenantId|userId|entityType|entityId|action|before|after|previousHash
        var hash = ComputeHash(
            sequenceNumber, entityType, now, tenantId, performedBy,
            entityType, entityId, action, oldJson, newJson, previousHash);

        var entry = new AuditLogEntry
        {
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
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
