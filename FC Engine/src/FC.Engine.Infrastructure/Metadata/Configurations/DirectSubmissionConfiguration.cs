using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class DirectSubmissionConfiguration : IEntityTypeConfiguration<DirectSubmission>
{
    public void Configure(EntityTypeBuilder<DirectSubmission> builder)
    {
        builder.ToTable("direct_submissions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.RegulatorCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Channel)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(SubmissionChannel.DirectApi)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .HasConversion<string>()
            .HasDefaultValue(DirectSubmissionStatus.Pending)
            .IsRequired();

        // Digital signature
        builder.Property(x => x.SignatureAlgorithm).HasMaxLength(50);
        builder.Property(x => x.SignatureHash).HasMaxLength(128);
        builder.Property(x => x.CertificateThumbprint).HasMaxLength(64);

        // Submission tracking
        builder.Property(x => x.RegulatorReference).HasMaxLength(200);
        builder.Property(x => x.RegulatorResponseBody).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");

        // Package info
        builder.Property(x => x.PackageStoragePath).HasMaxLength(500);
        builder.Property(x => x.PackageSha256).HasMaxLength(64);

        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId });
        builder.HasIndex(x => new { x.Status, x.NextRetryAt });
        builder.HasIndex(x => x.RegulatorReference);
    }
}
