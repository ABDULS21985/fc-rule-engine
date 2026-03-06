using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.ValueObjects;

namespace FC.Engine.Portal.Services;

public class ReportBuilderService
{
    private readonly ISavedReportRepository _reportRepo;
    private readonly IReportQueryEngine _queryEngine;
    private readonly IBoardPackGenerator _boardPackGenerator;
    private readonly IEntitlementService _entitlementService;
    private readonly ITemplateMetadataCache _templateCache;
    private readonly ITenantBrandingService _brandingService;
    private readonly IInstitutionRepository _institutionRepo;

    public ReportBuilderService(
        ISavedReportRepository reportRepo,
        IReportQueryEngine queryEngine,
        IBoardPackGenerator boardPackGenerator,
        IEntitlementService entitlementService,
        ITemplateMetadataCache templateCache,
        ITenantBrandingService brandingService,
        IInstitutionRepository institutionRepo)
    {
        _reportRepo = reportRepo;
        _queryEngine = queryEngine;
        _boardPackGenerator = boardPackGenerator;
        _entitlementService = entitlementService;
        _templateCache = templateCache;
        _brandingService = brandingService;
        _institutionRepo = institutionRepo;
    }

    public async Task<bool> HasReportBuilderAccess(Guid tenantId, CancellationToken ct = default)
    {
        return await _entitlementService.HasFeatureAccess(tenantId, "report_builder", ct);
    }

    public async Task<List<ModuleFieldTree>> GetFieldTree(Guid tenantId, CancellationToken ct = default)
    {
        var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
        var tree = new List<ModuleFieldTree>();

        var allTemplates = await _templateCache.GetAllPublishedTemplates(tenantId, ct);

        foreach (var module in entitlement.ActiveModules)
        {
            var moduleTemplates = allTemplates
                .Where(t => t.ModuleId == module.ModuleId)
                .ToList();

            var moduleNode = new ModuleFieldTree
            {
                ModuleCode = module.ModuleCode,
                ModuleName = module.ModuleName
            };

            foreach (var template in moduleTemplates)
            {
                if (template.CurrentVersion is null) continue;

                var templateNode = new TemplateFieldNode
                {
                    TemplateCode = template.ReturnCode,
                    TemplateName = template.Name
                };

                foreach (var field in template.CurrentVersion.Fields.OrderBy(f => f.FieldOrder))
                {
                    templateNode.Fields.Add(new FieldNode
                    {
                        FieldCode = field.FieldName,
                        FieldLabel = field.DisplayName ?? field.FieldName,
                        DataType = field.DataType.ToString()
                    });
                }

                moduleNode.Templates.Add(templateNode);
            }

            if (moduleNode.Templates.Count > 0)
                tree.Add(moduleNode);
        }

        return tree;
    }

    public async Task<ReportQueryResult> RunReport(
        ReportDefinition definition, Guid tenantId, CancellationToken ct = default)
    {
        var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
        var entitledModules = entitlement.ActiveModules
            .Select(m => m.ModuleCode)
            .ToList();

        return await _queryEngine.Execute(definition, tenantId, entitledModules, ct);
    }

    public async Task<IReadOnlyList<SavedReport>> GetSavedReports(
        Guid tenantId, int institutionId, CancellationToken ct = default)
    {
        return await _reportRepo.GetByTenant(tenantId, institutionId, ct);
    }

    public async Task<SavedReport?> GetReportById(int id, CancellationToken ct = default)
    {
        return await _reportRepo.GetById(id, ct);
    }

    public async Task<int> SaveReport(
        Guid tenantId,
        int institutionId,
        int userId,
        string name,
        string? description,
        ReportDefinition definition,
        bool isShared,
        CancellationToken ct = default)
    {
        var report = new SavedReport
        {
            TenantId = tenantId,
            InstitutionId = institutionId,
            Name = name,
            Description = description,
            Definition = JsonSerializer.Serialize(definition),
            IsShared = isShared,
            CreatedByUserId = userId
        };

        await _reportRepo.Add(report, ct);
        return report.Id;
    }

    public async Task UpdateReport(SavedReport report, CancellationToken ct = default)
    {
        await _reportRepo.Update(report, ct);
    }

    public async Task DeleteReport(int id, CancellationToken ct = default)
    {
        await _reportRepo.Delete(id, ct);
    }

    public async Task SetSchedule(
        int reportId,
        string cronExpression,
        string format,
        List<int> recipientIds,
        CancellationToken ct = default)
    {
        var report = await _reportRepo.GetById(reportId, ct);
        if (report is null) return;

        report.ScheduleCron = cronExpression;
        report.ScheduleFormat = format;
        report.ScheduleRecipients = JsonSerializer.Serialize(recipientIds);
        report.IsScheduleActive = true;

        await _reportRepo.Update(report, ct);
    }

    public async Task<byte[]> GenerateBoardPack(
        Guid tenantId,
        int institutionId,
        List<int> reportIds,
        string title,
        CancellationToken ct = default)
    {
        var entitlement = await _entitlementService.ResolveEntitlements(tenantId, ct);
        var entitledModules = entitlement.ActiveModules
            .Select(m => m.ModuleCode)
            .ToList();

        var branding = await _brandingService.GetBrandingConfig(tenantId);
        var sections = new List<BoardPackSection>();

        foreach (var reportId in reportIds)
        {
            var report = await _reportRepo.GetById(reportId, ct);
            if (report is null || report.TenantId != tenantId) continue;

            var definition = JsonSerializer.Deserialize<ReportDefinition>(report.Definition);
            if (definition is null) continue;

            var result = await _queryEngine.Execute(definition, tenantId, entitledModules, ct);

            sections.Add(new BoardPackSection
            {
                ReportName = report.Name,
                ColumnNames = result.Columns,
                Rows = result.Rows
            });
        }

        return await _boardPackGenerator.Generate(sections, branding, title, ct);
    }
}

// Field tree models for the UI
public class ModuleFieldTree
{
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public List<TemplateFieldNode> Templates { get; set; } = new();
}

public class TemplateFieldNode
{
    public string TemplateCode { get; set; } = string.Empty;
    public string TemplateName { get; set; } = string.Empty;
    public List<FieldNode> Fields { get; set; } = new();
}

public class FieldNode
{
    public string FieldCode { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
}
