using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using System.Text;

namespace FC.Engine.Portal.Services;

/// <summary>
/// Provides template browsing data and generates sample XML files
/// from template metadata for the FI Portal Template Browser page.
/// </summary>
public class TemplateBrowserService
{
    private readonly ITemplateMetadataCache _templateCache;
    private readonly ISubmissionRepository _submissionRepo;
    private readonly IXsdGenerator _xsdGenerator;
    private readonly IEntitlementService _entitlementService;
    private readonly ITenantContext _tenantContext;
    private readonly IFieldLocalisationService _fieldLocalisationService;
    private readonly IUserLanguagePreferenceService _languagePreferenceService;

    public TemplateBrowserService(
        ITemplateMetadataCache templateCache,
        ISubmissionRepository submissionRepo,
        IXsdGenerator xsdGenerator,
        IEntitlementService entitlementService,
        ITenantContext tenantContext,
        IFieldLocalisationService fieldLocalisationService,
        IUserLanguagePreferenceService languagePreferenceService)
    {
        _templateCache = templateCache;
        _submissionRepo = submissionRepo;
        _xsdGenerator = xsdGenerator;
        _entitlementService = entitlementService;
        _tenantContext = tenantContext;
        _fieldLocalisationService = fieldLocalisationService;
        _languagePreferenceService = languagePreferenceService;
    }

    /// <summary>
    /// Get all published templates for card display, filtered by the tenant's entitled modules.
    /// </summary>
    public async Task<List<TemplateBrowseItem>> GetAllTemplates(CancellationToken ct = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue)
        {
            return [];
        }

        return await GetAllTemplates(tenantId.Value, ct);
    }

    public async Task<List<TemplateBrowseItem>> GetAllTemplates(Guid tenantId, CancellationToken ct = default)
    {
        var templates = await GetScopedTemplatesAsync(tenantId, ct);
        return templates
            .Select(MapToBrowseItem)
            .OrderBy(t => t.ReturnCode)
            .ToList();
    }

    /// <summary>
    /// Get full template detail including fields, formulas, and item codes.
    /// </summary>
    public async Task<TemplateDetailModel?> GetTemplateDetail(string returnCode, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue)
        {
            return null;
        }

        return await GetTemplateDetail(returnCode, tenantId.Value, ct);
    }

    public async Task<TemplateDetailModel?> GetTemplateDetail(string returnCode, Guid tenantId, CancellationToken ct = default)
    {
        var template = await GetAccessibleTemplateAsync(returnCode, tenantId, ct);
        if (template is null)
        {
            return null;
        }

        var version = template.CurrentVersion;
        var language = await _languagePreferenceService.GetCurrentLanguage(ct);
        var localisations = await _fieldLocalisationService.GetLocalisations(
            version.Fields.Select(f => f.Id),
            language,
            ct);

        var fields = version.Fields
            .Select(f =>
            {
                localisations.TryGetValue(f.Id, out var localized);
                return new FieldDisplayItem
                {
                    FieldName = f.FieldName,
                    DisplayName = localized?.Label ?? f.DisplayName,
                    XmlElementName = f.XmlElementName,
                    DataType = f.DataType.ToString(),
                    IsRequired = f.IsRequired,
                    IsComputed = f.IsComputed,
                    IsKeyField = f.IsKeyField,
                    MinValue = f.MinValue,
                    MaxValue = f.MaxValue,
                    MaxLength = f.MaxLength,
                    AllowedValues = f.AllowedValues,
                    SectionName = f.SectionName ?? "General",
                    HelpText = localized?.HelpText ?? f.HelpText,
                    RegulatoryReference = f.RegulatoryReference,
                    FieldOrder = f.FieldOrder
                };
            })
            .OrderBy(f => f.SectionName)
            .ThenBy(f => f.FieldOrder)
            .ToList();

        var formulas = version.IntraSheetFormulas
            .Select(f => new FormulaDisplayItem
            {
                RuleCode = f.RuleCode,
                RuleName = f.RuleName,
                Type = f.FormulaType.ToString(),
                TargetField = f.TargetFieldName,
                OperandFields = f.OperandFields,
                Expression = f.CustomExpression ?? "",
                Severity = f.Severity.ToString(),
                ErrorMessage = f.ErrorMessage ?? ""
            })
            .OrderBy(f => f.RuleCode)
            .ToList();

        var itemCodes = version.ItemCodes
            .OrderBy(ic => ic.SortOrder)
            .Select(ic => new ItemCodeDisplayItem
            {
                ItemCode = ic.ItemCode,
                Description = ic.ItemDescription,
                IsTotalRow = ic.IsTotalRow
            })
            .ToList();

        return new TemplateDetailModel
        {
            ReturnCode = template.ReturnCode,
            TemplateName = template.Name,
            ModuleCode = template.ModuleCode,
            Frequency = template.Frequency.ToString(),
            StructuralCategory = template.StructuralCategory,
            XmlNamespace = template.XmlNamespace,
            XmlRootElement = template.XmlRootElement,
            FieldCount = fields.Count,
            FormulaCount = formulas.Count,
            ItemCodeCount = itemCodes.Count,
            Fields = fields,
            Formulas = formulas,
            ItemCodes = itemCodes
        };
    }

    /// <summary>
    /// Get XSD schema XML string for the given return code.
    /// </summary>
    public async Task<string?> GetSchemaXml(string returnCode, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue)
        {
            return null;
        }

        return await GetSchemaXml(returnCode, tenantId.Value, ct);
    }

    public async Task<string?> GetSchemaXml(string returnCode, Guid tenantId, CancellationToken ct = default)
    {
        if (await GetAccessibleTemplateAsync(returnCode, tenantId, ct) is null)
        {
            return null;
        }

        try
        {
            return await _xsdGenerator.GenerateSchemaXml(returnCode, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generate sample XML with placeholder values for the given return code.
    /// </summary>
    public async Task<string?> GenerateSampleXml(string returnCode, CancellationToken ct = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (!tenantId.HasValue)
        {
            return null;
        }

        return await GenerateSampleXml(returnCode, tenantId.Value, ct);
    }

    public async Task<string?> GenerateSampleXml(string returnCode, Guid tenantId, CancellationToken ct = default)
    {
        var template = await GetAccessibleTemplateAsync(returnCode, tenantId, ct);
        if (template is null)
        {
            return null;
        }

        var version = template.CurrentVersion;
        var fields = version.Fields;
        var ns = template.XmlNamespace;
        var rootElement = template.XmlRootElement;
        var category = template.StructuralCategory;

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<{rootElement} xmlns=\"{ns}\">");

        // Header section
        sb.AppendLine("  <Header>");
        sb.AppendLine("    <InstitutionCode>INST001</InstitutionCode>");
        sb.AppendLine($"    <ReportingDate>{DateTime.UtcNow.AddMonths(-1):yyyy-MM-dd}</ReportingDate>");
        sb.AppendLine($"    <ReturnCode>{template.ReturnCode}</ReturnCode>");
        sb.AppendLine("  </Header>");

        if (category.Equals("FixedRow", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("  <Data>");
            foreach (var field in fields)
            {
                var placeholder = GetPlaceholderValue(field);
                sb.AppendLine($"    <{field.XmlElementName}>{placeholder}</{field.XmlElementName}>");
            }
            sb.AppendLine("  </Data>");
        }
        else if (category.Equals("MultiRow", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine("  <Rows>");
            for (int row = 1; row <= 2; row++)
            {
                sb.AppendLine("    <Row>");
                foreach (var field in fields)
                {
                    if (field.FieldName == "serial_no")
                    {
                        sb.AppendLine($"      <{field.XmlElementName}>{row}</{field.XmlElementName}>");
                        continue;
                    }
                    var placeholder = GetPlaceholderValue(field);
                    sb.AppendLine($"      <{field.XmlElementName}>{placeholder}</{field.XmlElementName}>");
                }
                sb.AppendLine("    </Row>");
            }
            sb.AppendLine("  </Rows>");
        }
        else if (category.Equals("ItemCoded", StringComparison.OrdinalIgnoreCase))
        {
            var itemCodes = version.ItemCodes;
            var codes = itemCodes.Any()
                ? itemCodes.OrderBy(ic => ic.SortOrder).Take(3).Select(ic => ic.ItemCode).ToList()
                : new List<string> { "ITEM001", "ITEM002", "ITEM003" };

            sb.AppendLine("  <Rows>");
            foreach (var itemCode in codes)
            {
                sb.AppendLine("    <Row>");
                foreach (var field in fields)
                {
                    if (field.IsKeyField)
                    {
                        sb.AppendLine($"      <{field.XmlElementName}>{itemCode}</{field.XmlElementName}>");
                        continue;
                    }
                    var placeholder = GetPlaceholderValue(field);
                    sb.AppendLine($"      <{field.XmlElementName}>{placeholder}</{field.XmlElementName}>");
                }
                sb.AppendLine("    </Row>");
            }
            sb.AppendLine("  </Rows>");
        }

        sb.AppendLine($"</{rootElement}>");
        return sb.ToString();
    }

    /// <summary>
    /// Get submission history for a specific template and institution.
    /// </summary>
    public async Task<List<TemplateSubmissionHistoryItem>> GetSubmissionHistory(
        int institutionId, string returnCode, CancellationToken ct = default)
    {
        var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);

        return submissions
            .Where(s => s.ReturnCode.Equals(returnCode, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.SubmittedAt)
            .Take(10)
            .Select(s => new TemplateSubmissionHistoryItem
            {
                SubmissionId = s.Id,
                PeriodLabel = FormatPeriodLabel(s),
                SubmittedAt = s.SubmittedAt ?? default,
                Status = s.Status.ToString(),
                ErrorCount = s.ValidationReport?.ErrorCount ?? 0,
                WarningCount = s.ValidationReport?.WarningCount ?? 0
            })
            .ToList();
    }

    /// <summary>
    /// Get compliance status for each return code for a given institution.
    /// Returns a dictionary of returnCode -> "Submitted" | "Overdue" | "Pending"
    /// </summary>
    public async Task<Dictionary<string, string>> GetComplianceStatuses(int institutionId, CancellationToken ct = default)
    {
        if (institutionId <= 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var submissions = await _submissionRepo.GetByInstitution(institutionId, ct);

        return submissions
            .GroupBy(s => s.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var latest = g.OrderByDescending(s => s.SubmittedAt).First();
                    return latest.Status switch
                    {
                        SubmissionStatus.Accepted
                            or SubmissionStatus.AcceptedWithWarnings
                            or SubmissionStatus.SubmittedToRegulator
                            or SubmissionStatus.RegulatorAcknowledged
                            or SubmissionStatus.RegulatorAccepted
                            or SubmissionStatus.Historical => "Submitted",
                        SubmissionStatus.Rejected
                            or SubmissionStatus.ApprovalRejected
                            or SubmissionStatus.RegulatorQueriesRaised => "Overdue",
                        SubmissionStatus.PendingApproval
                            or SubmissionStatus.Validating
                            or SubmissionStatus.Parsing
                            or SubmissionStatus.Draft => "Pending",
                        _ => "Pending"
                    };
                },
                StringComparer.OrdinalIgnoreCase);
    }

    // ── Private Helpers ──────────────────────────────────────────

    private async Task<List<CachedTemplate>> GetScopedTemplatesAsync(Guid tenantId, CancellationToken ct)
    {
        var accessScope = await GetTemplateAccessScopeAsync(tenantId, ct);
        if (accessScope.ActiveModuleCodes.Count == 0 && accessScope.LicenceTypeCodes.Count == 0)
        {
            return [];
        }

        var templates = await _templateCache.GetAllPublishedTemplates(tenantId, ct);
        return templates
            .Where(t => IsTemplateAccessible(t, accessScope))
            .ToList();
    }

    private async Task<CachedTemplate?> GetAccessibleTemplateAsync(string returnCode, Guid tenantId, CancellationToken ct)
    {
        CachedTemplate template;
        try
        {
            template = await _templateCache.GetPublishedTemplate(tenantId, returnCode, ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var accessScope = await GetTemplateAccessScopeAsync(tenantId, ct);
        return IsTemplateAccessible(template, accessScope)
            ? template
            : null;
    }

    private async Task<TemplateAccessScope> GetTemplateAccessScopeAsync(Guid tenantId, CancellationToken ct)
    {
        var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
        return new TemplateAccessScope(
            entitlement.ActiveModules
                .Select(m => m.ModuleCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            entitlement.LicenceTypeCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsTemplateAccessible(CachedTemplate template, TemplateAccessScope accessScope)
    {
        if (!string.IsNullOrWhiteSpace(template.ModuleCode))
        {
            return accessScope.ActiveModuleCodes.Contains(template.ModuleCode);
        }

        if (!string.IsNullOrWhiteSpace(template.InstitutionType))
        {
            return accessScope.LicenceTypeCodes.Contains(template.InstitutionType);
        }

        return false;
    }

    private static TemplateBrowseItem MapToBrowseItem(CachedTemplate t)
    {
        return new TemplateBrowseItem
        {
            ReturnCode = t.ReturnCode,
            TemplateName = t.Name,
            Frequency = t.Frequency.ToString(),
            StructuralCategory = t.StructuralCategory,
            FieldCount = t.CurrentVersion.Fields.Count,
            FormulaCount = t.CurrentVersion.IntraSheetFormulas.Count,
            SectionCount = t.CurrentVersion.Fields
                .Select(f => f.SectionName ?? "General")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            ModuleCode = t.ModuleCode,
            RegulatoryBody = DeriveRegulatoryBody(t.ModuleCode, t.ReturnCode, t.InstitutionType)
        };
    }

    private static string DeriveRegulatoryBody(string? moduleCode, string returnCode, string? institutionType)
    {
        switch (institutionType?.Trim().ToUpperInvariant())
        {
            case "FC":
            case "BDC":
            case "DMB":
            case "MFB":
            case "PMB":
            case "PSP":
            case "DFI":
            case "IMTO":
                return "CBN";
            case "INSURANCE":
                return "NAICOM";
            case "PFA":
                return "PENCOM";
            case "CMO":
                return "SEC";
        }

        foreach (var s in new[] { moduleCode ?? "", returnCode })
        {
            if (s.StartsWith("CBN", StringComparison.OrdinalIgnoreCase)) return "CBN";
            if (s.StartsWith("SEC", StringComparison.OrdinalIgnoreCase)) return "SEC";
            if (s.StartsWith("NAICOM", StringComparison.OrdinalIgnoreCase)) return "NAICOM";
            if (s.StartsWith("PENCOM", StringComparison.OrdinalIgnoreCase)) return "PENCOM";
            if (s.StartsWith("FIRS", StringComparison.OrdinalIgnoreCase)) return "FIRS";
        }
        return "Other";
    }

    private sealed record TemplateAccessScope(
        HashSet<string> ActiveModuleCodes,
        HashSet<string> LicenceTypeCodes);

    private static string GetPlaceholderValue(TemplateField field)
    {
        if (field.DataType == FieldDataType.Money || field.DataType == FieldDataType.Decimal)
            return "0.00";
        if (field.DataType == FieldDataType.Percentage)
            return "0.00";
        if (field.DataType == FieldDataType.Integer)
            return "0";
        if (field.DataType == FieldDataType.Text)
            return "SAMPLE_TEXT";
        if (field.DataType == FieldDataType.Date)
            return DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (field.DataType == FieldDataType.Boolean)
            return "true";
        return "VALUE";
    }

    private static string FormatPeriodLabel(Domain.Entities.Submission s)
    {
        if (s.ReturnPeriod is not null)
        {
            var rp = s.ReturnPeriod;
            return new DateTime(rp.Year, rp.Month, 1).ToString("MMMM yyyy");
        }
        return (s.SubmittedAt ?? DateTime.UtcNow).ToString("MMMM yyyy");
    }
}

// ── Data Models ──────────────────────────────────────────────────────

public class TemplateBrowseItem
{
    public string ReturnCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string StructuralCategory { get; set; } = "";
    public int FieldCount { get; set; }
    public int FormulaCount { get; set; }
    public int SectionCount { get; set; }
    public string? ModuleCode { get; set; }
    public string RegulatoryBody { get; set; } = "Other";
}

public class TemplateDetailModel
{
    public string ReturnCode { get; set; } = "";
    public string TemplateName { get; set; } = "";
    public string? ModuleCode { get; set; }
    public string Frequency { get; set; } = "";
    public string StructuralCategory { get; set; } = "";
    public string XmlNamespace { get; set; } = "";
    public string XmlRootElement { get; set; } = "";
    public int FieldCount { get; set; }
    public int FormulaCount { get; set; }
    public int ItemCodeCount { get; set; }
    public List<FieldDisplayItem> Fields { get; set; } = new();
    public List<FormulaDisplayItem> Formulas { get; set; } = new();
    public List<ItemCodeDisplayItem> ItemCodes { get; set; } = new();
}

public class FieldDisplayItem
{
    public string FieldName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string XmlElementName { get; set; } = "";
    public string DataType { get; set; } = "";
    public bool IsRequired { get; set; }
    public bool IsComputed { get; set; }
    public bool IsKeyField { get; set; }
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public int? MaxLength { get; set; }
    public string? AllowedValues { get; set; }
    public string SectionName { get; set; } = "General";
    public string? HelpText { get; set; }
    public string? RegulatoryReference { get; set; }
    public int FieldOrder { get; set; }

    public string ConstraintsSummary
    {
        get
        {
            var parts = new List<string>();
            if (MinValue is not null) parts.Add($"Min: {MinValue}");
            if (MaxValue is not null) parts.Add($"Max: {MaxValue}");
            if (MaxLength is not null) parts.Add($"MaxLen: {MaxLength}");
            if (!string.IsNullOrEmpty(AllowedValues)) parts.Add($"Values: {AllowedValues}");
            return parts.Any() ? string.Join(" · ", parts) : "\u2014";
        }
    }
}

public class FormulaDisplayItem
{
    public string RuleCode { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string Type { get; set; } = "";
    public string TargetField { get; set; } = "";
    public string OperandFields { get; set; } = "";
    public string Expression { get; set; } = "";
    public string Severity { get; set; } = "Error";
    public string ErrorMessage { get; set; } = "";
}

public class ItemCodeDisplayItem
{
    public string ItemCode { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsTotalRow { get; set; }
}

public class TemplateSubmissionHistoryItem
{
    public int SubmissionId { get; set; }
    public string PeriodLabel { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = "";
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
}
