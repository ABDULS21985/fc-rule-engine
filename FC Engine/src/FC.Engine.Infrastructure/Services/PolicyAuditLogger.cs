using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class PolicyAuditLogger : IPolicyAuditLogger
{
    private readonly MetadataDbContext _db;
    private readonly ILogger<PolicyAuditLogger> _log;

    public PolicyAuditLogger(MetadataDbContext db, ILogger<PolicyAuditLogger> log)
    {
        _db = db;
        _log = log;
    }

    public async Task LogAsync(
        long? scenarioId, int regulatorId, Guid correlationId,
        string action, object? detail, int userId, CancellationToken ct = default)
    {
        var entry = new PolicyAuditLog
        {
            ScenarioId = scenarioId,
            RegulatorId = regulatorId,
            CorrelationId = correlationId,
            Action = action,
            Detail = detail is not null ? JsonSerializer.Serialize(detail) : null,
            PerformedByUserId = userId,
            PerformedAt = DateTime.UtcNow
        };

        _db.PolicyAuditLog.Add(entry);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "PolicyAudit: {Action} by User={UserId}, Scenario={ScenarioId}, Correlation={CorrelationId}",
            action, userId, scenarioId, correlationId);
    }
}
