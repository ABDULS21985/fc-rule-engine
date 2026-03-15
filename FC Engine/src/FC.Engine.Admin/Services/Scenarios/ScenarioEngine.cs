using System.Collections.Concurrent;
using System.Diagnostics;

namespace FC.Engine.Admin.Services.Scenarios;

/// <summary>
/// In-memory implementation of IScenarioEngine — used for dev/test only.
/// State is lost on restart. Production uses PersistedScenarioEngine.
/// </summary>
public class ScenarioEngine : IScenarioEngine
{
    private readonly ConcurrentDictionary<int, ScenarioDefinition> _scenarios = new();
    private readonly ConcurrentDictionary<int, ScenarioResult> _results = new();
    private int _nextId = 1;

    // ── CRUD ───────────────────────────────────────────────────────────────

    public Task<List<ScenarioDefinition>> GetAllScenarios()
        => Task.FromResult(_scenarios.Values.OrderByDescending(s => s.CreatedAt).ToList());

    public Task<ScenarioDefinition?> GetScenario(int id)
        => Task.FromResult(_scenarios.TryGetValue(id, out var s) ? s : null);

    public Task<ScenarioDefinition> SaveScenario(ScenarioDefinition scenario)
    {
        if (scenario.Id == 0)
            scenario.Id = Interlocked.Increment(ref _nextId);

        _scenarios[scenario.Id] = scenario;
        return Task.FromResult(scenario);
    }

    public Task DeleteScenario(int id)
    {
        _scenarios.TryRemove(id, out _);
        _results.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    // ── Run Single Scenario ────────────────────────────────────────────────

    public Task<ScenarioResult> RunScenario(Guid tenantId, int scenarioId)
    {
        if (!_scenarios.TryGetValue(scenarioId, out var scenario))
            throw new InvalidOperationException($"Scenario {scenarioId} not found");

        // Return the cached result if the scenario has already been run
        if (scenario.Status == ScenarioStatus.Completed && _results.TryGetValue(scenarioId, out var cached))
            return Task.FromResult(cached);

        scenario.Status = ScenarioStatus.Running;
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

        scenario.Status     = ScenarioStatus.Completed;
        scenario.CompletedAt = DateTime.UtcNow;
        _results[scenarioId] = result;

        return Task.FromResult(result);
    }

    // ── Compare Scenarios ──────────────────────────────────────────────────

    public async Task<ComparisonReport> CompareScenarios(Guid tenantId, List<int> scenarioIds)
    {
        var results = new List<ScenarioResult>();
        foreach (var id in scenarioIds)
        {
            if (_results.TryGetValue(id, out var cached))
                results.Add(cached);
            else
                results.Add(await RunScenario(tenantId, id));
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

    // ── Macro-Prudential (in-memory) ───────────────────────────────────────

    public Task<MacroPrudentialResult> RunMacroPrudential(Guid tenantId, ScenarioDefinition scenario)
    {
        var impactKey    = ScenarioComputationHelper.ResolveImpactKey(scenario);
        var institutions = ScenarioComputationHelper.GenerateMockInstitutions();
        var impacts      = new List<InstitutionImpact>();

        foreach (var inst in institutions)
        {
            var jitter  = 0.7m + (decimal)Random.Shared.NextDouble() * 0.6m;
            var metrics = ScenarioComputationHelper.ComputeMetrics(impactKey, scenario, jitter, inst.BaselineVariance);
            var breaches = ScenarioComputationHelper.DetectBreaches(metrics);
            var worstBreach = breaches.Count > 0 ? breaches.Max(b => b.Severity) : BreachSeverity.None;

            impacts.Add(new InstitutionImpact(inst.Id, inst.Name, inst.Type, inst.IsSii, metrics, breaches, worstBreach));
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
            ScenarioId           = scenario.Id,
            ShockName            = scenario.Name,
            RunAt                = DateTime.UtcNow,
            TotalInstitutions    = institutions.Count,
            InstitutionsBreaching = impacts.Count(i => i.WorstBreach >= BreachSeverity.Breach),
            CriticalBreaches     = impacts.Count(i => i.WorstBreach == BreachSeverity.Critical),
            Impacts              = impacts,
            SectorAggregates     = sectorAggregates,
        });
    }
}
