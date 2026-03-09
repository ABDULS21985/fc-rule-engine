namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Immutable append-only audit logger for all regulator policy simulation actions.
/// </summary>
public interface IPolicyAuditLogger
{
    Task LogAsync(
        long? scenarioId,
        int regulatorId,
        Guid correlationId,
        string action,
        object? detail,
        int userId,
        CancellationToken ct = default);
}
