namespace FC.Engine.Domain.Abstractions;

public interface IBulkUploadService
{
    Task<BulkUploadResult> ProcessExcelUpload(
        Stream fileStream,
        Guid tenantId,
        string returnCode,
        int institutionId,
        int returnPeriodId,
        int? requestedByUserId = null,
        CancellationToken ct = default);

    Task<BulkUploadResult> ProcessCsvUpload(
        Stream fileStream,
        Guid tenantId,
        string returnCode,
        int institutionId,
        int returnPeriodId,
        int? requestedByUserId = null,
        CancellationToken ct = default);
}

public class BulkUploadResult
{
    public bool Success { get; set; }
    public int SubmissionId { get; set; }
    public int RowsImported { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public List<string> UnmappedColumns { get; set; } = [];
    public List<BulkUploadError> Errors { get; set; } = [];
    public byte[]? ErrorFile { get; set; }
    public string? ErrorFileName { get; set; }
}

public class BulkUploadError
{
    public int RowNumber { get; set; }
    public string FieldCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = "Error";
    public string Category { get; set; } = "TypeRange";
    public string? ExpectedValue { get; set; }
}
