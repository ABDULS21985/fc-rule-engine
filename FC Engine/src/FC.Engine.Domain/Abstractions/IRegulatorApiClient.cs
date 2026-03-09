namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorApiClient
{
    string RegulatorCode { get; }

    Task<RegulatorApiResponse> SubmitAsync(
        byte[] package, byte[]? signature, RegulatorSubmissionContext context,
        CancellationToken ct = default);

    Task<RegulatorStatusResponse> CheckStatusAsync(
        string regulatorReference, CancellationToken ct = default);

    Task<List<RegulatorQueryInfo>> FetchQueriesAsync(
        string regulatorReference, CancellationToken ct = default);
}

public class RegulatorSubmissionContext
{
    public int SubmissionId { get; set; }
    public string InstitutionCode { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string ReturnCode { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}

public class RegulatorApiResponse
{
    public bool Success { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int HttpStatusCode { get; set; }
    public string? RawResponseBody { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}

public class RegulatorStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public List<RegulatorQueryInfo> Queries { get; set; } = new();
    public GoAmlQualityFeedback? QualityFeedback { get; set; }
}

public class GoAmlQualityFeedback
{
    public string OverallQuality { get; set; } = string.Empty;
    public List<string> Issues { get; set; } = new();
    public DateTime ReceivedAt { get; set; }
}
