using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;

namespace FC.Engine.Application.Services;

public class TemplateVersioningService
{
    private readonly ITemplateRepository _templateRepo;
    private readonly IDdlEngine _ddlEngine;
    private readonly IDdlMigrationExecutor _migrationExecutor;
    private readonly ITemplateMetadataCache _cache;
    private readonly IXsdGenerator _xsdGenerator;
    private readonly IAuditLogger _audit;

    public TemplateVersioningService(
        ITemplateRepository templateRepo,
        IDdlEngine ddlEngine,
        IDdlMigrationExecutor migrationExecutor,
        ITemplateMetadataCache cache,
        IXsdGenerator xsdGenerator,
        IAuditLogger audit)
    {
        _templateRepo = templateRepo;
        _ddlEngine = ddlEngine;
        _migrationExecutor = migrationExecutor;
        _cache = cache;
        _xsdGenerator = xsdGenerator;
        _audit = audit;
    }

    public async Task<TemplateVersion> CreateNewDraftVersion(int templateId, string createdBy, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        var currentPublished = template.CurrentPublishedVersion;
        var newDraft = template.CreateDraftVersion(createdBy);

        // Clone fields, item codes, and formulas from current published version
        if (currentPublished != null)
        {
            foreach (var field in currentPublished.Fields)
                newDraft.AddField(field.Clone());

            foreach (var itemCode in currentPublished.ItemCodes)
                newDraft.AddItemCode(itemCode.Clone());

            foreach (var formula in currentPublished.IntraSheetFormulas)
                newDraft.AddFormula(formula.Clone());
        }

        await _templateRepo.Update(template, ct);
        await _audit.Log("TemplateVersion", newDraft.Id, "DraftCreated", null, newDraft, createdBy, ct);

        return newDraft;
    }

    public async Task SubmitForReview(int templateId, int versionId, string submittedBy, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        var version = template.GetVersion(versionId);

        if (!version.Fields.Any())
            throw new InvalidOperationException("Cannot submit a version with no fields for review");

        version.SubmitForReview();
        await _templateRepo.Update(template, ct);
        await _audit.Log("TemplateVersion", versionId, "SubmittedForReview", null, null, submittedBy, ct);
    }

    public async Task<DdlScript> PreviewDdl(int templateId, int versionId, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        var version = template.GetVersion(versionId);
        var previousVersion = template.GetPreviousPublishedVersion(versionId);

        if (previousVersion == null)
            return _ddlEngine.GenerateCreateTable(template, version);
        else
            return _ddlEngine.GenerateAlterTable(template, previousVersion, version);
    }

    public async Task Publish(int templateId, int versionId, string approvedBy, CancellationToken ct = default)
    {
        var template = await _templateRepo.GetById(templateId, ct)
            ?? throw new InvalidOperationException($"Template {templateId} not found");

        var version = template.GetVersion(versionId);
        var previousVersion = template.GetPreviousPublishedVersion(versionId);

        // Generate DDL
        DdlScript ddl;
        string migrationType;
        if (previousVersion == null)
        {
            ddl = _ddlEngine.GenerateCreateTable(template, version);
            migrationType = "CreateTable";
        }
        else
        {
            ddl = _ddlEngine.GenerateAlterTable(template, previousVersion, version);
            migrationType = "AlterTable";
        }

        // Execute DDL
        var migrationResult = await _migrationExecutor.Execute(
            templateId,
            previousVersion?.VersionNumber,
            version.VersionNumber,
            ddl,
            approvedBy,
            ct);

        if (!migrationResult.Success)
            throw new InvalidOperationException($"DDL execution failed: {migrationResult.Error}");

        // Store DDL on version
        version.SetDdlScript(ddl.ForwardSql, ddl.RollbackSql);

        // Deprecate old published version
        if (previousVersion != null)
            previousVersion.Deprecate();

        // Publish the new version
        version.Publish(DateTime.UtcNow, approvedBy);

        template.UpdatedAt = DateTime.UtcNow;
        template.UpdatedBy = approvedBy;

        await _templateRepo.Update(template, ct);

        // Invalidate caches
        _cache.Invalidate(template.ReturnCode);
        _xsdGenerator.InvalidateCache(template.ReturnCode);

        await _audit.Log("TemplateVersion", versionId, "Published", null,
            new { template.ReturnCode, version.VersionNumber, migrationType }, approvedBy, ct);
    }
}
