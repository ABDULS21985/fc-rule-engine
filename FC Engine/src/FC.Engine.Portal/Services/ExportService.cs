using System.Globalization;
using System.Text;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Portal.Services;

public class ExportService
{
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IInstitutionRepository _institutionRepo;
    private readonly IInstitutionUserRepository _userRepo;
    private readonly ISubmissionApprovalRepository _approvalRepo;
    private readonly ITenantBrandingService _brandingService;

    public ExportService(
        ISubmissionRepository submissionRepo,
        IInstitutionRepository institutionRepo,
        IInstitutionUserRepository userRepo,
        ISubmissionApprovalRepository approvalRepo,
        ITenantBrandingService brandingService)
    {
        _submissionRepo = submissionRepo;
        _institutionRepo = institutionRepo;
        _userRepo = userRepo;
        _approvalRepo = approvalRepo;
        _brandingService = brandingService;
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
            CertificateNumber = $"CBN-FCE-{submission.Id:D8}-{submission.SubmittedAt:yyyyMMdd}",
            GeneratedAt = DateTime.UtcNow
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
