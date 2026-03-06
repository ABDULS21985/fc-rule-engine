using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class SavedReportConfiguration : IEntityTypeConfiguration<SavedReport>
{
    public void Configure(EntityTypeBuilder<SavedReport> builder)
    {
        builder.ToTable("saved_reports");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.InstitutionId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Definition).IsRequired();
        builder.Property(x => x.IsShared).HasDefaultValue(false);
        builder.Property(x => x.CreatedByUserId).IsRequired();

        builder.Property(x => x.ScheduleCron).HasMaxLength(100);
        builder.Property(x => x.ScheduleFormat).HasMaxLength(10);
        builder.Property(x => x.ScheduleRecipients);
        builder.Property(x => x.IsScheduleActive).HasDefaultValue(false);

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.InstitutionId });
        builder.HasIndex(x => new { x.IsScheduleActive, x.LastRunAt });
    }
}
