using System.Globalization;
using System.Text;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Portal.Services;

public class ExportService
{
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IInstitutionRepository _institutionRepo;
    private readonly IInstitutionUserRepository _userRepo;
    private readonly ISubmissionApprovalRepository _approvalRepo;
    private readonly ITenantBrandingService _brandingService;
    private readonly INotificationOrchestrator? _notificationOrchestrator;

    public ExportService(
        ISubmissionRepository submissionRepo,
        IInstitutionRepository institutionRepo,
        IInstitutionUserRepository userRepo,
        ISubmissionApprovalRepository approvalRepo,
        ITenantBrandingService brandingService,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _submissionRepo = submissionRepo;
        _institutionRepo = institutionRepo;
        _userRepo = userRepo;
        _approvalRepo = approvalRepo;
        _brandingService = brandingService;
        _notificationOrchestrator = notificationOrchestrator;
    }

    // === VALIDATION REPORT DATA ===

    public async Task<ValidationReportModel?> BuildValidationReportAsync(int submissionId, int institutionId)
    {
        var submission = await _submissionRepo.GetByIdWithReport(submissionId);
        if (submission is null || submission.InstitutionId != institutionId) return null;

        var institution = await _institutionRepo.GetById(institutionId);
        if (institution is null) return null;

        var submittedByName = "Unknown";
        if (submission.SubmittedByUserId is > 0)
        {
            var user = await _userRepo.GetById(submission.SubmittedByUserId.Value);
            submittedByName = user?.DisplayName ?? "Unknown";
        }

        var errors = new List<ValidationReportError>();
        if (submission.ValidationReport is not null)
        {
            foreach (var err in submission.ValidationReport.Errors)
            {
                errors.Add(new ValidationReportError
                {
                    Code = err.RuleId,
                    Severity = err.Severity.ToString(),
                    Category = err.Category.ToString(),
                    Message = err.Message,
                    FieldName = err.Field,
                    ExpectedValue = err.ExpectedValue,
                    ActualValue = err.ActualValue,
                });
            }
        }

        var errorCount = submission.ValidationReport?.ErrorCount ?? 0;
        var warningCount = submission.ValidationReport?.WarningCount ?? 0;

        return new ValidationReportModel
        {
            Branding = await _brandingService.GetBrandingConfig(submission.TenantId),
            InstitutionName = institution.InstitutionName,
            InstitutionCode = institution.InstitutionCode,
            InstitutionType = institution.LicenseType ?? "",
            SubmissionId = submission.Id,
            ReturnCode = submission.ReturnCode,
            Period = FormatPeriod(submission.ReturnPeriod),
            Status = submission.Status,
            SubmittedAt = submission.SubmittedAt,
            ProcessedAt = submission.ValidationReport?.FinalizedAt ?? submission.SubmittedAt,
            SubmittedBy = submittedByName,
            ErrorCount = errorCount,
            WarningCount = warningCount,
            Errors = errors,
            GeneratedAt = DateTime.UtcNow,
            GeneratedBy = submittedByName
        };
    }

    // === COMPLIANCE CERTIFICATE DATA ===

    public async Task<ComplianceCertificateModel?> BuildComplianceCertificateAsync(int submissionId, int institutionId)
    {
        var submission = await _submissionRepo.GetByIdWithReport(submissionId);
        if (submission is null || submission.InstitutionId != institutionId) return null;
        if (submission.Status != SubmissionStatus.Accepted && submission.Status != SubmissionStatus.AcceptedWithWarnings)
            return null;

        var institution = await _institutionRepo.GetById(institutionId);
        if (institution is null) return null;

        return await BuildCertificateModelAsync(submission, institution);
    }

    /// <summary>
    /// Verifies a certificate by its cert number (public — no institution ownership check).
    /// Returns null if not found, status is not accepted, or cert number is invalid/mismatched.
    /// </summary>
    public async Task<ComplianceCertificateModel?> VerifyCertificateAsync(string certId)
    {
        if (string.IsNullOrWhiteSpace(certId)) return null;

        // Format: CBN-FCE-{submissionId:D8}-{yyyyMMdd}
        var parts = certId.Split('-');
        if (parts.Length < 4) return null;
        if (!int.TryParse(parts[2], out var submissionId)) return null;

        var submission = await _submissionRepo.GetByIdWithReport(submissionId);
        if (submission is null) return null;
        if (submission.Status != SubmissionStatus.Accepted && submission.Status != SubmissionStatus.AcceptedWithWarnings)
            return null;

        var expectedCertNum = $"CBN-FCE-{submission.Id:D8}-{submission.SubmittedAt:yyyyMMdd}";
        if (!string.Equals(certId, expectedCertNum, StringComparison.OrdinalIgnoreCase))
            return null;

        var institution = await _institutionRepo.GetById(submission.InstitutionId);
        if (institution is null) return null;

        return await BuildCertificateModelAsync(submission, institution);
    }

    public async Task SendCertificateEmailAsync(
        int submissionId,
        int institutionId,
        int requestingUserId,
        string portalBaseUrl,
        IEnumerable<string>? additionalEmails = null,
        CancellationToken ct = default)
    {
        if (_notificationOrchestrator is null) return;

        var model = await BuildComplianceCertificateAsync(submissionId, institutionId);
        if (model is null) return;

        var institution = await _institutionRepo.GetById(institutionId, ct);
        if (institution is null) return;

        var verifyUrl = $"{portalBaseUrl.TrimEnd('/')}/verify/{Uri.EscapeDataString(model.CertificateNumber)}";
        var externalEmails = additionalEmails?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? new List<string>();

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = institution.TenantId,
            EventType = NotificationEvents.ExportReady,
            Title = $"Compliance Certificate — {model.ReturnCode} {model.Period}",
            Message = $"The Compliance Certificate for {model.InstitutionName} ({model.ReturnCode} — {model.Period}) is ready. Certificate No: {model.CertificateNumber}. Verify at: {verifyUrl}",
            Priority = NotificationPriority.Normal,
            ActionUrl = verifyUrl,
            RecipientUserIds = new List<int> { requestingUserId },
            ExternalEmailAddresses = externalEmails,
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CertificateNumber"] = model.CertificateNumber,
                ["VerifyUrl"] = verifyUrl,
                ["ReturnCode"] = model.ReturnCode,
                ["Period"] = model.Period
            }
        }, ct);
    }

    private async Task<ComplianceCertificateModel> BuildCertificateModelAsync(Submission submission, Institution institution)
    {
        var submittedByName = "Unknown";
        if (submission.SubmittedByUserId is > 0)
        {
            var user = await _userRepo.GetById(submission.SubmittedByUserId.Value);
            submittedByName = user?.DisplayName ?? "Unknown";
        }

        var approvedByName = "";
        var approval = await _approvalRepo.GetBySubmission(submission.Id);
        if (approval?.ReviewedByUserId is > 0)
        {
            var approver = await _userRepo.GetById(approval.ReviewedByUserId.Value);
            approvedByName = approver?.DisplayName ?? "";
        }
        if (string.IsNullOrEmpty(approvedByName) && approval is not null)
            approvedByName = "Regulatory Officer";

        var certNum = $"CBN-FCE-{submission.Id:D8}-{submission.SubmittedAt:yyyyMMdd}";

        return new ComplianceCertificateModel
        {
            Branding = await _brandingService.GetBrandingConfig(submission.TenantId),
            InstitutionName = institution.InstitutionName,
            InstitutionCode = institution.InstitutionCode,
            InstitutionType = institution.LicenseType ?? "",
            ReturnCode = submission.ReturnCode,
            Period = FormatPeriod(submission.ReturnPeriod),
            SubmissionId = submission.Id,
            Status = submission.Status,
            SubmittedAt = submission.SubmittedAt,
            AcceptedAt = submission.ValidationReport?.FinalizedAt ?? submission.SubmittedAt,
            CertificateNumber = certNum,
            VerificationToken = certNum,
            GeneratedAt = DateTime.UtcNow,
            SubmittedByName = submittedByName,
            ApprovedByName = approvedByName,
            IsSuperseded = false // future: check for amendment submissions
        };
    }

    // === PERIODIC COMPLIANCE REPORT ===

    public async Task<ComplianceReportModel> BuildComplianceReportAsync(int institutionId, DateTime startDate, DateTime endDate)
    {
        var institution = await _institutionRepo.GetById(institutionId);
        var submissions = await _submissionRepo.GetByInstitution(institutionId);
        var users = await _userRepo.GetByInstitution(institutionId);
        var userMap = users.ToDictionary(u => u.Id, u => u.DisplayName ?? u.Username);

        var filtered = submissions
            .Where(s => s.SubmittedAt >= startDate && s.SubmittedAt <= endDate)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();

        var accepted = filtered.Count(s => s.Status is SubmissionStatus.Accepted or SubmissionStatus.AcceptedWithWarnings);
        var rejected = filtered.Count(s => s.Status is SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected);
        var pending = filtered.Count(s => s.Status is SubmissionStatus.PendingApproval or SubmissionStatus.Validating or SubmissionStatus.Parsing);
        var total = filtered.Count;
        var compliancePercent = total > 0 ? Math.Round((double)accepted / total * 100, 1) : 0;

        return new ComplianceReportModel
        {
            Branding = institution is null
                ? BrandingConfig.WithDefaults()
                : await _brandingService.GetBrandingConfig(institution.TenantId),
            InstitutionName = institution?.InstitutionName ?? "Unknown",
            InstitutionCode = institution?.InstitutionCode ?? "",
            StartDate = startDate,
            EndDate = endDate,
            TotalSubmissions = total,
            AcceptedCount = accepted,
            RejectedCount = rejected,
            PendingCount = pending,
            CompliancePercent = compliancePercent,
            Submissions = filtered.Select(s => new ComplianceReportItem
            {
                SubmissionId = s.Id,
                ReturnCode = s.ReturnCode,
                Period = FormatPeriod(s.ReturnPeriod),
                Status = s.Status,
                SubmittedAt = s.SubmittedAt,
                SubmittedBy = s.SubmittedByUserId.HasValue
                    ? userMap.GetValueOrDefault(s.SubmittedByUserId.Value, "Unknown")
                    : "API",
                ErrorCount = s.ValidationReport?.ErrorCount ?? 0,
                WarningCount = s.ValidationReport?.WarningCount ?? 0
            }).ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    // === AUDIT TRAIL ===

    public async Task<AuditTrailModel> BuildAuditTrailAsync(int institutionId, DateTime startDate, DateTime endDate)
    {
        var institution = await _institutionRepo.GetById(institutionId);
        var submissions = await _submissionRepo.GetByInstitution(institutionId);
        var users = await _userRepo.GetByInstitution(institutionId);
        var userMap = users.ToDictionary(u => u.Id, u => u.DisplayName ?? u.Username);
        var entries = new List<AuditTrailEntry>();

        foreach (var sub in submissions)
        {
            if (sub.SubmittedAt >= startDate && sub.SubmittedAt <= endDate)
            {
                entries.Add(new AuditTrailEntry
                {
                    Timestamp = sub.SubmittedAt,
                    UserName = sub.SubmittedByUserId.HasValue
                        ? userMap.GetValueOrDefault(sub.SubmittedByUserId.Value, "Unknown")
                        : "API",
                    Action = "Submitted",
                    ReturnCode = sub.ReturnCode,
                    Period = FormatPeriod(sub.ReturnPeriod),
                    SubmissionId = sub.Id,
                    Status = sub.Status.ToString(),
                    Details = $"Return: {sub.ReturnCode}"
                });
            }

            // Include approval actions
            var approval = await _approvalRepo.GetBySubmission(sub.Id);
            if (approval?.ReviewedAt is not null
                && approval.ReviewedAt >= startDate
                && approval.ReviewedAt <= endDate)
            {
                entries.Add(new AuditTrailEntry
                {
                    Timestamp = approval.ReviewedAt.Value,
                    UserName = approval.ReviewedByUserId.HasValue
                        ? userMap.GetValueOrDefault(approval.ReviewedByUserId.Value, "Unknown")
                        : "Unknown",
                    Action = approval.Status == ApprovalStatus.Approved ? "Approved" : "Rejected",
                    ReturnCode = sub.ReturnCode,
                    Period = FormatPeriod(sub.ReturnPeriod),
                    SubmissionId = sub.Id,
                    Status = sub.Status.ToString(),
                    Details = approval.ReviewerComments ?? ""
                });
            }
        }

        return new AuditTrailModel
        {
            Branding = institution is null
                ? BrandingConfig.WithDefaults()
                : await _brandingService.GetBrandingConfig(institution.TenantId),
            InstitutionName = institution?.InstitutionName ?? "Unknown",
            InstitutionCode = institution?.InstitutionCode ?? "",
            StartDate = startDate,
            EndDate = endDate,
            TotalEntries = entries.Count,
            Entries = entries.OrderByDescending(e => e.Timestamp).ToList(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    // === SUBMISSION AUDIT TRAIL PDF ===

    /// <summary>
    /// Generates a printable HTML document representing the full audit trail for a single submission.
    /// The returned bytes can be downloaded as a .html file; on open the browser triggers print (→ PDF).
    /// </summary>
    public async Task<byte[]> ExportAuditTrailPdfAsync(int submissionId, int institutionId)
    {
        var submission  = await _submissionRepo.GetByIdWithReport(submissionId);
        if (submission is null || submission.InstitutionId != institutionId) return [];

        var institution = await _institutionRepo.GetById(institutionId);
        var branding    = institution is not null
            ? await _brandingService.GetBrandingConfig(institution.TenantId)
            : BrandingConfig.WithDefaults();

        var approval = await _approvalRepo.GetBySubmission(submissionId);

        // Build event rows
        var entries = new List<(DateTime At, string Actor, string Action, string Detail, string Comment)>();

        entries.Add((submission.SubmittedAt, "System", "Submission Created", $"Return {submission.ReturnCode} — {FormatPeriod(submission.ReturnPeriod)}", ""));

        if (submission.SubmittedByUserId.HasValue)
        {
            var user = await _userRepo.GetById(submission.SubmittedByUserId.Value);
            var name = user?.DisplayName ?? "Unknown";
            entries.Add((submission.SubmittedAt, name, "Submitted", $"Status: {submission.Status}", approval?.SubmitterNotes ?? ""));
        }

        if (submission.ValidationReport is not null)
        {
            var errors   = submission.ValidationReport.ErrorCount;
            var warnings = submission.ValidationReport.WarningCount;
            entries.Add((submission.ValidationReport.FinalizedAt ?? submission.SubmittedAt,
                "Validation Engine", "Validated",
                $"{errors} error(s), {warnings} warning(s)", ""));
        }

        if (approval is not null && approval.ReviewedAt.HasValue)
        {
            string reviewer = "Unknown";
            if (approval.ReviewedByUserId.HasValue)
            {
                var rev = await _userRepo.GetById(approval.ReviewedByUserId.Value);
                reviewer = rev?.DisplayName ?? "Unknown";
            }
            var action = approval.Status == ApprovalStatus.Approved ? "Approved" : "Rejected";
            entries.Add((approval.ReviewedAt.Value, reviewer, action,
                $"Approval decision — {action.ToLower()}", approval.ReviewerComments ?? ""));
        }

        entries = entries.OrderBy(e => e.At).ToList();

        var institutionName = institution?.InstitutionName ?? "Unknown Institution";
        var primaryColor    = branding.PrimaryColor ?? "#006B3F";
        var generatedAt     = DateTime.UtcNow.ToString("dd MMM yyyy HH:mm 'UTC'");
        var period          = FormatPeriod(submission.ReturnPeriod);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head><meta charset=\"UTF-8\"/>");
        sb.AppendLine($"<title>Audit Trail \u2014 {submission.ReturnCode} {period}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; padding: 0; color: #1f2937; font-size: 12px; }");
        sb.AppendLine($".header {{ background: {primaryColor}; color: #fff; padding: 24px 32px; }}");
        sb.AppendLine(".header h1 { margin: 0 0 4px; font-size: 20px; }");
        sb.AppendLine(".header p  { margin: 0; opacity: .85; font-size: 11px; }");
        sb.AppendLine(".meta-strip { display: flex; gap: 32px; padding: 12px 32px; background: #f9fafb; border-bottom: 1px solid #e5e7eb; }");
        sb.AppendLine(".meta-strip span { font-size: 11px; color: #6b7280; }");
        sb.AppendLine(".meta-strip strong { color: #111827; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin: 0; }");
        sb.AppendLine("th { background: #f3f4f6; text-align: left; padding: 8px 16px; font-size: 10px; text-transform: uppercase; letter-spacing: .05em; color: #6b7280; border-bottom: 1px solid #e5e7eb; }");
        sb.AppendLine("td { padding: 10px 16px; border-bottom: 1px solid #f3f4f6; vertical-align: top; }");
        sb.AppendLine("tr:last-child td { border-bottom: none; }");
        sb.AppendLine(".actor { font-weight: 600; }");
        sb.AppendLine(".action { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 10px; font-weight: 600; text-transform: uppercase; }");
        sb.AppendLine(".action-Submitted  { background: #dbeafe; color: #1d4ed8; }");
        sb.AppendLine(".action-Approved   { background: #dcfce7; color: #15803d; }");
        sb.AppendLine(".action-Rejected   { background: #fee2e2; color: #b91c1c; }");
        sb.AppendLine(".action-Validated  { background: #d1fae5; color: #065f46; }");
        sb.AppendLine(".comment { margin-top: 4px; font-style: italic; color: #6b7280; font-size: 11px; }");
        sb.AppendLine(".footer  { padding: 16px 32px; font-size: 10px; color: #9ca3af; border-top: 1px solid #e5e7eb; }");
        sb.AppendLine("@media print { @page { margin: 1cm; } body { print-color-adjust: exact; -webkit-print-color-adjust: exact; } }");
        sb.AppendLine("</style></head>");
        sb.AppendLine("<body onload=\"window.print()\">");
        sb.AppendLine($"<div class=\"header\"><h1>Audit Trail Report</h1><p>{Esc(institutionName)} &mdash; {submission.ReturnCode} ({period})</p></div>");
        sb.AppendLine($"<div class=\"meta-strip\"><span>Submission ID: <strong>#{submission.Id}</strong></span><span>Status: <strong>{submission.Status}</strong></span><span>Generated: <strong>{generatedAt}</strong></span></div>");
        sb.AppendLine("<table><thead><tr><th>Timestamp</th><th>Actor</th><th>Action</th><th>Detail</th></tr></thead><tbody>");

        foreach (var (at, actor, action, detail, comment) in entries)
        {
            var actionClass = action.Replace(" ", "-");
            var commentHtml = string.IsNullOrEmpty(comment) ? "" : $"<div class=\"comment\">&ldquo;{Esc(comment)}&rdquo;</div>";
            sb.AppendLine($"<tr><td>{at.ToLocalTime():dd MMM yyyy HH:mm:ss}</td><td class=\"actor\">{Esc(actor)}</td><td><span class=\"action action-{actionClass}\">{action}</span></td><td>{Esc(detail)}{commentHtml}</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine($"<div class=\"footer\">This document was generated automatically by FC Engine on {generatedAt}. Submission #{submission.Id} &bull; {submission.ReturnCode} &bull; {Esc(institutionName)}</div>");
        sb.AppendLine("</body></html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    // === CSV GENERATION ===

    public async Task<string> ExportValidationErrorsCsvAsync(int submissionId, int institutionId)
    {
        var submission = await _submissionRepo.GetByIdWithReport(submissionId);
        if (submission is null || submission.InstitutionId != institutionId) return "";
        if (submission.ValidationReport is null) return "";

        var sb = new StringBuilder();
        sb.AppendLine("Rule ID,Severity,Category,Field,Message,Expected,Actual");
        foreach (var err in submission.ValidationReport.Errors)
        {
            sb.AppendLine($"{Esc(err.RuleId)},{Esc(err.Severity.ToString())},{Esc(err.Category.ToString())},{Esc(err.Field)},{Esc(err.Message)},{Esc(err.ExpectedValue ?? "")},{Esc(err.ActualValue ?? "")}");
        }
        return sb.ToString();
    }

    public async Task<string?> GetSubmissionXmlAsync(int submissionId, int institutionId)
    {
        var submission = await _submissionRepo.GetById(submissionId);
        if (submission is null || submission.InstitutionId != institutionId) return null;
        return submission.RawXml;
    }

    public async Task NotifyExportReady(
        int institutionId,
        int requestingUserId,
        string exportName,
        string? actionUrl = null,
        CancellationToken ct = default)
    {
        if (_notificationOrchestrator is null)
        {
            return;
        }

        var institution = await _institutionRepo.GetById(institutionId, ct);
        if (institution is null)
        {
            return;
        }

        await _notificationOrchestrator.Notify(new NotificationRequest
        {
            TenantId = institution.TenantId,
            EventType = NotificationEvents.ExportReady,
            Title = "Export ready",
            Message = $"{exportName} export is ready for download.",
            Priority = NotificationPriority.Normal,
            ActionUrl = actionUrl,
            RecipientUserIds = new List<int> { requestingUserId },
            Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExportName"] = exportName
            }
        }, ct);
    }

    public string ExportComplianceReportCsv(ComplianceReportModel report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Compliance Report,{Esc(report.InstitutionName)} ({Esc(report.InstitutionCode)})");
        sb.AppendLine($"Period,{report.StartDate:dd MMM yyyy} to {report.EndDate:dd MMM yyyy}");
        sb.AppendLine($"Compliance Rate,{report.CompliancePercent}%");
        sb.AppendLine($"Total,{report.TotalSubmissions},Accepted,{report.AcceptedCount},Rejected,{report.RejectedCount}");
        sb.AppendLine();
        sb.AppendLine("Submission ID,Return Code,Period,Status,Submitted At,Submitted By,Errors,Warnings");
        foreach (var item in report.Submissions)
            sb.AppendLine($"{item.SubmissionId},{Esc(item.ReturnCode)},{Esc(item.Period)},{Esc(item.Status.ToString())},{item.SubmittedAt:yyyy-MM-dd HH:mm},{Esc(item.SubmittedBy)},{item.ErrorCount},{item.WarningCount}");
        return sb.ToString();
    }

    public string ExportAuditTrailCsv(AuditTrailModel trail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Audit Trail,{Esc(trail.InstitutionName)} ({Esc(trail.InstitutionCode)})");
        sb.AppendLine($"Period,{trail.StartDate:dd MMM yyyy} to {trail.EndDate:dd MMM yyyy}");
        sb.AppendLine($"Total Entries,{trail.TotalEntries}");
        sb.AppendLine();
        sb.AppendLine("Date/Time,User,Action,Return Code,Period,Submission ID,Status,Details");
        foreach (var e in trail.Entries)
            sb.AppendLine($"{e.Timestamp:yyyy-MM-dd HH:mm},{Esc(e.UserName)},{Esc(e.Action)},{Esc(e.ReturnCode)},{Esc(e.Period)},{e.SubmissionId},{Esc(e.Status)},{Esc(e.Details)}");
        return sb.ToString();
    }

    // === HELPERS ===

    private static string FormatPeriod(ReturnPeriod? rp)
    {
        if (rp is null) return "—";
        if (rp.Month is > 0 and <= 12)
        {
            var monthName = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(rp.Month);
            return $"{monthName} {rp.Year}";
        }
        return rp.Year.ToString();
    }

    private static string Esc(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

// === DATA MODELS ===

public class ValidationReportModel
{
    public BrandingConfig Branding { get; set; } = BrandingConfig.WithDefaults();
    public string InstitutionName { get; set; } = "";
    public string InstitutionCode { get; set; } = "";
    public string InstitutionType { get; set; } = "";
    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = "";
    public string Period { get; set; } = "";
    public SubmissionStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string SubmittedBy { get; set; } = "";
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public List<ValidationReportError> Errors { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = "";
}

public class ValidationReportError
{
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "Error";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
}

public class ComplianceCertificateModel
{
    public BrandingConfig Branding { get; set; } = BrandingConfig.WithDefaults();
    public string InstitutionName { get; set; } = "";
    public string InstitutionCode { get; set; } = "";
    public string InstitutionType { get; set; } = "";
    public string ReturnCode { get; set; } = "";
    public string Period { get; set; } = "";
    public int SubmissionId { get; set; }
    public SubmissionStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime AcceptedAt { get; set; }
    public string CertificateNumber { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    // Approval chain
    public string SubmittedByName { get; set; } = "";
    public string ApprovedByName { get; set; } = "";
    // Validity
    public bool IsSuperseded { get; set; }
    // Verification
    public string VerificationToken { get; set; } = "";
}

public class ComplianceReportModel
{
    public BrandingConfig Branding { get; set; } = BrandingConfig.WithDefaults();
    public string InstitutionName { get; set; } = "";
    public string InstitutionCode { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalSubmissions { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }
    public int PendingCount { get; set; }
    public double CompliancePercent { get; set; }
    public List<ComplianceReportItem> Submissions { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class ComplianceReportItem
{
    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = "";
    public string Period { get; set; } = "";
    public SubmissionStatus Status { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string SubmittedBy { get; set; } = "";
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}

public class AuditTrailModel
{
    public BrandingConfig Branding { get; set; } = BrandingConfig.WithDefaults();
    public string InstitutionName { get; set; } = "";
    public string InstitutionCode { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalEntries { get; set; }
    public List<AuditTrailEntry> Entries { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}

public class AuditTrailEntry
{
    public DateTime Timestamp { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string ReturnCode { get; set; } = "";
    public string Period { get; set; } = "";
    public int SubmissionId { get; set; }
    public string Status { get; set; } = "";
    public string Details { get; set; } = "";
}
