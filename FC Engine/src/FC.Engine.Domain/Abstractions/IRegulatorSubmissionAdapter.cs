using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Abstractions;

public interface IRegulatorSubmissionAdapter
{
    string RegulatorCode { get; }
    Task<byte[]> Package(Submission submission, ExportFormat preferredFormat, CancellationToken ct = default);
    Task<SubmissionReceipt> Submit(byte[] package, Submission submission, CancellationToken ct = default);
}

public class SubmissionReceipt
{
    public bool Success { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
