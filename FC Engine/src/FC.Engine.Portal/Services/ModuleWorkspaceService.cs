using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Portal.Services;

public sealed class ModuleWorkspaceService
{
    private readonly ITenantContext _tenantContext;
    private readonly IEntitlementService _entitlementService;
    private readonly IInstitutionRepository _institutionRepository;
    private readonly ISubmissionRepository _submissionRepository;
    private readonly ITemplateMetadataCache _templateCache;
    private readonly IDashboardService _dashboardService;
    private readonly IDbContextFactory<MetadataDbContext> _dbFactory;
    private readonly KnowledgeBaseService _knowledgeBaseService;
    private readonly ILogger<ModuleWorkspaceService> _logger;

    public ModuleWorkspaceService(
        ITenantContext tenantContext,
        IEntitlementService entitlementService,
        IInstitutionRepository institutionRepository,
        ISubmissionRepository submissionRepository,
        ITemplateMetadataCache templateCache,
        IDashboardService dashboardService,
        IDbContextFactory<MetadataDbContext> dbFactory,
        KnowledgeBaseService knowledgeBaseService,
        ILogger<ModuleWorkspaceService> logger)
    {
        _tenantContext = tenantContext;
        _entitlementService = entitlementService;
        _institutionRepository = institutionRepository;
        _submissionRepository = submissionRepository;
        _templateCache = templateCache;
        _dashboardService = dashboardService;
        _dbFactory = dbFactory;
        _knowledgeBaseService = knowledgeBaseService;
        _logger = logger;
    }

    public async Task<InstitutionModuleWorkspaceModel?> GetWorkspaceAsync(string moduleKey, CancellationToken ct = default)
    {
        if (!PortalModuleWorkspaceCatalog.TryGetDefinition(moduleKey, out var definition))
        {
            return null;
        }

        if (_tenantContext.CurrentTenantId is not { } tenantId)
        {
            return BuildUnavailableWorkspace(definition, ModuleWorkspaceAccessState.Unsupported, "Tenant context is not available in the current portal session.");
        }

        var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
        var accessState = entitlement.ActiveModules.Any(x => string.Equals(x.ModuleCode, definition.ModuleCode, StringComparison.OrdinalIgnoreCase))
            ? ModuleWorkspaceAccessState.Active
            : entitlement.EligibleModules.Any(x => string.Equals(x.ModuleCode, definition.ModuleCode, StringComparison.OrdinalIgnoreCase))
                ? ModuleWorkspaceAccessState.EligibleNotActivated
                : ModuleWorkspaceAccessState.NotAvailable;

        var institutions = await _institutionRepository.GetByTenant(tenantId, ct);
        var primaryInstitution = institutions
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Id)
            .FirstOrDefault();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var module = await db.Modules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ModuleCode == definition.ModuleCode, ct);

        var templates = (await _templateCache.GetAllPublishedTemplates(ct))
            .Where(x => string.Equals(x.ModuleCode, definition.ModuleCode, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var templateCodes = templates
            .Select(x => x.ReturnCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var openPeriods = module is null
            ? new List<ReturnPeriod>()
            : await db.ReturnPeriods
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.ModuleId == module.Id)
                .OrderByDescending(x => x.Year)
                .ThenByDescending(x => x.Month)
                .ThenByDescending(x => x.Quarter)
                .ThenByDescending(x => x.Id)
                .Take(12)
                .ToListAsync(ct);

        var submissions = primaryInstitution is null
            ? new List<Submission>()
            : (await _submissionRepository.GetByInstitution(primaryInstitution.Id, ct))
                .Where(x => templateCodes.Contains(x.ReturnCode))
                .OrderByDescending(x => x.SubmittedAt)
                .ToList();

        ModuleDashboardData? dashboard = null;
        if (accessState == ModuleWorkspaceAccessState.Active)
        {
            try
            {
                dashboard = await _dashboardService.GetModuleDashboard(tenantId, definition.ModuleCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Module dashboard load failed for tenant {TenantId} and module {ModuleCode}.", tenantId, definition.ModuleCode);
            }
        }

        var latestSubmissionByTemplate = submissions
            .GroupBy(x => x.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var openPeriodCount = openPeriods.Count(x => x.IsOpen);
        var dueSoonCount = openPeriods.Count(x => x.IsOpen && x.EffectiveDeadline <= now.AddDays(14));
        var pendingQueueCount = submissions.Count(x => x.Status is SubmissionStatus.PendingApproval or SubmissionStatus.Draft or SubmissionStatus.ApprovalRejected or SubmissionStatus.Rejected);

        var templateRows = templates
            .Select(template =>
            {
                latestSubmissionByTemplate.TryGetValue(template.ReturnCode, out var latestSubmission);
                return new ModuleWorkspaceTemplateRow
                {
                    ReturnCode = template.ReturnCode,
                    TemplateName = template.Name,
                    Frequency = template.Frequency.ToString(),
                    StructuralCategory = template.StructuralCategory,
                    FieldCount = template.CurrentVersion.Fields.Count,
                    FormulaCount = template.CurrentVersion.IntraSheetFormulas.Count,
                    LastSubmissionId = latestSubmission?.Id,
                    LastSubmissionAt = latestSubmission?.SubmittedAt,
                    LastSubmissionStatus = latestSubmission?.Status.ToString(),
                    StartHref = $"/submit?module={Uri.EscapeDataString(definition.ModuleCode)}&returnCode={Uri.EscapeDataString(template.ReturnCode)}",
                    DetailHref = $"/templates/{Uri.EscapeDataString(template.ReturnCode)}"
                };
            })
            .Take(8)
            .ToList();

        var recentSubmissions = submissions
            .Take(8)
            .Select(submission => new ModuleWorkspaceSubmissionRow
            {
                SubmissionId = submission.Id,
                ReturnCode = submission.ReturnCode,
                PeriodLabel = submission.ReturnPeriod is null
                    ? (submission.SubmittedAt ?? submission.CreatedAt).ToString("MMM yyyy")
                    : new DateTime(submission.ReturnPeriod.Year, submission.ReturnPeriod.Month, 1).ToString("MMM yyyy"),
                SubmittedAt = submission.SubmittedAt ?? submission.CreatedAt,
                Status = submission.Status.ToString(),
                ErrorCount = submission.ValidationReport?.ErrorCount ?? 0,
                WarningCount = submission.ValidationReport?.WarningCount ?? 0,
                DetailHref = $"/submissions/{submission.Id}",
                ActionLabel = ResolveSubmissionActionLabel(submission),
                ActionHref = ResolveSubmissionActionHref(definition.ModuleCode, submission)
            })
            .ToList();

        var attentionItems = submissions
            .Select(submission => BuildAttentionItem(definition.ModuleCode, submission))
            .Where(item => item is not null)
            .Cast<ModuleWorkspaceAttentionItem>()
            .OrderByDescending(item => item.Priority)
            .ThenByDescending(item => item.OccurredAt)
            .Take(6)
            .ToList();

        var periodRows = openPeriods
            .Take(8)
            .Select(period => new ModuleWorkspacePeriodRow
            {
                PeriodId = period.Id,
                Label = new DateTime(period.Year, period.Month, 1).ToString("MMMM yyyy"),
                ReportingDate = period.ReportingDate,
                Deadline = period.EffectiveDeadline,
                Status = period.Status,
                IsOpen = period.IsOpen,
                IsDueSoon = period.IsOpen && period.EffectiveDeadline <= now.AddDays(14)
            })
            .ToList();

        var helpArticles = (await _knowledgeBaseService.Search(
                query: null,
                moduleCode: definition.ModuleCode,
                category: null,
                take: 5,
                ct: ct))
            .ToList();

        return new InstitutionModuleWorkspaceModel
        {
            ModuleCode = definition.ModuleCode,
            ModuleName = module?.ModuleName ?? definition.Title,
            RegulatorCode = module?.RegulatorCode ?? "CBN",
            DefaultFrequency = module?.DefaultFrequency ?? templates.Select(x => x.Frequency.ToString()).FirstOrDefault() ?? "Monthly",
            SheetCount = module?.SheetCount ?? templates.Count,
            Title = definition.Title,
            Eyebrow = definition.Eyebrow,
            Summary = definition.Summary,
            FocusAreas = definition.FocusAreas.ToList(),
            WorkspaceHref = PortalModuleWorkspaceCatalog.GetWorkspaceHref(definition.ModuleCode),
            ModuleDashboardHref = $"/dashboard/module/{Uri.EscapeDataString(definition.ModuleCode)}",
            SubmitHref = $"/submit?module={Uri.EscapeDataString(definition.ModuleCode)}",
            BulkSubmitHref = PortalSubmissionLinkBuilder.BuildBulkSubmitHref(definition.ModuleCode),
            TemplatesHref = $"/templates?module={Uri.EscapeDataString(definition.ModuleCode)}",
            SubmissionsHref = $"/submissions?module={Uri.EscapeDataString(definition.ModuleCode)}",
            AccessState = accessState,
            AccessMessage = BuildAccessMessage(accessState, definition.Title),
            InstitutionName = primaryInstitution?.InstitutionName ?? "Institution workspace",
            GeneratedAt = now,
            Metrics =
            [
                new("Templates", templates.Count, "published returns available", "neutral"),
                new("Open periods", openPeriodCount, "period windows ready to file", openPeriodCount > 0 ? "success" : "warning"),
                new("Due in 14 days", dueSoonCount, "upcoming filing deadlines", dueSoonCount > 0 ? "warning" : "success"),
                new("Workflow queue", pendingQueueCount, "draft, rejected, or pending items", pendingQueueCount > 0 ? "danger" : "success"),
                new("Attention items", attentionItems.Count, "actions ready for filing, fixing, or review", attentionItems.Count > 0 ? "warning" : "success")
            ],
            WorkflowSteps = BuildWorkflowSteps(accessState, templates.Count, openPeriodCount, pendingQueueCount),
            TemplateRows = templateRows,
            RecentSubmissions = recentSubmissions,
            AttentionItems = attentionItems,
            PeriodRows = periodRows,
            CurrentPeriods = dashboard?.Periods.TakeLast(4).ToList() ?? new List<ModulePeriodStatusItem>(),
            HelpArticles = helpArticles
        };
    }

    private static InstitutionModuleWorkspaceModel BuildUnavailableWorkspace(
        PortalModuleWorkspaceDefinition definition,
        ModuleWorkspaceAccessState accessState,
        string message)
    {
        return new InstitutionModuleWorkspaceModel
        {
            ModuleCode = definition.ModuleCode,
            ModuleName = definition.Title,
            Title = definition.Title,
            Eyebrow = definition.Eyebrow,
            Summary = definition.Summary,
            FocusAreas = definition.FocusAreas.ToList(),
            WorkspaceHref = PortalModuleWorkspaceCatalog.GetWorkspaceHref(definition.ModuleCode),
            SubmitHref = "/submit",
            BulkSubmitHref = "/submit/bulk",
            TemplatesHref = "/templates",
            SubmissionsHref = "/submissions",
            ModuleDashboardHref = "/dashboard/compliance",
            AccessState = accessState,
            AccessMessage = message,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private static string BuildAccessMessage(ModuleWorkspaceAccessState accessState, string title) => accessState switch
    {
        ModuleWorkspaceAccessState.Active => $"{title} is active for this institution. Use this workspace to launch filings, review periods, and monitor recent module activity.",
        ModuleWorkspaceAccessState.EligibleNotActivated => $"{title} is available to your institution but has not been activated yet. Turn on the module in Subscription Modules to unlock filing, template, and dashboard actions.",
        ModuleWorkspaceAccessState.NotAvailable => $"{title} is not currently available for this institution's licence and plan combination.",
        _ => $"{title} cannot be resolved in the current portal context."
    };

    private static List<ModuleWorkspaceStep> BuildWorkflowSteps(
        ModuleWorkspaceAccessState accessState,
        int templateCount,
        int openPeriodCount,
        int pendingQueueCount)
    {
        return
        [
            new()
            {
                Title = "Confirm module access",
                Detail = accessState == ModuleWorkspaceAccessState.Active
                    ? "The module is active and ready for institution use."
                    : "Activate the module from Subscription Modules before filing.",
                Status = accessState == ModuleWorkspaceAccessState.Active ? "complete" : "attention"
            },
            new()
            {
                Title = "Review return templates",
                Detail = templateCount > 0
                    ? $"{templateCount} published return template(s) are ready."
                    : "No published templates are currently available for this module.",
                Status = templateCount > 0 ? "complete" : "attention"
            },
            new()
            {
                Title = "Open the filing window",
                Detail = openPeriodCount > 0
                    ? $"{openPeriodCount} period window(s) are currently open."
                    : "No open periods are available yet for this module.",
                Status = openPeriodCount > 0 ? "in-progress" : "attention"
            },
            new()
            {
                Title = "Resolve workflow queue",
                Detail = pendingQueueCount > 0
                    ? $"{pendingQueueCount} item(s) still need review, resubmission, or approval."
                    : "No draft, rejected, or approval-pending items are blocking the module queue.",
                Status = pendingQueueCount > 0 ? "in-progress" : "complete"
            }
        ];
    }

    private static ModuleWorkspaceAttentionItem? BuildAttentionItem(string moduleCode, Submission submission)
    {
        var periodLabel = submission.ReturnPeriod is null
            ? (submission.SubmittedAt ?? submission.CreatedAt).ToString("MMM yyyy")
            : new DateTime(submission.ReturnPeriod.Year, submission.ReturnPeriod.Month, 1).ToString("MMM yyyy");

        return submission.Status switch
        {
            SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected => new ModuleWorkspaceAttentionItem
            {
                ReturnCode = submission.ReturnCode,
                Title = $"{submission.ReturnCode} needs correction",
                Detail = $"{periodLabel} was rejected and should be corrected before the next submission attempt.",
                Status = submission.Status.ToString(),
                Tone = "danger",
                ActionLabel = "Fix Return",
                ActionHref = PortalSubmissionLinkBuilder.BuildSubmitHref(submission.ReturnCode, moduleCode, submission.ReturnPeriodId),
                OccurredAt = submission.SubmittedAt ?? submission.CreatedAt,
                Priority = 400
            },
            SubmissionStatus.PendingApproval => new ModuleWorkspaceAttentionItem
            {
                ReturnCode = submission.ReturnCode,
                Title = $"{submission.ReturnCode} is awaiting checker review",
                Detail = $"{periodLabel} is in the approval queue and should be reviewed before the filing window closes.",
                Status = submission.Status.ToString(),
                Tone = "info",
                ActionLabel = "Open Submission",
                ActionHref = $"/submissions/{submission.Id}",
                OccurredAt = submission.SubmittedAt ?? submission.CreatedAt,
                Priority = 300
            },
            SubmissionStatus.Draft => new ModuleWorkspaceAttentionItem
            {
                ReturnCode = submission.ReturnCode,
                Title = $"{submission.ReturnCode} draft is still open",
                Detail = $"{periodLabel} has a draft in progress. Resume filing to complete validation and submission.",
                Status = submission.Status.ToString(),
                Tone = "warning",
                ActionLabel = "Resume Filing",
                ActionHref = PortalSubmissionLinkBuilder.BuildSubmitHref(submission.ReturnCode, moduleCode, submission.ReturnPeriodId),
                OccurredAt = submission.SubmittedAt ?? submission.CreatedAt,
                Priority = 200
            },
            SubmissionStatus.AcceptedWithWarnings when (submission.ValidationReport?.WarningCount ?? 0) > 0 || (submission.ValidationReport?.ErrorCount ?? 0) > 0 => new ModuleWorkspaceAttentionItem
            {
                ReturnCode = submission.ReturnCode,
                Title = $"{submission.ReturnCode} has validation follow-up",
                Detail = $"{periodLabel} was accepted with validation findings that should still be reviewed in the validation hub.",
                Status = submission.Status.ToString(),
                Tone = "warning",
                ActionLabel = "Review Validation",
                ActionHref = $"/validation/hub/{submission.Id}",
                OccurredAt = submission.SubmittedAt ?? submission.CreatedAt,
                Priority = 100
            },
            _ => null
        };
    }

    private static string? ResolveSubmissionActionLabel(Submission submission) => submission.Status switch
    {
        SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected => "Fix Return",
        SubmissionStatus.PendingApproval => "Open Submission",
        SubmissionStatus.Draft => "Resume Filing",
        SubmissionStatus.AcceptedWithWarnings when (submission.ValidationReport?.WarningCount ?? 0) > 0 || (submission.ValidationReport?.ErrorCount ?? 0) > 0 => "Review Validation",
        _ => null
    };

    private static string? ResolveSubmissionActionHref(string moduleCode, Submission submission) => submission.Status switch
    {
        SubmissionStatus.Rejected or SubmissionStatus.ApprovalRejected => PortalSubmissionLinkBuilder.BuildSubmitHref(submission.ReturnCode, moduleCode, submission.ReturnPeriodId),
        SubmissionStatus.PendingApproval => $"/submissions/{submission.Id}",
        SubmissionStatus.Draft => PortalSubmissionLinkBuilder.BuildSubmitHref(submission.ReturnCode, moduleCode, submission.ReturnPeriodId),
        SubmissionStatus.AcceptedWithWarnings when (submission.ValidationReport?.WarningCount ?? 0) > 0 || (submission.ValidationReport?.ErrorCount ?? 0) > 0 => $"/validation/hub/{submission.Id}",
        _ => null
    };
}

public sealed class InstitutionModuleWorkspaceModel
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RegulatorCode { get; set; } = "CBN";
    public string DefaultFrequency { get; set; } = "Monthly";
    public int SheetCount { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Eyebrow { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public List<string> FocusAreas { get; set; } = new();
    public string WorkspaceHref { get; set; } = "/modules";
    public string SubmitHref { get; set; } = "/submit";
    public string BulkSubmitHref { get; set; } = "/submit/bulk";
    public string TemplatesHref { get; set; } = "/templates";
    public string SubmissionsHref { get; set; } = "/submissions";
    public string ModuleDashboardHref { get; set; } = "/dashboard/compliance";
    public ModuleWorkspaceAccessState AccessState { get; set; } = ModuleWorkspaceAccessState.Unsupported;
    public string AccessMessage { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<ModuleWorkspaceMetric> Metrics { get; set; } = new();
    public List<ModuleWorkspaceStep> WorkflowSteps { get; set; } = new();
    public List<ModuleWorkspaceTemplateRow> TemplateRows { get; set; } = new();
    public List<ModuleWorkspaceSubmissionRow> RecentSubmissions { get; set; } = new();
    public List<ModuleWorkspaceAttentionItem> AttentionItems { get; set; } = new();
    public List<ModuleWorkspacePeriodRow> PeriodRows { get; set; } = new();
    public List<ModulePeriodStatusItem> CurrentPeriods { get; set; } = new();
    public List<KnowledgeBaseArticleView> HelpArticles { get; set; } = new();
}

public enum ModuleWorkspaceAccessState
{
    Unsupported,
    NotAvailable,
    EligibleNotActivated,
    Active
}

public sealed record ModuleWorkspaceMetric(string Label, int Value, string Detail, string Tone);

public sealed class ModuleWorkspaceStep
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = "attention";
}

public sealed class ModuleWorkspaceTemplateRow
{
    public string ReturnCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public string StructuralCategory { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public int FormulaCount { get; set; }
    public int? LastSubmissionId { get; set; }
    public DateTime? LastSubmissionAt { get; set; }
    public string? LastSubmissionStatus { get; set; }
    public string StartHref { get; set; } = string.Empty;
    public string DetailHref { get; set; } = string.Empty;
}

public sealed class ModuleWorkspaceSubmissionRow
{
    public int SubmissionId { get; set; }
    public string ReturnCode { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public string DetailHref { get; set; } = string.Empty;
    public string? ActionLabel { get; set; }
    public string? ActionHref { get; set; }
}

public sealed class ModuleWorkspaceAttentionItem
{
    public string ReturnCode { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Tone { get; set; } = "warning";
    public string ActionLabel { get; set; } = string.Empty;
    public string ActionHref { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public int Priority { get; set; }
}

public sealed class ModuleWorkspacePeriodRow
{
    public int PeriodId { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime ReportingDate { get; set; }
    public DateTime Deadline { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public bool IsDueSoon { get; set; }
}
