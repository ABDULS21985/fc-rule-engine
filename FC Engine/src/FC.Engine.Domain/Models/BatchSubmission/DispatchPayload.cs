namespace FC.Engine.Domain.Models.BatchSubmission;

public sealed record DispatchPayload(
    string BatchReference,
    string RegulatorCode,
    string InstitutionCode,
    byte[] ExportedFileContent,
    string ExportedFileName,
    string ExportFormat,
    PayloadDigest Digest,
    BatchSignatureInfo Signature,
    byte[]? EvidencePackage,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record BatchRegulatorReceipt(
    string ReceiptReference,
    DateTimeOffset ReceiptTimestamp,
    int? HttpStatusCode,
    string? RawResponse);

public sealed record BatchRegulatorStatusResponse(
    string ReceiptReference,
    string RegulatorStatusCode,
    BatchSubmissionStatusValue MappedStatus,
    string? StatusMessage,
    DateTimeOffset? LastUpdated);

/// <summary>String-based status value (decoupled from EF enum).</summary>
public enum BatchSubmissionStatusValue
{
    Pending, Signing, Dispatching, Submitted, Acknowledged,
    Processing, Accepted, QueriesRaised, FinalAccepted, Rejected, Failed
}

public sealed record BatchRegulatorQueryDto(
    string QueryReference,
    string Type,
    string QueryText,
    DateOnly? DueDate,
    string Priority);

public sealed record QueryResponsePayload(
    string QueryReference,
    string ResponseText,
    IReadOnlyList<AttachmentPayload> Attachments,
    BatchSignatureInfo Signature);

public sealed record AttachmentPayload(
    string FileName,
    string ContentType,
    byte[] Content,
    string FileHash);
