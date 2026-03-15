using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FC.Engine.Infrastructure.Services;

public class InterModuleDataFlowEngine : IInterModuleDataFlowEngine
{
    private readonly MetadataDbContext _db;
    private readonly IEntitlementService _entitlementService;
    private readonly IGenericDataRepository _genericDataRepo;
    private readonly ILogger<InterModuleDataFlowEngine> _logger;

    public InterModuleDataFlowEngine(
        MetadataDbContext db,
        IEntitlementService entitlementService,
        IGenericDataRepository genericDataRepo,
        ILogger<InterModuleDataFlowEngine> logger)
    {
        _db = db;
        _entitlementService = entitlementService;
        _genericDataRepo = genericDataRepo;
        _logger = logger;
    }

    public async Task ProcessDataFlows(
        Guid tenantId,
        int submissionId,
        string sourceModuleCode,
        string sourceTemplateCode,
        int institutionId,
        int returnPeriodId,
        CancellationToken ct = default)
    {
        var flows = await _db.InterModuleDataFlows
            .Include(f => f.SourceModule)
            .Where(f => f.IsActive
                        && f.SourceModule != null
                        && f.SourceModule.ModuleCode == sourceModuleCode
                        && f.SourceTemplateCode == sourceTemplateCode)
            .ToListAsync(ct);

        if (flows.Count == 0)
        {
            return;
        }

        foreach (var flow in flows)
        {
            var hasTargetAccess = await _entitlementService.HasModuleAccess(tenantId, flow.TargetModuleCode, ct);
            if (!hasTargetAccess)
            {
                continue;
            }

            var targetSubmission = await FindOrCreateTargetSubmission(
                tenantId,
                institutionId,
                returnPeriodId,
                flow.TargetTemplateCode,
                ct);

            if (targetSubmission is null)
            {
                continue;
            }

            var sourceValue = await _genericDataRepo.ReadFieldValue(
                flow.SourceTemplateCode,
                submissionId,
                flow.SourceFieldCode,
                ct);

            if (sourceValue is null)
            {
                continue;
            }

            var targetValue = await TransformValue(
                flow,
                sourceValue,
                tenantId,
                submissionId,
                sourceTemplateCode,
                institutionId,
                returnPeriodId,
                ct);

            await _genericDataRepo.WriteFieldValue(
                flow.TargetTemplateCode,
                targetSubmission.Id,
                flow.TargetFieldCode,
                targetValue,
                "InterModule",
                $"{sourceModuleCode}/{flow.SourceTemplateCode}/{flow.SourceFieldCode}",
                changedBy: "System",
                ct);

            _logger.LogInformation(
                "Inter-module data flow applied: {Source} -> {Target} value={Value}",
                $"{sourceModuleCode}/{flow.SourceTemplateCode}/{flow.SourceFieldCode}",
                $"{flow.TargetModuleCode}/{flow.TargetTemplateCode}/{flow.TargetFieldCode}",
                targetValue);
        }
    }

    private async Task<object?> TransformValue(
        InterModuleDataFlow flow,
        object sourceValue,
        Guid tenantId,
        int submissionId,
        string sourceTemplateCode,
        int institutionId,
        int returnPeriodId,
        CancellationToken ct)
    {
        var mode = flow.TransformationType.Trim();
        if (mode.Equals("DirectCopy", StringComparison.OrdinalIgnoreCase))
        {
            return sourceValue;
        }

        if (mode.Equals("Sum", StringComparison.OrdinalIgnoreCase))
        {
            var groupFlows = await _db.InterModuleDataFlows
                .Include(f => f.SourceModule)
                .Where(f => f.IsActive
                            && f.TargetModuleCode == flow.TargetModuleCode
                            && f.TargetTemplateCode == flow.TargetTemplateCode
                            && f.TargetFieldCode == flow.TargetFieldCode)
                .ToListAsync(ct);

            decimal sum = 0m;
            foreach (var sumFlow in groupFlows)
            {
                var sourceSubmissionId = await ResolveSourceSubmissionId(
                    tenantId,
                    institutionId,
                    returnPeriodId,
                    sumFlow.SourceTemplateCode,
                    sourceTemplateCode,
                    submissionId,
                    ct);

                if (!sourceSubmissionId.HasValue)
                {
                    continue;
                }

                var value = await _genericDataRepo.ReadFieldValue(
                    sumFlow.SourceTemplateCode,
                    sourceSubmissionId.Value,
                    sumFlow.SourceFieldCode,
                    ct);

                if (TryToDecimal(value, out var dec))
                {
                    sum += dec;
                }
            }

            return sum;
        }

        if (mode.Equals("Formula", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryToDecimal(sourceValue, out var sourceDecimal))
            {
                return sourceValue;
            }

            if (string.IsNullOrWhiteSpace(flow.TransformFormula))
            {
                return sourceDecimal;
            }

            // Lightweight transformation support: expressions like
            // "value", "value*100", "value/1000", "value+10", "value-5".
            var expr = flow.TransformFormula.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (expr.Equals("value", StringComparison.OrdinalIgnoreCase))
            {
                return sourceDecimal;
            }

            var normalized = expr.ToLowerInvariant();
            if (normalized.StartsWith("value*", StringComparison.Ordinal) &&
                decimal.TryParse(normalized["value*".Length..], out var mul))
            {
                return sourceDecimal * mul;
            }

            if (normalized.StartsWith("value/", StringComparison.Ordinal) &&
                decimal.TryParse(normalized["value/".Length..], out var div) &&
                div != 0)
            {
                return sourceDecimal / div;
            }

            if (normalized.StartsWith("value+", StringComparison.Ordinal) &&
                decimal.TryParse(normalized["value+".Length..], out var add))
            {
                return sourceDecimal + add;
            }

            if (normalized.StartsWith("value-", StringComparison.Ordinal) &&
                decimal.TryParse(normalized["value-".Length..], out var sub))
            {
                return sourceDecimal - sub;
            }

            return sourceDecimal;
        }

        return sourceValue;
    }

    private async Task<int?> ResolveSourceSubmissionId(
        Guid tenantId,
        int institutionId,
        int returnPeriodId,
        string candidateSourceTemplateCode,
        string currentSourceTemplateCode,
        int currentSubmissionId,
        CancellationToken ct)
    {
        if (string.Equals(candidateSourceTemplateCode, currentSourceTemplateCode, StringComparison.OrdinalIgnoreCase))
        {
            return currentSubmissionId;
        }

        var sourceSubmission = await _db.Submissions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId
                        && s.InstitutionId == institutionId
                        && s.ReturnPeriodId == returnPeriodId
                        && s.ReturnCode == candidateSourceTemplateCode)
            .OrderByDescending(s => s.SubmittedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        return sourceSubmission?.Id;
    }

    private async Task<Submission?> FindOrCreateTargetSubmission(
        Guid tenantId,
        int institutionId,
        int returnPeriodId,
        string targetTemplateCode,
        CancellationToken ct)
    {
        var existing = await _db.Submissions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId
                                      && s.InstitutionId == institutionId
                                      && s.ReturnPeriodId == returnPeriodId
                                      && s.ReturnCode == targetTemplateCode,
                ct);

        if (existing is not null)
        {
            return existing;
        }

        var created = Submission.Create(institutionId, returnPeriodId, targetTemplateCode, tenantId);
        created.MarkSubmitted();
        _db.Submissions.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    private static bool TryToDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case decimal dec:
                result = dec;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case double d:
                result = (decimal)d;
                return true;
            case float f:
                result = (decimal)f;
                return true;
            case string s when decimal.TryParse(s, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
