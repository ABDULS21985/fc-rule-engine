using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure;

namespace FC.Engine.Infrastructure.Services;

public sealed class AlertManagementService : IAlertManagementService
{
    private readonly IDbConnectionFactory _db;
    private readonly IRegulatorTenantResolver _tenantResolver;

    public AlertManagementService(
        IDbConnectionFactory db,
        IRegulatorTenantResolver tenantResolver)
    {
        _db = db;
        _tenantResolver = tenantResolver;
    }

    public async Task ResolveAlertAsync(AlertResolution resolution, CancellationToken ct = default)
    {
        using var conn = await _db.CreateConnectionAsync(null, ct);
        var alert = await conn.QuerySingleOrDefaultAsync<AlertContextRow>(
            """
            SELECT sa.Id,
                   sa.TenantId,
                   sa.RegulatorCode
            FROM dbo.SurveillanceAlerts sa
            LEFT JOIN dbo.SurveillanceAlertResolutions sr ON sr.AlertId = sa.Id
            WHERE sa.Id = @AlertId
              AND sr.AlertId IS NULL
            """,
            new { resolution.AlertId });

        if (alert is null)
        {
            throw new InvalidOperationException($"Alert {resolution.AlertId} is already resolved or does not exist.");
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO dbo.SurveillanceAlertResolutions
                (TenantId, AlertId, RegulatorCode, ResolvedByUserId, ResolutionOutcome, ResolutionNote)
            VALUES
                (@TenantId, @AlertId, @RegulatorCode, @ResolvedByUserId, @ResolutionOutcome, @ResolutionNote)
            """,
            new
            {
                alert.TenantId,
                resolution.AlertId,
                alert.RegulatorCode,
                resolution.ResolvedByUserId,
                ResolutionOutcome = resolution.Outcome.Trim().ToUpperInvariant(),
                ResolutionNote = resolution.Note.Trim()
            });
    }

    public async Task<IReadOnlyList<SurveillanceAlertRow>> GetOpenAlertsAsync(
        string regulatorCode,
        AlertCategory? category,
        AlertSeverity? minSeverity,
        CancellationToken ct = default)
    {
        var context = await _tenantResolver.ResolveAsync(regulatorCode, ct);
        using var conn = await _db.CreateConnectionAsync(context.TenantId, ct);

        var severities = minSeverity switch
        {
            AlertSeverity.Critical => new[] { "CRITICAL" },
            AlertSeverity.High => new[] { "CRITICAL", "HIGH" },
            AlertSeverity.Medium => new[] { "CRITICAL", "HIGH", "MEDIUM" },
            _ => new[] { "CRITICAL", "HIGH", "MEDIUM", "LOW" }
        };

        try
        {
            return (await conn.QueryAsync<SurveillanceAlertRow>(
                """
                SELECT sa.Id AS AlertId,
                       sa.AlertCode,
                       sa.Category,
                       sa.Severity,
                       sa.Title,
                       sa.Detail,
                       sa.EvidenceJson,
                       sa.InstitutionId,
                       i.InstitutionName,
                       sa.DetectedAt,
                       CAST(CASE WHEN sr.AlertId IS NULL THEN 0 ELSE 1 END AS bit) AS IsResolved
                FROM dbo.SurveillanceAlerts sa
                LEFT JOIN dbo.institutions i ON i.Id = sa.InstitutionId
                LEFT JOIN (
                    SELECT AlertId, MAX(ResolvedAt) AS ResolvedAt
                    FROM dbo.SurveillanceAlertResolutions
                    GROUP BY AlertId
                ) sr ON sr.AlertId = sa.Id
                WHERE sa.TenantId = @TenantId
                  AND sa.RegulatorCode = @RegulatorCode
                  AND sr.AlertId IS NULL
                  AND (@Category IS NULL OR sa.Category = @Category)
                  AND sa.Severity IN @Severities
                ORDER BY CASE sa.Severity
                            WHEN 'CRITICAL' THEN 1
                            WHEN 'HIGH' THEN 2
                            WHEN 'MEDIUM' THEN 3
                            ELSE 4
                         END,
                         sa.DetectedAt DESC
                """,
                new
                {
                    TenantId = context.TenantId,
                    RegulatorCode = context.RegulatorCode,
                    Category = category is null ? null : SurveillanceSqlMapping.ToDbValue(category.Value),
                    Severities = severities
                })).ToList();
        }
        catch (Exception ex) when (ex.IsMissingSchemaObject())
        {
            return [];
        }
    }

    private sealed record AlertContextRow(long Id, Guid TenantId, string RegulatorCode);
}
