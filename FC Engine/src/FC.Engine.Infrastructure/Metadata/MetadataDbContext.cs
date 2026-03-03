using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata;

public class MetadataDbContext : DbContext
{
    public MetadataDbContext(DbContextOptions<MetadataDbContext> options) : base(options) { }

    // Metadata tables
    public DbSet<ReturnTemplate> ReturnTemplates => Set<ReturnTemplate>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<TemplateField> TemplateFields => Set<TemplateField>();
    public DbSet<TemplateItemCode> TemplateItemCodes => Set<TemplateItemCode>();
    public DbSet<TemplateSection> TemplateSections => Set<TemplateSection>();
    public DbSet<IntraSheetFormula> IntraSheetFormulas => Set<IntraSheetFormula>();
    public DbSet<CrossSheetRule> CrossSheetRules => Set<CrossSheetRule>();
    public DbSet<CrossSheetRuleOperand> CrossSheetRuleOperands => Set<CrossSheetRuleOperand>();
    public DbSet<CrossSheetRuleExpression> CrossSheetRuleExpressions => Set<CrossSheetRuleExpression>();
    public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();

    // Operational tables
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Institution> Institutions => Set<Institution>();
    public DbSet<ReturnPeriod> ReturnPeriods => Set<ReturnPeriod>();
    public DbSet<ValidationReport> ValidationReports => Set<ValidationReport>();
    public DbSet<ValidationError> ValidationErrors => Set<ValidationError>();

    // Portal users
    public DbSet<PortalUser> PortalUsers => Set<PortalUser>();

    // FI Portal
    public DbSet<InstitutionUser> InstitutionUsers => Set<InstitutionUser>();
    public DbSet<SubmissionApproval> SubmissionApprovals => Set<SubmissionApproval>();

    // Security
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    // Audit
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<DdlMigrationRecord> DdlMigrations => Set<DdlMigrationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MetadataDbContext).Assembly);
    }
}

// Audit log entity
public class AuditLogEntry
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
    public string? IpAddress { get; set; }
    public Guid? CorrelationId { get; set; }
}

// DDL migration record
public class DdlMigrationRecord
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public int? VersionFrom { get; set; }
    public int VersionTo { get; set; }
    public string MigrationType { get; set; } = string.Empty;
    public string DdlScript { get; set; } = string.Empty;
    public string RollbackScript { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public string ExecutedBy { get; set; } = string.Empty;
    public int? ExecutionDurationMs { get; set; }
    public bool IsRolledBack { get; set; }
    public DateTime? RolledBackAt { get; set; }
    public string? RolledBackBy { get; set; }
}
