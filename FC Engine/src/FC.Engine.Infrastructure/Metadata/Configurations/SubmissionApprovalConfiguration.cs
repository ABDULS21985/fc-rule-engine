using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class SubmissionApprovalConfiguration : IEntityTypeConfiguration<SubmissionApproval>
{
    public void Configure(EntityTypeBuilder<SubmissionApproval> builder)
    {
        builder.ToTable("submission_approvals", "meta");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();

        builder.Property(e => e.Status).HasMaxLength(20).IsRequired()
            .HasConversion<string>();
        builder.Property(e => e.SubmitterNotes).HasMaxLength(1000);
        builder.Property(e => e.ReviewerComments).HasMaxLength(2000);

        // Relationships
        builder.HasOne(e => e.Submission)
            .WithOne(s => s.Approval)
            .HasForeignKey<SubmissionApproval>(e => e.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.RequestedBy)
            .WithMany()
            .HasForeignKey(e => e.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ReviewedBy)
            .WithMany()
            .HasForeignKey(e => e.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(e => e.SubmissionId).IsUnique();
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.TenantId);
    }
}
