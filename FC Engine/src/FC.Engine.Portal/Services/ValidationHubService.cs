namespace FC.Engine.Portal.Services;

using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.ValueObjects;

public class ValidationHubService
{
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IInstitutionRepository _institutionRepo;
    private readonly IInstitutionUserRepository _userRepo;
    private readonly ITenantBrandingService _brandingService;
    private readonly ITemplateMetadataCache _templateCache;

    public ValidationHubService(
        ISubmissionRepository submissionRepo,
        IInstitutionRepository institutionRepo,
        IInstitutionUserRepository userRepo,
        ITenantBrandingService brandingService,
        ITemplateMetadataCache templateCache)
    {
        _submissionRepo = submissionRepo;
        _institutionRepo = institutionRepo;
        _userRepo = userRepo;
        _brandingService = brandingService;
        _templateCache = templateCache;
    }

    public async Task<ValidationHubData?> GetHubDataAsync(
        int submissionId, int institutionId, CancellationToken ct = default)
    {
        var submission = await _submissionRepo.GetByIdWithReport(submissionId, ct);
        if (submission is null || submission.InstitutionId != institutionId) return null;

        var institution = await _institutionRepo.GetById(institutionId);
        if (institution is null) return null;

        var branding = await _brandingService.GetBrandingConfig(submission.TenantId);

        var submittedByName = "Unknown";
        if (submission.SubmittedByUserId is > 0)
        {
            var user = await _userRepo.GetById(submission.SubmittedByUserId.Value);
            submittedByName = user?.DisplayName ?? "Unknown";
        }

        var template = await _templateCache.GetPublishedTemplate(submission.ReturnCode, ct);
        var moduleCode = template?.ModuleCode;
        var report = submission.ValidationReport;
        var errors = report?.Errors ?? (IReadOnlyList<ValidationError>)[];

        var errorCount = report?.ErrorCount ?? 0;
        var warningCount = report?.WarningCount ?? 0;

        // Estimate total rules checked: at minimum errors+warnings, plus a
        // base of 10 "passed" rules for any submission that made it to cross-sheet.
        var minPassed = Math.Max(10, errors.Count / 2);
        var totalChecked = errorCount + warningCount + minPassed;
        var passedCount = minPassed;
        var complianceScore = totalChecked > 0
            ? Math.Round((decimal)(totalChecked - errorCount) / totalChecked * 100, 1)
            : 100m;

        var errorGroups = BuildErrorGroups(errors, submission.ReturnCode, submission.ReturnPeriodId);
        var history = await BuildHistoryAsync(
            institutionId, submission.ReturnCode, submission.ReturnPeriodId, submissionId, ct);

        return new ValidationHubData
        {
            SubmissionId = submissionId,
            ReturnCode = submission.ReturnCode,
            PeriodName = FormatPeriod(submission.ReturnPeriod),
            InstitutionName = institution.InstitutionName,
            ModuleCode = moduleCode,
            ModuleName = PortalSubmissionLinkBuilder.ResolveModuleName(moduleCode),
            SubmissionsHref = PortalSubmissionLinkBuilder.BuildSubmissionListHref(moduleCode),
            WorkspaceHref = PortalSubmissionLinkBuilder.ResolveWorkspaceHref(moduleCode),
            SubmitHref = PortalSubmissionLinkBuilder.BuildSubmitHref(submission.ReturnCode, moduleCode),
            FixSubmissionHref = PortalSubmissionLinkBuilder.BuildSubmitHref(submission.ReturnCode, moduleCode, submission.ReturnPeriodId),
            SubmittedBy = submittedByName,
            ValidatedAt = report?.FinalizedAt ?? submission.SubmittedAt ?? default,
            Status = submission.Status,
            TotalRulesChecked = totalChecked,
            PassedCount = passedCount,
            SoftFailureCount = warningCount,
            HardFailureCount = errorCount,
            ComplianceScore = complianceScore,
            ErrorGroups = errorGroups,
            History = history,
            Branding = branding,
            ReturnPeriodId = submission.ReturnPeriodId,
        };
    }

    // ── Error grouping & enrichment ──────────────────────────────────────────

    private static List<ValidationErrorGroup> BuildErrorGroups(
        IReadOnlyList<ValidationError> errors, string returnCode, int periodId)
    {
        var groups = new List<ValidationErrorGroup>();

        // Identify mandatory violations first (subset of TypeRange)
        var mandatory = errors
            .Where(e => e.Category == ValidationCategory.TypeRange
                && (e.Message.Contains("required", StringComparison.OrdinalIgnoreCase)
                    || e.Message.Contains("mandatory", StringComparison.OrdinalIgnoreCase)
                    || (e.ExpectedValue != null && e.ActualValue is null)))
            .ToHashSet();

        var crossSheet = errors.Where(e => e.Category == ValidationCategory.CrossSheet).ToList();
        var formula    = errors.Where(e => e.Category == ValidationCategory.IntraSheet).ToList();
        var dataType   = errors.Where(e =>
            e.Category == ValidationCategory.Schema
            || (e.Category == ValidationCategory.TypeRange && !mandatory.Contains(e))).ToList();
        var business   = errors.Where(e => e.Category == ValidationCategory.Business).ToList();

        if (crossSheet.Count > 0)
            groups.Add(new("Cross-Sheet Rule Failures",  "crosssheet",
                crossSheet.Select(e => EnrichError(e, returnCode, periodId)).ToList()));

        if (mandatory.Count > 0)
            groups.Add(new("Mandatory Field Violations", "mandatory",
                mandatory.Select(e => EnrichError(e, returnCode, periodId)).ToList()));

        if (formula.Count > 0)
            groups.Add(new("Formula Mismatches",         "formula",
                formula.Select(e => EnrichError(e, returnCode, periodId)).ToList()));

        if (dataType.Count > 0)
            groups.Add(new("Data Type Errors",           "datatype",
                dataType.Select(e => EnrichError(e, returnCode, periodId)).ToList()));

        if (business.Count > 0)
            groups.Add(new("Business Rule Violations",   "business",
                business.Select(e => EnrichError(e, returnCode, periodId)).ToList()));

        return groups;
    }

    private static EnrichedValidationError EnrichError(
        ValidationError err, string returnCode, int periodId)
    {
        var (plain, fix) = GetEnrichment(err);
        return new EnrichedValidationError
        {
            RuleCode             = err.RuleId,
            Message              = err.Message,
            Severity             = err.Severity.ToString(),
            Category             = err.Category.ToString(),
            FieldName            = err.Field,
            ExpectedValue        = err.ExpectedValue,
            ActualValue          = err.ActualValue,
            ReferencedReturnCode = err.ReferencedReturnCode,
            PlainEnglish         = plain,
            HowToFix             = fix,
            NavigationUrl        = string.IsNullOrWhiteSpace(err.Field) ? null
                : $"/submit/form/{returnCode}?periodId={periodId}&highlightField={Uri.EscapeDataString(err.Field)}",
        };
    }

    private static (string plain, string fix) GetEnrichment(ValidationError err)
    {
        return err.Category switch
        {
            ValidationCategory.CrossSheet => (
                $"A cross-sheet consistency rule requires that '{err.Field}' matches the corresponding value in another return template. The two values do not currently satisfy the rule constraint.",
                $"1. Use the 'Go to Field' link to navigate to '{err.Field}' in this return.\n"
                + "2. Also open the referenced template and check the corresponding value.\n"
                + "3. Reconcile both figures — they must balance within the allowed tolerance.\n"
                + "4. If both values are genuinely correct, ask your compliance officer whether a tolerance adjustment is needed.\n"
                + "5. Re-submit after correcting."),

            ValidationCategory.TypeRange when
                err.Message.Contains("required", StringComparison.OrdinalIgnoreCase)
                || err.Message.Contains("mandatory", StringComparison.OrdinalIgnoreCase)
                || (err.ExpectedValue is not null && err.ActualValue is null) => (
                $"The field '{err.Field}' is mandatory and was left blank or empty. All required fields must contain a valid value before the return can be accepted.",
                $"1. Use 'Go to Field' to jump directly to '{err.Field}'.\n"
                + $"2. Enter a valid value. {(err.ExpectedValue != null ? $"Expected format: {err.ExpectedValue}." : "")}\n"
                + "3. Do not leave mandatory fields empty; if the true value is zero, enter 0.\n"
                + "4. Save and re-submit."),

            ValidationCategory.TypeRange => (
                $"The value submitted for '{err.Field}' does not meet the required format or range. "
                + (err.ExpectedValue != null ? $"Expected: {err.ExpectedValue}. " : "")
                + (err.ActualValue   != null ? $"Submitted: {err.ActualValue}."  : ""),
                $"1. Go to the field '{err.Field}' in your return.\n"
                + "2. Verify the value is of the correct type (number, date, text).\n"
                + "3. Check that numeric values are within the allowed minimum and maximum range.\n"
                + "4. Remove any commas, currency symbols or extra spaces from numeric cells.\n"
                + "5. Correct the value and re-submit."),

            ValidationCategory.IntraSheet => (
                $"A formula check failed for '{err.Field}'. The submitted value does not equal the computed result based on other fields in the same template. "
                + (err.ExpectedValue != null ? $"Calculated: {err.ExpectedValue}. " : "")
                + (err.ActualValue   != null ? $"Entered: {err.ActualValue}."        : ""),
                $"1. Open the field '{err.Field}' and the fields it sums or derives from.\n"
                + "2. Recalculate manually or in a spreadsheet to confirm the expected result.\n"
                + "3. Common cause: rounding — use exact figures, not rounded approximations.\n"
                + "4. Update the field to match the computed result and re-submit."),

            ValidationCategory.Schema => (
                $"A structural error was found in the submitted file. The element or attribute '{err.Field}' was missing, malformed, or did not match the schema definition.",
                "1. Download the latest XSD schema for this template from the Templates section.\n"
                + "2. Validate your XML file against the schema before uploading.\n"
                + "3. Ensure all required XML elements are present and correctly named (case-sensitive).\n"
                + "4. Remove any extra whitespace, special characters, or BOM markers from the file.\n"
                + "5. Re-upload the corrected file."),

            ValidationCategory.Business => (
                $"A business rule failed for '{err.Field}': {err.Message}",
                $"1. Review rule {err.RuleId} in the Help Center for the full rule description.\n"
                + $"2. Check the value of '{err.Field}' against the rule criteria.\n"
                + "3. Consult your compliance officer if you believe this rule does not apply to your institution.\n"
                + "4. Adjust the value and re-submit."),

            _ => (err.Message, "Review the highlighted field, ensure it meets all submission requirements, and re-submit.")
        };
    }

    // ── Validation history ────────────────────────────────────────────────────

    private async Task<List<ValidationAttemptSummary>> BuildHistoryAsync(
        int institutionId, string returnCode, int periodId,
        int currentSubmissionId, CancellationToken ct)
    {
        var all = await _submissionRepo.GetByInstitution(institutionId, ct);
        return all
            .Where(s => s.ReturnCode.Equals(returnCode, StringComparison.OrdinalIgnoreCase)
                        && s.ReturnPeriodId == periodId)
            .OrderBy(s => s.SubmittedAt)
            .Select((s, i) => new ValidationAttemptSummary
            {
                SubmissionId   = s.Id,
                AttemptNumber  = i + 1,
                AttemptedAt    = s.SubmittedAt ?? default,
                ErrorCount     = s.ValidationReport?.ErrorCount ?? 0,
                WarningCount   = s.ValidationReport?.WarningCount ?? 0,
                Status         = s.Status.ToString(),
                IsCurrent      = s.Id == currentSubmissionId,
            })
            .ToList();
    }

    private static string FormatPeriod(ReturnPeriod? period)
    {
        if (period is null) return "—";
        var start = new DateTime(period.Year, period.Month, 1);
        return period.Frequency switch
        {
            "Monthly"   => $"{start:MMM yyyy}",
            "Quarterly" => $"Q{period.Quarter ?? (period.Month - 1) / 3 + 1} {period.Year}",
            "Annual"    => $"{period.Year}",
            _           => $"{start:dd MMM yyyy} – {start.AddMonths(1).AddDays(-1):dd MMM yyyy}",
        };
    }
}

// ─── View Models ─────────────────────────────────────────────────────────────

public class ValidationHubData
{
    public int             SubmissionId      { get; set; }
    public string          ReturnCode        { get; set; } = "";
    public string          PeriodName        { get; set; } = "";
    public string          InstitutionName   { get; set; } = "";
    public string?         ModuleCode        { get; set; }
    public string?         ModuleName        { get; set; }
    public string          SubmissionsHref   { get; set; } = "/submissions";
    public string?         WorkspaceHref     { get; set; }
    public string          SubmitHref        { get; set; } = "/submit";
    public string          FixSubmissionHref { get; set; } = "/submit";
    public string          SubmittedBy       { get; set; } = "";
    public DateTime        ValidatedAt       { get; set; }
    public SubmissionStatus Status           { get; set; }
    public int             TotalRulesChecked { get; set; }
    public int             PassedCount       { get; set; }
    public int             SoftFailureCount  { get; set; }
    public int             HardFailureCount  { get; set; }
    public decimal         ComplianceScore   { get; set; }
    public int             ReturnPeriodId    { get; set; }
    public List<ValidationErrorGroup>    ErrorGroups { get; set; } = new();
    public List<ValidationAttemptSummary> History    { get; set; } = new();
    public BrandingConfig? Branding { get; set; }
}

public class ValidationErrorGroup
{
    public string Name { get; set; }
    public string Key  { get; set; }
    public int    Count => Errors.Count;
    public List<EnrichedValidationError> Errors { get; set; }

    public ValidationErrorGroup(string name, string key, List<EnrichedValidationError> errors)
    {
        Name   = name;
        Key    = key;
        Errors = errors;
    }
}

public class EnrichedValidationError
{
    public string  RuleCode             { get; set; } = "";
    public string  Message              { get; set; } = "";
    public string  Severity             { get; set; } = "Error";
    public string  Category             { get; set; } = "";
    public string  FieldName            { get; set; } = "";
    public string? ExpectedValue        { get; set; }
    public string? ActualValue          { get; set; }
    public string? ReferencedReturnCode { get; set; }
    public string  PlainEnglish         { get; set; } = "";
    public string  HowToFix             { get; set; } = "";
    public string? NavigationUrl        { get; set; }
}

public class ValidationAttemptSummary
{
    public int    SubmissionId  { get; set; }
    public int    AttemptNumber { get; set; }
    public DateTime AttemptedAt { get; set; }
    public int    ErrorCount    { get; set; }
    public int    WarningCount  { get; set; }
    public string Status        { get; set; } = "";
    public bool   IsCurrent     { get; set; }
}
