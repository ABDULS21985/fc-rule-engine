namespace FC.Engine.Domain.Abstractions;

public interface IHarmonisationAuditLogger
{
    Task LogAsync(
        int? groupId, string? jurisdictionCode, Guid correlationId,
        string action, object? detail, int? userId,
        CancellationToken ct = default);
}
