using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;

namespace FC.Engine.Infrastructure.Services.CrossBorder;

public sealed class HarmonisationAuditLogger : IHarmonisationAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MetadataDbContext _db;

    public HarmonisationAuditLogger(MetadataDbContext db) => _db = db;

    public async Task LogAsync(
        int? groupId, string? jurisdictionCode, Guid correlationId,
        string action, object? detail, int? userId,
        CancellationToken ct = default)
    {
        var entry = new HarmonisationAuditLog
        {
            GroupId = groupId,
            JurisdictionCode = jurisdictionCode,
            CorrelationId = correlationId,
            Action = action,
            Detail = detail is not null ? JsonSerializer.Serialize(detail, JsonOptions) : null,
            PerformedByUserId = userId,
            PerformedAt = DateTime.UtcNow
        };
        _db.HarmonisationAuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
