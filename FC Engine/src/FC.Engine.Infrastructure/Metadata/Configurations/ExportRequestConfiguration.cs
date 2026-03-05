using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class ExportRequestConfiguration : IEntityTypeConfiguration<ExportRequest>
{
    public void Configure(EntityTypeBuilder<ExportRequest> builder)
    {
        builder.ToTable("export_requests");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.Format)
            .HasMaxLength(10)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(Domain.Enums.ExportRequestStatus.Queued)
            .IsRequired();
        builder.Property(x => x.RequestedBy).IsRequired();
        builder.Property(x => x.RequestedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.FilePath).HasMaxLength(500);
        builder.Property(x => x.Sha256Hash).HasMaxLength(64);
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId, x.RequestedAt });
        builder.HasIndex(x => new { x.Status, x.RequestedAt });
        builder.HasIndex(x => x.ExpiresAt);
    }
}
