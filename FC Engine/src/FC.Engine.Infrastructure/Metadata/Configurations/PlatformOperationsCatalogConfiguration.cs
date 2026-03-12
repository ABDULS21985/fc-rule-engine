using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class PlatformInterventionRecordConfiguration : IEntityTypeConfiguration<PlatformInterventionRecord>
{
    public void Configure(EntityTypeBuilder<PlatformInterventionRecord> builder)
    {
        builder.ToTable("platform_interventions", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Domain).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(240).IsRequired();
        builder.Property(x => x.Signal).HasMaxLength(600).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(30).IsRequired();
        builder.Property(x => x.NextAction).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.DueDate).IsRequired();
        builder.Property(x => x.OwnerLane).HasMaxLength(120).IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => x.DueDate);
        builder.HasIndex(x => x.MaterializedAt);
    }
}

public sealed class PlatformActivityTimelineRecordConfiguration : IEntityTypeConfiguration<PlatformActivityTimelineRecord>
{
    public void Configure(EntityTypeBuilder<PlatformActivityTimelineRecord> builder)
    {
        builder.ToTable("platform_activity_timeline", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Domain).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(240).IsRequired();
        builder.Property(x => x.Detail).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.HappenedAt).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(30).IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.InstitutionId);
        builder.HasIndex(x => x.HappenedAt);
        builder.HasIndex(x => x.Severity);
        builder.HasIndex(x => x.MaterializedAt);
    }
}
