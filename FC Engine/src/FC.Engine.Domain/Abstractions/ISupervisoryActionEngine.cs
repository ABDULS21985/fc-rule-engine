namespace FC.Engine.Domain.Abstractions;

/// <summary>
/// Generates, issues, escalates, and closes supervisory regulatory actions.
/// Every action and state change is written to the immutable SupervisoryActionAuditLog.
/// </summary>
public interface ISupervisoryActionEngine
{
    /// <summary>
    /// Auto-generates supervisory actions (advisory or warning letters) for all
    /// CRITICAL/HIGH EWI triggers from the given computation run that have no open action.
    /// Returns the IDs of newly created actions.
    /// </summary>
    Task<IReadOnlyList<long>> GenerateActionsForRunAsync(
        Guid computationRunId,
        string regulatorCode,
        CancellationToken ct = default);

    /// <summary>Generates (or regenerates) the CBN-formatted letter content for an action.</summary>
    Task<string> GenerateLetterContentAsync(
        long supervisoryActionId,
        CancellationToken ct = default);

    /// <summary>Transitions a DRAFT action to ISSUED status.</summary>
    Task IssueActionAsync(
        long supervisoryActionId,
        int issuedByUserId,
        CancellationToken ct = default);

    /// <summary>Escalates an action to the next supervisory level (max: Governor).</summary>
    Task EscalateActionAsync(
        long supervisoryActionId,
        int escalatedByUserId,
        string reason,
        CancellationToken ct = default);

    /// <summary>Records a remediation plan update against an open action.</summary>
    Task RecordRemediationUpdateAsync(
        long supervisoryActionId,
        string updateJson,
        int updatedByUserId,
        CancellationToken ct = default);

    /// <summary>Closes a resolved or remediated action.</summary>
    Task CloseActionAsync(
        long supervisoryActionId,
        int closedByUserId,
        string closureReason,
        CancellationToken ct = default);
}
