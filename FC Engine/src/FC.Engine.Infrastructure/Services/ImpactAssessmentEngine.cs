using System.Diagnostics;
using System.Text.Json;
using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public sealed class ImpactAssessmentEngine : IImpactAssessmentEngine
{
    private readonly MetadataDbContext _db;
    private readonly IPolicyAuditLogger _audit;
    private readonly ILogger<ImpactAssessmentEngine> _log;

    public ImpactAssessmentEngine(
        MetadataDbContext db, IPolicyAuditLogger audit, ILogger<ImpactAssessmentEngine> log)
    {
        _db = db;
        _audit = audit;
        _log = log;
    }

    public async Task<ImpactAssessmentResult> RunAssessmentAsync(
        long scenarioId, int regulatorId, int userId, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();

        _log.LogInformation(
            "Starting impact assessment: ScenarioId={ScenarioId}, RegulatorId={RegulatorId}, CorrelationId={CorrelationId}",
            scenarioId, regulatorId, correlationId);

        // Step 1: Load scenario and validate ownership
        var scenario = await _db.PolicyScenarios
            .FirstOrDefaultAsync(s => s.Id == scenarioId && s.RegulatorId == regulatorId, ct)
            ?? throw new InvalidOperationException($"Policy scenario {scenarioId} not found.");

        if (scenario.Status == PolicyStatus.Draft)
            throw new InvalidOperationException("Cannot run assessment on a DRAFT scenario. Set parameters first.");

        // Step 2: Load parameters
        var parameters = await _db.PolicyParameters
            .Where(p => p.ScenarioId == scenarioId)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync(ct);

        if (parameters.Count == 0)
            throw new InvalidOperationException("Scenario has no parameters defined.");

        // Step 3: Determine run number
        var maxRunNumber = await _db.ImpactAssessmentRuns
            .Where(r => r.ScenarioId == scenarioId)
            .MaxAsync(r => (int?)r.RunNumber, ct) ?? 0;
        var runNumber = maxRunNumber + 1;

        // Step 4: Create immutable run record
        var run = new ImpactAssessmentRun
        {
            ScenarioId = scenarioId,
            RegulatorId = regulatorId,
            RunNumber = runNumber,
            Status = ImpactRunStatus.Running,
            SnapshotDate = scenario.BaselineDate,
            CorrelationId = correlationId,
            CreatedByUserId = userId
        };
        _db.ImpactAssessmentRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(scenarioId, regulatorId, correlationId,
            "SIMULATION_STARTED", new { runId = run.Id, runNumber, parameterCount = parameters.Count }, userId, ct);

        // Step 5: Load all supervised entities
        var targetTypes = scenario.TargetEntityTypes == "ALL"
            ? null
            : scenario.TargetEntityTypes.Split(',').Select(t => t.Trim()).ToList();

        // Query institutions from the Institutions table (existing entity in the codebase)
        var entitiesQuery = _db.Set<Institution>()
            .Where(i => i.IsActive);

        if (targetTypes != null)
            entitiesQuery = entitiesQuery.Where(i => i.LicenseType != null && targetTypes.Contains(i.LicenseType));

        var entities = await entitiesQuery
            .OrderBy(i => i.LicenseType).ThenBy(i => i.InstitutionCode)
            .Select(i => new { i.Id, i.InstitutionCode, Name = i.InstitutionName, EntityType = i.LicenseType ?? "" })
            .ToListAsync(ct);

        _log.LogInformation("Evaluating {Count} entities for scenario {ScenarioId}.", entities.Count, scenarioId);

        // Step 6: Evaluate each entity against each parameter
        int compliant = 0, wouldBreach = 0, alreadyBreaching = 0, notAffected = 0;
        decimal totalCapitalShortfall = 0m, totalComplianceCost = 0m;

        foreach (var entity in entities)
        {
            var parameterResults = new List<EntityImpactDetail>();
            var overallCategory = ImpactCategory.CurrentlyCompliant;
            decimal? primaryCurrentValue = null;
            decimal? primaryProposedThreshold = null;
            decimal totalGap = 0m;

            foreach (var param in parameters)
            {
                var applicableTypes = param.ApplicableEntityTypes == "ALL"
                    ? null
                    : param.ApplicableEntityTypes.Split(',').Select(t => t.Trim()).ToHashSet();

                if (applicableTypes != null && !applicableTypes.Contains(entity.EntityType))
                {
                    parameterResults.Add(new EntityImpactDetail(
                        param.ParameterCode, 0m, param.CurrentValue, param.ProposedValue, 0m, "NOT_APPLICABLE"));
                    continue;
                }

                // Fetch entity's actual metric value from return data
                var entityValue = await GetEntityMetricValue(entity.Id, param.ReturnLineReference, scenario.BaselineDate, ct);

                if (entityValue is null)
                {
                    parameterResults.Add(new EntityImpactDetail(
                        param.ParameterCode, 0m, param.CurrentValue, param.ProposedValue, 0m, "NO_DATA"));
                    continue;
                }

                var gap = entityValue.Value - param.ProposedValue;
                var currentGap = entityValue.Value - param.CurrentValue;

                string status;
                if (currentGap < 0)
                {
                    status = "ALREADY_BREACHING";
                    overallCategory = ImpactCategory.AlreadyBreaching;
                }
                else if (gap < 0)
                {
                    status = "WOULD_BREACH";
                    if (overallCategory != ImpactCategory.AlreadyBreaching)
                        overallCategory = ImpactCategory.WouldBreach;
                }
                else
                {
                    status = "COMPLIANT";
                }

                parameterResults.Add(new EntityImpactDetail(
                    param.ParameterCode, entityValue.Value, param.CurrentValue,
                    param.ProposedValue, gap, status));

                primaryCurrentValue ??= entityValue.Value;
                primaryProposedThreshold ??= param.ProposedValue;
                if (gap < 0) totalGap += gap;
            }

            if (parameterResults.All(p => p.Status is "NOT_APPLICABLE" or "NO_DATA"))
                overallCategory = ImpactCategory.NotAffected;

            var complianceCost = overallCategory == ImpactCategory.WouldBreach
                ? EstimateComplianceCost(entity.EntityType, totalGap)
                : 0m;

            switch (overallCategory)
            {
                case ImpactCategory.CurrentlyCompliant: compliant++; break;
                case ImpactCategory.WouldBreach: wouldBreach++; break;
                case ImpactCategory.AlreadyBreaching: alreadyBreaching++; break;
                case ImpactCategory.NotAffected: notAffected++; break;
            }
            if (complianceCost > 0) totalComplianceCost += complianceCost;
            if (totalGap < 0) totalCapitalShortfall += Math.Abs(totalGap);

            _db.EntityImpactResults.Add(new EntityImpactResult
            {
                RunId = run.Id,
                InstitutionId = entity.Id,
                InstitutionCode = entity.InstitutionCode,
                InstitutionName = entity.Name,
                EntityType = entity.EntityType,
                ImpactCategory = overallCategory,
                ParameterResults = JsonSerializer.Serialize(parameterResults),
                CurrentMetricValue = primaryCurrentValue,
                ProposedThreshold = primaryProposedThreshold,
                GapToCompliance = totalGap,
                EstimatedComplianceCost = complianceCost
            });
        }

        // Step 7: Update run with aggregate results
        sw.Stop();
        run.Status = ImpactRunStatus.Completed;
        run.TotalEntitiesEvaluated = entities.Count;
        run.EntitiesCurrentlyCompliant = compliant;
        run.EntitiesWouldBreach = wouldBreach;
        run.EntitiesAlreadyBreaching = alreadyBreaching;
        run.EntitiesNotAffected = notAffected;
        run.AggregateCapitalShortfall = totalCapitalShortfall;
        run.AggregateComplianceCost = totalComplianceCost;
        run.ExecutionTimeMs = sw.ElapsedMilliseconds;
        run.StartedAt = DateTime.UtcNow.AddMilliseconds(-sw.ElapsedMilliseconds);
        run.CompletedAt = DateTime.UtcNow;

        // Transition scenario status
        if (scenario.Status is PolicyStatus.ParametersSet or PolicyStatus.Simulated)
        {
            scenario.Status = PolicyStatus.Simulated;
            scenario.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(scenarioId, regulatorId, correlationId,
            "SIMULATION_COMPLETED", new
            {
                runId = run.Id, total = entities.Count, wouldBreach, alreadyBreaching,
                shortfall = totalCapitalShortfall, executionMs = sw.ElapsedMilliseconds
            }, userId, ct);

        _log.LogInformation(
            "Impact assessment completed: RunId={RunId}, Total={Total}, WouldBreach={WouldBreach}, " +
            "AlreadyBreaching={AlreadyBreaching}, ShortfallNGN={Shortfall}M, ElapsedMs={Ms}",
            run.Id, entities.Count, wouldBreach, alreadyBreaching, totalCapitalShortfall, sw.ElapsedMilliseconds);

        return new ImpactAssessmentResult(
            run.Id, scenarioId, runNumber, ImpactRunStatus.Completed,
            scenario.BaselineDate, entities.Count,
            compliant, wouldBreach, alreadyBreaching, notAffected,
            totalCapitalShortfall, totalComplianceCost,
            sw.ElapsedMilliseconds, correlationId);
    }

    private async Task<decimal?> GetEntityMetricValue(
        int institutionId, string? returnLineReference, DateOnly baselineDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(returnLineReference))
            return null;

        var parts = returnLineReference.Split('.');
        if (parts.Length != 2) return null;

        var returnCode = parts[0];
        var lineCode = parts[1];

        // Query ReturnLineValues through ReturnInstances
        // These are existing tables in the codebase
        var value = await _db.Database.SqlQueryRaw<decimal?>(
            """
            SELECT TOP 1 rl.Value
            FROM ReturnLineValues rl
            JOIN ReturnInstances ri ON ri.Id = rl.ReturnInstanceId
            WHERE ri.InstitutionId = {0}
              AND ri.ReturnCode = {1}
              AND ri.Status = 'APPROVED'
              AND ri.ReportingPeriodEnd <= {2}
              AND rl.LineCode = {3}
            ORDER BY ri.ReportingPeriodEnd DESC
            """,
            institutionId, returnCode, baselineDate, lineCode)
            .FirstOrDefaultAsync(ct);

        return value;
    }

    private static decimal EstimateComplianceCost(string entityType, decimal totalGap)
    {
        var absGap = Math.Abs(totalGap);
        var costMultiplier = entityType switch
        {
            "DMB" => 500m,
            "MFB" => 5m,
            "PMB" => 2m,
            "MERC" => 50m,
            _ => 10m
        };
        return Math.Round(absGap * costMultiplier, 2);
    }

    public async Task<ImpactAssessmentResult> GetRunResultAsync(
        long runId, int regulatorId, CancellationToken ct = default)
    {
        var run = await _db.ImpactAssessmentRuns
            .FirstOrDefaultAsync(r => r.Id == runId && r.RegulatorId == regulatorId, ct)
            ?? throw new InvalidOperationException($"Impact assessment run {runId} not found.");

        return MapToResult(run);
    }

    public async Task<PagedResult<EntityImpactSummary>> GetEntityResultsAsync(
        long runId, int regulatorId, ImpactCategory? categoryFilter,
        string? entityTypeFilter, int page, int pageSize, CancellationToken ct = default)
    {
        var runExists = await _db.ImpactAssessmentRuns
            .AnyAsync(r => r.Id == runId && r.RegulatorId == regulatorId, ct);
        if (!runExists)
            throw new InvalidOperationException($"Impact assessment run {runId} not found.");

        var query = _db.EntityImpactResults.Where(e => e.RunId == runId);

        if (categoryFilter.HasValue)
            query = query.Where(e => e.ImpactCategory == categoryFilter.Value);
        if (!string.IsNullOrEmpty(entityTypeFilter))
            query = query.Where(e => e.EntityType == entityTypeFilter);

        var totalCount = await query.CountAsync(ct);

        var results = await query
            .OrderBy(e => e.ImpactCategory).ThenBy(e => e.InstitutionCode)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        var summaries = results.Select(r =>
        {
            var details = JsonSerializer.Deserialize<List<EntityImpactDetail>>(r.ParameterResults) ?? [];
            return new EntityImpactSummary(
                r.InstitutionId, r.InstitutionCode, r.InstitutionName, r.EntityType,
                r.ImpactCategory, r.CurrentMetricValue, r.ProposedThreshold,
                r.GapToCompliance, r.EstimatedComplianceCost, details);
        }).ToList();

        return new PagedResult<EntityImpactSummary>(summaries, totalCount, page, pageSize);
    }

    public async Task<ScenarioComparisonResult> CompareRunsAsync(
        IReadOnlyList<long> runIds, int regulatorId, CancellationToken ct = default)
    {
        var runs = await _db.ImpactAssessmentRuns
            .Include(r => r.Scenario)
            .Where(r => runIds.Contains(r.Id) && r.RegulatorId == regulatorId)
            .ToListAsync(ct);

        var columns = runs.Select(r => new ScenarioComparisonColumn(
            r.Id, r.ScenarioId, r.Scenario?.Title ?? "",
            r.EntitiesWouldBreach, r.AggregateCapitalShortfall)).ToList();

        var allEntityResults = await _db.EntityImpactResults
            .Where(e => runIds.Contains(e.RunId))
            .Select(e => new { e.RunId, e.InstitutionId, e.InstitutionCode, e.EntityType, e.ImpactCategory })
            .ToListAsync(ct);

        var groupedByEntity = allEntityResults
            .GroupBy(e => e.InstitutionId)
            .Select(g => new ComparisonRow(
                g.Key,
                g.First().InstitutionCode,
                g.First().EntityType,
                g.ToDictionary(e => e.RunId, e => e.ImpactCategory)))
            .ToList();

        return new ScenarioComparisonResult(columns, groupedByEntity);
    }

    private static ImpactAssessmentResult MapToResult(ImpactAssessmentRun r) => new(
        r.Id, r.ScenarioId, r.RunNumber, r.Status,
        r.SnapshotDate, r.TotalEntitiesEvaluated,
        r.EntitiesCurrentlyCompliant, r.EntitiesWouldBreach,
        r.EntitiesAlreadyBreaching, r.EntitiesNotAffected,
        r.AggregateCapitalShortfall, r.AggregateComplianceCost,
        r.ExecutionTimeMs ?? 0, r.CorrelationId);
}
