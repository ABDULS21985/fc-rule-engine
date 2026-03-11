using System.Reflection;
using System.Text.Json;
using FC.Engine.Application.Models;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class SeedModuleDefinitionBootstrapService
{
    private const string BootstrapUser = "platform-bootstrap";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly IReadOnlyList<SeedModuleDefinitionResource> Definitions =
    [
        new(
            "OPS_RESILIENCE",
            "FC.Engine.Infrastructure.SeedData.ModuleDefinitions.ops-resilience-module-definition.json"),
        new(
            "MODEL_RISK",
            "FC.Engine.Infrastructure.SeedData.ModuleDefinitions.rg50-model-risk-module-definition.json")
    ];

    private readonly MetadataDbContext _db;
    private readonly ModuleRegistryBootstrapService _moduleRegistryBootstrapService;
    private readonly IModuleImportService _moduleImportService;
    private readonly ILogger<SeedModuleDefinitionBootstrapService> _logger;

    public SeedModuleDefinitionBootstrapService(
        MetadataDbContext db,
        ModuleRegistryBootstrapService moduleRegistryBootstrapService,
        IModuleImportService moduleImportService,
        ILogger<SeedModuleDefinitionBootstrapService> logger)
    {
        _db = db;
        _moduleRegistryBootstrapService = moduleRegistryBootstrapService;
        _moduleImportService = moduleImportService;
        _logger = logger;
    }

    public async Task<SeedModuleDefinitionBootstrapResult> EnsureSeedModulesInstalledAsync(CancellationToken ct = default)
    {
        var result = new SeedModuleDefinitionBootstrapResult();
        await _moduleRegistryBootstrapService.EnsureBaselineModulesAsync(ct);

        foreach (var definition in Definitions)
        {
            var payload = await LoadDefinitionPayloadAsync(definition.ResourceName, ct);
            var moduleDefinition = JsonSerializer.Deserialize<ModuleDefinition>(payload, JsonOptions)
                ?? throw new InvalidOperationException($"Could not deserialize module definition resource '{definition.ResourceName}'.");

            var returnCodes = moduleDefinition.Templates
                .Select(x => x.ReturnCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (returnCodes.Count == 0)
            {
                result.Errors.Add($"Seed definition for module '{definition.ModuleCode}' contains no return codes.");
                continue;
            }

            var state = await GetInstallStateAsync(definition.ModuleCode, returnCodes, ct);

            if (state.TemplateCount == 0)
            {
                var import = await _moduleImportService.ImportModule(payload, BootstrapUser, ct);
                if (!import.Success)
                {
                    var afterImportState = await GetInstallStateAsync(definition.ModuleCode, returnCodes, ct);
                    if (afterImportState.TemplateCount != returnCodes.Count)
                    {
                        result.Errors.Add(
                            $"Module '{definition.ModuleCode}' import failed: {string.Join(" | ", import.Errors)}");
                        continue;
                    }
                }
                else
                {
                    result.ModulesImported++;
                }

                state = await GetInstallStateAsync(definition.ModuleCode, returnCodes, ct);
            }
            else if (state.TemplateCount != returnCodes.Count)
            {
                result.Warnings.Add(
                    $"Module '{definition.ModuleCode}' is partially installed ({state.TemplateCount}/{returnCodes.Count} templates). Automatic import skipped.");
                continue;
            }

            if (state.PublishedTemplateCount == returnCodes.Count)
            {
                continue;
            }

            var publish = await _moduleImportService.PublishModule(definition.ModuleCode, BootstrapUser, ct);
            if (!publish.Success)
            {
                var afterPublishState = await GetInstallStateAsync(definition.ModuleCode, returnCodes, ct);
                if (afterPublishState.PublishedTemplateCount != returnCodes.Count)
                {
                    result.Errors.Add(
                        $"Module '{definition.ModuleCode}' publish failed: {string.Join(" | ", publish.Errors)}");
                    continue;
                }
            }
            else
            {
                result.ModulesPublished++;
            }
        }

        _logger.LogInformation(
            "Seed module definition bootstrap completed. Imported={Imported} Published={Published} Warnings={Warnings} Errors={Errors}",
            result.ModulesImported,
            result.ModulesPublished,
            result.Warnings.Count,
            result.Errors.Count);

        return result;
    }

    private async Task<SeedModuleInstallState> GetInstallStateAsync(
        string moduleCode,
        IReadOnlyCollection<string> returnCodes,
        CancellationToken ct)
    {
        var moduleId = await _db.Modules
            .Where(x => x.ModuleCode == moduleCode)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(ct);

        if (moduleId is null)
        {
            return new SeedModuleInstallState();
        }

        var templates = await _db.ReturnTemplates
            .Where(x => x.ModuleId == moduleId.Value && returnCodes.Contains(x.ReturnCode))
            .Select(x => new { x.Id, x.ReturnCode })
            .ToListAsync(ct);

        var templateIds = templates.Select(x => x.Id).ToList();
        var publishedTemplateIds = templateIds.Count == 0
            ? []
            : await _db.TemplateVersions
                .Where(x => templateIds.Contains(x.TemplateId) && x.Status == TemplateStatus.Published)
                .Select(x => x.TemplateId)
                .Distinct()
                .ToListAsync(ct);

        return new SeedModuleInstallState
        {
            TemplateCount = templates.Count,
            PublishedTemplateCount = publishedTemplateIds.Count
        };
    }

    internal static async Task<string> LoadDefinitionPayloadAsync(string resourceName, CancellationToken ct = default)
    {
        var assembly = typeof(SeedModuleDefinitionBootstrapService).Assembly;
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }
}

public sealed class SeedModuleDefinitionBootstrapResult
{
    public int ModulesImported { get; set; }
    public int ModulesPublished { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
}

internal sealed class SeedModuleInstallState
{
    public int TemplateCount { get; init; }
    public int PublishedTemplateCount { get; init; }
}

internal sealed record SeedModuleDefinitionResource(string ModuleCode, string ResourceName);
