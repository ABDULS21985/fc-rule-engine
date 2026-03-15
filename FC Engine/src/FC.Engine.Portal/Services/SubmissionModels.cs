using FC.Engine.Application.DTOs;

namespace FC.Engine.Portal.Services;

/// <summary>
/// View model for the submission list page — one item per submission.
/// </summary>
public class SubmissionListItem
{
    public int Id { get; set; }
    public string ReturnCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string? ModuleCode { get; set; }
    public string PeriodLabel { get; set; } = "—";
    public DateTime? SubmittedAt { get; set; }
    public string Status { get; set; } = "";
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int? ProcessingDurationMs { get; set; }
    public DateTime? DeadlineDate { get; set; }
    public string AssigneeInitials { get; set; } = "ME";
    public bool IsCurrentUser { get; set; } = true;
}

/// <summary>
/// View model for the submission detail page.
/// </summary>
public class SubmissionDetailModel
{
    public int Id { get; set; }
    public string ReturnCode { get; set; } = "";
    public int InstitutionId { get; set; }
    public int ReturnPeriodId { get; set; }
    public string Status { get; set; } = "";
    public DateTime? SubmittedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? ProcessingDurationMs { get; set; }
    public string? RawXml { get; set; }
    public bool ApprovalRequired { get; set; }
    public int? SubmittedByUserId { get; set; }
    public Guid TenantId { get; set; }

    // Resolved display values
    public string TemplateName { get; set; } = "";
    public string? ModuleCode { get; set; }
    public string PeriodLabel { get; set; } = "—";
    public string? SubmitterName { get; set; }

    // Validation data (unified as DTOs)
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<ValidationErrorDto> ValidationErrors { get; set; } = new();

    // Approval data
    public SubmissionApprovalModel? Approval { get; set; }
}

/// <summary>
/// View model for the approval record on a submission detail.
/// </summary>
public class SubmissionApprovalModel
{
    public int Id { get; set; }
    public int RequestedByUserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public string? SubmitterNotes { get; set; }
    public string Status { get; set; } = "";
    public int? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewerComments { get; set; }
    public string? ReviewerName { get; set; }
    public int? OriginalSubmissionId { get; set; }
}

/// <summary>
/// View model for template selection in the submission wizard.
/// </summary>
public class TemplateSelectItem
{
    public string ReturnCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string StructuralCategory { get; set; } = "";
    public string? ModuleCode { get; set; }
    public bool AlreadySubmitted { get; set; }
}

/// <summary>
/// View model for period selection in the submission wizard.
/// </summary>
public class PeriodSelectItem
{
    public int ReturnPeriodId { get; set; }
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
    public DateTime ReportingDate { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public bool HasExistingSubmission { get; set; }
    /// <summary>Effective deadline for this period (override date takes precedence over base deadline).</summary>
    public DateTime DeadlineDate { get; set; }
    /// <summary>Period is past its filing window with no extension granted — no new submissions allowed.</summary>
    public bool IsLocked { get; set; }
    /// <summary>Status of the existing submission for this period, if one exists.</summary>
    public FC.Engine.Domain.Enums.SubmissionStatus? ExistingSubmissionStatus { get; set; }
    /// <summary>ID of the existing submission for this period, used to navigate to read-only view.</summary>
    public int? ExistingSubmissionId { get; set; }
}

/// <summary>
/// Flattened validation error for display in the wizard.
/// </summary>
public class ValidationDisplayError
{
    public string RuleId { get; set; } = "";
    public string Field { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsError { get; set; }
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
}

public class RegulatoryChannelOption
{
    public string RegulatorCode { get; set; } = "";
    public string RegulatorName { get; set; } = "";
    public string PortalName { get; set; } = "";
    public string IntegrationMethod { get; set; } = "";
    public bool RequiresCertificate { get; set; }
}

public class SubmissionBatchListResult
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<SubmissionBatchListItem> Items { get; set; } = new();
}

public class SubmissionBatchListItem
{
    public long Id { get; set; }
    public string BatchReference { get; set; } = "";
    public string RegulatorCode { get; set; } = "";
    public string RegulatorName { get; set; } = "";
    public string Status { get; set; } = "";
    public int ItemCount { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? FinalStatusAt { get; set; }
    public int RetryCount { get; set; }
    public string? LatestReceipt { get; set; }
    public int OpenQueries { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SubmissionBatchDetailModel
{
    public long Id { get; set; }
    public int InstitutionId { get; set; }
    public string BatchReference { get; set; } = "";
    public string RegulatorCode { get; set; } = "";
    public string RegulatorName { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid CorrelationId { get; set; }
    public string? LastError { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? FinalStatusAt { get; set; }
    public List<SubmissionBatchItemModel> Items { get; set; } = new();
    public List<SubmissionBatchReceiptModel> Receipts { get; set; } = new();
    public List<SubmissionBatchQueryModel> Queries { get; set; } = new();
    public List<SubmissionBatchAuditModel> AuditLogs { get; set; } = new();
}

public class SubmissionBatchItemModel
{
    public long Id { get; set; }
    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = "";
    public string ReportingPeriod { get; set; } = "";
    public string ExportFormat { get; set; } = "";
    public long ExportPayloadSize { get; set; }
    public string ExportPayloadHash { get; set; } = "";
    public string Status { get; set; } = "";
}

public class SubmissionBatchReceiptModel
{
    public string RegulatorCode { get; set; } = "";
    public string ReceiptReference { get; set; } = "";
    public DateTime ReceiptTimestamp { get; set; }
    public int? HttpStatusCode { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public class SubmissionBatchQueryModel
{
    public long QueryId { get; set; }
    public string QueryReference { get; set; } = "";
    public string QueryType { get; set; } = "";
    public string QueryText { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public int? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public int ResponseCount { get; set; }
    public DateTime? LastResponseAt { get; set; }
}

public class SubmissionBatchAuditModel
{
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
    public int? PerformedBy { get; set; }
    public string? PerformedByName { get; set; }
    public DateTime PerformedAt { get; set; }
}

public class SubmissionBatchEligibilityState
{
    public int SubmissionId { get; set; }
    public long BatchId { get; set; }
    public string BatchReference { get; set; } = "";
    public string BatchStatus { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class RegulatoryQueryListItem
{
    public long QueryId { get; set; }
    public long BatchId { get; set; }
    public string BatchReference { get; set; } = "";
    public string RegulatorCode { get; set; } = "";
    public string RegulatorName { get; set; } = "";
    public string QueryReference { get; set; } = "";
    public string QueryType { get; set; } = "";
    public string QueryText { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public int? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public int ResponseCount { get; set; }
    public DateTime? LastResponseAt { get; set; }
}

public class RegulatoryQueryDetailModel
{
    public long QueryId { get; set; }
    public long BatchId { get; set; }
    public string BatchReference { get; set; } = "";
    public string BatchStatus { get; set; } = "";
    public string RegulatorCode { get; set; } = "";
    public string RegulatorName { get; set; } = "";
    public string QueryReference { get; set; } = "";
    public string QueryType { get; set; } = "";
    public string QueryText { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public int? AssignedToUserId { get; set; }
    public string? AssignedToName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public List<RegulatoryQueryResponseModel> Responses { get; set; } = new();
}

public class RegulatoryQueryResponseModel
{
    public long ResponseId { get; set; }
    public string ResponseText { get; set; } = "";
    public int AttachmentCount { get; set; }
    public bool SubmittedToRegulator { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? RegulatorAckRef { get; set; }
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<RegulatoryQueryAttachmentModel> Attachments { get; set; } = new();
}

public class RegulatoryQueryAttachmentModel
{
    public long Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string FileHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
