using FC.Engine.Domain.Entities;

namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorySubmissionService
{
    Task<DirectSubmissionResult> SubmitToRegulatorAsync(
        int submissionId, string regulatorCode, string submittedBy,
        CancellationToken ct = default);

    Task<DirectSubmissionResult> RetrySubmissionAsync(
        int directSubmissionId, CancellationToken ct = default);

    Task<DirectSubmissionStatusResult> CheckStatusAsync(
        int directSubmissionId, CancellationToken ct = default);

    Task<List<DirectSubmission>> GetSubmissionHistoryAsync(
        Guid tenantId, int submissionId, CancellationToken ct = default);
}

public class DirectSubmissionResult
{
    public bool Success { get; set; }
    public int DirectSubmissionId { get; set; }
    public string RegulatorReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime? SubmittedAt { get; set; }
}

public class DirectSubmissionStatusResult
{
    public int DirectSubmissionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = string.Empty;
    public string? RegulatorReference { get; set; }
    public string? LatestMessage { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public List<RegulatorQueryInfo> Queries { get; set; } = new();
}

public class RegulatorQueryInfo
{
    public string QueryId { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime RaisedAt { get; set; }
}
