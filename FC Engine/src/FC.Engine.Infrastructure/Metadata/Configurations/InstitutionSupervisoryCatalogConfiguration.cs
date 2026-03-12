using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class InstitutionSupervisoryScorecardRecordConfiguration : IEntityTypeConfiguration<InstitutionSupervisoryScorecardRecord>
{
    public void Configure(EntityTypeBuilder<InstitutionSupervisoryScorecardRecord> builder)
    {
        builder.ToTable("institution_supervisory_scorecards", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstitutionId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.InstitutionName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.LicenceType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.OverdueObligations).IsRequired();
        builder.Property(x => x.DueSoonObligations).IsRequired();
        builder.Property(x => x.CapitalScore).HasColumnType("decimal(9,2)");
        builder.Property(x => x.OpenResilienceIncidents).IsRequired();
        builder.Property(x => x.OpenSecurityAlerts).IsRequired();
        builder.Property(x => x.ModelReviewItems).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.InstitutionId).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => x.MaterializedAt);
    }
}

public sealed class InstitutionSupervisoryDetailRecordConfiguration : IEntityTypeConfiguration<InstitutionSupervisoryDetailRecord>
{
    public void Configure(EntityTypeBuilder<InstitutionSupervisoryDetailRecord> builder)
    {
        builder.ToTable("institution_supervisory_details", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstitutionId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.InstitutionName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.InstitutionCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.LicenceType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.CapitalScore).HasColumnType("decimal(9,2)");
        builder.Property(x => x.CapitalAlert).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.OverdueObligations).IsRequired();
        builder.Property(x => x.DueSoonObligations).IsRequired();
        builder.Property(x => x.OpenResilienceIncidents).IsRequired();
        builder.Property(x => x.OpenSecurityAlerts).IsRequired();
        builder.Property(x => x.ModelReviewItems).IsRequired();
        builder.Property(x => x.TopObligationsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RecentSubmissionsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RecentActivityJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.InstitutionId).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => x.MaterializedAt);
    }
}
