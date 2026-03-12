using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class MarketplaceRolloutModuleRecordConfiguration : IEntityTypeConfiguration<MarketplaceRolloutModuleRecord>
{
    public void Configure(EntityTypeBuilder<MarketplaceRolloutModuleRecord> builder)
    {
        builder.ToTable("marketplace_rollout_modules", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ModuleCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ModuleName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.EligibleTenants).IsRequired();
        builder.Property(x => x.ActiveEntitlements).IsRequired();
        builder.Property(x => x.PendingEntitlements).IsRequired();
        builder.Property(x => x.StaleTenants).IsRequired();
        builder.Property(x => x.IncludedBasePlans).IsRequired();
        builder.Property(x => x.AddOnPlans).IsRequired();
        builder.Property(x => x.AdoptionRatePercent).HasColumnType("decimal(9,2)").IsRequired();
        builder.Property(x => x.Signal).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Commentary).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.RecommendedAction).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.ModuleCode).IsUnique();
        builder.HasIndex(x => x.Signal);
        builder.HasIndex(x => x.MaterializedAt);
    }
}

public sealed class MarketplaceRolloutPlanCoverageRecordConfiguration : IEntityTypeConfiguration<MarketplaceRolloutPlanCoverageRecord>
{
    public void Configure(EntityTypeBuilder<MarketplaceRolloutPlanCoverageRecord> builder)
    {
        builder.ToTable("marketplace_rollout_plan_coverage", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ModuleCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ModuleName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.PlanCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PlanName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.CoverageMode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.EligibleTenants).IsRequired();
        builder.Property(x => x.ActiveEntitlements).IsRequired();
        builder.Property(x => x.PendingEntitlements).IsRequired();
        builder.Property(x => x.PriceMonthly).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.PriceAnnual).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Signal).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Commentary).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => new { x.ModuleCode, x.PlanCode }).IsUnique();
        builder.HasIndex(x => x.Signal);
        builder.HasIndex(x => x.MaterializedAt);
    }
}

public sealed class MarketplaceRolloutQueueRecordConfiguration : IEntityTypeConfiguration<MarketplaceRolloutQueueRecord>
{
    public void Configure(EntityTypeBuilder<MarketplaceRolloutQueueRecord> builder)
    {
        builder.ToTable("marketplace_rollout_reconciliation_queue", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TenantName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.PlanCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PlanName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.PendingModuleCount).IsRequired();
        builder.Property(x => x.PendingModules).HasMaxLength(600).IsRequired();
        builder.Property(x => x.State).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Signal).HasMaxLength(30).IsRequired();
        builder.Property(x => x.LastEntitlementAction).HasMaxLength(120);
        builder.Property(x => x.RecommendedAction).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasIndex(x => x.State);
        builder.HasIndex(x => x.Signal);
        builder.HasIndex(x => x.MaterializedAt);
    }
}
