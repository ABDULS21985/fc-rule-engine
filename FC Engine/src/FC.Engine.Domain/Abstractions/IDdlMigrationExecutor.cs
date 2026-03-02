namespace FC.Engine.Domain.Abstractions;

public interface IDdlMigrationExecutor
{
    Task<MigrationResult> Execute(
        int templateId,
        int? versionFrom,
        int versionTo,
        DdlScript ddlScript,
        string executedBy,
        CancellationToken ct = default);

    Task<MigrationResult> Rollback(int migrationId, string rolledBackBy, CancellationToken ct = default);
}

public record MigrationResult(bool Success, string? Error);
