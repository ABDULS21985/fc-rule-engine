using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class CapitalPlanningScenarioRecordConfiguration : IEntityTypeConfiguration<CapitalPlanningScenarioRecord>
{
    public void Configure(EntityTypeBuilder<CapitalPlanningScenarioRecord> builder)
    {
        builder.ToTable("capital_planning_scenarios", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ScenarioKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.CurrentCarPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CurrentRwaBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.QuarterlyRwaGrowthPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.QuarterlyRetainedEarningsBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CapitalActionBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MinimumRequirementPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.ConservationBufferPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CountercyclicalBufferPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.DsibBufferPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.RwaOptimisationPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.TargetCarPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Cet1CostPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.At1CostPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Tier2CostPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MaxAt1SharePercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MaxTier2SharePercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.StepPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.SavedAtUtc).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.ScenarioKey).IsUnique();
        builder.HasIndex(x => x.SavedAtUtc);
    }
}

public sealed class CapitalPlanningScenarioHistoryRecordConfiguration : IEntityTypeConfiguration<CapitalPlanningScenarioHistoryRecord>
{
    public void Configure(EntityTypeBuilder<CapitalPlanningScenarioHistoryRecord> builder)
    {
        builder.ToTable("capital_planning_scenario_history", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CurrentCarPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CurrentRwaBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.QuarterlyRwaGrowthPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.QuarterlyRetainedEarningsBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CapitalActionBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MinimumRequirementPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.ConservationBufferPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.CountercyclicalBufferPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.DsibBufferPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.RwaOptimisationPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.TargetCarPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Cet1CostPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.At1CostPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.Tier2CostPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MaxAt1SharePercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MaxTier2SharePercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.StepPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.SavedAtUtc).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.SavedAtUtc);
    }
}
