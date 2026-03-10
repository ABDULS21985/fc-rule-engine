using System.Text.Json;
using System.Text.RegularExpressions;
using FC.Engine.Application.Models;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Notifications;
using FC.Engine.Domain.Validation;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public partial class ModuleImportService : IModuleImportService
{
    private readonly MetadataDbContext _db;
    private readonly IDdlEngine _ddlEngine;
    private readonly IDdlMigrationExecutor _ddlExecutor;
    private readonly ITemplateMetadataCache _cache;
    private readonly ISqlTypeMapper _sqlTypeMapper;
    private readonly INotificationOrchestrator? _notificationOrchestrator;
    private readonly ILogger<ModuleImportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] SensitivePersonalDataKeywords =
    [
        "bvn", "nin", "passport", "biometric", "fingerprint", "tax_id", "tin", "national_id"
    ];

    private static readonly string[] PersonalDataKeywords =
    [
        "name", "email", "phone", "mobile", "address", "dob", "birth", "next_of_kin", "director"
    ];

    private static readonly string[] ConfidentialDataKeywords =
    [
        "salary", "balance", "amount", "revenue", "asset", "liability", "turnover", "income", "expense"
    ];

    public ModuleImportService(
        MetadataDbContext db,
        IDdlEngine ddlEngine,
        IDdlMigrationExecutor ddlExecutor,
        ITemplateMetadataCache cache,
        ISqlTypeMapper sqlTypeMapper,
        ILogger<ModuleImportService> logger,
        INotificationOrchestrator? notificationOrchestrator = null)
    {
        _db = db;
        _ddlEngine = ddlEngine;
        _ddlExecutor = ddlExecutor;
        _cache = cache;
        _sqlTypeMapper = sqlTypeMapper;
        _notificationOrchestrator = notificationOrchestrator;
        _logger = logger;
    }

    public async Task<ModuleValidationResult> ValidateDefinition(string jsonDefinition, CancellationToken ct = default)
    {
        var result = new ModuleValidationResult();

        if (string.IsNullOrWhiteSpace(jsonDefinition))
        {
            result.Errors.Add("Module definition JSON is required.");
            return result;
        }

        ModuleDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<ModuleDefinition>(jsonDefinition, JsonOptions);
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Invalid JSON: {ex.Message}");
            return result;
        }

        if (definition is null)
        {
            result.Errors.Add("Definition payload could not be deserialized.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(definition.ModuleCode))
        {
            result.Errors.Add("moduleCode is required.");
            return result;
        }

        if (!await _db.Modules.AnyAsync(m => m.ModuleCode == definition.ModuleCode, ct))
        {
            result.Errors.Add($"Module '{definition.ModuleCode}' not found. Seed module reference data first.");
        }

        if (definition.Templates.Count == 0)
        {
            result.Errors.Add("At least one template definition is required.");
        }

        var duplicateReturnCodes = definition.Templates
            .Where(t => !string.IsNullOrWhiteSpace(t.ReturnCode))
            .GroupBy(t => t.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var dup in duplicateReturnCodes)
        {
            result.Errors.Add($"Duplicate ReturnCode '{dup}'.");
        }

        var templateLookup = definition.Templates
            .GroupBy(t => t.ReturnCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToDictionary(t => t.ReturnCode, StringComparer.OrdinalIgnoreCase);
        var dbReturnCodes = await _db.ReturnTemplates
            .Where(t => definition.Templates.Select(x => x.ReturnCode).Contains(t.ReturnCode))
            .Select(t => t.ReturnCode)
            .ToListAsync(ct);

        foreach (var existing in dbReturnCodes)
        {
            result.Errors.Add($"ReturnCode '{existing}' already exists in metadata.");
        }

        foreach (var template in definition.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.ReturnCode))
            {
                result.Errors.Add("Template returnCode is required.");
                continue;
            }

            if (template.Fields.Count == 0)
            {
                result.Errors.Add($"Template '{template.ReturnCode}' requires at least one field.");
            }

            var sectionCodes = template.Sections
                .Select(s => s.Code)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var duplicateFieldCodes = template.Fields
                .Where(f => !string.IsNullOrWhiteSpace(f.FieldCode))
                .GroupBy(f => f.FieldCode, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            foreach (var dup in duplicateFieldCodes)
            {
                result.Errors.Add($"Template '{template.ReturnCode}': duplicate field code '{dup}'.");
            }

            var duplicateNormalizedFieldCodes = template.Fields
                .Where(f => !string.IsNullOrWhiteSpace(f.FieldCode))
                .GroupBy(f => ToSafeSqlIdentifier(f.FieldCode), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var dup in duplicateNormalizedFieldCodes)
            {
                var originals = string.Join(", ", dup.Select(f => f.FieldCode));
                result.Errors.Add(
                    $"Template '{template.ReturnCode}': field codes [{originals}] normalize to the same SQL identifier '{dup.Key}'.");
            }

            var fieldCodes = template.Fields
                .Select(f => f.FieldCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var field in template.Fields)
            {
                if (!string.IsNullOrWhiteSpace(field.Section) && !sectionCodes.Contains(field.Section))
                {
                    result.Warnings.Add(
                        $"Template '{template.ReturnCode}': field '{field.FieldCode}' references unknown section '{field.Section}'.");
                }
            }

            for (var i = 0; i < template.Formulas.Count; i++)
            {
                var formula = template.Formulas[i];
                if (!string.IsNullOrWhiteSpace(formula.TargetField) && !fieldCodes.Contains(formula.TargetField))
                {
                    result.Errors.Add(
                        $"Template '{template.ReturnCode}': formula #{i + 1} target '{formula.TargetField}' is not a valid field.");
                }

                foreach (var source in formula.SourceFields)
                {
                    if (!fieldCodes.Contains(source))
                    {
                        result.Errors.Add(
                            $"Template '{template.ReturnCode}': formula #{i + 1} source '{source}' is not a valid field.");
                    }
                }
            }

            foreach (var rule in template.CrossSheetRules)
            {
                if (!templateLookup.ContainsKey(rule.SourceTemplate))
                {
                    result.Errors.Add(
                        $"Template '{template.ReturnCode}': cross-sheet source template '{rule.SourceTemplate}' not found.");
                    continue;
                }

                if (!templateLookup.ContainsKey(rule.TargetTemplate))
                {
                    result.Errors.Add(
                        $"Template '{template.ReturnCode}': cross-sheet target template '{rule.TargetTemplate}' not found.");
                    continue;
                }

                var sourceTemplate = templateLookup[rule.SourceTemplate];
                var targetTemplate = templateLookup[rule.TargetTemplate];
                var sourceFields = sourceTemplate.Fields.Select(f => f.FieldCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var targetFields = targetTemplate.Fields.Select(f => f.FieldCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!sourceFields.Contains(rule.SourceField))
                {
                    result.Errors.Add(
                        $"Cross-sheet rule source field '{rule.SourceField}' is invalid for template '{rule.SourceTemplate}'.");
                }

                if (!targetFields.Contains(rule.TargetField))
                {
                    result.Errors.Add(
                        $"Cross-sheet rule target field '{rule.TargetField}' is invalid for template '{rule.TargetTemplate}'.");
                }
            }

            ValidateNoCyclicDependencies(template, result);
            result.FieldCount += template.Fields.Count;
            result.FormulaCount += template.Formulas.Count;
            result.CrossSheetRuleCount += template.CrossSheetRules.Count;
        }

        result.TemplateCount = definition.Templates.Count;
        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    public async Task<ModuleImportResult> ImportModule(string jsonDefinition, string performedBy, CancellationToken ct = default)
    {
        var validation = await ValidateDefinition(jsonDefinition, ct);
        if (!validation.IsValid)
        {
            return new ModuleImportResult
            {
                Success = false,
                Errors = validation.Errors
            };
        }

        var definition = JsonSerializer.Deserialize<ModuleDefinition>(jsonDefinition, JsonOptions)
            ?? throw new InvalidOperationException("Module definition could not be deserialized.");

        var module = await _db.Modules.FirstAsync(m => m.ModuleCode == definition.ModuleCode, ct);
        var result = new ModuleImportResult
        {
            ModuleCode = module.ModuleCode
        };

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        await using var tx = await BeginTransactionIfSupported(ct);
        try
        {
            module.Description = definition.Description ?? module.Description;

            var moduleVersion = await _db.ModuleVersions
                .FirstOrDefaultAsync(v => v.ModuleId == module.Id && v.VersionCode == definition.ModuleVersion, ct);

            if (moduleVersion is null)
            {
                moduleVersion = new ModuleVersion
                {
                    ModuleId = module.Id,
                    VersionCode = string.IsNullOrWhiteSpace(definition.ModuleVersion) ? "1.0.0" : definition.ModuleVersion,
                    Status = "Draft",
                    CreatedAt = DateTime.UtcNow
                };
                _db.ModuleVersions.Add(moduleVersion);
            }
            else
            {
                moduleVersion.Status = "Draft";
            }

            await _db.SaveChangesAsync(ct);

            foreach (var templateDef in definition.Templates)
            {
                var tableName = GenerateTableName(module.ModuleCode, templateDef);
                var now = DateTime.UtcNow;

                var template = new ReturnTemplate
                {
                    TenantId = null,
                    ModuleId = module.Id,
                    ReturnCode = templateDef.ReturnCode,
                    Name = templateDef.Name,
                    Description = definition.Description,
                    Frequency = ParseFrequency(templateDef.Frequency),
                    StructuralCategory = ParseStructuralCategory(templateDef.StructuralCategory),
                    PhysicalTableName = tableName,
                    XmlRootElement = ToSafeSqlIdentifier(templateDef.ReturnCode),
                    XmlNamespace = $"urn:regos:{module.ModuleCode.ToLowerInvariant()}:{templateDef.ReturnCode.ToLowerInvariant()}",
                    IsSystemTemplate = true,
                    OwnerDepartment = module.RegulatorCode,
                    InstitutionType = module.ModuleCode.Split('_', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "GEN",
                    CreatedAt = now,
                    CreatedBy = performedBy,
                    UpdatedAt = now,
                    UpdatedBy = performedBy
                };

                _db.ReturnTemplates.Add(template);
                await _db.SaveChangesAsync(ct);

                var version = new TemplateVersion
                {
                    TenantId = null,
                    TemplateId = template.Id,
                    VersionNumber = 1,
                    Status = TemplateStatus.Draft,
                    CreatedAt = now,
                    CreatedBy = performedBy
                };
                _db.TemplateVersions.Add(version);
                await _db.SaveChangesAsync(ct);

                var sectionOrderMap = templateDef.Sections
                    .ToDictionary(s => s.Code, s => s.DisplayOrder, StringComparer.OrdinalIgnoreCase);

                foreach (var sectionDef in templateDef.Sections)
                {
                    _db.TemplateSections.Add(new TemplateSection
                    {
                        TemplateVersionId = version.Id,
                        SectionName = sectionDef.Name,
                        SectionOrder = sectionDef.DisplayOrder,
                        Description = sectionDef.Code,
                        IsRepeating = false
                    });
                }

                foreach (var fieldDef in templateDef.Fields)
                {
                    var fieldName = ToSafeSqlIdentifier(fieldDef.FieldCode);
                    var dataType = ParseFieldDataType(fieldDef.DataType);
                    var classification = InferDataClassification(fieldDef);
                    _db.TemplateFields.Add(new TemplateField
                    {
                        TemplateVersionId = version.Id,
                        FieldName = fieldName,
                        DisplayName = fieldDef.Label,
                        XmlElementName = fieldName,
                        SectionName = fieldDef.Section,
                        SectionOrder = GetSectionOrder(fieldDef.Section, sectionOrderMap),
                        FieldOrder = fieldDef.DisplayOrder,
                        DataType = dataType,
                        SqlType = ResolveSqlType(dataType, fieldDef),
                        IsRequired = fieldDef.Required,
                        IsComputed = false,
                        IsKeyField = false,
                        MinValue = fieldDef.MinValue?.ToString(),
                        MaxValue = fieldDef.MaxValue?.ToString(),
                        AllowedValues = fieldDef.EnumValues,
                        HelpText = fieldDef.ValidationNote ?? fieldDef.HelpText,
                        RegulatoryReference = fieldDef.RegulatoryReference,
                        IsYtdField = fieldDef.CarryForward,
                        DataClassification = classification,
                        CreatedAt = now
                    });
                    result.FieldsCreated++;
                }

                foreach (var itemCodeDef in templateDef.ItemCodes)
                {
                    _db.TemplateItemCodes.Add(new TemplateItemCode
                    {
                        TemplateVersionId = version.Id,
                        ItemCode = itemCodeDef.Code,
                        ItemDescription = itemCodeDef.Label,
                        SortOrder = itemCodeDef.DisplayOrder,
                        IsTotalRow = false,
                        CreatedAt = now
                    });
                }

                for (var i = 0; i < templateDef.Formulas.Count; i++)
                {
                    var formulaDef = templateDef.Formulas[i];
                    _db.IntraSheetFormulas.Add(new IntraSheetFormula
                    {
                        TemplateVersionId = version.Id,
                        RuleCode = BuildRuleCode(templateDef.ReturnCode, formulaDef, i + 1),
                        RuleName = formulaDef.Description ?? $"{formulaDef.FormulaType} rule {i + 1}",
                        FormulaType = ParseFormulaType(formulaDef.FormulaType),
                        TargetFieldName = ToSafeSqlIdentifier(
                            formulaDef.TargetField
                            ?? formulaDef.SourceFields.FirstOrDefault()
                            ?? $"rule_target_{i + 1}"),
                        OperandFields = JsonSerializer.Serialize(
                            formulaDef.SourceFields.Select(ToSafeSqlIdentifier).ToList()),
                        CustomExpression = BuildCustomExpression(formulaDef),
                        ToleranceAmount = formulaDef.ToleranceAmount ?? 0m,
                        TolerancePercent = formulaDef.TolerancePercent,
                        Severity = ParseSeverity(formulaDef.Severity),
                        ErrorMessage = formulaDef.Description,
                        IsActive = true,
                        SortOrder = i + 1,
                        CreatedAt = now,
                        CreatedBy = performedBy
                    });
                    result.FormulasCreated++;
                }

                for (var i = 0; i < templateDef.CrossSheetRules.Count; i++)
                {
                    var ruleDef = templateDef.CrossSheetRules[i];
                    var rule = new CrossSheetRule
                    {
                        TenantId = null,
                        RuleCode = BuildCrossSheetRuleCode(module.ModuleCode, templateDef.ReturnCode, i + 1),
                        RuleName = ruleDef.Description ?? $"{ruleDef.SourceTemplate} to {ruleDef.TargetTemplate}",
                        Description = ruleDef.Description,
                        ModuleId = module.Id,
                        SourceModuleId = module.Id,
                        TargetModuleId = module.Id,
                        SourceTemplateCode = ruleDef.SourceTemplate,
                        SourceFieldCode = ToSafeSqlIdentifier(ruleDef.SourceField),
                        TargetTemplateCode = ruleDef.TargetTemplate,
                        TargetFieldCode = ToSafeSqlIdentifier(ruleDef.TargetField),
                        Operator = ruleDef.Operator,
                        ToleranceAmount = ruleDef.ToleranceAmount ?? 0m,
                        TolerancePercent = ruleDef.TolerancePercent,
                        Severity = ParseSeverity(ruleDef.Severity),
                        IsActive = true,
                        CreatedAt = now,
                        CreatedBy = performedBy,
                        Expression = new CrossSheetRuleExpression
                        {
                            Expression = ToCrossSheetExpression(ruleDef.Operator),
                            ToleranceAmount = ruleDef.ToleranceAmount ?? 0m,
                            TolerancePercent = ruleDef.TolerancePercent,
                            ErrorMessage = ruleDef.Description
                        }
                    };

                    rule.AddOperand(new CrossSheetRuleOperand
                    {
                        OperandAlias = "A",
                        TemplateReturnCode = ruleDef.SourceTemplate,
                        FieldName = ToSafeSqlIdentifier(ruleDef.SourceField),
                        SortOrder = 1
                    });
                    rule.AddOperand(new CrossSheetRuleOperand
                    {
                        OperandAlias = "B",
                        TemplateReturnCode = ruleDef.TargetTemplate,
                        FieldName = ToSafeSqlIdentifier(ruleDef.TargetField),
                        SortOrder = 2
                    });

                    _db.CrossSheetRules.Add(rule);
                    result.CrossSheetRulesCreated++;
                }

                await UpsertDataProcessingActivity(module, templateDef, now, ct);

                result.TemplatesCreated++;
            }

            foreach (var flowDef in definition.InterModuleDataFlows)
            {
                _db.InterModuleDataFlows.Add(new InterModuleDataFlow
                {
                    SourceModuleId = module.Id,
                    SourceTemplateCode = flowDef.SourceTemplate,
                    SourceFieldCode = ToSafeSqlIdentifier(flowDef.SourceField),
                    TargetModuleCode = flowDef.TargetModule,
                    TargetTemplateCode = flowDef.TargetTemplate,
                    TargetFieldCode = ToSafeSqlIdentifier(flowDef.TargetField),
                    TransformationType = flowDef.TransformationType,
                    TransformFormula = flowDef.TransformFormula,
                    Description = flowDef.Description,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(ct);
            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
            result.Success = true;

            _logger.LogInformation(
                "Module import completed for {ModuleCode}: templates={Templates}, fields={Fields}, formulas={Formulas}, crossSheetRules={Rules}",
                result.ModuleCode,
                result.TemplatesCreated,
                result.FieldsCreated,
                result.FormulasCreated,
                result.CrossSheetRulesCreated);
        }
        catch (Exception ex)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }
            result.Success = false;
            result.Errors.Add($"Import failed: {BuildExceptionMessage(ex)}");
            _logger.LogError(ex, "Module import failed for {ModuleCode}", result.ModuleCode);
        }
        });

        return result;
    }

    public async Task<ModulePublishResult> PublishModule(string moduleCode, string approvedBy, CancellationToken ct = default)
    {
        var module = await _db.Modules.FirstOrDefaultAsync(m => m.ModuleCode == moduleCode, ct);
        if (module is null)
        {
            return new ModulePublishResult
            {
                Success = false,
                ModuleCode = moduleCode,
                Errors = { $"Module '{moduleCode}' not found." }
            };
        }

        var draftVersions = await _db.TemplateVersions
            .Include(v => v.Fields)
            .Join(
                _db.ReturnTemplates.Where(t => t.ModuleId == module.Id),
                version => version.TemplateId,
                template => template.Id,
                (version, template) => new { Version = version, Template = template })
            .Where(x => x.Version.Status == TemplateStatus.Draft)
            .ToListAsync(ct);

        if (draftVersions.Count == 0)
        {
            return new ModulePublishResult
            {
                Success = false,
                ModuleCode = moduleCode,
                Errors = { "No draft template versions found for module." }
            };
        }

        var result = new ModulePublishResult
        {
            ModuleCode = moduleCode
        };

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
        await using var tx = await BeginTransactionIfSupported(ct);
        try
        {
            foreach (var draft in draftVersions)
            {
                var publishedVersion = await _db.TemplateVersions
                    .Include(v => v.Fields)
                    .Where(v => v.TemplateId == draft.Template.Id && v.Status == TemplateStatus.Published)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefaultAsync(ct);

                var ddl = publishedVersion is null
                    ? _ddlEngine.GenerateCreateTable(draft.Template, draft.Version)
                    : _ddlEngine.GenerateAlterTable(draft.Template, publishedVersion, draft.Version);

                result.DdlStatements.Add(ddl.ForwardSql);

                var migration = await _ddlExecutor.Execute(
                    draft.Template.Id,
                    publishedVersion?.VersionNumber,
                    draft.Version.VersionNumber,
                    ddl,
                    approvedBy,
                    ct);

                if (!migration.Success)
                {
                    throw new InvalidOperationException(
                        $"DDL migration failed for '{draft.Template.ReturnCode}': {migration.Error}");
                }

                await EnsureRlsPolicyOnTable(draft.Template.PhysicalTableName, ct);

                if (publishedVersion is not null)
                {
                    publishedVersion.Deprecate();
                }

                draft.Version.Status = TemplateStatus.Review;
                draft.Version.Publish(DateTime.UtcNow, approvedBy);
                draft.Template.UpdatedAt = DateTime.UtcNow;
                draft.Template.UpdatedBy = approvedBy;

                result.TablesCreated++;
            }

            var now = DateTime.UtcNow;
            var publishedModuleVersions = await _db.ModuleVersions
                .Where(v => v.ModuleId == module.Id && v.Status == "Published")
                .ToListAsync(ct);

            foreach (var version in publishedModuleVersions)
            {
                version.Status = "Deprecated";
                version.DeprecatedAt = now;
            }

            var moduleVersion = await _db.ModuleVersions
                .Where(v => v.ModuleId == module.Id && v.Status == "Draft")
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync(ct);

            if (moduleVersion is null)
            {
                moduleVersion = new ModuleVersion
                {
                    ModuleId = module.Id,
                    VersionCode = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmm"),
                    Status = "Published",
                    PublishedAt = now,
                    CreatedAt = now
                };
                _db.ModuleVersions.Add(moduleVersion);
            }
            else
            {
                moduleVersion.Status = "Published";
                moduleVersion.PublishedAt = now;
                moduleVersion.DeprecatedAt = null;
            }

            module.SheetCount = await _db.ReturnTemplates.CountAsync(t => t.ModuleId == module.Id, ct);
            result.VersionsPublished = 1;

            await _db.SaveChangesAsync(ct);
            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }

            _cache.InvalidateModule(module.Id);
            result.Success = true;

            _logger.LogInformation(
                "Module publish completed for {ModuleCode}: tables={TableCount}, version={VersionCode}",
                moduleCode,
                result.TablesCreated,
                moduleVersion.VersionCode);

            await NotifySubscribedTenants(module, moduleVersion.VersionCode, approvedBy, ct);
        }
        catch (Exception ex)
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(ct);
            }
            result.Success = false;
            result.Errors.Add($"Publish failed: {BuildExceptionMessage(ex)}");
            _logger.LogError(ex, "Module publish failed for {ModuleCode}", moduleCode);
        }
        });

        return result;
    }

    private async Task NotifySubscribedTenants(
        Module module,
        string versionCode,
        string approvedBy,
        CancellationToken ct)
    {
        if (_notificationOrchestrator is null)
        {
            return;
        }

        var tenantIds = await _db.SubscriptionModules
            .AsNoTracking()
            .Where(sm => sm.IsActive && sm.ModuleId == module.Id)
            .Join(
                _db.Subscriptions.AsNoTracking()
                    .Where(s => s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired),
                sm => sm.SubscriptionId,
                s => s.Id,
                (_, s) => s.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                await _notificationOrchestrator.Notify(new NotificationRequest
                {
                    TenantId = tenantId,
                    EventType = NotificationEvents.SystemAnnouncement,
                    Title = $"{module.ModuleName} module updated",
                    Message = $"Module {module.ModuleCode} has been published as version {versionCode}.",
                    Priority = NotificationPriority.Normal,
                    RecipientRoles = new List<string> { "Admin", "Approver" },
                    ActionUrl = $"/modules/{module.ModuleCode}",
                    Data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ModuleCode"] = module.ModuleCode,
                        ["ModuleName"] = module.ModuleName,
                        ["VersionCode"] = versionCode,
                        ["ApprovedBy"] = approvedBy
                    }
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to emit module publish notification for module {ModuleCode} tenant {TenantId}",
                    module.ModuleCode,
                    tenantId);
            }
        }
    }

    private async Task EnsureRlsPolicyOnTable(string tableName, CancellationToken ct)
    {
        if (!_db.Database.IsSqlServer())
        {
            return;
        }

        var safeTableName = ValidateSqlIdentifier(tableName);
        var sql = $@"
            IF OBJECT_ID(N'dbo.TenantSecurityPolicy', N'SP') IS NOT NULL
            BEGIN
                BEGIN TRY
                    EXEC(N'ALTER SECURITY POLICY dbo.TenantSecurityPolicy
                        ADD FILTER PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.[{safeTableName}]');
                END TRY
                BEGIN CATCH
                    IF ERROR_MESSAGE() NOT LIKE '%already has%'
                    BEGIN
                        THROW;
                    END
                END CATCH;

                BEGIN TRY
                    EXEC(N'ALTER SECURITY POLICY dbo.TenantSecurityPolicy
                        ADD BLOCK PREDICATE dbo.fn_TenantFilter(TenantId) ON dbo.[{safeTableName}]');
                END TRY
                BEGIN CATCH
                    IF ERROR_MESSAGE() NOT LIKE '%already has%'
                    BEGIN
                        THROW;
                    END
                END CATCH;
            END;";

        await _db.Database.ExecuteSqlRawAsync(sql, ct);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfSupported(CancellationToken ct)
    {
        if (!_db.Database.IsRelational())
        {
            return null;
        }

        return await _db.Database.BeginTransactionAsync(ct);
    }

    private static string BuildCustomExpression(FormulaDef formula)
    {
        if (!string.Equals(formula.FormulaType, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            return formula.CustomFunction ?? formula.Description ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(formula.CustomFunction))
        {
            var args = formula.SourceFields.Count == 0
                ? string.Empty
                : string.Join(",", formula.SourceFields.Select(ToSafeSqlIdentifier));

            if (formula.Parameters is { Count: > 0 })
            {
                var pairs = string.Join(",",
                    formula.Parameters.Select(p => $"{ToSafeSqlIdentifier(p.Key)}={p.Value}"));
                if (!string.IsNullOrWhiteSpace(pairs))
                {
                    args = string.IsNullOrWhiteSpace(args) ? pairs : $"{args},{pairs}";
                }
            }

            return $"FUNC:{formula.CustomFunction}({args})";
        }

        return formula.Description ?? string.Empty;
    }

    private static string BuildRuleCode(string returnCode, FormulaDef formula, int index)
    {
        var prefix = string.IsNullOrWhiteSpace(formula.FormulaType) ? "RULE" : formula.FormulaType.ToUpperInvariant();
        var rule = $"{returnCode}_{prefix}_{index}";
        return rule.Length <= 50 ? rule : rule[..50];
    }

    private static string BuildCrossSheetRuleCode(string moduleCode, string returnCode, int index)
    {
        var rule = $"{moduleCode}_{returnCode}_CSR_{index}";
        return rule.Length <= 50 ? rule : rule[..50];
    }

    private static string ToCrossSheetExpression(string? op)
    {
        return op?.Trim().ToUpperInvariant() switch
        {
            "EQUALS" => "A = B",
            "GREATERTHAN" => "A > B",
            "LESSTHAN" => "A < B",
            "GREATEREQUAL" => "A >= B",
            "LESSEQUAL" => "A <= B",
            "BETWEEN" => "A = B",
            _ => "A = B"
        };
    }

    private static string BuildExceptionMessage(Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        return ReferenceEquals(root, ex)
            ? ex.Message
            : $"{ex.Message} | Inner: {root.Message}";
    }

    private static int GetSectionOrder(string? sectionCode, IReadOnlyDictionary<string, int> sectionOrderMap)
    {
        if (string.IsNullOrWhiteSpace(sectionCode))
        {
            return 0;
        }

        return sectionOrderMap.TryGetValue(sectionCode, out var order) ? order : 0;
    }

    private string ResolveSqlType(FieldDataType dataType, FieldDef fieldDef)
    {
        if (fieldDef.DecimalPlaces.HasValue && dataType is FieldDataType.Money or FieldDataType.Decimal or FieldDataType.Percentage)
        {
            var scale = Math.Clamp(fieldDef.DecimalPlaces.Value, 0, 8);
            return dataType switch
            {
                FieldDataType.Money => $"DECIMAL(20,{Math.Max(2, scale)})",
                FieldDataType.Percentage => $"DECIMAL(10,{Math.Max(2, scale)})",
                _ => $"DECIMAL(20,{Math.Max(2, scale)})"
            };
        }

        if (dataType == FieldDataType.Text && !string.IsNullOrWhiteSpace(fieldDef.EnumValues))
        {
            return "NVARCHAR(100)";
        }

        return _sqlTypeMapper.MapToSqlType(dataType);
    }

    private static DataClassification InferDataClassification(FieldDef fieldDef)
    {
        var aggregate = string.Join(' ', new[]
        {
            fieldDef.FieldCode,
            fieldDef.Label,
            fieldDef.HelpText ?? string.Empty,
            fieldDef.RegulatoryReference ?? string.Empty
        }).ToLowerInvariant();

        if (ContainsAny(aggregate, SensitivePersonalDataKeywords))
        {
            return DataClassification.SensitivePersonalData;
        }

        if (ContainsAny(aggregate, PersonalDataKeywords))
        {
            return DataClassification.PersonalData;
        }

        if (ContainsAny(aggregate, ConfidentialDataKeywords))
        {
            return DataClassification.Confidential;
        }

        return DataClassification.Internal;
    }

    private async Task UpsertDataProcessingActivity(Module module, TemplateDef templateDef, DateTime now, CancellationToken ct)
    {
        var activityName = $"{templateDef.ReturnCode} submission processing";
        var fieldClassifications = templateDef.Fields
            .Select(InferDataClassification)
            .ToList();

        var categories = BuildDataCategories(fieldClassifications);
        var subjects = BuildDataSubjects(templateDef);
        var securityMeasures = new[]
        {
            "Role-based access controls",
            "SQL row-level security",
            "TLS encryption in transit",
            "Encrypted storage at rest",
            "Immutable audit trail hashing"
        };
        var sharing = new[] { module.RegulatorCode };

        var existing = await _db.DataProcessingActivities
            .FirstOrDefaultAsync(x =>
                x.ModuleCode == module.ModuleCode &&
                x.ActivityName == activityName,
                ct);

        var purpose = $"Collection, validation, and submission of {templateDef.Name} ({templateDef.ReturnCode}) for {module.RegulatorCode} compliance.";
        if (existing is null)
        {
            _db.DataProcessingActivities.Add(new DataProcessingActivity
            {
                ModuleCode = module.ModuleCode,
                ActivityName = activityName,
                Purpose = purpose,
                LegalBasis = "LegalObligation",
                DataCategories = JsonSerializer.Serialize(categories),
                DataSubjects = JsonSerializer.Serialize(subjects),
                RetentionPeriod = "7 years from submission",
                ThirdPartySharing = JsonSerializer.Serialize(sharing),
                SecurityMeasures = JsonSerializer.Serialize(securityMeasures),
                IsAutoGenerated = true,
                LastUpdated = now
            });
            return;
        }

        existing.Purpose = purpose;
        existing.LegalBasis = "LegalObligation";
        existing.DataCategories = JsonSerializer.Serialize(categories);
        existing.DataSubjects = JsonSerializer.Serialize(subjects);
        existing.RetentionPeriod = "7 years from submission";
        existing.ThirdPartySharing = JsonSerializer.Serialize(sharing);
        existing.SecurityMeasures = JsonSerializer.Serialize(securityMeasures);
        existing.IsAutoGenerated = true;
        existing.LastUpdated = now;
    }

    private static List<string> BuildDataCategories(IEnumerable<DataClassification> classifications)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "regulatory_data" };
        foreach (var classification in classifications)
        {
            switch (classification)
            {
                case DataClassification.SensitivePersonalData:
                    result.Add("sensitive_personal_data");
                    result.Add("personal_data");
                    result.Add("identification_data");
                    break;
                case DataClassification.PersonalData:
                    result.Add("personal_data");
                    break;
                case DataClassification.Confidential:
                    result.Add("financial_data");
                    break;
                case DataClassification.Internal:
                    result.Add("operational_data");
                    break;
                case DataClassification.Public:
                    result.Add("public_data");
                    break;
            }
        }

        return result.OrderBy(x => x).ToList();
    }

    private static List<string> BuildDataSubjects(TemplateDef templateDef)
    {
        var aggregate = string.Join(' ', templateDef.Fields
            .Select(f => $"{f.FieldCode} {f.Label} {f.HelpText}"))
            .ToLowerInvariant();

        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "regulated_institutions"
        };

        if (aggregate.Contains("employee", StringComparison.OrdinalIgnoreCase))
        {
            subjects.Add("employees");
        }

        if (aggregate.Contains("customer", StringComparison.OrdinalIgnoreCase) ||
            aggregate.Contains("client", StringComparison.OrdinalIgnoreCase))
        {
            subjects.Add("customers");
        }

        if (aggregate.Contains("director", StringComparison.OrdinalIgnoreCase))
        {
            subjects.Add("directors");
        }

        return subjects.OrderBy(x => x).ToList();
    }

    private static bool ContainsAny(string aggregate, IEnumerable<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (aggregate.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static ReturnFrequency ParseFrequency(string? value)
    {
        if (Enum.TryParse<ReturnFrequency>(value, true, out var parsed))
        {
            return parsed;
        }

        return ReturnFrequency.Monthly;
    }

    private static StructuralCategory ParseStructuralCategory(string? value)
    {
        if (Enum.TryParse<StructuralCategory>(value, true, out var parsed))
        {
            return parsed;
        }

        return StructuralCategory.FixedRow;
    }

    private static FormulaType ParseFormulaType(string? value)
    {
        if (Enum.TryParse<FormulaType>(value, true, out var parsed))
        {
            return parsed;
        }

        return FormulaType.Custom;
    }

    private static ValidationSeverity ParseSeverity(string? value)
    {
        if (Enum.TryParse<ValidationSeverity>(value, true, out var parsed))
        {
            return parsed;
        }

        return ValidationSeverity.Error;
    }

    private static FieldDataType ParseFieldDataType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FieldDataType.Text;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "MONEY" => FieldDataType.Money,
            "RATE" => FieldDataType.Decimal,
            "DECIMAL" => FieldDataType.Decimal,
            "INTEGER" => FieldDataType.Integer,
            "TEXT" => FieldDataType.Text,
            "DATE" => FieldDataType.Date,
            "BOOLEAN" => FieldDataType.Boolean,
            "PERCENT" => FieldDataType.Percentage,
            "PERCENTAGE" => FieldDataType.Percentage,
            "ENUM" => FieldDataType.Text,
            _ => FieldDataType.Text
        };
    }

    private static string GenerateTableName(string moduleCode, TemplateDef template)
    {
        var prefix = string.IsNullOrWhiteSpace(template.TablePrefix)
            ? moduleCode.ToLowerInvariant().Replace("_", string.Empty)
            : template.TablePrefix.ToLowerInvariant();
        var suffix = template.ReturnCode.Split('_').LastOrDefault() ?? template.ReturnCode;
        return ToSafeSqlIdentifier($"{prefix}_{suffix}");
    }

    private void ValidateNoCyclicDependencies(TemplateDef template, ModuleValidationResult result)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var formula in template.Formulas)
        {
            if (string.IsNullOrWhiteSpace(formula.TargetField) || formula.SourceFields.Count == 0)
            {
                continue;
            }

            if (!graph.TryGetValue(formula.TargetField, out var dependencies))
            {
                dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                graph[formula.TargetField] = dependencies;
            }

            foreach (var source in formula.SourceFields)
            {
                dependencies.Add(source);
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in graph.Keys)
        {
            if (HasCycle(node, graph, visited, stack))
            {
                result.Errors.Add(
                    $"Template '{template.ReturnCode}': circular formula dependency detected involving '{node}'.");
                break;
            }
        }
    }

    private static bool HasCycle(
        string node,
        IReadOnlyDictionary<string, HashSet<string>> graph,
        HashSet<string> visited,
        HashSet<string> stack)
    {
        if (stack.Contains(node))
        {
            return true;
        }

        if (visited.Contains(node))
        {
            return false;
        }

        visited.Add(node);
        stack.Add(node);

        if (graph.TryGetValue(node, out var deps))
        {
            foreach (var dep in deps)
            {
                if (HasCycle(dep, graph, visited, stack))
                {
                    return true;
                }
            }
        }

        stack.Remove(node);
        return false;
    }

    private static string ValidateSqlIdentifier(string value)
    {
        var normalized = ToSafeSqlIdentifier(value);
        if (!SafeSqlIdentifierRegex().IsMatch(normalized))
        {
            throw new InvalidOperationException($"Invalid SQL identifier '{value}'.");
        }

        return normalized;
    }

    private static string ToSafeSqlIdentifier(string value)
    {
        var candidate = value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        candidate = NonAlphaNumRegex().Replace(candidate, "_");
        candidate = MultiUnderscoreRegex().Replace(candidate, "_").Trim('_');

        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = "field";
        }

        if (!char.IsLetter(candidate[0]) && candidate[0] != '_')
        {
            candidate = $"f_{candidate}";
        }

        return candidate;
    }

    [GeneratedRegex("[^a-z0-9_]", RegexOptions.Compiled)]
    private static partial Regex NonAlphaNumRegex();

    [GeneratedRegex("_+", RegexOptions.Compiled)]
    private static partial Regex MultiUnderscoreRegex();

    [GeneratedRegex("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex SafeSqlIdentifierRegex();
}
