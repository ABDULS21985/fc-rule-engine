using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class RegulatorReceiptConfiguration : IEntityTypeConfiguration<RegulatorReceipt>
{
    public void Configure(EntityTypeBuilder<RegulatorReceipt> builder)
    {
        builder.ToTable("regulator_receipts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.RegulatorTenantId).IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(30)
            .HasConversion<string>()
            .HasDefaultValue(RegulatorReceiptStatus.Received)
            .IsRequired();
        builder.Property(x => x.ReceivedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.Notes).HasColumnType("nvarchar(max)");

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.RegulatorTenantId);
        builder.HasIndex(x => new { x.RegulatorTenantId, x.SubmissionId }).IsUnique();
        builder.HasIndex(x => new { x.RegulatorTenantId, x.Status, x.ReceivedAt });
    }
}

public class ExaminerQueryConfiguration : IEntityTypeConfiguration<ExaminerQuery>
{
    public void Configure(EntityTypeBuilder<ExaminerQuery> builder)
    {
        builder.ToTable("examiner_queries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.RegulatorTenantId).IsRequired();
        builder.Property(x => x.FieldCode).HasMaxLength(50);
        builder.Property(x => x.QueryText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RaisedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.ResponseText).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ExaminerQueryStatus.Open)
            .IsRequired();
        builder.Property(x => x.Priority)
            .HasMaxLength(10)
            .HasConversion<string>()
            .HasDefaultValue(ExaminerQueryPriority.Normal)
            .HasSentinel((ExaminerQueryPriority)(-1))
            .IsRequired();

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.RegulatorTenantId);
        builder.HasIndex(x => new { x.RegulatorTenantId, x.SubmissionId, x.Status });
        builder.HasIndex(x => new { x.RegulatorTenantId, x.RaisedAt });
    }
}

public class ExaminationProjectConfiguration : IEntityTypeConfiguration<ExaminationProject>
{
    public void Configure(EntityTypeBuilder<ExaminationProject> builder)
    {
        builder.ToTable("examination_projects");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.EntityIdsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ModuleCodesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.TeamAssignmentsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.TimelineJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ExaminationProjectStatus.Draft)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.ReportFilePath).HasMaxLength(500);
        builder.Property(x => x.IntelligencePackFilePath).HasMaxLength(500);

        builder.HasMany(x => x.Annotations)
            .WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Findings)
            .WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.EvidenceRequests)
            .WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.EvidenceFiles)
            .WithOne(x => x.Project)
            .HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
    }
}

public class ExaminationAnnotationConfiguration : IEntityTypeConfiguration<ExaminationAnnotation>
{
    public void Configure(EntityTypeBuilder<ExaminationAnnotation> builder)
    {
        builder.ToTable("examination_annotations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.FieldCode).HasMaxLength(50);
        builder.Property(x => x.Note).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Submission)
            .WithMany()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ProjectId, x.SubmissionId });
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
    }
}
