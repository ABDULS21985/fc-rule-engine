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
    public DbSet<ReturnDraft> ReturnDrafts => Set<ReturnDraft>();
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
    public DbSet<DataSourceRegistration> DataSourceRegistrations => Set<DataSourceRegistration>();
    public DbSet<DataPipelineDefinition> DataPipelineDefinitions => Set<DataPipelineDefinition>();
    public DbSet<DataPipelineExecution> DataPipelineExecutions => Set<DataPipelineExecution>();
    public DbSet<DspmScanRecord> DspmScanRecords => Set<DspmScanRecord>();
    public DbSet<DspmColumnFinding> DspmColumnFindings => Set<DspmColumnFinding>();
    public DbSet<ShadowCopyRecord> ShadowCopyRecords => Set<ShadowCopyRecord>();
    public DbSet<CyberAsset> CyberAssets => Set<CyberAsset>();
    public DbSet<CyberAssetDependency> CyberAssetDependencies => Set<CyberAssetDependency>();
    public DbSet<SecurityAlert> SecurityAlerts => Set<SecurityAlert>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<RootCauseAnalysisRecord> RootCauseAnalysisRecords => Set<RootCauseAnalysisRecord>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();
    public DbSet<ImportMapping> ImportMappings => Set<ImportMapping>();
    public DbSet<MigrationModuleSignOff> MigrationModuleSignOffs => Set<MigrationModuleSignOff>();
    public DbSet<KnowledgeBaseArticle> KnowledgeBaseArticles => Set<KnowledgeBaseArticle>();
    public DbSet<KnowledgeGraphNode> KnowledgeGraphNodes => Set<KnowledgeGraphNode>();
    public DbSet<KnowledgeGraphEdge> KnowledgeGraphEdges => Set<KnowledgeGraphEdge>();
    public DbSet<CapitalPackSheetRecord> CapitalPackSheets => Set<CapitalPackSheetRecord>();
    public DbSet<OpsResiliencePackSheetRecord> OpsResiliencePackSheets => Set<OpsResiliencePackSheetRecord>();
    public DbSet<ModelRiskPackSheetRecord> ModelRiskPackSheets => Set<ModelRiskPackSheetRecord>();
    public DbSet<SanctionsCatalogSourceRecord> SanctionsCatalogSources => Set<SanctionsCatalogSourceRecord>();
    public DbSet<SanctionsCatalogEntryRecord> SanctionsCatalogEntries => Set<SanctionsCatalogEntryRecord>();
    public DbSet<SanctionsFalsePositiveEntry> SanctionsFalsePositiveEntries => Set<SanctionsFalsePositiveEntry>();
    public DbSet<SanctionsDecisionAuditRecord> SanctionsDecisionAuditRecords => Set<SanctionsDecisionAuditRecord>();
    public DbSet<ModelApprovalWorkflowStateRecord> ModelApprovalWorkflowStates => Set<ModelApprovalWorkflowStateRecord>();
    public DbSet<ModelApprovalAuditRecord> ModelApprovalAuditRecords => Set<ModelApprovalAuditRecord>();
    public DbSet<ResilienceAssessmentResponseRecord> ResilienceAssessmentResponses => Set<ResilienceAssessmentResponseRecord>();

    // Direct regulatory submissions (RG-34 legacy)
    public DbSet<DirectSubmission> DirectSubmissions => Set<DirectSubmission>();

    // Batch regulatory submission engine (RG-34 v2)
    public DbSet<RegulatoryChannel> RegulatoryChannels => Set<RegulatoryChannel>();
    public DbSet<SubmissionBatch> SubmissionBatches => Set<SubmissionBatch>();
    public DbSet<SubmissionItem> SubmissionItems => Set<SubmissionItem>();
    public DbSet<SubmissionSignatureRecord> SubmissionSignatureRecords => Set<SubmissionSignatureRecord>();
    public DbSet<SubmissionBatchReceipt> SubmissionBatchReceipts => Set<SubmissionBatchReceipt>();
    public DbSet<RegulatoryQueryRecord> RegulatoryQueryRecords => Set<RegulatoryQueryRecord>();
    public DbSet<QueryResponse> QueryResponses => Set<QueryResponse>();
    public DbSet<QueryResponseAttachment> QueryResponseAttachments => Set<QueryResponseAttachment>();
    public DbSet<SubmissionBatchAuditLog> SubmissionBatchAuditLogs => Set<SubmissionBatchAuditLog>();

    // Regulator portal (RG-25)
    public DbSet<RegulatorReceipt> RegulatorReceipts => Set<RegulatorReceipt>();
    public DbSet<ExaminerQuery> ExaminerQueries => Set<ExaminerQuery>();
    public DbSet<ExaminationProject> ExaminationProjects => Set<ExaminationProject>();
    public DbSet<ExaminationAnnotation> ExaminationAnnotations => Set<ExaminationAnnotation>();
    public DbSet<ExaminationFinding> ExaminationFindings => Set<ExaminationFinding>();
    public DbSet<ExaminationEvidenceRequest> ExaminationEvidenceRequests => Set<ExaminationEvidenceRequest>();
    public DbSet<ExaminationEvidenceFile> ExaminationEvidenceFiles => Set<ExaminationEvidenceFile>();

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

    // Webhooks (RG-30)
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    // Audit
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<FieldChangeHistory> FieldChangeHistory => Set<FieldChangeHistory>();
    public DbSet<EvidencePackage> EvidencePackages => Set<EvidencePackage>();
    public DbSet<DdlMigrationRecord> DdlMigrations => Set<DdlMigrationRecord>();

    // Compliance Health Scoring (RG-32)
    public DbSet<ChsScoreSnapshot> ChsScoreSnapshots => Set<ChsScoreSnapshot>();

    // Cross-Border Harmonisation (RG-41)
    public DbSet<RegulatoryJurisdiction> RegulatoryJurisdictions => Set<RegulatoryJurisdiction>();
    public DbSet<FinancialGroup> FinancialGroups => Set<FinancialGroup>();
    public DbSet<GroupSubsidiary> GroupSubsidiaries => Set<GroupSubsidiary>();
    public DbSet<RegulatoryEquivalenceMapping> RegulatoryEquivalenceMappings => Set<RegulatoryEquivalenceMapping>();
    public DbSet<EquivalenceMappingEntry> EquivalenceMappingEntries => Set<EquivalenceMappingEntry>();
    public DbSet<CrossBorderFxRate> CrossBorderFxRates => Set<CrossBorderFxRate>();
    public DbSet<ConsolidationRun> ConsolidationRuns => Set<ConsolidationRun>();
    public DbSet<ConsolidationSubsidiarySnapshot> ConsolidationSubsidiarySnapshots => Set<ConsolidationSubsidiarySnapshot>();
    public DbSet<GroupConsolidationAdjustment> GroupConsolidationAdjustments => Set<GroupConsolidationAdjustment>();
    public DbSet<CrossBorderDataFlow> CrossBorderDataFlows => Set<CrossBorderDataFlow>();
    public DbSet<DataFlowExecution> DataFlowExecutions => Set<DataFlowExecution>();
    public DbSet<RegulatoryDivergence> RegulatoryDivergences => Set<RegulatoryDivergence>();
    public DbSet<DivergenceNotification> DivergenceNotifications => Set<DivergenceNotification>();
    public DbSet<AfcftaProtocolTracking> AfcftaProtocolTracking => Set<AfcftaProtocolTracking>();
    public DbSet<RegulatoryDeadline> RegulatoryDeadlines => Set<RegulatoryDeadline>();
    public DbSet<HarmonisationAuditLog> HarmonisationAuditLogs => Set<HarmonisationAuditLog>();

    // Policy Simulation & What-If Modelling (RG-40)
    public DbSet<PolicyScenario> PolicyScenarios => Set<PolicyScenario>();
    public DbSet<PolicyParameter> PolicyParameters => Set<PolicyParameter>();
    public DbSet<PolicyParameterPreset> PolicyParameterPresets => Set<PolicyParameterPreset>();
    public DbSet<ImpactAssessmentRun> ImpactAssessmentRuns => Set<ImpactAssessmentRun>();
    public DbSet<EntityImpactResult> EntityImpactResults => Set<EntityImpactResult>();
    public DbSet<CostBenefitAnalysis> CostBenefitAnalyses => Set<CostBenefitAnalysis>();
    public DbSet<ConsultationRound> ConsultationRounds => Set<ConsultationRound>();
    public DbSet<ConsultationProvision> ConsultationProvisions => Set<ConsultationProvision>();
    public DbSet<ConsultationFeedback> ConsultationFeedback => Set<ConsultationFeedback>();
    public DbSet<ProvisionFeedbackEntry> ProvisionFeedback => Set<ProvisionFeedbackEntry>();
    public DbSet<FeedbackAggregation> FeedbackAggregations => Set<FeedbackAggregation>();
    public DbSet<PolicyDecision> PolicyDecisions => Set<PolicyDecision>();
    public DbSet<HistoricalImpactTracking> HistoricalImpactTracking => Set<HistoricalImpactTracking>();
    public DbSet<PolicyAuditLog> PolicyAuditLog => Set<PolicyAuditLog>();

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
