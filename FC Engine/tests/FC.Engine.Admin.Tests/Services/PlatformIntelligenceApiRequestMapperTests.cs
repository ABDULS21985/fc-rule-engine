using FC.Engine.Admin.Services;
using FluentAssertions;
using Xunit;

namespace FC.Engine.Admin.Tests.Services;

public class PlatformIntelligenceApiRequestMapperTests
{
    [Fact]
    public void NormalizeTake_Clamps_Requested_Value()
    {
        PlatformIntelligenceApiRequestMapper.NormalizeTake(null, 25).Should().Be(25);
        PlatformIntelligenceApiRequestMapper.NormalizeTake(0, 25).Should().Be(1);
        PlatformIntelligenceApiRequestMapper.NormalizeTake(120, 25, 50).Should().Be(50);
    }

    [Fact]
    public void TryNormalizeKnowledgeNavigatorKey_Rejects_Blank_Key()
    {
        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeKnowledgeNavigatorKey("   ", out var key, out var error);

        success.Should().BeFalse();
        key.Should().BeEmpty();
        error.Should().Be("Navigator key is required.");
    }

    [Fact]
    public void TryNormalizeKnowledgeNavigatorKey_Trims_Key()
    {
        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeKnowledgeNavigatorKey("  rg31|bdc_f xv|field ", out var key, out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        key.Should().Be("rg31|bdc_f xv|field");
    }

    [Fact]
    public void TryNormalizeDashboardBriefingPackQuery_Defaults_To_Governor()
    {
        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeDashboardBriefingPackQuery(
            null,
            institutionId: 19,
            out var query,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        query.Lens.Should().Be("governor");
        query.InstitutionId.Should().BeNull();
    }

    [Fact]
    public void TryNormalizeDashboardBriefingPackQuery_Requires_Institution_For_Executive()
    {
        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeDashboardBriefingPackQuery(
            " executive ",
            institutionId: null,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("InstitutionId is required for the executive lens.");
    }

    [Fact]
    public void TryNormalizeDashboardBriefingPackQuery_Rejects_Invalid_Lens()
    {
        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeDashboardBriefingPackQuery(
            " board ",
            institutionId: null,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("Lens must be governor, deputy, director, or executive.");
    }

    [Fact]
    public void TryNormalizeResilienceAssessmentRequest_Maps_Valid_Request()
    {
        var request = new ResilienceAssessmentApiRequest
        {
            QuestionId = "  service-mapping-1 ",
            Domain = " Service Mapping ",
            Prompt = "  Are critical services fully mapped? ",
            Score = 4
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeResilienceAssessmentRequest(
            request,
            out var command,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        command.QuestionId.Should().Be("service-mapping-1");
        command.Domain.Should().Be("Service Mapping");
        command.Prompt.Should().Be("Are critical services fully mapped?");
        command.Score.Should().Be(4);
        command.AnsweredAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryNormalizeResilienceAssessmentRequest_Rejects_Out_Of_Range_Score()
    {
        var request = new ResilienceAssessmentApiRequest
        {
            QuestionId = "service-mapping-1",
            Domain = "Service Mapping",
            Prompt = "Are critical services fully mapped?",
            Score = 6
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeResilienceAssessmentRequest(
            request,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("Score must be between 1 and 5.");
    }

    [Fact]
    public void TryNormalizeModelApprovalStageRequest_Maps_Valid_Request()
    {
        var request = new ModelApprovalStageApiRequest
        {
            WorkflowKey = "  ifrs9-ecl-main ",
            ModelCode = " IFRS9_ECL ",
            ModelName = " IFRS 9 ECL Main ",
            Artifact = " parameter-set-v3 ",
            PreviousStage = " Validation Team ",
            Stage = " Board Review "
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeModelApprovalStageRequest(
            request,
            out var command,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        command.WorkflowKey.Should().Be("ifrs9-ecl-main");
        command.ModelCode.Should().Be("IFRS9_ECL");
        command.ModelName.Should().Be("IFRS 9 ECL Main");
        command.Artifact.Should().Be("parameter-set-v3");
        command.PreviousStage.Should().Be("Validation Team");
        command.Stage.Should().Be("Board Review");
        command.ChangedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryNormalizeModelApprovalStageRequest_Rejects_Invalid_Stage()
    {
        var request = new ModelApprovalStageApiRequest
        {
            WorkflowKey = "ifrs9-ecl-main",
            ModelCode = "IFRS9_ECL",
            ModelName = "IFRS 9 ECL Main",
            Artifact = "parameter-set-v3",
            Stage = "Final Signoff"
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeModelApprovalStageRequest(
            request,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("Stage is not valid for model approval workflow.");
    }

    [Fact]
    public void TryNormalizeCapitalScenarioRequest_Maps_Valid_Request()
    {
        var request = new CapitalPlanningScenarioApiRequest
        {
            CurrentCarPercent = 14.5m,
            CurrentRwaBn = 120m,
            QuarterlyRwaGrowthPercent = 3m,
            QuarterlyRetainedEarningsBn = 4m,
            CapitalActionBn = 12m,
            MinimumRequirementPercent = 10m,
            ConservationBufferPercent = 2.5m,
            CountercyclicalBufferPercent = 1m,
            DsibBufferPercent = 1m,
            RwaOptimisationPercent = 4m,
            TargetCarPercent = 18m,
            Cet1CostPercent = 13m,
            At1CostPercent = 15m,
            Tier2CostPercent = 11m,
            MaxAt1SharePercent = 20m,
            MaxTier2SharePercent = 25m,
            StepPercent = 5m
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeCapitalScenarioRequest(
            request,
            out var command,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        command.CurrentCarPercent.Should().Be(14.5m);
        command.TargetCarPercent.Should().Be(18m);
        command.CurrentRwaBn.Should().Be(120m);
        command.MaxAt1SharePercent.Should().Be(20m);
        command.MaxTier2SharePercent.Should().Be(25m);
        command.StepPercent.Should().Be(5m);
        command.SavedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryNormalizeCapitalScenarioRequest_Rejects_Invalid_Share_Caps()
    {
        var request = new CapitalPlanningScenarioApiRequest
        {
            CurrentCarPercent = 14m,
            CurrentRwaBn = 100m,
            TargetCarPercent = 16m,
            MaxAt1SharePercent = 60m,
            MaxTier2SharePercent = 50m,
            StepPercent = 5m
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeCapitalScenarioRequest(
            request,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("Combined AT1 and Tier 2 share caps cannot exceed 100.");
    }

    [Fact]
    public void TryNormalizeBatchScreeningRequest_Trims_Deduplicates_And_Defaults_Threshold()
    {
        var request = new SanctionsBatchScreeningApiRequest
        {
            Subjects = ["  Alpha Bank  ", "alpha bank", "", "  BDC One "]
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeBatchScreeningRequest(
            request,
            out var command,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        command.Subjects.Should().Equal("Alpha Bank", "BDC One");
        command.Threshold.Should().Be(0.86d);
    }

    [Fact]
    public void TryNormalizeBatchScreeningRequest_Rejects_Invalid_Threshold()
    {
        var request = new SanctionsBatchScreeningApiRequest
        {
            Subjects = ["Alpha Bank"],
            ThresholdPercent = 60d
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeBatchScreeningRequest(
            request,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("ThresholdPercent must be between 70 and 95.");
    }

    [Fact]
    public void TryNormalizeTransactionScreeningRequest_Rejects_Request_Without_Parties()
    {
        var request = new SanctionsTransactionScreeningApiRequest
        {
            TransactionReference = "TX-001",
            Amount = 125000m,
            Currency = "ngn",
            Channel = "Wire"
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeTransactionScreeningRequest(
            request,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("At least one transaction party is required.");
    }

    [Fact]
    public void TryNormalizeTransactionScreeningRequest_Maps_Trimmed_Request()
    {
        var request = new SanctionsTransactionScreeningApiRequest
        {
            TransactionReference = "  TX-900  ",
            Amount = 500000m,
            Currency = " usd ",
            Channel = "  SWIFT ",
            OriginatorName = "  Example Originator ",
            BeneficiaryName = " Example Beneficiary  ",
            HighRisk = true
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeTransactionScreeningRequest(
            request,
            out var command,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        command.TransactionReference.Should().Be("TX-900");
        command.Currency.Should().Be("USD");
        command.Channel.Should().Be("SWIFT");
        command.OriginatorName.Should().Be("Example Originator");
        command.BeneficiaryName.Should().Be("Example Beneficiary");
        command.CounterpartyName.Should().BeEmpty();
        command.HighRisk.Should().BeTrue();
    }

    [Fact]
    public void TryNormalizeSanctionsWorkflowDecisionRequest_Maps_Valid_Request()
    {
        var request = new SanctionsWorkflowDecisionApiRequest
        {
            MatchKey = "  alpha|ofac|92.0 ",
            Subject = "  Alpha Bank Plc ",
            MatchedName = " Alpha Bank Holdings ",
            SourceCode = " ofac ",
            RiskLevel = " high ",
            PreviousDecision = " Review ",
            Decision = " False Positive "
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeSanctionsWorkflowDecisionRequest(
            request,
            out var command,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        command.MatchKey.Should().Be("alpha|ofac|92.0");
        command.Subject.Should().Be("Alpha Bank Plc");
        command.MatchedName.Should().Be("Alpha Bank Holdings");
        command.SourceCode.Should().Be("OFAC");
        command.RiskLevel.Should().Be("high");
        command.PreviousDecision.Should().Be("Review");
        command.Decision.Should().Be("False Positive");
        command.ReviewedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TryNormalizeSanctionsWorkflowDecisionRequest_Rejects_Invalid_Decision()
    {
        var request = new SanctionsWorkflowDecisionApiRequest
        {
            MatchKey = "alpha|ofac|92.0",
            Subject = "Alpha Bank Plc",
            MatchedName = "Alpha Bank Holdings",
            SourceCode = "OFAC",
            RiskLevel = "high",
            Decision = "Freeze"
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeSanctionsWorkflowDecisionRequest(
            request,
            out _,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("Decision is not valid for sanctions workflow.");
    }

    [Fact]
    public void TryNormalizeRolloutReconciliationRequest_Maps_Distinct_Tenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var request = new RolloutReconciliationApiRequest
        {
            TenantIds = [tenantA, Guid.Empty, tenantA, tenantB]
        };

        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeRolloutReconciliationRequest(
            request,
            out var tenantIds,
            out var error);

        success.Should().BeTrue();
        error.Should().BeEmpty();
        tenantIds.Should().Equal(tenantA, tenantB);
    }

    [Fact]
    public void TryNormalizeRolloutReconciliationRequest_Rejects_Empty_Request()
    {
        var success = PlatformIntelligenceApiRequestMapper.TryNormalizeRolloutReconciliationRequest(
            new RolloutReconciliationApiRequest(),
            out var tenantIds,
            out var error);

        success.Should().BeFalse();
        error.Should().Be("At least one TenantId is required.");
        tenantIds.Should().BeEmpty();
    }
}
