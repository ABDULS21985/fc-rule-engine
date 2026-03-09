namespace FC.Engine.Domain.Abstractions;

public enum AutoFilingPhase { Extract, Validate, Submit, Complete, Failed }

public interface ICaaSAutoFilingService
{
    /// <summary>
    /// Executes an auto-filing schedule: extract → validate → (optionally) submit.
    /// Records every phase transition in CaaSAutoFilingRuns.
    /// </summary>
    Task<CaaSAutoFilingRun> ExecuteScheduleAsync(
        int scheduleId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CaaSAutoFilingRun>> GetRunHistoryAsync(
        int partnerId,
        int scheduleId,
        int page,
        int pageSize,
        CancellationToken ct = default);
}

public sealed class CaaSAutoFilingRun
{
    public long Id { get; set; }
    public int ScheduleId { get; set; }
    public int PartnerId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string PeriodCode { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public long? ValidationSessionId { get; set; }
    public long? ReturnInstanceId { get; set; }
    public long? BatchId { get; set; }
    public bool? IsClean { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
