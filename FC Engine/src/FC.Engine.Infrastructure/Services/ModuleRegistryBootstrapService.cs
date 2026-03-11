using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class ModuleRegistryBootstrapService
{
    private static readonly IReadOnlyList<ModuleRegistryDefinition> Definitions =
    [
        new(
            "OPS_RESILIENCE",
            "Operational Resilience & ICT Risk",
            "CBN",
            "Operational resilience, ICT risk, testing, incident lifecycle, and board oversight return pack.",
            10,
            "Quarterly",
            16,
            45),
        new(
            "MODEL_RISK",
            "Model Risk Management & Validation",
            "CBN",
            "Supervisory model inventory, validation, performance monitoring, approvals, and model risk reporting pack.",
            9,
            "Quarterly",
            17,
            45)
    ];

    private readonly MetadataDbContext _db;
    private readonly ILogger<ModuleRegistryBootstrapService> _logger;

    public ModuleRegistryBootstrapService(
        MetadataDbContext db,
        ILogger<ModuleRegistryBootstrapService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<ModuleRegistryBootstrapResult> EnsureBaselineModulesAsync(CancellationToken ct = default)
    {
        var moduleCodes = Definitions.Select(x => x.ModuleCode).ToList();
        var existingModules = await _db.Modules
            .Where(x => moduleCodes.Contains(x.ModuleCode))
            .ToDictionaryAsync(x => x.ModuleCode, StringComparer.OrdinalIgnoreCase, ct);

        var licenceTypes = await _db.LicenceTypes
            .Where(x => x.IsActive)
            .ToListAsync(ct);

        var modulesCreated = 0;
        var modulesUpdated = 0;
        var mappingsCreated = 0;
        var mappingsUpdated = 0;
        var now = DateTime.UtcNow;

        foreach (var definition in Definitions)
        {
            if (!existingModules.TryGetValue(definition.ModuleCode, out var module))
            {
                module = new Module
                {
                    ModuleCode = definition.ModuleCode,
                    ModuleName = definition.ModuleName,
                    RegulatorCode = definition.RegulatorCode,
                    Description = definition.Description,
                    SheetCount = definition.SheetCount,
                    DefaultFrequency = definition.DefaultFrequency,
                    DisplayOrder = definition.DisplayOrder,
                    DeadlineOffsetDays = definition.DeadlineOffsetDays,
                    IsActive = true,
                    CreatedAt = now
                };

                _db.Modules.Add(module);
                existingModules[definition.ModuleCode] = module;
                modulesCreated++;
            }
            else
            {
                var changed = false;
                changed |= UpdateIfDifferent(module, x => x.ModuleName, definition.ModuleName);
                changed |= UpdateIfDifferent(module, x => x.RegulatorCode, definition.RegulatorCode);
                changed |= UpdateIfDifferent(module, x => x.Description, definition.Description);
                changed |= UpdateIfDifferent(module, x => x.SheetCount, definition.SheetCount);
                changed |= UpdateIfDifferent(module, x => x.DefaultFrequency, definition.DefaultFrequency);
                changed |= UpdateIfDifferent(module, x => x.DisplayOrder, definition.DisplayOrder);
                changed |= UpdateIfDifferent(module, x => x.DeadlineOffsetDays, definition.DeadlineOffsetDays);

                if (!module.IsActive)
                {
                    module.IsActive = true;
                    changed = true;
                }

                if (changed)
                {
                    modulesUpdated++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        var moduleIds = existingModules.Values.Select(x => x.Id).ToList();
        var existingMappings = await _db.LicenceModuleMatrix
            .Where(x => moduleIds.Contains(x.ModuleId))
            .ToListAsync(ct);

        foreach (var definition in Definitions)
        {
            var module = existingModules[definition.ModuleCode];
            foreach (var licenceType in licenceTypes)
            {
                var mapping = existingMappings.FirstOrDefault(x => x.ModuleId == module.Id && x.LicenceTypeId == licenceType.Id);
                if (mapping is null)
                {
                    _db.LicenceModuleMatrix.Add(new LicenceModuleMatrix
                    {
                        ModuleId = module.Id,
                        LicenceTypeId = licenceType.Id,
                        IsRequired = false,
                        IsOptional = true
                    });
                    mappingsCreated++;
                    continue;
                }

                if (mapping.IsRequired || !mapping.IsOptional)
                {
                    mapping.IsRequired = false;
                    mapping.IsOptional = true;
                    mappingsUpdated++;
                }
            }
        }

        if (mappingsCreated > 0 || mappingsUpdated > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Module registry bootstrap completed. ModulesCreated={ModulesCreated} ModulesUpdated={ModulesUpdated} MappingsCreated={MappingsCreated} MappingsUpdated={MappingsUpdated}",
            modulesCreated,
            modulesUpdated,
            mappingsCreated,
            mappingsUpdated);

        return new ModuleRegistryBootstrapResult
        {
            ModulesCreated = modulesCreated,
            ModulesUpdated = modulesUpdated,
            MappingsCreated = mappingsCreated,
            MappingsUpdated = mappingsUpdated
        };
    }

    private static bool UpdateIfDifferent<TModule, TValue>(
        TModule target,
        System.Linq.Expressions.Expression<Func<TModule, TValue>> accessor,
        TValue value)
    {
        var member = (System.Linq.Expressions.MemberExpression)accessor.Body;
        var property = (System.Reflection.PropertyInfo)member.Member;
        var current = (TValue?)property.GetValue(target);
        if (EqualityComparer<TValue>.Default.Equals(current, value))
        {
            return false;
        }

        property.SetValue(target, value);
        return true;
    }
}

public sealed class ModuleRegistryBootstrapResult
{
    public int ModulesCreated { get; init; }
    public int ModulesUpdated { get; init; }
    public int MappingsCreated { get; init; }
    public int MappingsUpdated { get; init; }
}

internal sealed record ModuleRegistryDefinition(
    string ModuleCode,
    string ModuleName,
    string RegulatorCode,
    string Description,
    int SheetCount,
    string DefaultFrequency,
    int DisplayOrder,
    int DeadlineOffsetDays);
