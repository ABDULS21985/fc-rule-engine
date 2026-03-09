using Dapper;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FC.Engine.Infrastructure.BackgroundJobs;

/// <summary>
/// Runs the full EWI computation cycle on a configurable interval.
/// Each cycle: scores all CAMELS ratings → evaluates all EWIs →
/// aggregates systemic indicators → runs contagion analysis →
/// generates supervisory actions for new CRITICAL/HIGH triggers.
/// </summary>
public sealed class EWICycleBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptions<EWIEngineOptions> _options;
    private readonly ILogger<EWICycleBackgroundService> _log;

    public EWICycleBackgroundService(
        IServiceProvider services,
        IOptions<EWIEngineOptions> options,
        ILogger<EWICycleBackgroundService> log)
    {
        _services = services;
        _options  = options;
        _log      = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("EWI background service started. Interval={Min}m",
            _options.Value.CycleIntervalMinutes);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogError(ex,
                    "EWI cycle threw unhandled exception — will retry next interval.");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(_options.Value.CycleIntervalMinutes), ct);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = _services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var opts      = _options.Value;
        var db        = sp.GetRequiredService<IDbConnectionFactory>();
        var camels    = sp.GetRequiredService<ICAMELSScorer>();
        var ewi       = sp.GetRequiredService<IEWIEngine>();
        var systemic  = sp.GetRequiredService<ISystemicRiskAggregator>();
        var contagion = sp.GetRequiredService<IContagionAnalyzer>();

        var periodCode = DeriveCurrentPeriod();
        var runId      = Guid.NewGuid();

        _log.LogInformation("EWI cycle: RunId={RunId} Period={Period}", runId, periodCode);

        using var conn = await db.CreateConnectionAsync(null, ct);

        var institutionTypes = (await conn.QueryAsync<string>(
            """
            SELECT DISTINCT InstitutionType FROM meta.prudential_metrics
            WHERE  RegulatorCode = @Regulator
            """,
            new { Regulator = opts.DefaultRegulatorCode })).ToList();

        // Step 1: Score all CAMELS ratings
        foreach (var type in institutionTypes)
        {
            await camels.ScoreSectorAsync(
                opts.DefaultRegulatorCode, type, periodCode, runId, ct);
        }

        // Step 2: Run full EWI cycle
        var summary = await ewi.RunFullCycleAsync(
            opts.DefaultRegulatorCode, periodCode, ct);

        _log.LogInformation(
            "EWI cycle summary: Triggered={T} Cleared={C} Actions={A}",
            summary.EWIsTriggered, summary.EWIsCleared, summary.ActionsGenerated);

        // Step 3: Aggregate systemic indicators per institution type
        foreach (var type in institutionTypes)
        {
            await systemic.AggregateAsync(
                opts.DefaultRegulatorCode, type, periodCode, runId, ct);
        }

        // Step 4: Contagion analysis
        if (opts.RunContagionAnalysis)
        {
            await contagion.AnalyzeAsync(
                opts.DefaultRegulatorCode, periodCode, runId, ct);
        }
    }

    /// <summary>Returns the most recently completed reporting period (previous month).</summary>
    private static string DeriveCurrentPeriod()
    {
        var now = DateTime.UtcNow.AddMonths(-1);
        return $"{now.Year}-{now.Month:D2}";
    }
}
