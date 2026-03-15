using System.Diagnostics;
using System.Text.Json;
using Dapper;
using FC.Engine.Domain.Abstractions;

namespace FC.Engine.Admin.Services.Scenarios;

/// <summary>
/// Production IScenarioEngine implementation.
/// Persists scenarios and results to meta.scenario_definitions / meta.scenario_results
/// via Dapper. Scenarios are scoped to the authenticated regulator's RegulatorCode so
/// that each regulatory body only sees its own sandbox scenarios.
/// </summary>
public sealed class PersistedScenarioEngine : IScenarioEngine
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IDbConnectionFactory _db;
    private readonly RegulatorSessionService _session;

    public PersistedScenarioEngine(IDbConnectionFactory db, RegulatorSessionService session)
    {
        _db      = db;
        _session = session;
    }

    // ── Regulator context ──────────────────────────────────────────────────

    private async Task<(string RegulatorCode, Guid TenantId)> GetContextAsync()
    {
        var ctx = await _session.GetRequiredAsync();
        return (ctx.RegulatorCode, ctx.TenantId);
    }

    // ── CRUD ───────────────────────────────────────────────────────────────

    public async Task<List<ScenarioDefinition>> GetAllScenarios()
    {
        var (regulatorCode, _) = await GetContextAsync();
        using var conn = await _db.OpenAsync();

        var rows = await conn.QueryAsync<ScenarioRow>(
            """
            SELECT Id, RegulatorCode, Name, Description, TemplateId,
                   Status, Scope,
                   OverridesJson, MacroShocksJson, AffectedModulesJson,
                   CreatedAt, CompletedAt
            FROM   meta.scenario_definitions
            WHERE  RegulatorCode = @RegulatorCode
            ORDER  BY CreatedAt DESC
            """,
            new { RegulatorCode = regulatorCode });

        return rows.Select(MapToDefinition).ToList();
    }

    public async Task<ScenarioDefinition?> GetScenario(int id)
    {
        var (regulatorCode, _) = await GetContextAsync();
        using var conn = await _db.OpenAsync();

        var row = await conn.QuerySingleOrDefaultAsync<ScenarioRow>(
            """
            SELECT Id, RegulatorCode, Name, Description, TemplateId,
                   Status, Scope,
                   OverridesJson, MacroShocksJson, AffectedModulesJson,
                   CreatedAt, CompletedAt
            FROM   meta.scenario_definitions
            WHERE  Id            = @Id
              AND  RegulatorCode = @RegulatorCode
            """,
            new { Id = id, RegulatorCode = regulatorCode });

        return row is null ? null : MapToDefinition(row);
    }

    public async Task<ScenarioDefinition> SaveScenario(ScenarioDefinition scenario)
    {
        var (regulatorCode, _) = await GetContextAsync();
        using var conn = await _db.OpenAsync();

        var overridesJson       = JsonSerializer.Serialize(scenario.Overrides,       _json);
        var macroShocksJson     = JsonSerializer.Serialize(scenario.MacroShocks,     _json);
        var affectedModulesJson = JsonSerializer.Serialize(scenario.AffectedModules, _json);

        if (scenario.Id == 0)
        {
            // INSERT
            var newId = await conn.ExecuteScalarAsync<int>(
                """
                INSERT INTO meta.scenario_definitions
                    (RegulatorCode, Name, Description, TemplateId,
                     Status, Scope,
                     OverridesJson, MacroShocksJson, AffectedModulesJson,
                     CreatedAt, CompletedAt)
                OUTPUT INSERTED.Id
                VALUES
                    (@RegulatorCode, @Name, @Description, @TemplateId,
                     @Status, @Scope,
                     @OverridesJson, @MacroShocksJson, @AffectedModulesJson,
                     @CreatedAt, @CompletedAt)
                """,
                new
                {
                    RegulatorCode       = regulatorCode,
                    scenario.Name,
                    Description         = scenario.Description,
                    TemplateId          = scenario.TemplateId,
                    Status              = scenario.Status.ToString(),
                    Scope               = scenario.Scope.ToString(),
                    OverridesJson       = overridesJson,
                    MacroShocksJson     = macroShocksJson,
                    AffectedModulesJson = affectedModulesJson,
                    scenario.CreatedAt,
                    scenario.CompletedAt,
                });

            scenario.Id = newId;
        }
        else
        {
            // UPDATE (enforce regulator ownership)
            await conn.ExecuteAsync(
                """
                UPDATE meta.scenario_definitions
                SET    Name                = @Name,
                       Description         = @Description,
                       TemplateId          = @TemplateId,
                       Status              = @Status,
                       Scope               = @Scope,
                       OverridesJson       = @OverridesJson,
                       MacroShocksJson     = @MacroShocksJson,
                       AffectedModulesJson = @AffectedModulesJson,
                       CompletedAt         = @CompletedAt
                WHERE  Id            = @Id
                  AND  RegulatorCode = @RegulatorCode
                """,
                new
                {
                    scenario.Id,
                    RegulatorCode       = regulatorCode,
                    scenario.Name,
                    Description         = scenario.Description,
                    TemplateId          = scenario.TemplateId,
                    Status              = scenario.Status.ToString(),
                    Scope               = scenario.Scope.ToString(),
                    OverridesJson       = overridesJson,
                    MacroShocksJson     = macroShocksJson,
                    AffectedModulesJson = affectedModulesJson,
                    scenario.CompletedAt,
                });
        }

        return scenario;
    }

    public async Task DeleteScenario(int id)
    {
        var (regulatorCode, _) = await GetContextAsync();
        using var conn = await _db.OpenAsync();

        // FK ON DELETE CASCADE removes meta.scenario_results automatically
        await conn.ExecuteAsync(
            """
            DELETE FROM meta.scenario_definitions
            WHERE  Id            = @Id
              AND  RegulatorCode = @RegulatorCode
            """,
            new { Id = id, RegulatorCode = regulatorCode });
    }

    // ── Run Single Scenario ────────────────────────────────────────────────

    public async Task<ScenarioResult> RunScenario(Guid tenantId, int scenarioId)
    {
        var scenario = await GetScenario(scenarioId)
            ?? throw new InvalidOperationException($"Scenario {scenarioId} not found");

        // Return cached result if already computed
        if (scenario.Status == ScenarioStatus.Completed)
        {
            var cached = await TryGetCachedResultAsync(scenarioId);
            if (cached is not null) return cached;
        }

        // Mark as running and update DB
        scenario.Status = ScenarioStatus.Running;
        await SaveScenario(scenario);

        var sw = Stopwatch.StartNew();

        var impactKey = ScenarioComputationHelper.ResolveImpactKey(scenario);
        var metrics   = ScenarioComputationHelper.ComputeMetrics(impactKey, scenario);
        var breaches  = ScenarioComputationHelper.DetectBreaches(metrics);
        var proForma  = ScenarioComputationHelper.BuildProFormaFields(metrics, scenario);

        sw.Stop();

        var result = new ScenarioResult
        {
            ScenarioId          = scenarioId,
            ScenarioName        = scenario.Name,
            RunAt               = DateTime.UtcNow,
            DurationMs          = sw.ElapsedMilliseconds,
            KeyMetrics          = metrics,
            Breaches            = breaches,
            ProFormaFields      = proForma,
            TotalFieldsAffected = proForma.Count(f => f.IsOverridden || f.IsComputed),
            FormulasRecomputed  = proForma.Count(f => f.IsComputed),
            ValidationErrors    = breaches.Count(b => b.Severity == BreachSeverity.Critical),
            ValidationWarnings  = breaches.Count(b => b.Severity is BreachSeverity.Warning or BreachSeverity.Breach),
        };

        // Persist result and update scenario status
        await UpsertResultAsync(result);

        scenario.Status      = ScenarioStatus.Completed;
        scenario.CompletedAt = DateTime.UtcNow;
        await SaveScenario(scenario);

        return result;
    }

    // ── Compare Scenarios ──────────────────────────────────────────────────

    public async Task<ComparisonReport> CompareScenarios(Guid tenantId, List<int> scenarioIds)
    {
        var results = new List<ScenarioResult>();
        foreach (var id in scenarioIds)
        {
            var cached = await TryGetCachedResultAsync(id);
            results.Add(cached ?? await RunScenario(tenantId, id));
        }

        return new ComparisonReport
        {
            GeneratedAt       = DateTime.UtcNow,
            Scenarios         = results,
            SharedMetricNames = results
                .SelectMany(r => r.KeyMetrics.Select(m => m.MetricName))
                .Distinct()
                .ToList(),
        };
    }

    // ── Macro-Prudential ───────────────────────────────────────────────────
    // The MacroPrudential page uses IStressTestOrchestrator (fully persisted)
    // rather than IScenarioEngine.RunMacroPrudential. This implementation is
    // kept for callers that use IScenarioEngine directly (e.g. comparison tabs).

    public Task<MacroPrudentialResult> RunMacroPrudential(Guid tenantId, ScenarioDefinition scenario)
    {
        var impactKey    = ScenarioComputationHelper.ResolveImpactKey(scenario);
        var institutions = ScenarioComputationHelper.GenerateMockInstitutions();
        var impacts      = new List<InstitutionImpact>();

        foreach (var inst in institutions)
        {
            var jitter   = 0.7m + (decimal)Random.Shared.NextDouble() * 0.6m;
            var metrics  = ScenarioComputationHelper.ComputeMetrics(impactKey, scenario, jitter, inst.BaselineVariance);
            var breaches = ScenarioComputationHelper.DetectBreaches(metrics);
            var worst    = breaches.Count > 0 ? breaches.Max(b => b.Severity) : BreachSeverity.None;

            impacts.Add(new InstitutionImpact(inst.Id, inst.Name, inst.Type, inst.IsSii, metrics, breaches, worst));
        }

        var sectorAggregates = new Dictionary<string, SectorAggregate>();
        foreach (var metricName in impacts.SelectMany(i => i.Metrics.Select(m => m.MetricName)).Distinct())
        {
            var values = impacts
                .Select(i => i.Metrics.FirstOrDefault(m => m.MetricName == metricName))
                .Where(m => m != null)
                .ToList();

            if (values.Count == 0) continue;

            var higherIsBetter = ScenarioComputationHelper.BaselineMetrics.TryGetValue(metricName, out var bm) && bm.HigherIsBetter;
            var worstVal  = higherIsBetter ? values.Min(v => v!.ScenarioValue) : values.Max(v => v!.ScenarioValue);
            var worstInst = impacts.First(i => i.Metrics.Any(m => m.MetricName == metricName && m.ScenarioValue == worstVal));
            var threshold = values.First()!.Threshold;
            var breachingCount = threshold.HasValue
                ? values.Count(v => higherIsBetter ? v!.ScenarioValue < threshold.Value : v!.ScenarioValue > threshold.Value)
                : 0;

            sectorAggregates[metricName] = new SectorAggregate(
                metricName,
                Math.Round(values.Average(v => v!.BaselineValue), 2),
                Math.Round(values.Average(v => v!.ScenarioValue), 2),
                worstVal,
                worstInst.InstitutionName,
                breachingCount
            );
        }

        return Task.FromResult(new MacroPrudentialResult
        {
            ScenarioId            = scenario.Id,
            ShockName             = scenario.Name,
            RunAt                 = DateTime.UtcNow,
            TotalInstitutions     = institutions.Count,
            InstitutionsBreaching = impacts.Count(i => i.WorstBreach >= BreachSeverity.Breach),
            CriticalBreaches      = impacts.Count(i => i.WorstBreach == BreachSeverity.Critical),
            Impacts               = impacts,
            SectorAggregates      = sectorAggregates,
        });
    }

    // ── Persistence helpers ────────────────────────────────────────────────

    private async Task<ScenarioResult?> TryGetCachedResultAsync(int scenarioId)
    {
        using var conn = await _db.OpenAsync();

        var row = await conn.QuerySingleOrDefaultAsync<ResultRow>(
            """
            SELECT Id, ScenarioId, ScenarioName, RunAt, DurationMs,
                   KeyMetricsJson, BreachesJson, ProFormaFieldsJson,
                   TotalFieldsAffected, FormulasRecomputed,
                   ValidationErrors, ValidationWarnings
            FROM   meta.scenario_results
            WHERE  ScenarioId = @ScenarioId
            """,
            new { ScenarioId = scenarioId });

        return row is null ? null : MapToResult(row);
    }

    private async Task UpsertResultAsync(ScenarioResult result)
    {
        using var conn = await _db.OpenAsync();

        await conn.ExecuteAsync(
            """
            MERGE meta.scenario_results AS target
            USING (SELECT @ScenarioId AS ScenarioId) AS src
              ON  target.ScenarioId = src.ScenarioId
            WHEN MATCHED THEN
                UPDATE SET
                    ScenarioName        = @ScenarioName,
                    RunAt               = @RunAt,
                    DurationMs          = @DurationMs,
                    KeyMetricsJson      = @KeyMetricsJson,
                    BreachesJson        = @BreachesJson,
                    ProFormaFieldsJson  = @ProFormaFieldsJson,
                    TotalFieldsAffected = @TotalFieldsAffected,
                    FormulasRecomputed  = @FormulasRecomputed,
                    ValidationErrors    = @ValidationErrors,
                    ValidationWarnings  = @ValidationWarnings
            WHEN NOT MATCHED THEN
                INSERT (ScenarioId, ScenarioName, RunAt, DurationMs,
                        KeyMetricsJson, BreachesJson, ProFormaFieldsJson,
                        TotalFieldsAffected, FormulasRecomputed,
                        ValidationErrors, ValidationWarnings)
                VALUES (@ScenarioId, @ScenarioName, @RunAt, @DurationMs,
                        @KeyMetricsJson, @BreachesJson, @ProFormaFieldsJson,
                        @TotalFieldsAffected, @FormulasRecomputed,
                        @ValidationErrors, @ValidationWarnings);
            """,
            new
            {
                result.ScenarioId,
                result.ScenarioName,
                result.RunAt,
                result.DurationMs,
                KeyMetricsJson     = JsonSerializer.Serialize(result.KeyMetrics,     _json),
                BreachesJson       = JsonSerializer.Serialize(result.Breaches,       _json),
                ProFormaFieldsJson = JsonSerializer.Serialize(result.ProFormaFields, _json),
                result.TotalFieldsAffected,
                result.FormulasRecomputed,
                result.ValidationErrors,
                result.ValidationWarnings,
            });
    }

    // ── Dapper row types ───────────────────────────────────────────────────

    private sealed class ScenarioRow
    {
        public int      Id                  { get; set; }
        public string   RegulatorCode       { get; set; } = "";
        public string   Name                { get; set; } = "";
        public string?  Description         { get; set; }
        public string?  TemplateId          { get; set; }
        public string   Status              { get; set; } = "Draft";
        public string   Scope               { get; set; } = "Single";
        public string   OverridesJson       { get; set; } = "[]";
        public string   MacroShocksJson     { get; set; } = "[]";
        public string   AffectedModulesJson { get; set; } = "[]";
        public DateTime CreatedAt           { get; set; }
        public DateTime? CompletedAt        { get; set; }
    }

    private sealed class ResultRow
    {
        public int      Id                  { get; set; }
        public int      ScenarioId          { get; set; }
        public string   ScenarioName        { get; set; } = "";
        public DateTime RunAt               { get; set; }
        public long     DurationMs          { get; set; }
        public string   KeyMetricsJson      { get; set; } = "[]";
        public string   BreachesJson        { get; set; } = "[]";
        public string   ProFormaFieldsJson  { get; set; } = "[]";
        public int      TotalFieldsAffected { get; set; }
        public int      FormulasRecomputed  { get; set; }
        public int      ValidationErrors    { get; set; }
        public int      ValidationWarnings  { get; set; }
    }

    // ── Mapping helpers ────────────────────────────────────────────────────

    private static ScenarioDefinition MapToDefinition(ScenarioRow r) => new()
    {
        Id              = r.Id,
        Name            = r.Name,
        Description     = r.Description ?? "",
        TemplateId      = r.TemplateId,
        Status          = Enum.TryParse<ScenarioStatus>(r.Status, out var s) ? s : ScenarioStatus.Draft,
        Scope           = Enum.TryParse<ScenarioScope>(r.Scope, out var sc) ? sc : ScenarioScope.Single,
        Overrides       = Deserialize<List<FieldOverride>>(r.OverridesJson),
        MacroShocks     = Deserialize<List<MacroShock>>(r.MacroShocksJson),
        AffectedModules = Deserialize<List<string>>(r.AffectedModulesJson),
        CreatedAt       = r.CreatedAt,
        CompletedAt     = r.CompletedAt,
    };

    private static ScenarioResult MapToResult(ResultRow r) => new()
    {
        ScenarioId          = r.ScenarioId,
        ScenarioName        = r.ScenarioName,
        RunAt               = r.RunAt,
        DurationMs          = r.DurationMs,
        KeyMetrics          = Deserialize<List<MetricResult>>(r.KeyMetricsJson),
        Breaches            = Deserialize<List<BreachAlert>>(r.BreachesJson),
        ProFormaFields      = Deserialize<List<ProFormaField>>(r.ProFormaFieldsJson),
        TotalFieldsAffected = r.TotalFieldsAffected,
        FormulasRecomputed  = r.FormulasRecomputed,
        ValidationErrors    = r.ValidationErrors,
        ValidationWarnings  = r.ValidationWarnings,
    };

    private static T Deserialize<T>(string json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json, _json) ?? new T(); }
        catch { return new T(); }
    }
}
