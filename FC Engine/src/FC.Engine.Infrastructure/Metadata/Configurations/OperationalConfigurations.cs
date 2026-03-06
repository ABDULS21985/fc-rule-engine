using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("return_submissions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.ReturnCode).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Status).HasMaxLength(30).IsRequired()
            .HasConversion<string>();

        builder.HasOne(s => s.Institution).WithMany().HasForeignKey(s => s.InstitutionId);
        builder.HasOne(s => s.ReturnPeriod).WithMany().HasForeignKey(s => s.ReturnPeriodId);
        builder.HasOne(s => s.ValidationReport).WithOne().HasForeignKey<ValidationReport>(r => r.SubmissionId);

        // ── FI Portal Extensions ──
        builder.Property(s => s.SubmittedByUserId);
        builder.Property(s => s.ApprovalRequired).HasDefaultValue(false);
        builder.Property(s => s.IsRetentionAnonymised).HasDefaultValue(false);
        // Navigation to Approval is configured in SubmissionApprovalConfiguration

        builder.HasIndex(s => s.TenantId);
    }
}

public class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> builder)
    {
        builder.ToTable("institutions");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.JurisdictionId).HasDefaultValue(1).IsRequired();
        builder.Property(i => i.InstitutionCode).HasMaxLength(20).IsRequired();
        builder.Property(i => i.InstitutionName).HasMaxLength(255).IsRequired();
        builder.Property(i => i.LicenseType).HasMaxLength(100);

        // ── Hierarchy ──
        builder.Property(i => i.EntityType).HasMaxLength(30)
            .HasConversion<string>()
            .HasDefaultValue(Domain.Enums.EntityType.HeadOffice);
        builder.Property(i => i.BranchCode).HasMaxLength(20);
        builder.Property(i => i.Location).HasMaxLength(200);

        builder.HasOne(i => i.ParentInstitution)
            .WithMany(i => i.ChildInstitutions)
            .HasForeignKey(i => i.ParentInstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Jurisdiction)
            .WithMany(j => j.Institutions)
            .HasForeignKey(i => i.JurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── FI Portal Extensions ──
        builder.Property(i => i.ContactEmail).HasMaxLength(256);
        builder.Property(i => i.ContactPhone).HasMaxLength(50);
        builder.Property(i => i.Address).HasMaxLength(500);
        builder.Property(i => i.MaxUsersAllowed).HasDefaultValue(10);
        builder.Property(i => i.SubscriptionTier).HasMaxLength(50).HasDefaultValue("Basic");
        builder.Property(i => i.MakerCheckerEnabled).HasDefaultValue(false);
        builder.Property(i => i.SettingsJson).HasColumnType("nvarchar(max)");
        // Navigation to InstitutionUsers is configured in InstitutionUserConfiguration
        // Navigation to Tenant is configured in TenantConfiguration

        builder.HasIndex(i => i.TenantId);
        builder.HasIndex(i => i.JurisdictionId);
    }
}

public class ReturnPeriodConfiguration : IEntityTypeConfiguration<ReturnPeriod>
{
    public void Configure(EntityTypeBuilder<ReturnPeriod> builder)
    {
        builder.ToTable("return_periods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Frequency).HasMaxLength(20).IsRequired();

        // ── Filing Calendar (RG-12) ──
        builder.Property(p => p.ModuleId);
        builder.Property(p => p.Quarter);
        builder.Property(p => p.DeadlineDate);
        builder.Property(p => p.DeadlineOverrideDate);
        builder.Property(p => p.DeadlineOverrideBy);
        builder.Property(p => p.DeadlineOverrideReason).HasMaxLength(500);
        builder.Property(p => p.AutoCreatedReturnId);
        builder.Property(p => p.Status).HasMaxLength(20).HasDefaultValue("Upcoming");
        builder.Property(p => p.NotificationLevel).HasDefaultValue(0);
        builder.Ignore(p => p.EffectiveDeadline);

        builder.HasOne(p => p.Module).WithMany().HasForeignKey(p => p.ModuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.AutoCreatedReturn).WithMany().HasForeignKey(p => p.AutoCreatedReturnId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.TenantId);
        builder.HasIndex(p => new { p.TenantId, p.ModuleId, p.Year, p.Month, p.Quarter });
    }
}

public class ValidationReportConfiguration : IEntityTypeConfiguration<ValidationReport>
{
    public void Configure(EntityTypeBuilder<ValidationReport> builder)
    {
        builder.ToTable("validation_reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).IsRequired();
        builder.Ignore(r => r.IsValid);
        builder.Ignore(r => r.HasWarnings);
        builder.Ignore(r => r.HasErrors);
        builder.Ignore(r => r.ErrorCount);
        builder.Ignore(r => r.WarningCount);

        builder.HasMany(r => r.Errors).WithOne().HasForeignKey(e => e.ValidationReportId);

        builder.HasIndex(r => r.TenantId);
    }
}

public class ValidationErrorConfiguration : IEntityTypeConfiguration<ValidationError>
{
    public void Configure(EntityTypeBuilder<ValidationError> builder)
    {
        builder.ToTable("validation_errors");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.RuleId).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Field).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Message).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.Severity).HasMaxLength(10).IsRequired()
            .HasConversion<string>();
        builder.Property(e => e.Category).HasMaxLength(20).IsRequired()
            .HasConversion<string>();
        builder.Property(e => e.ExpectedValue).HasMaxLength(100);
        builder.Property(e => e.ActualValue).HasMaxLength(100);
        builder.Property(e => e.ReferencedReturnCode).HasMaxLength(20);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log", "meta");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(20).IsRequired();
        builder.Property(a => a.PerformedBy).HasMaxLength(100).IsRequired();
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.Property(a => a.Hash).HasMaxLength(64).IsRequired();
        builder.Property(a => a.PreviousHash).HasMaxLength(64).IsRequired();
        builder.Property(a => a.SequenceNumber).IsRequired();

        builder.HasIndex(a => a.TenantId);
        builder.HasIndex(a => new { a.TenantId, a.SequenceNumber })
            .IsUnique()
            .HasFilter("[SequenceNumber] > 0");
    }
}

public class FieldChangeHistoryConfiguration : IEntityTypeConfiguration<FieldChangeHistory>
{
    public void Configure(EntityTypeBuilder<FieldChangeHistory> builder)
    {
        builder.ToTable("field_change_history", "meta");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.TenantId).IsRequired();
        builder.Property(f => f.ReturnCode).HasMaxLength(20).IsRequired();
        builder.Property(f => f.FieldName).HasMaxLength(100).IsRequired();
        builder.Property(f => f.ChangeSource).HasMaxLength(20).IsRequired();
        builder.Property(f => f.SourceDetail).HasMaxLength(200);
        builder.Property(f => f.ChangedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(f => new { f.TenantId, f.SubmissionId, f.FieldName });
    }
}

public class EvidencePackageConfiguration : IEntityTypeConfiguration<EvidencePackage>
{
    public void Configure(EntityTypeBuilder<EvidencePackage> builder)
    {
        builder.ToTable("evidence_packages", "meta");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.PackageHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
        builder.Property(e => e.GeneratedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.SubmissionId });
    }
}

public class DdlMigrationConfiguration : IEntityTypeConfiguration<DdlMigrationRecord>
{
    public void Configure(EntityTypeBuilder<DdlMigrationRecord> builder)
    {
        builder.ToTable("ddl_migrations", "meta");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MigrationType).HasMaxLength(20).IsRequired();
        builder.Property(m => m.DdlScript).IsRequired();
        builder.Property(m => m.RollbackScript).IsRequired();
        builder.Property(m => m.ExecutedBy).HasMaxLength(100).IsRequired();
        builder.Property(m => m.RolledBackBy).HasMaxLength(100);
    }
}
