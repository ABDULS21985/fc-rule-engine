using FC.Engine.Infrastructure.Services;

namespace FC.Engine.Admin.Services;

public static class PlatformIntelligenceApiRequestMapper
{
    private static readonly HashSet<string> AllowedDashboardLenses = new(StringComparer.OrdinalIgnoreCase)
    {
        "governor",
        "deputy",
        "director",
        "executive"
    };

    private static readonly HashSet<string> AllowedModelApprovalStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Model Owner",
        "Validation Team",
        "Model Risk Committee",
        "Board Review",
        "Approved",
        "Rejected"
    };

    private static readonly HashSet<string> AllowedSanctionsDecisions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Clear",
        "Review",
        "Confirm Match",
        "False Positive",
        "Escalate"
    };

    public static int NormalizeTake(int? take, int defaultValue, int maxValue = 100)
    {
        if (!take.HasValue)
        {
            return defaultValue;
        }

        return Math.Clamp(take.Value, 1, maxValue);
    }

    public static string? NormalizeOptionalFilter(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    public static bool TryNormalizeKnowledgeNavigatorKey(
        string? navigatorKey,
        out string normalized,
        out string error)
    {
        normalized = navigatorKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "Navigator key is required.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeDashboardBriefingPackQuery(
        string? lens,
        int? institutionId,
        out DashboardBriefingPackQuery query,
        out string error)
    {
        var normalizedLens = string.IsNullOrWhiteSpace(lens)
            ? "governor"
            : lens.Trim().ToLowerInvariant();

        if (!AllowedDashboardLenses.Contains(normalizedLens))
        {
            query = new DashboardBriefingPackQuery();
            error = "Lens must be governor, deputy, director, or executive.";
            return false;
        }

        if (normalizedLens == "executive")
        {
            if (!institutionId.HasValue || institutionId.Value <= 0)
            {
                query = new DashboardBriefingPackQuery();
                error = "InstitutionId is required for the executive lens.";
                return false;
            }

            query = new DashboardBriefingPackQuery
            {
                Lens = normalizedLens,
                InstitutionId = institutionId.Value
            };
            error = string.Empty;
            return true;
        }

        query = new DashboardBriefingPackQuery
        {
            Lens = normalizedLens
        };
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeResilienceAssessmentRequest(
        ResilienceAssessmentApiRequest? request,
        out ResilienceAssessmentResponseCommand command,
        out string error)
    {
        var questionId = request?.QuestionId?.Trim() ?? string.Empty;
        var domain = request?.Domain?.Trim() ?? string.Empty;
        var prompt = request?.Prompt?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(questionId))
        {
            command = new ResilienceAssessmentResponseCommand();
            error = "QuestionId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(domain))
        {
            command = new ResilienceAssessmentResponseCommand();
            error = "Domain is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            command = new ResilienceAssessmentResponseCommand();
            error = "Prompt is required.";
            return false;
        }

        var score = request?.Score ?? 0;
        if (score is < 1 or > 5)
        {
            command = new ResilienceAssessmentResponseCommand();
            error = "Score must be between 1 and 5.";
            return false;
        }

        command = new ResilienceAssessmentResponseCommand
        {
            QuestionId = questionId,
            Domain = domain,
            Prompt = prompt,
            Score = score,
            AnsweredAtUtc = DateTime.UtcNow
        };
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeModelApprovalStageRequest(
        ModelApprovalStageApiRequest? request,
        out ModelApprovalWorkflowCommand command,
        out string error)
    {
        var workflowKey = request?.WorkflowKey?.Trim() ?? string.Empty;
        var modelCode = request?.ModelCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var modelName = request?.ModelName?.Trim() ?? string.Empty;
        var artifact = request?.Artifact?.Trim() ?? string.Empty;
        var stage = request?.Stage?.Trim() ?? string.Empty;
        var previousStage = request?.PreviousStage?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workflowKey))
        {
            command = new ModelApprovalWorkflowCommand();
            error = "WorkflowKey is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(modelCode))
        {
            command = new ModelApprovalWorkflowCommand();
            error = "ModelCode is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            command = new ModelApprovalWorkflowCommand();
            error = "ModelName is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(artifact))
        {
            command = new ModelApprovalWorkflowCommand();
            error = "Artifact is required.";
            return false;
        }

        if (!AllowedModelApprovalStages.Contains(stage))
        {
            command = new ModelApprovalWorkflowCommand();
            error = "Stage is not valid for model approval workflow.";
            return false;
        }

        command = new ModelApprovalWorkflowCommand
        {
            WorkflowKey = workflowKey,
            ModelCode = modelCode,
            ModelName = modelName,
            Artifact = artifact,
            PreviousStage = string.IsNullOrWhiteSpace(previousStage) ? "Unassigned" : previousStage,
            Stage = stage,
            ChangedAtUtc = DateTime.UtcNow
        };
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeCapitalScenarioRequest(
        CapitalPlanningScenarioApiRequest? request,
        out CapitalPlanningScenarioCommand command,
        out string error)
    {
        if (request is null)
        {
            command = new CapitalPlanningScenarioCommand();
            error = "Request body is required.";
            return false;
        }

        if (request.CurrentRwaBn <= 0m)
        {
            command = new CapitalPlanningScenarioCommand();
            error = "CurrentRwaBn must be greater than zero.";
            return false;
        }

        if (request.CurrentCarPercent is < 0m or > 100m)
        {
            command = new CapitalPlanningScenarioCommand();
            error = "CurrentCarPercent must be between 0 and 100.";
            return false;
        }

        if (request.TargetCarPercent is < 0m or > 100m)
        {
            command = new CapitalPlanningScenarioCommand();
            error = "TargetCarPercent must be between 0 and 100.";
            return false;
        }

        if (request.StepPercent <= 0m)
        {
            command = new CapitalPlanningScenarioCommand();
            error = "StepPercent must be greater than zero.";
            return false;
        }

        if (request.MaxAt1SharePercent is < 0m or > 100m || request.MaxTier2SharePercent is < 0m or > 100m)
        {
            command = new CapitalPlanningScenarioCommand();
            error = "Max AT1 and Tier 2 shares must be between 0 and 100.";
            return false;
        }

        if (request.MaxAt1SharePercent + request.MaxTier2SharePercent > 100m)
        {
            command = new CapitalPlanningScenarioCommand();
            error = "Combined AT1 and Tier 2 share caps cannot exceed 100.";
            return false;
        }

        command = new CapitalPlanningScenarioCommand
        {
            CurrentCarPercent = request.CurrentCarPercent,
            CurrentRwaBn = request.CurrentRwaBn,
            QuarterlyRwaGrowthPercent = request.QuarterlyRwaGrowthPercent,
            QuarterlyRetainedEarningsBn = request.QuarterlyRetainedEarningsBn,
            CapitalActionBn = request.CapitalActionBn,
            MinimumRequirementPercent = request.MinimumRequirementPercent,
            ConservationBufferPercent = request.ConservationBufferPercent,
            CountercyclicalBufferPercent = request.CountercyclicalBufferPercent,
            DsibBufferPercent = request.DsibBufferPercent,
            RwaOptimisationPercent = request.RwaOptimisationPercent,
            TargetCarPercent = request.TargetCarPercent,
            Cet1CostPercent = request.Cet1CostPercent,
            At1CostPercent = request.At1CostPercent,
            Tier2CostPercent = request.Tier2CostPercent,
            MaxAt1SharePercent = request.MaxAt1SharePercent,
            MaxTier2SharePercent = request.MaxTier2SharePercent,
            StepPercent = request.StepPercent,
            SavedAtUtc = DateTime.UtcNow
        };
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeBatchScreeningRequest(
        SanctionsBatchScreeningApiRequest? request,
        out SanctionsBatchScreeningCommand command,
        out string error)
    {
        var subjects = (request?.Subjects ?? [])
            .Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToList();

        if (subjects.Count == 0)
        {
            command = new SanctionsBatchScreeningCommand();
            error = "At least one subject is required.";
            return false;
        }

        if (subjects.Count > 250)
        {
            command = new SanctionsBatchScreeningCommand();
            error = "A maximum of 250 subjects is allowed per screening run.";
            return false;
        }

        var thresholdPercent = request?.ThresholdPercent ?? 86d;
        if (thresholdPercent is < 70d or > 95d)
        {
            command = new SanctionsBatchScreeningCommand();
            error = "ThresholdPercent must be between 70 and 95.";
            return false;
        }

        command = new SanctionsBatchScreeningCommand
        {
            Subjects = subjects,
            Threshold = thresholdPercent / 100d
        };
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeTransactionScreeningRequest(
        SanctionsTransactionScreeningApiRequest? request,
        out SanctionsTransactionScreeningRequest command,
        out string error)
    {
        var transactionReference = request?.TransactionReference?.Trim() ?? string.Empty;
        var currency = request?.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        var channel = request?.Channel?.Trim() ?? string.Empty;
        var originatorName = request?.OriginatorName?.Trim() ?? string.Empty;
        var beneficiaryName = request?.BeneficiaryName?.Trim() ?? string.Empty;
        var counterpartyName = request?.CounterpartyName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(transactionReference))
        {
            command = new SanctionsTransactionScreeningRequest();
            error = "TransactionReference is required.";
            return false;
        }

        if (request?.Amount <= 0m)
        {
            command = new SanctionsTransactionScreeningRequest();
            error = "Amount must be greater than zero.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            command = new SanctionsTransactionScreeningRequest();
            error = "Currency is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(channel))
        {
            command = new SanctionsTransactionScreeningRequest();
            error = "Channel is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(originatorName)
            && string.IsNullOrWhiteSpace(beneficiaryName)
            && string.IsNullOrWhiteSpace(counterpartyName))
        {
            command = new SanctionsTransactionScreeningRequest();
            error = "At least one transaction party is required.";
            return false;
        }

        command = new SanctionsTransactionScreeningRequest
        {
            TransactionReference = transactionReference,
            Amount = request!.Amount,
            Currency = currency,
            Channel = channel,
            OriginatorName = originatorName,
            BeneficiaryName = beneficiaryName,
            CounterpartyName = counterpartyName,
            HighRisk = request.HighRisk
        };
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeSanctionsWorkflowDecisionRequest(
        SanctionsWorkflowDecisionApiRequest? request,
        out SanctionsWorkflowDecisionCommand command,
        out string error)
    {
        var matchKey = request?.MatchKey?.Trim() ?? string.Empty;
        var subject = request?.Subject?.Trim() ?? string.Empty;
        var matchedName = request?.MatchedName?.Trim() ?? string.Empty;
        var sourceCode = request?.SourceCode?.Trim().ToUpperInvariant() ?? string.Empty;
        var riskLevel = request?.RiskLevel?.Trim() ?? string.Empty;
        var previousDecision = request?.PreviousDecision?.Trim() ?? string.Empty;
        var decision = request?.Decision?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(matchKey))
        {
            command = new SanctionsWorkflowDecisionCommand();
            error = "MatchKey is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            command = new SanctionsWorkflowDecisionCommand();
            error = "Subject is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(matchedName))
        {
            command = new SanctionsWorkflowDecisionCommand();
            error = "MatchedName is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            command = new SanctionsWorkflowDecisionCommand();
            error = "SourceCode is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(riskLevel))
        {
            command = new SanctionsWorkflowDecisionCommand();
            error = "RiskLevel is required.";
            return false;
        }

        if (!AllowedSanctionsDecisions.Contains(decision))
        {
            command = new SanctionsWorkflowDecisionCommand();
            error = "Decision is not valid for sanctions workflow.";
            return false;
        }

        command = new SanctionsWorkflowDecisionCommand
        {
            MatchKey = matchKey,
            Subject = subject,
            MatchedName = matchedName,
            SourceCode = sourceCode,
            RiskLevel = riskLevel,
            PreviousDecision = string.IsNullOrWhiteSpace(previousDecision) ? "Clear" : previousDecision,
            Decision = decision,
            ReviewedAtUtc = DateTime.UtcNow
        };
        error = string.Empty;
        return true;
    }

    public static bool TryNormalizeRolloutReconciliationRequest(
        RolloutReconciliationApiRequest? request,
        out IReadOnlyList<Guid> tenantIds,
        out string error)
    {
        tenantIds = (request?.TenantIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();

        if (tenantIds.Count == 0)
        {
            error = "At least one TenantId is required.";
            return false;
        }

        if (tenantIds.Count > 200)
        {
            error = "A maximum of 200 tenants can be reconciled per request.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public sealed class SanctionsBatchScreeningApiRequest
{
    public List<string> Subjects { get; set; } = [];
    public double? ThresholdPercent { get; set; }
}

public sealed class DashboardBriefingPackQuery
{
    public string Lens { get; init; } = string.Empty;
    public int? InstitutionId { get; init; }
}

public sealed class CapitalPlanningScenarioApiRequest
{
    public decimal CurrentCarPercent { get; set; }
    public decimal CurrentRwaBn { get; set; }
    public decimal QuarterlyRwaGrowthPercent { get; set; }
    public decimal QuarterlyRetainedEarningsBn { get; set; }
    public decimal CapitalActionBn { get; set; }
    public decimal MinimumRequirementPercent { get; set; }
    public decimal ConservationBufferPercent { get; set; }
    public decimal CountercyclicalBufferPercent { get; set; }
    public decimal DsibBufferPercent { get; set; }
    public decimal RwaOptimisationPercent { get; set; }
    public decimal TargetCarPercent { get; set; }
    public decimal Cet1CostPercent { get; set; }
    public decimal At1CostPercent { get; set; }
    public decimal Tier2CostPercent { get; set; }
    public decimal MaxAt1SharePercent { get; set; }
    public decimal MaxTier2SharePercent { get; set; }
    public decimal StepPercent { get; set; }
}

public sealed class ResilienceAssessmentApiRequest
{
    public string QuestionId { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public int Score { get; set; }
}

public sealed class ModelApprovalStageApiRequest
{
    public string WorkflowKey { get; set; } = string.Empty;
    public string ModelCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Artifact { get; set; } = string.Empty;
    public string PreviousStage { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
}

public sealed class SanctionsBatchScreeningCommand
{
    public List<string> Subjects { get; init; } = [];
    public double Threshold { get; init; }
}

public sealed class SanctionsTransactionScreeningApiRequest
{
    public string TransactionReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string OriginatorName { get; set; } = string.Empty;
    public string BeneficiaryName { get; set; } = string.Empty;
    public string CounterpartyName { get; set; } = string.Empty;
    public bool HighRisk { get; set; }
}

public sealed class SanctionsWorkflowDecisionApiRequest
{
    public string MatchKey { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string MatchedName { get; set; } = string.Empty;
    public string SourceCode { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = string.Empty;
    public string PreviousDecision { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
}

public sealed class RolloutReconciliationApiRequest
{
    public List<Guid> TenantIds { get; set; } = [];
}
