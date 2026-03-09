using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class ExaminationFindingConfiguration : IEntityTypeConfiguration<ExaminationFinding>
{
    public void Configure(EntityTypeBuilder<ExaminationFinding> builder)
    {
        builder.ToTable("examination_findings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.RiskArea).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Observation).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Recommendation).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(40)
            .HasConversion<string>()
            .HasDefaultValue(ExaminationWorkflowStatus.ToReview)
            .IsRequired();
        builder.Property(x => x.RemediationStatus)
            .HasMaxLength(40)
            .HasConversion<string>()
            .HasDefaultValue(ExaminationRemediationStatus.Open)
            .IsRequired();
        builder.Property(x => x.RiskRating)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ExaminationRiskRating.Medium)
            .IsRequired();
        builder.Property(x => x.ModuleCode).HasMaxLength(60);
        builder.Property(x => x.PeriodLabel).HasMaxLength(60);
        builder.Property(x => x.FieldCode).HasMaxLength(120);
        builder.Property(x => x.FieldValue).HasMaxLength(500);
        builder.Property(x => x.ValidationRuleId).HasMaxLength(120);
        builder.Property(x => x.ValidationMessage).HasMaxLength(1000);
        builder.Property(x => x.EvidenceReference).HasMaxLength(500);
        builder.Property(x => x.ManagementResponse).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ManagementActionPlan).HasColumnType("nvarchar(max)");
        builder.Property(x => x.EscalationReason).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Project)
            .WithMany(x => x.Findings)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Institution)
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CarriedForwardFromFinding)
            .WithMany(x => x.CarriedForwardChildren)
            .HasForeignKey(x => x.CarriedForwardFromFindingId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.RemediationStatus });
        builder.HasIndex(x => new { x.TenantId, x.InstitutionId, x.ManagementResponseDeadline });
    }
}

public class ExaminationEvidenceRequestConfiguration : IEntityTypeConfiguration<ExaminationEvidenceRequest>
{
    public void Configure(EntityTypeBuilder<ExaminationEvidenceRequest> builder)
    {
        builder.ToTable("examination_evidence_requests");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.RequestText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RequestedItemsJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ExaminationEvidenceRequestStatus.Open)
            .IsRequired();
        builder.Property(x => x.RequestedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Project)
            .WithMany(x => x.EvidenceRequests)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Finding)
            .WithMany(x => x.EvidenceRequests)
            .HasForeignKey(x => x.FindingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Institution)
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.FindingId, x.Status });
    }
}

public class ExaminationEvidenceFileConfiguration : IEntityTypeConfiguration<ExaminationEvidenceFile>
{
    public void Configure(EntityTypeBuilder<ExaminationEvidenceFile> builder)
    {
        builder.ToTable("examination_evidence_files");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.FileHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Kind)
            .HasMaxLength(40)
            .HasConversion<string>()
            .HasDefaultValue(ExaminationEvidenceKind.SupportingDocument)
            .IsRequired();
        builder.Property(x => x.UploadedByRole)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ExaminationEvidenceUploaderRole.Examiner)
            .IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.UploadedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Project)
            .WithMany(x => x.EvidenceFiles)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Finding)
            .WithMany(x => x.EvidenceFiles)
            .HasForeignKey(x => x.FindingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.EvidenceRequest)
            .WithMany(x => x.EvidenceFiles)
            .HasForeignKey(x => x.EvidenceRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Institution)
            .WithMany()
            .HasForeignKey(x => x.InstitutionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.UploadedAt });
        builder.HasIndex(x => new { x.TenantId, x.FindingId });
        builder.HasIndex(x => x.FileHash);
    }
}
