using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class CapitalActionTemplateRecordConfiguration : IEntityTypeConfiguration<CapitalActionTemplateRecord>
{
    public void Configure(EntityTypeBuilder<CapitalActionTemplateRecord> builder)
    {
        builder.ToTable("capital_action_templates", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(240).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.PrimaryLever).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CapitalActionBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.RwaOptimisationPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.QuarterlyRetainedEarningsDeltaBn).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.EstimatedAnnualCostPercent).HasColumnType("decimal(18,4)").IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.PrimaryLever);
        builder.HasIndex(x => x.MaterializedAt);
    }
}
