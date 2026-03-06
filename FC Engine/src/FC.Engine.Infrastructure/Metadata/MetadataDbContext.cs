using FC.Engine.Domain.Abstractions;
using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Metadata;
using FC.Engine.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace FC.Engine.Infrastructure.Metadata;

public class MetadataDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public MetadataDbContext(DbContextOptions<MetadataDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Multi-tenancy
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Jurisdiction> Jurisdictions => Set<Jurisdiction>();
    public DbSet<JurisdictionFxRate> JurisdictionFxRates => Set<JurisdictionFxRate>();
    public DbSet<ConsolidationAdjustment> ConsolidationAdjustments => Set<ConsolidationAdjustment>();

    // Licensing & modules
    public DbSet<LicenceType> LicenceTypes => Set<LicenceType>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<ModuleVersion> ModuleVersions => Set<ModuleVersion>();
    public DbSet<InterModuleDataFlow> InterModuleDataFlows => Set<InterModuleDataFlow>();
    public DbSet<SubmissionFieldSource> SubmissionFieldSources => Set<SubmissionFieldSource>();
    public DbSet<LicenceModuleMatrix> LicenceModuleMatrix => Set<LicenceModuleMatrix>();
    public DbSet<TenantLicenceType> TenantLicenceTypes => Set<TenantLicenceType>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PlanModulePricing> PlanModulePricing => Set<PlanModulePricing>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionModule> SubscriptionModules => Set<SubscriptionModule>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<PartnerConfig> PartnerConfigs => Set<PartnerConfig>();
    public DbSet<PartnerRevenueRecord> PartnerRevenueRecords => Set<PartnerRevenueRecord>();
    public DbSet<PartnerSupportTicket> PartnerSupportTickets => Set<PartnerSupportTicket>();

    // Metadata tables
    public DbSet<ReturnTemplate> ReturnTemplates => Set<ReturnTemplate>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<TemplateField> TemplateFields => Set<TemplateField>();
    public DbSet<FieldLocalisation> FieldLocalisations => Set<FieldLocalisation>();
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
    public DbSet<PortalNotification> PortalNotifications => Set<PortalNotification>();
    public DbSet<ExportRequest> ExportRequests => Set<ExportRequest>();
    public DbSet<ReturnLock> ReturnLocks => Set<ReturnLock>();
    public DbSet<DataFeedRequestLog> DataFeedRequestLogs => Set<DataFeedRequestLog>();
    public DbSet<TenantFieldMapping> TenantFieldMappings => Set<TenantFieldMapping>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<DataSubjectRequest> DataSubjectRequests => Set<DataSubjectRequest>();
    public DbSet<DataProcessingActivity> DataProcessingActivities => Set<DataProcessingActivity>();
    public DbSet<DataBreachIncident> DataBreachIncidents => Set<DataBreachIncident>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportMapping> ImportMappings => Set<ImportMapping>();
    public DbSet<MigrationModuleSignOff> MigrationModuleSignOffs => Set<MigrationModuleSignOff>();
    public DbSet<KnowledgeBaseArticle> KnowledgeBaseArticles => Set<KnowledgeBaseArticle>();

    // Regulator portal (RG-25)
    public DbSet<RegulatorReceipt> RegulatorReceipts => Set<RegulatorReceipt>();
    public DbSet<ExaminerQuery> ExaminerQueries => Set<ExaminerQuery>();
    public DbSet<ExaminationProject> ExaminationProjects => Set<ExaminationProject>();
    public DbSet<ExaminationAnnotation> ExaminationAnnotations => Set<ExaminationAnnotation>();

    // Security
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<UserMfaConfig> UserMfaConfigs => Set<UserMfaConfig>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<TenantSsoConfig> TenantSsoConfigs => Set<TenantSsoConfig>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    // Filing SLA (RG-12)
    public DbSet<FilingSlaRecord> FilingSlaRecords => Set<FilingSlaRecord>();

    // Reports (RG-18)
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();

    // Audit
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<FieldChangeHistory> FieldChangeHistory => Set<FieldChangeHistory>();
    public DbSet<EvidencePackage> EvidencePackages => Set<EvidencePackage>();
    public DbSet<DdlMigrationRecord> DdlMigrations => Set<DdlMigrationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MetadataDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantContextDefaults();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantContextDefaults();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantContextDefaults()
    {
        var currentTenantId = _tenantContext?.CurrentTenantId;
        if (!currentTenantId.HasValue)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries()
                     .Where(e => e.State == EntityState.Added))
        {
            var tenantProperty = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "TenantId");
            if (tenantProperty is null)
            {
                continue;
            }

            if (tenantProperty.Metadata.ClrType == typeof(Guid))
            {
                var currentValue = tenantProperty.CurrentValue as Guid? ?? Guid.Empty;
                if (currentValue == Guid.Empty)
                {
                    tenantProperty.CurrentValue = currentTenantId.Value;
                }
            }
            else if (tenantProperty.Metadata.ClrType == typeof(Guid?))
            {
                var currentValue = tenantProperty.CurrentValue as Guid?;
                if (!currentValue.HasValue)
                {
                    tenantProperty.CurrentValue = currentTenantId.Value;
                }
            }
        }
    }
}

// Audit log entity
public class AuditLogEntry
{
    public long Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid? TenantId { get; set; }

    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public DateTime PerformedAt { get; set; }
    public string? IpAddress { get; set; }
    public Guid? CorrelationId { get; set; }

    /// <summary>SHA-256 hash of this entry's canonical representation.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Hash of the previous entry in the chain, or "GENESIS" for the first entry.</summary>
    public string PreviousHash { get; set; } = "GENESIS";

    /// <summary>Sequential counter per tenant for chain ordering.</summary>
    public long SequenceNumber { get; set; }
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
