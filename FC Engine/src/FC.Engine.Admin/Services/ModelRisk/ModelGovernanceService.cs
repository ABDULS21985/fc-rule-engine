using FC.Engine.Admin.Services;
using FC.Engine.Infrastructure.Services;

namespace FC.Engine.Admin.Services.ModelRisk;

/// <summary>
/// Aggregates model risk data from PlatformIntelligenceService and model approval workflow store.
/// All data flows through real DB-backed services.
/// </summary>
public sealed class ModelGovernanceService
{
    private readonly PlatformIntelligenceService _intelligence;
    private readonly ModelApprovalWorkflowStoreService _approvalStore;
    private readonly ModelInventoryCatalogService _modelInventoryCatalog;

    public ModelGovernanceService(
        PlatformIntelligenceService intelligence,
        ModelApprovalWorkflowStoreService approvalStore,
        ModelInventoryCatalogService modelInventoryCatalog)
    {
        _intelligence = intelligence;
        _approvalStore = approvalStore;
        _modelInventoryCatalog = modelInventoryCatalog;
    }

    public async Task<ModelDashboardData> GetDashboardAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var m = ws.ModelRisk;
        return new ModelDashboardData
        {
            InventoryCount = m.InventoryCount,
            DueValidationCount = m.DueValidationCount,
            PerformanceAlertCount = m.PerformanceAlertCount,
            ApprovalQueueCount = m.ApprovalQueueCount,
            AverageAccuracyScore = m.AverageAccuracyScore,
            RiskAppetiteScore = m.RiskAppetiteScore,
            InAppetiteCount = m.InAppetiteCount,
            WatchCount = m.WatchCount,
            BreachCount = m.BreachCount,
            ChangeReviewCount = m.ChangeReviewCount
        };
    }

    public async Task<List<ModelInventoryRow>> GetInventoryAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        return ws.ModelRisk.Inventory;
    }

    public async Task<List<ModelInventoryDefinitionState>> GetInventoryCatalogAsync(CancellationToken ct = default)
    {
        var state = await _intelligence.GetModelInventoryCatalogStateAsync(ct);
        return state.Definitions;
    }

    public async Task CreateInventoryDefinitionAsync(ModelInventoryDefinitionInput definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var state = await _intelligence.GetModelInventoryCatalogStateAsync(ct);
        var normalized = NormalizeDefinition(definition);

        if (state.Definitions.Any(x => x.ModelCode.Equals(normalized.ModelCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A model with code '{normalized.ModelCode}' already exists.");
        }

        var definitions = state.Definitions
            .Select(MapDefinition)
            .Append(normalized)
            .OrderBy(x => x.ModelCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _modelInventoryCatalog.MaterializeAsync(definitions, ct);
    }

    public async Task UpdateInventoryDefinitionAsync(
        string existingModelCode,
        ModelInventoryDefinitionInput definition,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingModelCode);
        ArgumentNullException.ThrowIfNull(definition);

        var state = await _intelligence.GetModelInventoryCatalogStateAsync(ct);
        var normalizedExistingCode = existingModelCode.Trim();
        var existing = state.Definitions.FirstOrDefault(x =>
            x.ModelCode.Equals(normalizedExistingCode, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            throw new InvalidOperationException($"Model '{existingModelCode}' could not be found.");
        }

        var normalized = NormalizeDefinition(definition);

        if (state.Definitions.Any(x =>
                !x.ModelCode.Equals(normalizedExistingCode, StringComparison.OrdinalIgnoreCase) &&
                x.ModelCode.Equals(normalized.ModelCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A model with code '{normalized.ModelCode}' already exists.");
        }

        var definitions = state.Definitions
            .Where(x => !x.ModelCode.Equals(normalizedExistingCode, StringComparison.OrdinalIgnoreCase))
            .Select(MapDefinition)
            .Append(normalized)
            .OrderBy(x => x.ModelCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await _modelInventoryCatalog.MaterializeAsync(definitions, ct);
    }

    public async Task DeleteInventoryDefinitionAsync(string modelCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelCode);

        var state = await _intelligence.GetModelInventoryCatalogStateAsync(ct);
        if (state.Definitions.Count <= 1)
        {
            throw new InvalidOperationException("The last model catalog record cannot be deleted.");
        }

        var normalizedCode = modelCode.Trim();
        var definitions = state.Definitions
            .Where(x => !x.ModelCode.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase))
            .Select(MapDefinition)
            .ToList();

        if (definitions.Count == state.Definitions.Count)
        {
            throw new InvalidOperationException($"Model '{modelCode}' could not be found.");
        }

        await _modelInventoryCatalog.MaterializeAsync(definitions, ct);
    }

    public async Task<List<ModelValidationScheduleRow>> GetValidationScheduleAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        return ws.ModelRisk.ValidationCalendar;
    }

    public async Task<ModelPerformanceData> GetPerformanceDataAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var m = ws.ModelRisk;
        return new ModelPerformanceData
        {
            PerformanceEvidence = m.PerformanceEvidence,
            Backtesting = m.Backtesting,
            MonitoringSummary = m.MonitoringSummary
        };
    }

    public async Task<ModelChangeData> GetChangeDataAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var workflow = await _approvalStore.LoadAsync(ct);
        return new ModelChangeData
        {
            ApprovalQueue = ws.ModelRisk.ApprovalQueue,
            RecentChanges = ws.ModelRisk.RecentChanges,
            WorkflowStages = workflow.Stages
                .Select(s => new ModelWorkflowStageItem
                {
                    WorkflowKey = s.WorkflowKey,
                    ModelCode = s.ModelCode,
                    ModelName = s.ModelName,
                    Artifact = s.Artifact,
                    Stage = s.Stage,
                    ChangedAtUtc = s.ChangedAtUtc
                }).ToList(),
            AuditTrail = workflow.AuditTrail
                .Select(a => new ModelWorkflowAuditItem
                {
                    WorkflowKey = a.WorkflowKey,
                    ModelCode = a.ModelCode,
                    ModelName = a.ModelName,
                    Artifact = a.Artifact,
                    PreviousStage = a.PreviousStage,
                    Stage = a.Stage,
                    ChangedAtUtc = a.ChangedAtUtc
                }).ToList()
        };
    }

    public async Task AdvanceWorkflowAsync(
        string workflowKey, string modelCode, string modelName,
        string artifact, string currentStage, string newStage,
        CancellationToken ct = default)
    {
        await _approvalStore.RecordStageChangeAsync(new ModelApprovalWorkflowCommand
        {
            WorkflowKey = workflowKey,
            ModelCode = modelCode,
            ModelName = modelName,
            Artifact = artifact,
            PreviousStage = currentStage,
            Stage = newStage,
            ChangedAtUtc = DateTime.UtcNow
        }, ct);
    }

    public async Task<ModelReportingData> GetReportingDataAsync(CancellationToken ct = default)
    {
        var ws = await _intelligence.GetWorkspaceAsync(ct);
        var m = ws.ModelRisk;
        return new ModelReportingData
        {
            ReportingPack = m.ReportingPack,
            AppetiteRegister = m.AppetiteRegister,
            ReturnPack = m.ReturnPack
        };
    }

    private static ModelInventoryDefinitionInput NormalizeDefinition(ModelInventoryDefinitionInput definition)
    {
        var modelCode = NormalizeRequired(definition.ModelCode, "Model code").ToUpperInvariant();
        var modelName = NormalizeRequired(definition.ModelName, "Model name");
        var tier = NormalizeRequired(definition.Tier, "Tier");
        var owner = NormalizeRequired(definition.Owner, "Owner");
        var returnHint = NormalizeRequired(definition.ReturnHint, "Return hint");
        var matchTerms = NormalizeMatchTerms(definition.MatchTerms);

        return new ModelInventoryDefinitionInput
        {
            ModelCode = modelCode,
            ModelName = modelName,
            Tier = tier,
            Owner = owner,
            ReturnHint = returnHint,
            MatchTerms = matchTerms
        };
    }

    private static ModelInventoryDefinitionInput MapDefinition(ModelInventoryDefinitionState definition) =>
        new()
        {
            ModelCode = definition.ModelCode,
            ModelName = definition.ModelName,
            Tier = definition.Tier,
            Owner = definition.Owner,
            ReturnHint = definition.ReturnHint,
            MatchTerms = definition.MatchTerms
        };

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName} is required.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeMatchTerms(IReadOnlyList<string>? matchTerms)
    {
        var normalized = (matchTerms ?? [])
            .SelectMany(x => (x ?? string.Empty).Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
        {
            throw new InvalidOperationException("At least one match term is required.");
        }

        return normalized;
    }
}

public sealed class ModelDashboardData
{
    public int InventoryCount { get; set; }
    public int DueValidationCount { get; set; }
    public int PerformanceAlertCount { get; set; }
    public int ApprovalQueueCount { get; set; }
    public decimal? AverageAccuracyScore { get; set; }
    public decimal RiskAppetiteScore { get; set; }
    public int InAppetiteCount { get; set; }
    public int WatchCount { get; set; }
    public int BreachCount { get; set; }
    public int ChangeReviewCount { get; set; }
}

public sealed class ModelPerformanceData
{
    public List<ModelPerformanceEvidenceRow> PerformanceEvidence { get; set; } = [];
    public List<ModelBacktestingRow> Backtesting { get; set; } = [];
    public List<ModelMonitoringSummaryRow> MonitoringSummary { get; set; } = [];
}

public sealed class ModelChangeData
{
    public List<ModelApprovalQueueRow> ApprovalQueue { get; set; } = [];
    public List<ModelChangeRow> RecentChanges { get; set; } = [];
    public List<ModelWorkflowStageItem> WorkflowStages { get; set; } = [];
    public List<ModelWorkflowAuditItem> AuditTrail { get; set; } = [];
}

public sealed class ModelWorkflowStageItem
{
    public string WorkflowKey { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Artifact { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
}

public sealed class ModelWorkflowAuditItem
{
    public string WorkflowKey { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Artifact { get; set; } = string.Empty;
    public string PreviousStage { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
}

public sealed class ModelReportingData
{
    public List<ModelRiskReportingRow> ReportingPack { get; set; } = [];
    public List<ModelRiskAppetiteRow> AppetiteRegister { get; set; } = [];
    public List<ModelRiskSheetRow> ReturnPack { get; set; } = [];
}
