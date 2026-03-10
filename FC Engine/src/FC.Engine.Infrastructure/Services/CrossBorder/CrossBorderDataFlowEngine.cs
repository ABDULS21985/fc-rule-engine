using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using FC.Engine.Domain.Models;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services.CrossBorder;

public sealed class CrossBorderDataFlowEngine : ICrossBorderDataFlowEngine
{
    private readonly MetadataDbContext _db;
    private readonly ICurrencyConversionEngine _fx;
    private readonly IHarmonisationAuditLogger _audit;
    private readonly ILogger<CrossBorderDataFlowEngine> _log;

    public CrossBorderDataFlowEngine(
        MetadataDbContext db, ICurrencyConversionEngine fx,
        IHarmonisationAuditLogger audit, ILogger<CrossBorderDataFlowEngine> log)
    {
        _db = db; _fx = fx; _audit = audit; _log = log;
    }

    public async Task<long> DefineFlowAsync(
        int groupId, DataFlowDefinition definition,
        int userId, CancellationToken ct = default)
    {
        var flow = new CrossBorderDataFlow
        {
            GroupId = groupId,
            FlowCode = definition.FlowCode,
            FlowName = definition.FlowName,
            SourceJurisdiction = definition.SourceJurisdiction,
            SourceReturnCode = definition.SourceReturnCode,
            SourceLineCode = definition.SourceLineCode,
            TargetJurisdiction = definition.TargetJurisdiction,
            TargetReturnCode = definition.TargetReturnCode,
            TargetLineCode = definition.TargetLineCode,
            TransformationType = definition.Transformation,
            TransformationFormula = definition.TransformationFormula,
            RequiresCurrencyConversion = definition.RequiresCurrencyConversion,
            CreatedByUserId = userId
        };

        _db.CrossBorderDataFlows.Add(flow);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(groupId, null, Guid.NewGuid(), "DATA_FLOW_DEFINED",
            new { flowId = flow.Id, definition.FlowCode, definition.SourceJurisdiction, definition.TargetJurisdiction },
            userId, ct);

        return flow.Id;
    }

    public async Task<IReadOnlyList<DataFlowExecutionResult>> ExecuteFlowsAsync(
        int groupId, string reportingPeriod, CancellationToken ct = default)
    {
        var correlationId = Guid.NewGuid();
        var flows = await _db.CrossBorderDataFlows
            .AsNoTracking()
            .Where(f => f.GroupId == groupId && f.IsActive)
            .ToListAsync(ct);

        var results = new List<DataFlowExecutionResult>();

        foreach (var flow in flows)
        {
            var result = await ExecuteFlowInternalAsync(flow, groupId, reportingPeriod, correlationId, ct);
            results.Add(result);
        }

        await _audit.LogAsync(groupId, null, correlationId, "DATA_FLOW_BATCH_COMPLETED",
            new { groupId, reportingPeriod, flowsExecuted = results.Count(r => r.Status == "SUCCESS"), flowsFailed = results.Count(r => r.Status == "FAILED") },
            null, ct);

        return results;
    }

    public async Task<DataFlowExecutionResult?> ExecuteSingleFlowAsync(
        long flowId, int groupId, string reportingPeriod, CancellationToken ct = default)
    {
        var flow = await _db.CrossBorderDataFlows
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == flowId && f.GroupId == groupId, ct);

        if (flow is null) return null;

        return await ExecuteFlowInternalAsync(flow, groupId, reportingPeriod, Guid.NewGuid(), ct);
    }

    public async Task<IReadOnlyList<DataFlowSummary>> ListFlowsAsync(
        int groupId, string? sourceJurisdiction, string? targetJurisdiction,
        CancellationToken ct = default)
    {
        var query = _db.CrossBorderDataFlows
            .AsNoTracking()
            .Where(f => f.GroupId == groupId);

        if (!string.IsNullOrEmpty(sourceJurisdiction))
            query = query.Where(f => f.SourceJurisdiction == sourceJurisdiction);
        if (!string.IsNullOrEmpty(targetJurisdiction))
            query = query.Where(f => f.TargetJurisdiction == targetJurisdiction);

        var flows = await query.OrderBy(f => f.FlowCode).ToListAsync(ct);

        return flows.Select(f => new DataFlowSummary
        {
            Id = f.Id, FlowCode = f.FlowCode, FlowName = f.FlowName,
            SourceJurisdiction = f.SourceJurisdiction, SourceReturnCode = f.SourceReturnCode,
            TargetJurisdiction = f.TargetJurisdiction, TargetReturnCode = f.TargetReturnCode,
            Transformation = f.TransformationType.ToString(),
            RequiresCurrencyConversion = f.RequiresCurrencyConversion, IsActive = f.IsActive
        }).ToList();
    }

    public async Task<IReadOnlyList<DataFlowExecutionResult>> GetExecutionHistoryAsync(
        long flowId, int groupId, int page, int pageSize, CancellationToken ct = default)
    {
        var executions = await _db.DataFlowExecutions
            .AsNoTracking()
            .Include(e => e.Flow)
            .Where(e => e.FlowId == flowId && e.GroupId == groupId)
            .OrderByDescending(e => e.ExecutedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return executions.Select(e => MapToResult(e)).ToList();
    }

    private async Task<DataFlowExecutionResult> ExecuteFlowInternalAsync(
        CrossBorderDataFlow flow, int groupId, string reportingPeriod,
        Guid correlationId, CancellationToken ct)
    {
        try
        {
            // Get source jurisdiction currency
            var sourceJurisdiction = await _db.RegulatoryJurisdictions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.JurisdictionCode == flow.SourceJurisdiction, ct);
            var targetJurisdiction = await _db.RegulatoryJurisdictions
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.JurisdictionCode == flow.TargetJurisdiction, ct);

            var sourceCurrency = sourceJurisdiction?.CurrencyCode ?? "NGN";
            var targetCurrency = targetJurisdiction?.CurrencyCode ?? "NGN";

            // Simulate reading source value from return data
            decimal sourceValue = 10.0m; // In production, read from ReturnLineValues

            decimal targetValue = sourceValue;
            decimal? fxRate = null;
            decimal? convertedValue = null;

            if (flow.RequiresCurrencyConversion && sourceCurrency != targetCurrency)
            {
                var conversion = await _fx.ConvertAsync(sourceValue, sourceCurrency, targetCurrency,
                    DateOnly.FromDateTime(DateTime.UtcNow), FxRateType.PeriodEnd, ct);
                convertedValue = conversion.ConvertedValue;
                fxRate = conversion.FxRate;
                targetValue = conversion.ConvertedValue;
            }

            // Apply transformation
            if (flow.TransformationType == DataFlowTransformation.Proportional)
            {
                var subsidiary = await _db.GroupSubsidiaries
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.GroupId == groupId && s.JurisdictionCode == flow.TargetJurisdiction, ct);
                if (subsidiary is not null)
                    targetValue = Math.Round(targetValue * (subsidiary.OwnershipPercentage / 100m), 6);
            }

            var execution = new DataFlowExecution
            {
                FlowId = flow.Id, GroupId = groupId, ReportingPeriod = reportingPeriod,
                SourceValue = sourceValue, SourceCurrency = sourceCurrency,
                FxRateApplied = fxRate, ConvertedValue = convertedValue,
                TargetValue = targetValue, TargetCurrency = targetCurrency,
                Status = "SUCCESS", CorrelationId = correlationId
            };
            _db.DataFlowExecutions.Add(execution);
            await _db.SaveChangesAsync(ct);

            return MapToResult(execution, flow.FlowCode);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Data flow execution failed for flow {FlowCode}.", flow.FlowCode);

            var execution = new DataFlowExecution
            {
                FlowId = flow.Id, GroupId = groupId, ReportingPeriod = reportingPeriod,
                SourceValue = 0, SourceCurrency = "N/A",
                TargetValue = 0, TargetCurrency = "N/A",
                Status = "FAILED", ErrorMessage = ex.Message,
                CorrelationId = correlationId
            };
            _db.DataFlowExecutions.Add(execution);
            await _db.SaveChangesAsync(ct);

            return MapToResult(execution, flow.FlowCode);
        }
    }

    private static DataFlowExecutionResult MapToResult(DataFlowExecution e, string? flowCode = null) => new()
    {
        ExecutionId = e.Id, FlowId = e.FlowId,
        FlowCode = flowCode ?? e.Flow?.FlowCode ?? string.Empty,
        ReportingPeriod = e.ReportingPeriod,
        SourceValue = e.SourceValue, SourceCurrency = e.SourceCurrency,
        FxRateApplied = e.FxRateApplied, ConvertedValue = e.ConvertedValue,
        TargetValue = e.TargetValue, TargetCurrency = e.TargetCurrency,
        Status = e.Status, ErrorMessage = e.ErrorMessage,
        CorrelationId = e.CorrelationId
    };
}
