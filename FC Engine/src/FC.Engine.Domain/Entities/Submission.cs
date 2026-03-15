using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class Submission
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid TenantId { get; set; }

    public int InstitutionId { get; set; }
    public int ReturnPeriodId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public int? TemplateVersionId { get; set; }
    public SubmissionStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? RawXml { get; set; }
    public string? ParsedDataJson { get; set; }
    public int? ProcessingDurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsRetentionAnonymised { get; set; }

    // ── FI Portal Extensions ──

    /// <summary>Which institution user submitted this return (null for API submissions).</summary>
    public int? SubmittedByUserId { get; set; }

    /// <summary>Whether this submission requires checker approval before finalizing.</summary>
    public bool ApprovalRequired { get; set; }

    // Navigation
    public Institution? Institution { get; set; }
    public ReturnPeriod? ReturnPeriod { get; set; }
    public ValidationReport? ValidationReport { get; set; }

    /// <summary>Maker-checker approval record, if applicable.</summary>
    public SubmissionApproval? Approval { get; set; }

    public static Submission Create(int institutionId, int returnPeriodId, string returnCode, Guid? tenantId = null)
    {
        var submission = new Submission
        {
            InstitutionId = institutionId,
            ReturnPeriodId = returnPeriodId,
            ReturnCode = returnCode,
            Status = SubmissionStatus.Draft,
            SubmittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        if (tenantId.HasValue)
        {
            submission.TenantId = tenantId.Value;
        }

        return submission;
    }

    public void SetTemplateVersion(int templateVersionId) => TemplateVersionId = templateVersionId;
    public void MarkParsing() => Status = SubmissionStatus.Parsing;
    public void MarkValidating() => Status = SubmissionStatus.Validating;
    public void MarkAccepted() => Status = SubmissionStatus.Accepted;
    public void MarkAcceptedWithWarnings() => Status = SubmissionStatus.AcceptedWithWarnings;
    public void MarkRejected() => Status = SubmissionStatus.Rejected;
    public void MarkPendingApproval() => Status = SubmissionStatus.PendingApproval;
    public void MarkApprovalRejected() => Status = SubmissionStatus.ApprovalRejected;
    public void AttachValidationReport(ValidationReport report) => ValidationReport = report;
    public void StoreRawXml(string xml) => RawXml = xml;
    public void StoreParsedDataJson(string json) => ParsedDataJson = json;

    // ── Direct Regulatory Submission (RG-34) ──
    public void MarkSubmittedToRegulator() => Status = SubmissionStatus.SubmittedToRegulator;
    public void MarkRegulatorAcknowledged() => Status = SubmissionStatus.RegulatorAcknowledged;
    public void MarkRegulatorAccepted() => Status = SubmissionStatus.RegulatorAccepted;
    public void MarkRegulatorQueriesRaised() => Status = SubmissionStatus.RegulatorQueriesRaised;
    public void MarkHistorical() => Status = SubmissionStatus.Historical;
}
