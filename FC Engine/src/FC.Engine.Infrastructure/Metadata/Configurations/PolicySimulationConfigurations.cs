using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class PolicyScenarioConfiguration : IEntityTypeConfiguration<PolicyScenario>
{
    public void Configure(EntityTypeBuilder<PolicyScenario> builder)
    {
        builder.ToTable("policy_scenarios");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");
        builder.Property(x => x.PolicyDomain)
            .HasMaxLength(40).HasConversion<string>().IsRequired();
        builder.Property(x => x.TargetEntityTypes).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BaselineDate).IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(30).HasConversion<string>()
            .HasDefaultValue(PolicyStatus.Draft).IsRequired();
        builder.Property(x => x.Version).HasDefaultValue(1);
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => new { x.RegulatorId, x.Status });
        builder.HasIndex(x => new { x.PolicyDomain, x.Status });
    }
}

public class PolicyParameterConfiguration : IEntityTypeConfiguration<PolicyParameter>
{
    public void Configure(EntityTypeBuilder<PolicyParameter> builder)
    {
        builder.ToTable("policy_parameters");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ParameterCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ParameterName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CurrentValue).HasColumnType("decimal(18,6)");
        builder.Property(x => x.ProposedValue).HasColumnType("decimal(18,6)");
        builder.Property(x => x.Unit).HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.ApplicableEntityTypes).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ReturnLineReference).HasMaxLength(60);
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Scenario).WithMany(x => x.Parameters)
            .HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ScenarioId);
        builder.HasIndex(x => new { x.ScenarioId, x.ParameterCode }).IsUnique();
    }
}

public class PolicyParameterPresetConfiguration : IEntityTypeConfiguration<PolicyParameterPreset>
{
    public void Configure(EntityTypeBuilder<PolicyParameterPreset> builder)
    {
        builder.ToTable("policy_parameter_presets");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ParameterCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ParameterName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PolicyDomain).HasMaxLength(40).HasConversion<string>().IsRequired();
        builder.Property(x => x.CurrentBaseline).HasColumnType("decimal(18,6)");
        builder.Property(x => x.Unit).HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.ReturnLineReference).HasMaxLength(60);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.RegulatorCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new { x.ParameterCode, x.RegulatorCode }).IsUnique();
    }
}

public class ImpactAssessmentRunConfiguration : IEntityTypeConfiguration<ImpactAssessmentRun>
{
    public void Configure(EntityTypeBuilder<ImpactAssessmentRun> builder)
    {
        builder.ToTable("impact_assessment_runs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasMaxLength(20).HasConversion<string>()
            .HasDefaultValue(ImpactRunStatus.Pending).IsRequired();
        builder.Property(x => x.SnapshotDate).IsRequired();
        builder.Property(x => x.AggregateCapitalShortfall).HasColumnType("decimal(18,2)");
        builder.Property(x => x.AggregateComplianceCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.StartedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.CompletedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Scenario).WithMany(x => x.ImpactRuns)
            .HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.ScenarioId, x.Status });
        builder.HasIndex(x => new { x.ScenarioId, x.RunNumber }).IsUnique();
    }
}

public class EntityImpactResultConfiguration : IEntityTypeConfiguration<EntityImpactResult>
{
    public void Configure(EntityTypeBuilder<EntityImpactResult> builder)
    {
        builder.ToTable("entity_impact_results");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstitutionCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.InstitutionName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ImpactCategory)
            .HasMaxLength(30).HasConversion<string>().IsRequired();
        builder.Property(x => x.ParameterResults).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CurrentMetricValue).HasColumnType("decimal(18,6)");
        builder.Property(x => x.ProposedThreshold).HasColumnType("decimal(18,6)");
        builder.Property(x => x.GapToCompliance).HasColumnType("decimal(18,6)");
        builder.Property(x => x.EstimatedComplianceCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.RiskScore).HasColumnType("decimal(5,2)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Run).WithMany(x => x.EntityResults)
            .HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.RunId, x.ImpactCategory });
        builder.HasIndex(x => new { x.InstitutionId, x.RunId });
    }
}

public class CostBenefitAnalysisConfiguration : IEntityTypeConfiguration<CostBenefitAnalysis>
{
    public void Configure(EntityTypeBuilder<CostBenefitAnalysis> builder)
    {
        builder.ToTable("cost_benefit_analyses");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TotalIndustryComplianceCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CostToSmallEntities).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CostToMediumEntities).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CostToLargeEntities).HasColumnType("decimal(18,2)");
        builder.Property(x => x.SectorCARImprovement).HasColumnType("decimal(8,4)");
        builder.Property(x => x.SectorLCRImprovement).HasColumnType("decimal(8,4)");
        builder.Property(x => x.EstimatedRiskReduction).HasColumnType("decimal(8,4)");
        builder.Property(x => x.EstimatedDepositProtection).HasColumnType("decimal(18,2)");
        builder.Property(x => x.ImmediateImpactSummary).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.PhaseIn12MonthSummary).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.PhaseIn24MonthSummary).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.NetBenefitScore).HasColumnType("decimal(8,4)");
        builder.Property(x => x.Recommendation).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Scenario).WithMany()
            .HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Run).WithOne(x => x.CostBenefitAnalysis)
            .HasForeignKey<CostBenefitAnalysis>(x => x.RunId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.RunId).IsUnique();
    }
}

public class ConsultationRoundConfiguration : IEntityTypeConfiguration<ConsultationRound>
{
    public void Configure(EntityTypeBuilder<ConsultationRound> builder)
    {
        builder.ToTable("consultation_rounds");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.CoverNote).HasColumnType("nvarchar(max)");
        builder.Property(x => x.PublishedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.DeadlineDate).IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(20).HasConversion<string>()
            .HasDefaultValue(ConsultationStatus.Draft).IsRequired();
        builder.Property(x => x.TargetEntityTypes).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AggregationCompletedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Scenario).WithMany(x => x.Consultations)
            .HasForeignKey(x => x.ScenarioId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.ScenarioId);
        builder.HasIndex(x => new { x.DeadlineDate, x.Status });
    }
}

public class ConsultationProvisionConfiguration : IEntityTypeConfiguration<ConsultationProvision>
{
    public void Configure(EntityTypeBuilder<ConsultationProvision> builder)
    {
        builder.ToTable("consultation_provisions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProvisionTitle).HasMaxLength(300).IsRequired();
        builder.Property(x => x.ProvisionText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RelatedParameterCode).HasMaxLength(40);

        builder.HasOne(x => x.Consultation).WithMany(x => x.Provisions)
            .HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ConsultationId, x.ProvisionNumber }).IsUnique();
    }
}

public class ConsultationFeedbackConfiguration : IEntityTypeConfiguration<ConsultationFeedback>
{
    public void Configure(EntityTypeBuilder<ConsultationFeedback> builder)
    {
        builder.ToTable("consultation_feedback");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstitutionCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OverallPosition)
            .HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.GeneralComments).HasColumnType("nvarchar(max)");
        builder.Property(x => x.SubmittedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Consultation).WithMany(x => x.Feedback)
            .HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.ConsultationId);
        builder.HasIndex(x => new { x.ConsultationId, x.InstitutionId }).IsUnique();
    }
}

public class ProvisionFeedbackEntryConfiguration : IEntityTypeConfiguration<ProvisionFeedbackEntry>
{
    public void Configure(EntityTypeBuilder<ProvisionFeedbackEntry> builder)
    {
        builder.ToTable("provision_feedback");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Position)
            .HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.Reasoning).HasColumnType("nvarchar(max)");
        builder.Property(x => x.SuggestedAmendment).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ImpactAssessment).HasColumnType("nvarchar(max)");

        builder.HasOne(x => x.Feedback).WithMany(x => x.ProvisionFeedback)
            .HasForeignKey(x => x.FeedbackId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Provision).WithMany(x => x.ProvisionFeedback)
            .HasForeignKey(x => x.ProvisionId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.FeedbackId, x.ProvisionId }).IsUnique();
    }
}

public class FeedbackAggregationConfiguration : IEntityTypeConfiguration<FeedbackAggregation>
{
    public void Configure(EntityTypeBuilder<FeedbackAggregation> builder)
    {
        builder.ToTable("feedback_aggregations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SupportPercentage).HasColumnType("decimal(5,2)");
        builder.Property(x => x.OpposePercentage).HasColumnType("decimal(5,2)");
        builder.Property(x => x.ByEntityType).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.TopConcerns).HasColumnType("nvarchar(max)");
        builder.Property(x => x.TopSuggestedAmendments).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ComputedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Consultation).WithMany()
            .HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Provision).WithOne(x => x.Aggregation)
            .HasForeignKey<FeedbackAggregation>(x => x.ProvisionId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.ConsultationId, x.ProvisionId }).IsUnique();
    }
}

public class PolicyDecisionConfiguration : IEntityTypeConfiguration<PolicyDecision>
{
    public void Configure(EntityTypeBuilder<PolicyDecision> builder)
    {
        builder.ToTable("policy_decisions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DecisionType)
            .HasMaxLength(20).HasConversion<string>().IsRequired();
        builder.Property(x => x.DecisionSummary).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.FinalParametersJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CircularReference).HasMaxLength(80);
        builder.Property(x => x.DocumentBlobPath).HasMaxLength(500);
        builder.Property(x => x.DecidedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Scenario).WithOne(x => x.Decision)
            .HasForeignKey<PolicyDecision>(x => x.ScenarioId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.ScenarioId).IsUnique();
    }
}

public class HistoricalImpactTrackingConfiguration : IEntityTypeConfiguration<HistoricalImpactTracking>
{
    public void Configure(EntityTypeBuilder<HistoricalImpactTracking> builder)
    {
        builder.ToTable("historical_impact_tracking");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PredictedCapitalShortfall).HasColumnType("decimal(18,2)");
        builder.Property(x => x.PredictedComplianceCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.ActualCapitalShortfall).HasColumnType("decimal(18,2)");
        builder.Property(x => x.ActualComplianceCost).HasColumnType("decimal(18,2)");
        builder.Property(x => x.BreachCountVariance).HasColumnType("decimal(8,4)");
        builder.Property(x => x.ShortfallVariance).HasColumnType("decimal(8,4)");
        builder.Property(x => x.AccuracyScore).HasColumnType("decimal(5,2)");
        builder.Property(x => x.Notes).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Decision).WithMany(x => x.TrackingEntries)
            .HasForeignKey(x => x.DecisionId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.DecisionId, x.TrackingDate });
        builder.HasIndex(x => new { x.DecisionId, x.TrackingDate }).IsUnique();
    }
}

public class PolicyAuditLogConfiguration : IEntityTypeConfiguration<PolicyAuditLog>
{
    public void Configure(EntityTypeBuilder<PolicyAuditLog> builder)
    {
        builder.ToTable("policy_audit_log");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Detail).HasColumnType("nvarchar(max)");
        builder.Property(x => x.PerformedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.ScenarioId);
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => x.PerformedAt).IsDescending();
    }
}
