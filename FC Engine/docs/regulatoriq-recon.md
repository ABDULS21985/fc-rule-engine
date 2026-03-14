# RegulatorIQ Reconnaissance Report

## Scope and critical caveats

This reconnaissance was performed against `/Users/mac/codes/fcs/FC Engine`.

Three repository realities materially affect any RegulatorIQ design:

1. The requested AI-02 naming does not exist as `nlquery_*`; the implemented equivalent is `complianceiq_*`, backed by `IComplianceIqService` and a deterministic template dispatcher rather than an LLM. Evidence: [ComplianceIqConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ComplianceIqConfigurations.cs), [IComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IComplianceIqService.cs), [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).
2. The requested AI-03, AI-05, AI-06, AI-08, AI-09, and AI-10 product families are not implemented under those names in this repository. No `docintel_*`, `dataguard_*`, `regwatch_*`, `sentinel_*`, `nexus_*`, or `narrative_*` tables/services were found. Closest equivalents are RG-36 early warning/systemic risk, RG-37 stress testing, RG-41 cross-border analytics, and AI-04 ForeSight. Evidence: [MetadataDbContext.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/MetadataDbContext.cs), [20260320000000_AddEarlyWarningSchema.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Migrations/20260320000000_AddEarlyWarningSchema.cs), [20260325000000_AddStressTestingSchema.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Migrations/20260325000000_AddStressTestingSchema.cs).
3. Regulator access is not modeled as a standalone role enum. It is modeled through `TenantType.Regulator`, the `RegulatorOnly` authorization policy, SQL Server session context, and row-level security. Evidence: [TenantType.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Enums/TenantType.cs), [Program.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Program.cs), [RegulatorTenantAccessHandler.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Auth/RegulatorTenantAccessHandler.cs), [TenantAwareConnectionFactory.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/MultiTenancy/TenantAwareConnectionFactory.cs).

## Section 1: Data Source Inventory

### 1.1 Inventory rules that matter for RegulatorIQ

- Dynamic return tables are created by the import engine, not EF `DbSet<>` declarations. Every physical return table is created in `dbo` with a common shape: `id`, `submission_id`, `TenantId`, generated field columns, and `created_at`. Evidence: [DdlEngine.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/DynamicSchema/DdlEngine.cs), [ModuleImportService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ModuleImportService.cs).
- Physical table names are deterministic: `tablePrefix` or normalized `moduleCode`, plus the last segment of `ReturnCode`. Evidence: [ModuleImportService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ModuleImportService.cs), [ReturnTemplate.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Metadata/ReturnTemplate.cs).
- Current regulator chat flows do **not** query those dynamic tables directly. `ComplianceIqService` materializes answers primarily from `return_submissions.ParsedDataJson`, anomaly tables, CHS, and other curated stores. Evidence: [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).
- No regulator SQL views were found (`CREATE VIEW` / `ToView`) for RG-25 or adjacent modules. Regulator analytics are service/read-model driven. Evidence: [RegulatorPortalConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/RegulatorPortalConfigurations.cs), [HeatmapQueryService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/HeatmapQueryService.cs).

### 1.2 Dynamic regulatory return tables (`dbo`)

All tables in this subsection are tenant-scoped at row level because they carry `TenantId` and `submission_id`; regulator access happens through submission scope, RLS, and higher-level services. Volume is always “per entity, per return, per reporting period”, with row counts driven by template granularity.

| Module pack | Dynamic tables | Intelligence-relevant columns | Access pattern | Volume profile | Evidence |
|---|---|---|---|---|---|
| `BDC_CBN` (RG-08) | `bdc_cov`, `bdc_fxv`, `bdc_cpl`, `bdc_aml`, `bdc_cap`, `bdc_fin`, `bdc_cus`, `bdc_opr`, `bdc_gov`, `bdc_brn`, `bdc_lic`, `bdc_dic` | shared columns `submission_id`, `TenantId`, plus BDC prudential/AML/customer and branch fields | tenant-scoped; regulator-readable through supervised submission scope | monthly per BDC institution; moderate row counts per table | [rg08-bdc-cbn-module-definition.json](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-bdc-cbn-module-definition.json), [DdlEngine.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/DynamicSchema/DdlEngine.cs) |
| `MFB_PAR` (RG-08) | `mfb_cov`, `mfb_par`, `mfb_cap`, `mfb_fin`, `mfb_ifr`, `mfb_dep`, `mfb_lnd`, `mfb_oss`, `mfb_gov`, `mfb_aml`, `mfb_brn`, `mfb_dic` | shared columns plus PAR, capital, lending, deposit, governance, AML fields | tenant-scoped | monthly per MFB institution; moderate/high | [rg08-mfb-par-module-definition.json](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-mfb-par-module-definition.json), [ModuleImportService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ModuleImportService.cs) |
| `NFIU_AML` (RG-08) | `nfiu_cov`, `nfiu_str`, `nfiu_ctr`, `nfiu_ftr`, `nfiu_nil`, `nfiu_pep`, `nfiu_sar`, `nfiu_tfs`, `nfiu_cmp`, `nfiu_rba`, `nfiu_enf`, `nfiu_dic` | shared columns plus STR/CTR/TFS/PEP/compliance/risk-based AML fields | tenant-scoped; regulator-only consumers likely NFIU | monthly/trigger-driven per reporting entity; moderate | [rg08-nfiu-aml-module-definition.json](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-nfiu-aml-module-definition.json), [DdlEngine.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/DynamicSchema/DdlEngine.cs) |
| `DMB_BASEL3` (RG-09) | `dmb_cov`, `dmb_cap`, `dmb_crr`, `dmb_mkr`, `dmb_opr`, `dmb_ifr`, `dmb_lcr`, `dmb_nsf`, `dmb_npl`, `dmb_dep`, `dmb_lnd`, `dmb_gov`, `dmb_aml`, `dmb_fin`, `dmb_dic` | shared columns plus Basel III, CAR, CRR, market risk, IFRS, LCR/NSFR, NPL, deposit and governance fields | tenant-scoped; heavily regulator-relevant | quarterly/monthly by bank; high-value prudential fact tables | [dmb_basel3.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg09/dmb_basel3.json), [rg09/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg09/README.md) |
| `NDIC_RETURNS` (RG-09) | `ndic_cov`, `ndic_fin`, `ndic_dep`, `ndic_dpa`, `ndic_asq`, `ndic_cap`, `ndic_liq`, `ndic_ews`, `ndic_pay`, `ndic_gov`, `ndic_dic` | shared columns plus insured deposits, asset quality, liquidity, EWS and payout fields | tenant-scoped | periodic per insured institution; moderate | [ndic_returns.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg09/ndic_returns.json), [rg09/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg09/README.md) |
| `PMB_CBN` (RG-09) | `pmb_cov`, `pmb_nhf`, `pmb_mtg`, `pmb_del`, `pmb_cap`, `pmb_fin`, `pmb_fmb`, `pmb_dep`, `pmb_aml`, `pmb_gov`, `pmb_reg`, `pmb_dic` | shared columns plus mortgage/NHF/deposit/capital/AML fields | tenant-scoped | periodic per PMB; moderate | [pmb_cbn.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg09/pmb_cbn.json), [ModuleImportService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ModuleImportService.cs) |
| `PSP_FINTECH` (RG-09) | `psp_cov`, `psp_trx`, `psp_flt`, `psp_agt`, `psp_frd`, `psp_cap`, `psp_fin`, `psp_kyc`, `psp_tec`, `psp_cmp`, `psp_drp`, `psp_reg`, `psp_gov`, `psp_dic` | shared columns plus transactions, float, agent network, fraud, KYC, tech/compliance, dispute and governance fields | tenant-scoped | high-frequency transactional snapshots per PSP | [psp_fintech.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg09/psp_fintech.json), [rg09/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg09/README.md) |
| `CMO_SEC` (RG-10) | `cmo_cov`, `cmo_cap`, `cmo_cli`, `cmo_trd`, `cmo_aum`, `cmo_rev`, `cmo_fin`, `cmo_rsk`, `cmo_ipr`, `cmo_aml`, `cmo_reg`, `cmo_gov`, `cmo_dic` | shared columns plus capital, client assets, trade volume, AUM, risk, AML and governance fields | tenant-scoped | periodic per capital-market operator; moderate/high | [cmo_sec.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/cmo_sec.json), [rg10/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/README.md) |
| `DFI_CBN` (RG-10) | `dfi_cov`, `dfi_sec`, `dfi_con`, `dfi_imp`, `dfi_cap`, `dfi_fin`, `dfi_ifr`, `dfi_int`, `dfi_aml`, `dfi_rsk`, `dfi_gov`, `dfi_dic` | shared columns plus sectoral concentration, impact, capital, finance, intervention and AML fields | tenant-scoped | periodic per DFI; moderate | [dfi_cbn.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/dfi_cbn.json), [rg10/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/README.md) |
| `IMTO_CBN` (RG-10) | `imto_cov`, `imto_inb`, `imto_cor`, `imto_agt`, `imto_pay`, `imto_ben`, `imto_fin`, `imto_cap`, `imto_aml`, `imto_nfe`, `imto_gov`, `imto_dic` | shared columns plus corridor, agent, payout, beneficiary, capital, AML and non-financial exposure fields | tenant-scoped | periodic per IMTO; moderate | [imto_cbn.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/imto_cbn.json), [ModuleImportService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ModuleImportService.cs) |
| `INSURANCE_NAICOM` (RG-10) | `ins_cov`, `ins_sol`, `ins_prm`, `ins_clm`, `ins_tpr`, `ins_inv`, `ins_rei`, `ins_fin`, `ins_rsk`, `ins_aml`, `ins_gov`, `ins_dic` | shared columns plus solvency, premium, claims, investment, reinsurance, risk and AML fields | tenant-scoped | periodic per insurer; moderate/high | [insurance_naicom.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/insurance_naicom.json), [rg10/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/README.md) |
| `PFA_PENCOM` (RG-10) | `pfa_cov`, `pfa_nav`, `pfa_fd1`, `pfa_fd2`, `pfa_rsa`, `pfa_aal`, `pfa_con`, `pfa_ben`, `pfa_inc`, `pfa_aml`, `pfa_gov`, `pfa_dic` | shared columns plus NAV, fund, RSA, contribution, benefits and governance fields | tenant-scoped | periodic per PFA; moderate | [pfa_pencom.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/pfa_pencom.json), [rg10/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg10/README.md) |
| `ESG_CLIMATE` (RG-11) | `esg_cov`, `esg_gov`, `esg_str`, `esg_rmg`, `esg_met`, `esg_emissions`, `esg_sec`, `esg_nsb`, `esg_ghg`, `esg_cca`, `esg_etp`, `esg_tar`, `esg_dic` | shared columns plus governance, strategy, risk management, metrics, emissions, scenario and transition-plan fields | tenant-scoped | per entity per climate reporting cycle; moderate/high | [esg_climate.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg11/esg_climate.json), [rg11/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg11/README.md) |
| `FATF_EVAL` (RG-11) | `fatf_cov`, `fatf_tc`, `fatf_io`, `fatf_nra`, `fatf_lea`, `fatf_tfs`, `fatf_bo`, `fatf_dnf`, `fatf_icp`, `fatf_apt`, `fatf_sup`, `fatf_vas`, `fatf_dic` | shared columns plus technical compliance, immediate outcomes, NRA, TFS, beneficial ownership, DNFBP, supervision and VASP fields | tenant-scoped/systemic for AML assessments | periodic and assessment-driven; high analytic value | [fatf_eval.json](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg11/fatf_eval.json), [rg11/README.md](/Users/mac/codes/fcs/FC Engine/docs/module-definitions/rg11/README.md) |

### 1.3 Fixed tables by module family

The table below groups tables that share ownership, access semantics, and volume shape. Every table name listed was found in the EF model or explicit migrations.

| Tables | Module owner | Key columns relevant to regulatory intelligence | Access pattern | Volume profile | Evidence |
|---|---|---|---|---|---|
| `dbo.tenants`, `dbo.jurisdictions`, `dbo.jurisdiction_fx_rates`, `dbo.consolidation_adjustments`, `dbo.tenant_licence_types` | Core tenancy / RG-41 support | tenant master data, `TenantType`, regulator codes, jurisdiction, FX, licence mapping | mostly system-wide reference; `tenant_licence_types` is tenant-scoped but regulator-discoverable | low/moderate | [MetadataDbContext.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/MetadataDbContext.cs), [TenantConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/TenantConfiguration.cs), [JurisdictionConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/JurisdictionConfigurations.cs), [TenantLicenceTypeConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/TenantLicenceTypeConfiguration.cs) |
| `dbo.licence_types`, `dbo.modules`, `dbo.module_versions`, `dbo.inter_module_data_flows`, `meta.submission_field_sources`, `dbo.licence_module_matrix` | RG-07 template/module registry | `ModuleCode`, `RegulatorCode`, `LicenceCategory`, data-flow lineage, field provenance, module/version status | system-wide metadata; `submission_field_sources` is tenant-scoped lineage | low for registry, high for field provenance | [ModuleConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ModuleConfiguration.cs), [DynamicDataConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/DynamicDataConfigurations.cs), [LicenceModuleMatrixConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/LicenceModuleMatrixConfiguration.cs) |
| `dbo.subscription_plans`, `dbo.plan_module_pricing`, `dbo.subscriptions`, `dbo.subscription_modules`, `dbo.invoices`, `dbo.invoice_line_items`, `dbo.payments`, `dbo.usage_records`, `dbo.partner_configs`, `dbo.partner_revenue_records`, `dbo.partner_support_tickets` | RG-03 / partner-commercial | plan, module entitlement, payment and usage data; useful mainly for platform intelligence, not frontline supervision | tenant-scoped with platform-wide admin access | low/moderate | [BillingConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/BillingConfigurations.cs), [PartnerConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/PartnerConfigurations.cs), [SubscriptionPlanConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SubscriptionPlanConfiguration.cs) |
| `meta.return_templates`, `meta.template_versions`, `meta.template_fields`, `dbo.field_localisations`, `meta.template_item_codes`, `meta.template_sections`, `meta.intra_sheet_formulas`, `meta.cross_sheet_rules`, `meta.cross_sheet_rule_operands`, `meta.cross_sheet_rule_expressions`, `meta.business_rules` | RG-07 through RG-11 template metadata | `ReturnCode`, `PhysicalTableName`, `ModuleId`, field `FieldName`, `DataType`, `RegulatoryReference`, formulas and rule definitions | mostly system-wide metadata with optional tenant override fields | low for templates, very high for fields/rules | [ReturnTemplateConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ReturnTemplateConfiguration.cs), [ValidationRuleConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ValidationRuleConfigurations.cs), [JurisdictionConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/JurisdictionConfigurations.cs) |
| `dbo.institutions`, `dbo.return_periods`, `dbo.return_submissions`, `dbo.validation_reports`, `dbo.validation_errors` | Core submissions / RG-11 | institution identifiers, `TenantId`, module/period, `ParsedDataJson`, submission status, validation detail | tenant-scoped with regulator RLS access | high for submissions and validation errors | [OperationalConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/OperationalConfigurations.cs) |
| `dbo.return_drafts`, `dbo.export_requests`, `dbo.return_locks`, `dbo.data_feed_request_logs`, `dbo.tenant_field_mappings` | Draft/export/data capture | draft state, export status, lock ownership, feed lineage, field mapping | tenant-scoped | moderate | [ReturnDraftConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ReturnDraftConfiguration.cs), [ExportRequestConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ExportRequestConfiguration.cs), [DataCaptureEvolutionConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/DataCaptureEvolutionConfigurations.cs) |
| `meta.portal_users`, `meta.institution_users`, `meta.submission_approvals`, `dbo.portal_notifications` | RG-05 / workflow | portal identities, institution users, maker-checker approvals, notification state | tenant-scoped | moderate | [PortalUserConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/PortalUserConfiguration.cs), [InstitutionUserConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/InstitutionUserConfiguration.cs), [SubmissionApprovalConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SubmissionApprovalConfiguration.cs), [PortalNotificationConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/PortalNotificationConfiguration.cs) |
| `dbo.consent_records`, `dbo.data_subject_requests`, `dbo.data_processing_activities`, `dbo.data_breach_incidents` | Privacy compliance | consent, DSAR, processing register, breach status | tenant-scoped with DPO/platform access | low/moderate | [PrivacyComplianceConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/PrivacyComplianceConfigurations.cs) |
| `dbo.data_source_registrations`, `dbo.data_pipeline_definitions`, `dbo.data_pipeline_executions`, `dbo.dspm_scan_records`, `dbo.dspm_column_findings`, `dbo.shadow_copy_records`, `dbo.cyber_assets`, `dbo.cyber_asset_dependencies`, `dbo.security_alerts`, `dbo.security_events`, `dbo.root_cause_analysis_records` | Data protection / DSPM / cyber | data-source lineage, pipeline execution, shadow copies, cyber assets, alerts/events, RCA | tenant-scoped with optional platform scans (`Guid? tenantId`) | moderate/high for scans and events | [DataProtectionConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/DataProtectionConfigurations.cs) |
| `dbo.import_jobs`, `dbo.import_mappings`, `dbo.migration_module_signoffs` | Historical migration | import status, source identifiers, mapping quality, module sign-off | tenant-scoped | moderate during migration waves | [HistoricalMigrationConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/HistoricalMigrationConfigurations.cs) |
| `dbo.knowledge_base_articles`, `meta.kg_nodes`, `meta.kg_edges`, `meta.knowledge_graph_dossier_sections`, `meta.dashboard_briefing_pack_sections` | Knowledge / briefing intelligence | regulatory lookup content, graph nodes/edges, dossier sections, briefing sections | system-wide, some regulator-oriented | moderate | [KnowledgeBaseConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/KnowledgeBaseConfiguration.cs), [KnowledgeGraphCatalogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/KnowledgeGraphCatalogConfiguration.cs), [KnowledgeGraphDossierConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/KnowledgeGraphDossierConfiguration.cs), [DashboardBriefingPackConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/DashboardBriefingPackConfiguration.cs) |
| `meta.capital_action_templates`, `meta.capital_planning_scenarios`, `meta.capital_planning_scenario_history`, `meta.model_inventory_definitions`, `meta.capital_pack_sheets`, `meta.ops_resilience_pack_sheets`, `meta.model_risk_pack_sheets`, `meta.institution_supervisory_scorecards`, `meta.institution_supervisory_details`, `meta.platform_intelligence_refresh_runs`, `meta.platform_interventions`, `meta.platform_activity_timeline` | Supervisory packs / platform intelligence | scorecards, model inventory, capital actions, intelligence refresh cadence, intervention tracking | mostly system-wide or regulator-facing | low/moderate | [CapitalActionCatalogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/CapitalActionCatalogConfiguration.cs), [CapitalPlanningScenarioConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/CapitalPlanningScenarioConfiguration.cs), [ModelInventoryCatalogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ModelInventoryCatalogConfiguration.cs), [InstitutionSupervisoryCatalogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/InstitutionSupervisoryCatalogConfiguration.cs), [PlatformIntelligenceRefreshRunConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/PlatformIntelligenceRefreshRunConfiguration.cs), [PlatformOperationsCatalogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/PlatformOperationsCatalogConfiguration.cs) |
| `meta.sanctions_watchlist_sources`, `meta.sanctions_watchlist_entries`, `meta.sanctions_pack_sections`, `meta.sanctions_screening_runs`, `meta.sanctions_screening_results`, `meta.sanctions_transaction_checks`, `meta.sanctions_transaction_party_results`, `meta.sanctions_str_drafts`, `meta.sanctions_false_positive_library`, `meta.sanctions_decision_audit` | RG-48 sanctions / AML | watchlist source metadata, entry aliases/risk, screening session/results, transaction checks, STR drafts, false positive patterns, decision audit | system-wide and regulator-only operational workflows | watchlist entries can be very high volume; results are high-volume per run | [SanctionsCatalogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SanctionsCatalogConfiguration.cs), [SanctionsPackConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SanctionsPackConfiguration.cs), [SanctionsScreeningSessionConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SanctionsScreeningSessionConfiguration.cs), [SanctionsStrDraftConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SanctionsStrDraftConfiguration.cs), [SanctionsWorkflowConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SanctionsWorkflowConfiguration.cs) |
| `meta.model_approval_states`, `meta.model_approval_audit`, `meta.resilience_self_assessment_responses`, `meta.marketplace_rollout_modules`, `meta.marketplace_rollout_plan_coverage`, `meta.marketplace_rollout_reconciliation_queue` | Model governance / resilience / marketplace rollout | model approval state, resilience responses, rollout coverage/reconciliation | mostly system-wide / tenant-scoped support data | low/moderate | [ModelApprovalWorkflowConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ModelApprovalWorkflowConfiguration.cs), [ResilienceAssessmentConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ResilienceAssessmentConfiguration.cs), [MarketplaceRolloutCatalogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/MarketplaceRolloutCatalogConfiguration.cs) |
| `dbo.direct_submissions`, `meta.regulatory_channels`, `meta.submission_batches`, `meta.submission_items`, `meta.submission_signatures`, `meta.submission_batch_receipts`, `meta.regulatory_query_records`, `meta.query_responses`, `meta.query_response_attachments`, `meta.submission_batch_audit_log` | RG-34 direct and batch submissions | regulator channel, batch references, signatures, receipts, query/response workflow | tenant-scoped but regulator-linked operational data | moderate/high for batch channels | [DirectSubmissionConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/DirectSubmissionConfiguration.cs), [RegulatoryChannelConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/RegulatoryChannelConfiguration.cs), [SubmissionBatchConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SubmissionBatchConfiguration.cs), [SubmissionItemConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SubmissionItemConfiguration.cs), [SubmissionSignatureRecordConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SubmissionSignatureRecordConfiguration.cs), [SubmissionBatchReceiptConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SubmissionBatchReceiptConfiguration.cs), [RegulatoryQueryRecordConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/RegulatoryQueryRecordConfiguration.cs), [QueryResponseConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/QueryResponseConfiguration.cs), [QueryResponseAttachmentConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/QueryResponseAttachmentConfiguration.cs), [SubmissionBatchAuditLogConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SubmissionBatchAuditLogConfiguration.cs) |
| `dbo.regulator_receipts`, `dbo.examiner_queries`, `dbo.examination_projects`, `dbo.examination_annotations`, `dbo.examination_findings`, `dbo.examination_evidence_requests`, `dbo.examination_evidence_files` | RG-25 regulator inbox / RG-39 examination toolkit | regulator tenant id, submission scope, query status/priority, project scope JSON, annotations, findings, evidence request/file workflow | regulator-only and cross-tenant by design | low/moderate for projects, moderate/high for queries and annotations | [RegulatorPortalConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/RegulatorPortalConfigurations.cs), [ExaminationToolkitConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ExaminationToolkitConfigurations.cs) |
| `meta.login_attempts`, `meta.password_reset_tokens`, `dbo.refresh_tokens`, `dbo.user_mfa_configs`, `dbo.permissions`, `dbo.roles`, `dbo.role_permissions`, `dbo.tenant_sso_configs`, `dbo.api_keys`, `dbo.feature_flags`, `dbo.email_templates`, `dbo.notification_preferences`, `dbo.notification_deliveries` | RG-05 auth/security/notifications | login security, MFA/SSO, RBAC, API keys, flags, delivery audit | tenant-scoped plus system-wide RBAC/flags | low/moderate; login attempts can grow quickly | [SecurityConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SecurityConfigurations.cs), [AuthEvolutionConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/AuthEvolutionConfigurations.cs), [NotificationEvolutionConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/NotificationEvolutionConfigurations.cs) |
| `dbo.filing_sla_records`, `dbo.saved_reports`, `dbo.webhook_endpoints`, `dbo.webhook_deliveries`, `meta.audit_log`, `meta.field_change_history`, `meta.evidence_packages`, `meta.ddl_migrations` | RG-12, RG-18, RG-30, RG-14 | filing timeliness, saved analytics, webhook audit, immutable audit chain, field-level change history, evidence package hashes | mostly tenant-scoped; `ddl_migrations` is system-wide | low/moderate except audit/change history which can be high | [FilingSlaRecordConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/FilingSlaRecordConfiguration.cs), [SavedReportConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/SavedReportConfiguration.cs), [WebhookConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/WebhookConfigurations.cs), [OperationalConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/OperationalConfigurations.cs) |
| `dbo.chs_score_snapshots` | RG-32 compliance health score | `TenantId`, `PeriodLabel`, `OverallScore`, `Rating`, pillar scores | tenant-scoped but consumed cross-tenant by regulator summary services | one row per tenant per score period | [ChsScoreSnapshotConfiguration.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ChsScoreSnapshotConfiguration.cs) |
| `meta.anomaly_threshold_config`, `meta.anomaly_model_versions`, `meta.anomaly_field_models`, `meta.anomaly_correlation_rules`, `meta.anomaly_peer_group_stats`, `meta.anomaly_rule_baselines`, `meta.anomaly_seed_correlation_rules`, `meta.anomaly_reports`, `meta.anomaly_findings` | AI-01 anomaly detection | model versions, peer-group baselines, field stats, correlation rules, report quality score, finding severity/ack state | configs are system-wide; reports/findings are tenant-scoped and regulator-readable | model tables low/moderate; findings high-volume | [AnomalyConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/AnomalyConfigurations.cs), [AnomalyEntities.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Entities/AnomalyEntities.cs) |
| `meta.complianceiq_config`, `meta.complianceiq_intents`, `meta.complianceiq_templates`, `meta.complianceiq_field_synonyms`, `meta.complianceiq_conversations`, `meta.complianceiq_turns`, `meta.complianceiq_quick_questions`, `meta.complianceiq_feedback` | AI-02 ComplianceIQ | rate/confidence config, intent/template catalog, field synonyms, conversations/turns, citations/follow-ups, feedback | config/catalog tables are system-wide; conversations and turns are tenant-scoped, including regulator-tenant conversations | low for config/catalog; high for turns over time | [ComplianceIqConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ComplianceIqConfigurations.cs), [ComplianceIqEntities.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Entities/ComplianceIqEntities.cs) |
| `meta.foresight_config`, `meta.foresight_model_versions`, `meta.foresight_feature_definitions`, `meta.foresight_regulatory_thresholds`, `meta.foresight_predictions`, `meta.foresight_prediction_features`, `meta.foresight_alerts` | AI-04 ForeSight | model config/versioning, feature definitions, regulatory thresholds, predictions, factor contributions, alert state | config/threshold tables system-wide; predictions and alerts tenant-scoped; regulator ranking reads cross-tenant | moderate | [ForeSightConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ForeSightConfigurations.cs), [ForeSightEntities.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Entities/ForeSightEntities.cs) |
| `dbo.regulatory_jurisdictions`, `dbo.financial_groups`, `dbo.group_subsidiaries`, `dbo.regulatory_equivalence_mappings`, `dbo.equivalence_mapping_entries`, `dbo.cross_border_fx_rates`, `dbo.consolidation_runs`, `dbo.consolidation_subsidiary_snapshots`, `dbo.group_consolidation_adjustments`, `dbo.cross_border_data_flows`, `dbo.data_flow_executions`, `dbo.regulatory_divergences`, `dbo.divergence_notifications`, `dbo.afcfta_protocol_tracking`, `dbo.regulatory_deadlines`, `dbo.harmonisation_audit_log` | RG-41 cross-border / pan-African | group structure, equivalence thresholds, FX, consolidation, data flows, divergence alerts, AfCFTA and deadline tracking | group/regulator-scoped | low/moderate | [CrossBorderConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/CrossBorderConfigurations.cs) |
| `dbo.policy_scenarios`, `dbo.policy_parameters`, `dbo.policy_parameter_presets`, `dbo.impact_assessment_runs`, `dbo.entity_impact_results`, `dbo.cost_benefit_analyses`, `dbo.consultation_rounds`, `dbo.consultation_provisions`, `dbo.consultation_feedback`, `dbo.provision_feedback`, `dbo.feedback_aggregations`, `dbo.policy_decisions`, `dbo.historical_impact_tracking`, `dbo.policy_audit_log` | RG-40 policy simulation | scenario lifecycle, parameters, simulated impacts, consultations, feedback and decisions | regulator-scoped | low/moderate | [PolicySimulationConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/PolicySimulationConfigurations.cs) |
| `meta.prudential_metrics`, `meta.ewi_definitions`, `meta.ewi_triggers`, `meta.camels_ratings`, `meta.systemic_risk_indicators`, `meta.interbank_exposures`, `meta.contagion_analysis_results`, `meta.supervisory_actions`, `meta.supervisory_action_audit_log`, `meta.ewi_computation_runs` | RG-36 early warning / systemic risk | time-series prudential metrics, EWI thresholds/triggers, CAMELS, network exposures, contagion, supervisory actions | regulator-scoped, system-wide across supervised entities | moderate/high; exposures and triggers can become large | [20260320000000_AddEarlyWarningSchema.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Migrations/20260320000000_AddEarlyWarningSchema.cs), [RegulatorPortalModels.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Models/RegulatorPortalModels.cs) |
| `meta.StressScenarios`, `meta.StressScenarioParameters`, `meta.StressTestRuns`, `meta.StressTestEntityResults`, `meta.StressTestContagionEvents`, `meta.StressTestSectorAggregates` | RG-37 stress testing | scenario definitions, run metadata, entity result set, contagion events, sector aggregates | regulator-scoped | high per run for entity/event tables | [20260325000000_AddStressTestingSchema.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Migrations/20260325000000_AddStressTestingSchema.cs), [StressTestModels.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Models/StressTestModels.cs) |

### 1.4 Requested table gaps and mismatches

| Requested artifact | Repository reality | Impact on RegulatorIQ |
|---|---|---|
| `nlquery_*` tables | Implemented as `complianceiq_*` | Reuse AI-02 conversation/template infrastructure, but rename expectations in prompt/design docs |
| `anomaly_field_result` | Closest table is `meta.anomaly_findings` | Use `anomaly_findings` for field/result-level anomaly explanations |
| `foresight_feature_vector` | Implemented as `meta.foresight_prediction_features` | Existing factor-contribution records are already sufficient for explainability |
| `docintel_*`, `dataguard_*`, `regwatch_*`, `sentinel_*`, `nexus_*`, `narrative_*` | Not found in this repository | Any RegulatorIQ requirement depending on these needs new schema/services or a different repository |

## Section 2: Service Interface Map

### 2.1 Headline findings

- There is no `INlQueryService`; the implemented equivalent is `IComplianceIqService`. Evidence: [IComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IComplianceIqService.cs).
- There is no `ISentinelService`. Closest equivalents are `IEarlyWarningService`, `IHeatmapQueryService`, `ISystemicRiskService`, `IAnomalyDetectionService`, and `IForeSightService`. Evidence: [IEarlyWarningService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEarlyWarningService.cs), [IHeatmapQueryService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IHeatmapQueryService.cs), [ISystemicRiskService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ISystemicRiskService.cs), [IForeSightService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IForeSightService.cs).
- There is no `INexusService`. Closest equivalents are `ISystemicRiskService`, `IStressTestService`, `IPanAfricanDashboardService`, `IEquivalenceMappingService`, `IAfcftaTrackingService`, and the RG-40 policy services. Evidence: [IStressTestService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IStressTestService.cs), [IPanAfricanDashboardService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPanAfricanDashboardService.cs), [IEquivalenceMappingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEquivalenceMappingService.cs), [IAfcftaTrackingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IAfcftaTrackingService.cs), [IPolicyScenarioService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPolicyScenarioService.cs).

### 2.2 Interface ownership and regulator-readiness

| Interface | Module owner | Cross-tenant | Existing regulator-facing methods |
|---|---|---|---|
| `ITablePresetService` | Admin UI | No | None explicit |
| `IScenarioTemplateService` | Admin scenario templates | No | None explicit |
| `IAfcftaTrackingService` | RG-41 AfCFTA tracking | Yes | All methods are regulator-facing |
| `IAnomalyDetectionService` | AI-01 anomaly detection | Partial | `GetSectorSummaryAsync` |
| `IAnomalyModelTrainingService` | AI-01 anomaly detection | No | None explicit |
| `IApiKeyService` | RG-05 auth/API keys | No | None explicit |
| `IBenchmarkingService` | Tenant analytics | No | None explicit |
| `IBulkUploadService` | RG-11 submissions | No | None explicit |
| `ICaaSApiKeyService` | CaaS partner APIs | No | None explicit |
| `ICaaSAutoFilingService` | CaaS auto-filing | No | None explicit |
| `ICaaSService` | CaaS partner APIs | No | None explicit |
| `ICarryForwardService` | Submission preparation | No | None explicit |
| `IComplianceHealthService` | RG-32 compliance health | Partial | `GetSectorSummary`, `GetWatchList`, `GetSectorHeatmap` |
| `IComplianceIqService` | AI-02 ComplianceIQ | Partial | `QueryAsync(IsRegulatorContext=true)`, `GetQuickQuestionsAsync(true)` |
| `IConsentService` | Privacy / consent | No | None explicit |
| `IConsultationService` | RG-40 policy simulation | Partial | regulator consultation lifecycle methods |
| `IDashboardService` | RG-17 dashboards | No | None explicit |
| `IDataBreachService` | Privacy / breach | No | None explicit |
| `IDataFeedService` | Data capture integrations | No | None explicit |
| `IDataProtectionService` | Privacy / data protection | Partial | no dedicated regulator method names, but methods support tenant-optional scans |
| `IDigitalSignatureService` | RG-11 batch signing | No | None explicit |
| `IDivergenceDetectionService` | RG-41 cross-border divergence | Yes | divergence detection and notification methods |
| `IDraftDataService` | Submission drafts | No | None explicit |
| `IDsarService` | Privacy / DSAR | No | None explicit |
| `IEarlyWarningService` | RG-36 early warning | Yes | `ComputeFlags` |
| `IEntitlementService` | RG-02 entitlements | No | None explicit |
| `IEntityBenchmarkingService` | RG-36 benchmarking | Yes | `GetEntityBenchmark` |
| `IEquivalenceMappingService` | RG-41 equivalence | Yes | mapping management and `GetCrossBorderComparisonAsync` |
| `IEvidencePackageService` | RG-14 audit/evidence | No | None explicit |
| `IExaminationWorkspaceService` | RG-25/RG-39 regulator workspace | Yes | all methods are regulator-facing |
| `IFeatureFlagService` | Platform config | No | None explicit |
| `IFieldLocalisationService` | Localization | No | None explicit |
| `IFileStorageService` | Shared infrastructure | No | None explicit |
| `IFilingCalendarService` | RG-12 filing calendar | Partial | `OverrideDeadline` is the regulator-facing extension path |
| `IForeSightService` | AI-04 ForeSight | Partial | `GetRegulatoryRiskRankingAsync` |
| `IFormDataService` | Submission form data | No | None explicit |
| `IHeatmapQueryService` | RG-36 heatmap | Yes | all methods are regulator-facing |
| `IHistoricalMigrationService` | Historical migration/import | No | None explicit |
| `IJurisdictionConsolidationService` | RG-41 consolidation | Partial | consolidation methods are group/regulator adjacent |
| `IJwtTokenService` | RG-05 auth/JWT | No | None explicit |
| `IMfaService` | RG-05 MFA | No | None explicit |
| `IModuleImportService` | RG-07/RG-08 module import | No | None explicit |
| `IPanAfricanDashboardService` | RG-41 pan-African dashboard | Yes | all methods are group/regulator-facing |
| `IPartnerManagementService` | Partner/white-label | No | None explicit |
| `IPermissionService` | RG-05 RBAC | No | None explicit |
| `IPolicyDecisionService` | RG-40 policy simulation | Yes | all methods are regulator-facing |
| `IPolicyScenarioService` | RG-40 policy simulation | Yes | all methods are regulator-facing |
| `IPrivacyDashboardService` | Privacy dashboard | Partial | `GetDashboard(Guid? tenantId)` |
| `IRegulatorInboxService` | RG-25 regulator inbox | Yes | all methods except institution response helper are regulator-facing |
| `IRegulatorQueryService` | RG-34/RG-11 regulator queries | No | institution response workflow, not regulator console |
| `IRegulatorySubmissionService` | RG-34 direct submission | No | None explicit |
| `IReturnLockService` | Data capture locking | No | None explicit |
| `IReturnTimelineService` | RG-14 timeline | No | None explicit |
| `IRootCauseAnalysisService` | Privacy/security RCA | No | None explicit |
| `ISectorAnalyticsService` | RG-36 sector analytics | Yes | all methods are regulator-facing |
| `IStressTestService` | RG-37 stress testing | Yes | all methods are regulator-facing |
| `ISubmissionSigningService` | RG-11 batch signing | No | None explicit |
| `ISubscriptionService` | RG-03 billing | No | None explicit |
| `ISystemicRiskService` | RG-36 systemic risk | Yes | all methods are regulator-facing |
| `ITemplateDownloadService` | Template distribution | No | None explicit |
| `ITenantBrandingService` | RG-06 branding | No | None explicit |
| `ITenantOnboardingService` | Tenant onboarding | No | None explicit |
| `IUserLanguagePreferenceService` | Localization | No | None explicit |
| `IWebhookService` | RG-30 webhooks | No | None explicit |
| `IAuditCommentService` | Portal UI collaboration | No | None explicit |

### 2.3 High-priority interfaces for RegulatorIQ reuse

| Interface | Key methods | What it already gives RegulatorIQ | Evidence |
|---|---|---|---|
| `IAnomalyDetectionService` | `GetSectorSummaryAsync`, `GetReportsForTenantAsync` | Cross-tenant anomaly rollups, unacknowledged finding counts, regulator ranking inputs | [IAnomalyDetectionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IAnomalyDetectionService.cs), [AnomalyDetectionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/AnomalyDetectionService.cs) |
| `IComplianceIqService` | `QueryAsync`, `GetQuickQuestionsAsync`, `GetConversationHistoryAsync`, `ExportConversationPdfAsync` | Existing chat/conversation history/export/feedback pattern to extend | [IComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IComplianceIqService.cs), [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs) |
| `IForeSightService` | `GetRegulatoryRiskRankingAsync` | Existing cross-tenant regulatory action ranking | [IForeSightService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IForeSightService.cs), [ForeSightService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ForeSightService.cs) |
| `IHeatmapQueryService` | `GetSectorHeatmapAsync`, `GetInstitutionEWIHistoryAsync`, `GetCorrelationMatrixAsync` | Sector heatmap, institution flag history, contagion/correlation substrate | [IHeatmapQueryService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IHeatmapQueryService.cs), [HeatmapQueryService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/HeatmapQueryService.cs) |
| `ISectorAnalyticsService` | `GetCarDistribution`, `GetNplTrend`, `GetDepositStructure`, `GetFilingTimeliness`, `GetFilingHeatmap` | Sector-wide time series and filing aggregates | [ISectorAnalyticsService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ISectorAnalyticsService.cs), [SectorAnalyticsService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/SectorAnalyticsService.cs) |
| `ISystemicRiskService` | `GetDashboard`, `ComputeCamelsScores`, `ComputeSystemicIndicators`, `AnalyzeContagion`, `GenerateSupervisoryAction` | Closest existing “Sentinel/Nexus” systemic intelligence engine | [ISystemicRiskService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ISystemicRiskService.cs), [SystemicRiskService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/SystemicRiskService.cs) |
| `IStressTestService` | `RunStressTestAsync`, `GetAvailableScenarios`, `GenerateReportPdfAsync` | Stress-test scenarios, runs, and report export | [IStressTestService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IStressTestService.cs), [StressTestEndpoints.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Api/Endpoints/StressTestEndpoints.cs) |
| `IRegulatorInboxService` | inbox/detail/query/receipt methods | Existing regulator-facing cross-tenant submission review workflow | [IRegulatorInboxService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IRegulatorInboxService.cs), [RegulatorInboxService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/RegulatorInboxService.cs) |
| `IExaminationWorkspaceService` | project/workspace/intelligence pack/finding/evidence methods | Examination workspace and evidence-backed supervisory packs | [IExaminationWorkspaceService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IExaminationWorkspaceService.cs) |
| `IPanAfricanDashboardService`, `IEquivalenceMappingService`, `IAfcftaTrackingService` | group overview, mappings, protocol tracking | Cross-border group and harmonisation intelligence | [IPanAfricanDashboardService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPanAfricanDashboardService.cs), [IEquivalenceMappingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEquivalenceMappingService.cs), [IAfcftaTrackingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IAfcftaTrackingService.cs) |

## Section 3: Query Template Analysis

### 3.1 AI-02 template reality

The repository does not store SQL templates in an `nlquery_template.sql_template` column. Instead:

- templates live in `meta.complianceiq_templates`
- the persisted field is `TemplateBody`
- execution is hard-coded in `ComplianceIqService.ExecuteTemplateAsync(...)`
- `BuildPlan(...)` injects runtime parameters such as `tenantId` and `regulatorContext`

Evidence: [ComplianceIqConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ComplianceIqConfigurations.cs), [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).

### 3.2 Seeded template catalog

| Template code | Intent code | Stored template body | Parameter schema | Cross-tenant regulator support | Evidence |
|---|---|---|---|---|---|
| `CV_SINGLE_FIELD` | `CURRENT_VALUE` | latest accepted submission, extract requested metric, cite module and period | `{"fieldCode":"string"}` | No | [ComplianceIqConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ComplianceIqConfigurations.cs) |
| `CV_KEY_RATIOS` | `CURRENT_VALUE` | latest accepted submission, extract key-ratio bundle | `{"moduleCode":"string"}` | No | same as above |
| `TR_FIELD_HISTORY` | `TREND` | accepted submissions ordered by period, return time series | `{"fieldCode":"string","periodCount":"int"}` | No | same as above |
| `CP_PEER_METRIC` | `COMPARISON_PEER` | latest accepted submission plus peer stats | `{"fieldCode":"string","licenceCategory":"string"}` | No | same as above |
| `CPR_TWO_PERIODS` | `COMPARISON_PERIOD` | locate two periods and compute deltas | `{"fieldCode":"string","periodA":"string","periodB":"string"}` | No | same as above |
| `DL_CALENDAR` | `DEADLINE` | tenant return periods and deadlines | `{"regulatorCode":"string?","overdueOnly":"bool"}` | Partial; still tenant-oriented | same as above |
| `RL_KNOWLEDGE` | `REGULATORY_LOOKUP` | search knowledge-base and graph | `{"keyword":"string"}` | Indirectly yes, but not cross-tenant data | same as above |
| `CS_HEALTH_SCORE` | `COMPLIANCE_STATUS` | current CHS snapshot and pillar scores | `{}` | No | same as above |
| `AS_LATEST_REPORT` | `ANOMALY_STATUS` | latest anomaly report and findings | `{"moduleCode":"string?"}` | No | same as above |
| `SC_CAR_NPL` | `SCENARIO` | apply NPL multiplier and recompute CAR/NPL/LDR | `{"scenarioMultiplier":"decimal"}` | No | same as above |
| `SR_VALIDATION_ERRORS` | `SEARCH` | submissions joined to validation history | `{"keyword":"string?"}` | No | same as above |
| `SA_FIELD_AGGREGATE` | `SECTOR_AGGREGATE` | cross-tenant accepted submissions for regulator context, aggregate metric | `{"fieldCode":"string","periodCode":"string?","licenceCategory":"string?"}` | Yes | same as above |
| `EC_ENTITY_COMPARE` | `ENTITY_COMPARE` | cross-tenant accepted submissions for named institutions | `{"fieldCode":"string","entityNames":"string[]"}` | Yes | same as above |
| `RR_ANOMALY_RANKING` | `RISK_RANKING` | cross-tenant anomaly reports ordered by quality score | `{"periodCode":"string?","moduleCode":"string?"}` | Yes | same as above |

### 3.3 Existing regulator-capable templates

The only seeded regulator-cross-tenant templates are:

- `SA_FIELD_AGGREGATE`
- `EC_ENTITY_COMPARE`
- `RR_ANOMALY_RANKING`

Those are selected by the regulator-only intents `SECTOR_AGGREGATE`, `ENTITY_COMPARE`, and `RISK_RANKING`. Evidence: [ComplianceIqConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ComplianceIqConfigurations.cs), [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).

### 3.4 Parameter-handling pattern

- Stored `ParameterSchema` does **not** explicitly include `tenantId`.
- Runtime `BuildPlan(...)` always injects `tenantId` and `regulatorContext`.
- Regulator execution paths call `LoadSnapshotsAsync(db, null, ...)`, i.e. omit tenant scoping and rely on regulator scope. Evidence: [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).

### 3.5 Gap analysis

Missing template classes for a regulator-grade chat include:

- sector trend over time by metric and licence segment
- top-N / bottom-N by arbitrary metric, not just anomaly quality
- institution 360 dossier across CHS, anomalies, EWIs, sanctions, stress, policy and cross-border data
- cross-module discrepancy detection across filings and generated analytics
- sanctions/watchlist exposure summaries
- stress-test ranking and scenario impact comparison
- supervisory action backlog, overdue actions, and remediation follow-up
- group/consolidated cross-border views
- validation-error hot spots across entities
- provenance/citation templates using `submission_field_sources`
- regulator knowledge templates tied to circular impact across modules rather than simple keyword search

## Section 4: Authentication & Role Architecture

### 4.1 How roles and regulator identity are actually modeled

| Concern | Current implementation | Evidence |
|---|---|---|
| Formal admin/portal roles | `PortalRole`: `Viewer`, `Approver`, `Admin` | [PortalRole.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Enums/PortalRole.cs) |
| Formal institution roles | `InstitutionRole`: `Admin`, `Maker`, `Checker`, `Viewer`, `Approver` | [InstitutionRole.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Enums/InstitutionRole.cs) |
| Formal tenant identity | `TenantType`: `Institution`, `Regulator`, `HoldingGroup`, `ConsultingFirm`, `WhiteLabelPartner`, `Sandbox` | [TenantType.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Enums/TenantType.cs) |
| Seeded RBAC roles | `Admin`, `Maker`, `Checker`, `Approver`, `Viewer`, `PlatformAdmin` in auth evolution migration | [20260305120000_AddAuthEvolutionRg05.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Migrations/20260305120000_AddAuthEvolutionRg05.cs) |
| Regulator personas such as `Examiner`, `Governor`, `DeputyGovernor`, `ComplianceOfficer`, `MLRO` | Present as workflow labels, alert recipients, field names or dashboard personas; not core auth-role enums | [RegulatorWorkflowEnums.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Enums/RegulatorWorkflowEnums.cs), [ForeSightService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ForeSightService.cs), [rg08-nfiu-aml-module-definition.json](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Migrator/SeedData/ModuleDefinitions/rg08-nfiu-aml-module-definition.json) |

### 4.2 How tenant context is resolved

- `AuthService.BuildClaimsPrincipal(...)` writes `TenantId` when available; otherwise the principal becomes `PlatformAdmin`. Evidence: [AuthService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Application/Services/AuthService.cs).
- `TenantClaimsTransformation` enriches authenticated principals with `TenantType`, `TenantSlug`, `RegulatorCode`, and `RegulatorId`. Evidence: [TenantClaimsTransformation.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Auth/TenantClaimsTransformation.cs).
- `TenantContextMiddleware` resolves tenant context from claims for regular users and supports platform-admin impersonation or auto-binding to a regulator tenant for `/regulator` and `/scenarios/macro`. Evidence: [TenantContextMiddleware.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/MultiTenancy/TenantContextMiddleware.cs).
- `RegulatorSessionService` enforces that the active tenant really is a regulator tenant and surfaces `TenantId`, `RegulatorCode`, `RegulatorId`, and `TenantName` to Blazor pages. Evidence: [RegulatorSessionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Services/RegulatorSessionService.cs).

### 4.3 How regulator users bypass normal tenant filtering

- The Admin app defines a `RegulatorOnly` policy using `RegulatorTenantAccessRequirement`. Evidence: [Program.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Program.cs), [RegulatorTenantAccessHandler.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Auth/RegulatorTenantAccessHandler.cs).
- SQL connections set `SESSION_CONTEXT('TenantId')`, `SESSION_CONTEXT('TenantType')`, and `SESSION_CONTEXT('RegulatorCode')`. Evidence: [TenantAwareConnectionFactory.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/MultiTenancy/TenantAwareConnectionFactory.cs), [TenantSessionContextInterceptor.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Persistence/Interceptors/TenantSessionContextInterceptor.cs).
- The row-level-security function in RG-25 allows access when `TenantType = 'Regulator'` and the target tenant has active licence-module mappings whose module regulator code matches the regulator session context. Evidence: [20260306210000_AddRegulatorPortalRg25.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Migrations/20260306210000_AddRegulatorPortalRg25.cs).

### 4.4 Authorize patterns on regulator pages

Every regulator workspace page examined uses page-level `[Authorize(Policy = "RegulatorOnly")]`. Examples:

- [ComplianceChatRegulator.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/ComplianceChatRegulator.razor)
- [SectorHeatmap.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SectorHeatmap.razor)
- [SystemicRisk.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SystemicRisk.razor)
- [StressTest.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/StressTest.razor)
- [CrossBorder/Dashboard.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/CrossBorder/Dashboard.razor)
- [Sanctions/SanctionsDashboard.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Sanctions/SanctionsDashboard.razor)

### 4.5 How “Nexus” and “Sentinel” style access is currently handled

There are no literal `Nexus` or `SentinelAI` modules in this repo. The closest current regulator-only access model is:

- RG-36 pages and services for heatmap, warnings, systemic risk
- RG-37 pages and services for stress testing
- RG-41 pages and services for cross-border/group views
- AI-04 ForeSight platform/regulator ranking surfaces

All of those reuse the same `RegulatorOnly` policy plus regulator session context rather than bespoke product-specific auth rules.

## Section 5: LLM Integration Pattern

### 5.1 Requested LLM pattern vs actual repository state

| Requested concern | Actual repository state | Evidence |
|---|---|---|
| `ILlmService` abstraction | Not present | [IComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IComplianceIqService.cs), [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs) |
| Anthropic API / Claude call pattern | Not present | [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs) |
| Prompt-engineered intent classification | Not present; intent classification is heuristic string matching | [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs) |
| Structured JSON parsing from LLM responses | Not present; JSON in AI-02 is internal persistence of extracted entities, plans, citations and response payloads | [ComplianceIqEntities.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Entities/ComplianceIqEntities.cs), [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs) |
| Token usage/cost tracking | Not present | [ComplianceIqConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ComplianceIqConfigurations.cs) |
| LLM fallback chain | Not present; failures return `RATE_LIMITED`, `NO_TEMPLATE`, `UNCLEAR`, or standard error messages | [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs) |
| `nlquery_config` model settings | Closest table is `meta.complianceiq_config`, but it stores rate limits, row caps, confidence thresholds, welcome text, and scenario defaults only | [ComplianceIqConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ComplianceIqConfigurations.cs) |

### 5.2 What the current AI-02 implementation actually does

- `QueryAsync(...)` rate-limits, classifies intent, extracts entities, selects a template, executes deterministic C# code, stores the turn, and audits the event. Evidence: [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).
- `ClassifyIntentAsync(...)` uses keyword/regex heuristics such as `deadline`, `anomaly`, `compare`, `rank`, and `what if`. Evidence: [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).
- `ExecuteTemplateAsync(...)` is a switch statement over template codes, not SQL templates or prompt-driven tools. Evidence: [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).
- Regulator answers are generated through `ExecuteSectorAggregateAsync`, `ExecuteEntityCompareAsync`, and `ExecuteRiskRankingAsync`. Evidence: [ComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ComplianceIqService.cs).

### 5.3 ForeSight and RegWatch implications

- AI-04 ForeSight is predictive, but not LLM-backed. It computes predictions and explainability features from structured data and persists them in `foresight_predictions` and `foresight_prediction_features`. Evidence: [ForeSightService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Services/ForeSightService.cs), [ForeSightConfigurations.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Infrastructure/Metadata/Configurations/ForeSightConfigurations.cs).
- AI-06 RegWatch is not implemented in this repo, so there is no existing prompt, summarization, classification, or LLM parsing pattern to reuse from that module.

### 5.4 Consequence for RegulatorIQ

RegulatorIQ cannot be built by “plugging into the existing LLM layer”, because there is no existing LLM layer here. If a true conversational AI is desired, new components are required:

- `ILlmService` abstraction and provider implementation
- configuration for model name, temperature, max tokens, timeouts
- prompt library and versioning
- structured output parser and validation
- token/cost telemetry
- guardrails/fallback path when the model output is invalid or unsupported

## Section 6: Blazor UI Patterns

### 6.1 Existing chat/dashboard surfaces

| Surface | Route | Pattern summary | Evidence |
|---|---|---|---|
| Institution chat | `/analytics/chat` | `@attribute [Authorize]`, `@CascadingParameter Task<AuthenticationState>`, tenant ID pulled from claims, local `_messages`, `_isLoading`, `_error`, feedback and export actions | [ComplianceChat.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Components/Pages/Analytics/ComplianceChat.razor) |
| Regulator chat | `/regulator/complianceiq` | `RegulatorOnly` page, `RegulatorSessionService`, `AuthenticationStateProvider`, regulator quick questions, table/citation/follow-up rendering, conversation export | [ComplianceChatRegulator.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/ComplianceChatRegulator.razor) |
| Sector heatmap | `/regulator/heatmap` | `SectionErrorBoundary`, skeleton loaders, summary banner, bubble heatmap, drilldown to institution page, regulator session bootstrap | [SectorHeatmap.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SectorHeatmap.razor) |
| Systemic dashboard | `/regulator/systemic-risk` | `SectionErrorBoundary`, KPI cards, tables, canvases for Chart.js, long-form supervisory commentary | [SystemicRisk.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SystemicRisk.razor) |
| Sector analytics | `/regulator/analytics` | chart-heavy analytics with `ChartJsInterop`, skeleton loaders, regulator-only access | [SectorAnalytics.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SectorAnalytics.razor) |
| Early warnings | `/regulator/warnings` | regulator-only list/table pattern over computed EWI flags | [EarlyWarnings.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/EarlyWarnings.razor) |
| Stress testing | `/regulator/stress-test` | regulator-only scenario selection, run invocation, skeleton loaders, report export | [StressTest.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/StressTest.razor) |

### 6.2 Authentication state usage pattern

- Institution analytics pages typically use `[CascadingParameter] private Task<AuthenticationState> AuthenticationStateTask`.
- Regulator analytics pages more often use `RegulatorSessionService` plus `AuthenticationStateProvider` directly.
- In both cases, the UI layer constructs request DTOs with `TenantId`, `UserId`, `UserRole`, and sometimes `RegulatorCode`. Evidence: [ComplianceChat.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Components/Pages/Analytics/ComplianceChat.razor), [ComplianceChatRegulator.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/ComplianceChatRegulator.razor), [ForeSight.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Components/Pages/Analytics/ForeSight.razor).

### 6.3 Role-based rendering pattern

- Access is coarse-grained at page level through `[Authorize]`, `[Authorize(Policy = "RegulatorOnly")]`, or institution-specific policies.
- Fine-grained role display is usually implemented as text labels, chips, or workflow states inside the page rather than component-level RBAC branches.

### 6.4 Visualization pattern

- Chart.js is the dominant chart integration through `FC.Engine.Infrastructure.Charts.ChartJsInterop`. Evidence: [Program.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Program.cs), [SystemicRisk.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SystemicRisk.razor), [SectorAnalytics.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SectorAnalytics.razor).
- Many regulator pages use `SkeletonLoader` and `SectionErrorBoundary` as the standard loading/error composition.

### 6.5 Real-time update pattern

- SignalR is enabled in the Portal and mapped to `/hubs/notifications` and `/hubs/returnlock`.
- This is used for notifications and collaborative return locks, not for regulator chat or regulator dashboards.
- No regulator page reviewed used SignalR for live supervisory intelligence refresh. Evidence: [Program.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Program.cs), [DataEntryForm.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Components/Pages/Submissions/DataEntryForm.razor), [NotificationBell.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Components/Shared/NotificationBell.razor).

## Section 7: Existing Regulator-Facing Features

### 7.1 Regulator pages already present

| Route family | Representative routes | Capability | Evidence |
|---|---|---|---|
| Regulator landing and dashboards | `/regulator`, `/regulator/dashboards/executive`, `/regulator/dashboards/examiner`, `/regulator/dashboards/governor`, `/regulator/dashboards/deputy` | regulator control-room and stakeholder dashboards | [DashboardHome.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/DashboardHome.razor), [ExecutiveDashboard.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Dashboards/ExecutiveDashboard.razor), [GovernorDashboard.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Dashboards/GovernorDashboard.razor) |
| ComplianceIQ regulator chat | `/regulator/complianceiq` | cross-tenant aggregate/compare/ranking conversational surface | [ComplianceChatRegulator.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/ComplianceChatRegulator.razor) |
| Sector/regulator analytics | `/regulator/heatmap`, `/regulator/analytics`, `/regulator/sector-health`, `/regulator/anomalies`, `/regulator/warnings`, `/regulator/institution/{InstitutionId:int}` | sector heatmap, CHS, anomaly heatmap, warning table, institution drilldown | [SectorHeatmap.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SectorHeatmap.razor), [SectorAnalytics.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SectorAnalytics.razor), [SectorHealth.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SectorHealth.razor), [AnomalyHeatmap.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/AnomalyHeatmap.razor), [EarlyWarnings.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/EarlyWarnings.razor), [InstitutionDrillDown.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/InstitutionDrillDown.razor) |
| Systemic risk and stress | `/regulator/systemic-risk`, `/regulator/contagion`, `/regulator/stress-test` | systemic dashboard, contagion, stress testing | [SystemicRisk.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SystemicRisk.razor), [ContagionNetwork.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/ContagionNetwork.razor), [StressTest.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/StressTest.razor) |
| Inbox and examination workspace | `/regulator/inbox`, `/regulator/workspace`, `/regulator/submissionreview` | regulator receipts, examiner queries, project workspace, evidence workflow | [Inbox.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Inbox.razor), [Workspace.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Workspace.razor), [SubmissionReview.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/SubmissionReview.razor) |
| Sanctions/AML | `/regulator/sanctions`, `/regulator/sanctions/watchlists`, `/regulator/sanctions/screening`, `/regulator/sanctions/alerts`, `/regulator/sanctions/false-positives` | watchlist management, screening, alerts, false positive library | [SanctionsDashboard.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Sanctions/SanctionsDashboard.razor), [WatchlistManagement.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Sanctions/WatchlistManagement.razor), [Screening.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Sanctions/Screening.razor), [AlertManagement.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Sanctions/AlertManagement.razor), [FalsePositiveLibrary.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/Sanctions/FalsePositiveLibrary.razor) |
| Cross-border / group supervision | `/regulator/cross-border`, `/regulator/cross-border/mappings`, `/regulator/cross-border/dataflows`, `/regulator/cross-border/consolidation`, `/regulator/cross-border/divergences`, `/regulator/cross-border/deadlines`, `/regulator/cross-border/fx-rates`, `/regulator/cross-border/afcfta` | cross-border equivalence, deadlines, divergence management, consolidation and AfCFTA tracking | [CrossBorder/Dashboard.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/CrossBorder/Dashboard.razor), [CrossBorder/EquivalenceMappings.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/CrossBorder/EquivalenceMappings.razor), [CrossBorder/Consolidation.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/CrossBorder/Consolidation.razor), [CrossBorder/Divergences.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/CrossBorder/Divergences.razor), [CrossBorder/AfcftaTracker.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/CrossBorder/AfcftaTracker.razor) |
| Policy simulation | `/regulator/policies`, `/regulator/policies/new`, `/regulator/policies/compare`, `/regulator/policies/presets` | scenario creation, comparison, presets and consultation lifecycle | [PolicySimulation.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/PolicySimulation/PolicySimulation.razor), [PolicySimulationNew.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/PolicySimulation/PolicySimulationNew.razor), [PolicySimulationCompare.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/PolicySimulation/PolicySimulationCompare.razor), [PolicySimulationPresets.razor](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Components/Pages/Regulator/PolicySimulation/PolicySimulationPresets.razor) |

### 7.2 Regulator-facing or regulator-useful endpoints

| Endpoint | What it does | Evidence |
|---|---|---|
| `GET /compliance/sector/{regulatorCode}` | sector CHS summary | [ComplianceEndpoints.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Api/Endpoints/ComplianceEndpoints.cs) |
| `GET /compliance/watchlist/{regulatorCode}` | CHS watchlist | [ComplianceEndpoints.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Api/Endpoints/ComplianceEndpoints.cs) |
| `GET /compliance/heatmap/{regulatorCode}` | CHS heatmap | [ComplianceEndpoints.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Api/Endpoints/ComplianceEndpoints.cs) |
| `GET /stress-test/scenarios`, `POST /stress-test/run`, `POST /stress-test/report/pdf` | stress-test scenario/run/export | [StressTestEndpoints.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Api/Endpoints/StressTestEndpoints.cs) |
| `GET /regulator/workspace/{projectId:int}/report`, `GET /regulator/workspace/{projectId:int}/intelligence-pack`, `GET /regulator/workspace/{projectId:int}/evidence/{evidenceId:int}` | regulator workspace document/evidence export | [Program.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Program.cs) |
| `GET /regulator/stress-test/report/pdf` | regulator stress-test PDF export | [Program.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Program.cs) |
| `GET /regulator/complianceiq/conversations/{conversationId:guid}/export` | regulator conversation export | [Program.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Program.cs) |
| `/api/v1/regulator/policies/...` | policy simulation, parameters, runs, consultations, decisions, presets | [PolicySimulationEndpoints.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Api/Endpoints/PolicySimulationEndpoints.cs) |
| `/cross-border/...` | equivalence, FX, consolidation, flows, divergences, deadlines, AfCFTA | [CrossBorderEndpoints.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Api/Endpoints/CrossBorderEndpoints.cs) |

### 7.3 What regulators can already ask or inspect today

- sector-wide compliance health, watchlists, and heatmaps
- sector anomaly ranking/aggregation/compare through ComplianceIQ regulator mode
- institution-level drilldown with risk flags and supervisory action generation
- systemic risk, contagion, CAMELS, and stress-test outputs
- submission inbox, examiner query workflow, and examination evidence
- sanctions screening/watchlists/false-positive workflows
- cross-border equivalence/divergence/deadline/group views
- policy scenario and consultation workflows

## Section 8: Gap Analysis for RegulatorIQ

### 8.1 Data that is already regulator-queryable

Already accessible through existing regulator UIs or services:

- anomaly sector summary and institution ranking through `IAnomalyDetectionService` and regulator ComplianceIQ
- CHS sector summary/watchlist/heatmap through `IComplianceHealthService`
- EWI, CAMELS, systemic indicators, contagion, and supervisory actions through `ISystemicRiskService` and `IEarlyWarningService`
- stress-test scenario and run outputs through `IStressTestService`
- cross-border group, equivalence, divergence, AfCFTA, and deadline data through RG-41 services
- sanctions catalog, screening, and STR-draft workflows through RG-48 pages
- examination inbox/workspace/evidence through RG-25 and RG-39 services

### 8.2 Data that exists but has no regulator conversational interface today

- almost all dynamic `dbo` return tables across RG-08 to RG-11
- `validation_errors` and `validation_reports` across entities
- `submission_field_sources` provenance data
- `filing_sla_records` in conversational regulator mode
- sanctions screening/session/result tables
- cross-border consolidation and divergence tables
- policy simulation scenario/result/consultation tables
- data-protection/security/RCA tables
- institutional supervisory pack tables (`institution_supervisory_scorecards`, `institution_supervisory_details`, capital/model/resilience packs)

### 8.3 Missing cross-entity intelligence capabilities

- arbitrary metric ranking across supervised entities
- multi-metric institution dossiers combining filings, anomalies, CHS, EWIs, sanctions, stress and cross-border posture
- cross-module discrepancy detection across returns and analytics outputs
- sector trend narratives by regulator, licence class, and period
- linked evidence/provenance explanations at answer time
- systemic and contagion answers framed as conversational drilldowns rather than page-specific dashboards
- cross-entity validation error hot-spot discovery

### 8.4 ComplianceIQ patterns RegulatorIQ should extend

Reusable patterns:

- conversation persistence in `complianceiq_conversations` / `complianceiq_turns`
- quick-question catalog
- follow-up suggestion rendering
- confidence labels and rate limiting
- PDF export and feedback capture
- citations and answer metadata persistence

Patterns that need extension:

- replace heuristic intent classifier with broader regulator taxonomy
- widen entity extraction and field-synonym coverage beyond a small metric subset
- support multiple data estates, not just `return_submissions.ParsedDataJson`
- support tool routing across anomaly/CHS/EWI/stress/sanctions/cross-border/policy services

### 8.5 New query templates RegulatorIQ specifically needs

Minimum new regulator templates:

- `SECTOR_TREND_METRIC`
- `TOP_N_BY_METRIC`
- `INSTITUTION_360`
- `CROSS_MODULE_DISCREPANCY`
- `FILINGS_DELINQUENCY_RANKING`
- `VALIDATION_HOTSPOT`
- `SANCTIONS_EXPOSURE_SUMMARY`
- `STRESS_IMPACT_RANKING`
- `SYSTEMIC_CLUSTER_EXPLAIN`
- `SUPERVISORY_ACTION_BACKLOG`
- `GROUP_CONSOLIDATION_POSTURE`
- `REGULATORY_CHANGE_IMPACT` if AI-06/RegWatch is later introduced

### 8.6 Reuse vs new service work

Can reuse directly or with thin adapters:

- `IComplianceIqService` for conversation persistence/export/quick-question plumbing
- `IAnomalyDetectionService.GetSectorSummaryAsync`
- `IComplianceHealthService` sector methods
- `IForeSightService.GetRegulatoryRiskRankingAsync`
- `IHeatmapQueryService`
- `ISectorAnalyticsService`
- `ISystemicRiskService`
- `IStressTestService`
- `IRegulatorInboxService`
- `IExaminationWorkspaceService`
- RG-41 services for cross-border views
- RG-40 services for policy simulation data

Needs new service methods or a new orchestration layer:

- unified regulator entity dossier service
- arbitrary dynamic-field search/ranking over imported return tables
- cross-module discrepancy engine
- provenance/citation resolver across `ParsedDataJson`, anomaly findings, CHS, EWI, sanctions, stress, and policy stores
- regulator-grade semantic field catalog for all module families
- if true GenAI is required, an entirely new LLM integration layer

### 8.7 Architectural conclusion

The fastest credible RegulatorIQ path is **not** to invent `Sentinel`/`Nexus`/`nlquery` abstractions inside this repo first. The shortest path is:

1. keep AI-02 conversation persistence and Blazor chat patterns
2. add a regulator-intelligence orchestration layer over existing RG-25, RG-32, RG-36, RG-37, RG-41, RG-48, AI-01, and AI-04 services
3. build a proper field/metric catalog over the dynamic return tables
4. only then add an LLM layer if natural-language generation beyond deterministic templates is required

## Appendix A: Complete Service Interface Signatures

### `FC.Engine.Admin.Services.ITablePresetService`

File: [ITablePresetService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Services/ITablePresetService.cs)

- `Task<IReadOnlyList<TablePreset>> GetSharedPresetsAsync(string pageKey, CancellationToken ct = default);`
- `Task SaveSharedPresetAsync(string pageKey, TablePreset preset, CancellationToken ct = default);`
- `Task DeleteSharedPresetAsync(string pageKey, string presetId, CancellationToken ct = default);`

### `FC.Engine.Admin.Services.Scenarios.IScenarioTemplateService`

File: [IScenarioTemplateService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Admin/Services/Scenarios/IScenarioTemplateService.cs)

- `List<ScenarioTemplate> GetAllTemplates();`
- `ScenarioTemplate? GetTemplate(string id);`
- `ScenarioDefinition CreateFromTemplate(string templateId);`

### `FC.Engine.Domain.Abstractions.IAfcftaTrackingService`

File: [IAfcftaTrackingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IAfcftaTrackingService.cs)

- `Task<IReadOnlyList<AfcftaProtocolDto>> ListProtocolsAsync(CancellationToken ct = default);`
- `Task<AfcftaProtocolDto?> GetProtocolAsync(string protocolCode, CancellationToken ct = default);`
- `Task UpdateProtocolStatusAsync(string protocolCode, Enums.AfcftaProtocolStatus newStatus, int userId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IAnomalyDetectionService`

File: [IAnomalyDetectionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IAnomalyDetectionService.cs)

- `Task<AnomalyReport> AnalyzeSubmissionAsync(int submissionId, Guid tenantId, string performedBy, CancellationToken ct = default);`
- `Task<AnomalyReport?> GetLatestReportForSubmissionAsync(int submissionId, Guid tenantId, CancellationToken ct = default);`
- `Task<AnomalyReport?> GetReportByIdAsync(int reportId, Guid tenantId, CancellationToken ct = default);`
- `Task<List<AnomalyReport>> GetReportsForTenantAsync(Guid tenantId, string? moduleCode = null, string? periodCode = null, CancellationToken ct = default);`
- `Task<List<AnomalySectorSummary>> GetSectorSummaryAsync(string regulatorCode, string? moduleCode = null, string? periodCode = null, CancellationToken ct = default);`
- `Task AcknowledgeFindingAsync(AnomalyAcknowledgementRequest request, CancellationToken ct = default);`
- `Task RevokeAcknowledgementAsync(int findingId, Guid tenantId, string revokedBy, CancellationToken ct = default);`
- `Task<byte[]> ExportReportPdfAsync(int reportId, Guid tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IAnomalyModelTrainingService`

File: [IAnomalyModelTrainingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IAnomalyModelTrainingService.cs)

- `Task<AnomalyModelVersion> TrainModuleModelAsync(string moduleCode, string initiatedBy, bool promoteImmediately = false, CancellationToken ct = default);`
- `Task PromoteModelAsync(int modelVersionId, string promotedBy, CancellationToken ct = default);`
- `Task RollbackModelAsync(string moduleCode, string rolledBackBy, CancellationToken ct = default);`
- `Task<List<AnomalyModelTrainingSummary>> GetModelHistoryAsync(string moduleCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IApiKeyService`

File: [IApiKeyService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IApiKeyService.cs)

- `Task<ApiKeyCreateResult> CreateApiKey(Guid tenantId, int createdByUserId, CreateApiKeyRequest request, CancellationToken ct = default);`
- `Task<ApiKeyValidationResult?> ValidateApiKey(string rawKey, string? ipAddress, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IBenchmarkingService`

File: [IBenchmarkingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IBenchmarkingService.cs)

- `Task<BenchmarkResult?> GetPeerBenchmark(Guid tenantId, string moduleCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IBulkUploadService`

File: [IBulkUploadService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IBulkUploadService.cs)

- `Task<BulkUploadResult> ProcessExcelUpload(Stream fileStream, Guid tenantId, string returnCode, int institutionId, int returnPeriodId, int? requestedByUserId = null, CancellationToken ct = default);`
- `Task<BulkUploadResult> ProcessCsvUpload(Stream fileStream, Guid tenantId, string returnCode, int institutionId, int returnPeriodId, int? requestedByUserId = null, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ICaaSApiKeyService`

File: [ICaaSApiKeyService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ICaaSApiKeyService.cs)

- `Task<(string RawKey, CaaSApiKeyInfo Info)> CreateKeyAsync(int partnerId, string displayName, CaaSEnvironment environment, DateTimeOffset? expiresAt, int createdByUserId, CancellationToken ct = default);`
- `Task<ResolvedPartner?> ValidateKeyAsync(string rawKey, CancellationToken ct = default);`
- `Task RevokeKeyAsync(int partnerId, long keyId, int revokedByUserId, CancellationToken ct = default);`
- `Task<IReadOnlyList<CaaSApiKeyInfo>> ListKeysAsync(int partnerId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ICaaSAutoFilingService`

File: [ICaaSAutoFilingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ICaaSAutoFilingService.cs)

- `Task<CaaSAutoFilingRun> ExecuteScheduleAsync(int scheduleId, CancellationToken ct = default);`
- `Task<IReadOnlyList<CaaSAutoFilingRun>> GetRunHistoryAsync(int partnerId, int scheduleId, int page, int pageSize, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ICaaSService`

File: [ICaaSService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ICaaSService.cs)

- `Task<CaaSValidateResponse> ValidateAsync(ResolvedPartner partner, CaaSValidateRequest request, Guid requestId, CancellationToken ct = default);`
- `Task<CaaSSubmitResponse> SubmitAsync(ResolvedPartner partner, CaaSSubmitRequest request, Guid requestId, CancellationToken ct = default);`
- `Task<CaaSTemplateResponse> GetTemplateAsync(ResolvedPartner partner, string moduleCode, Guid requestId, CancellationToken ct = default);`
- `Task<CaaSDeadlinesResponse> GetDeadlinesAsync(ResolvedPartner partner, Guid requestId, CancellationToken ct = default);`
- `Task<CaaSScoreResponse> GetScoreAsync(ResolvedPartner partner, CaaSScoreRequest request, Guid requestId, CancellationToken ct = default);`
- `Task<CaaSChangesResponse> GetChangesAsync(ResolvedPartner partner, Guid requestId, CancellationToken ct = default);`
- `Task<CaaSSimulateResponse> SimulateAsync(ResolvedPartner partner, CaaSSimulateRequest request, Guid requestId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ICarryForwardService`

File: [ICarryForwardService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ICarryForwardService.cs)

- `Task<CarryForwardResult> GetCarryForwardValues(Guid tenantId, string returnCode, int currentPeriodId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IComplianceHealthService`

File: [IComplianceHealthService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IComplianceHealthService.cs)

- `Task<ComplianceHealthScore> GetCurrentScore(Guid tenantId, CancellationToken ct = default);`
- `Task<ChsDashboardData> GetDashboard(Guid tenantId, CancellationToken ct = default);`
- `Task<ChsTrendData> GetTrend(Guid tenantId, int periods = 12, CancellationToken ct = default);`
- `Task<ChsPeerComparison> GetPeerComparison(Guid tenantId, CancellationToken ct = default);`
- `Task<List<ChsAlert>> GetAlerts(Guid tenantId, CancellationToken ct = default);`
- `Task<SectorChsSummary> GetSectorSummary(string regulatorCode, CancellationToken ct = default);`
- `Task<List<ChsWatchListItem>> GetWatchList(string regulatorCode, CancellationToken ct = default);`
- `Task<List<ChsHeatmapItem>> GetSectorHeatmap(string regulatorCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IComplianceIqService`

File: [IComplianceIqService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IComplianceIqService.cs)

- `Task<ComplianceIqQueryResponse> QueryAsync(ComplianceIqQueryRequest request, CancellationToken ct = default);`
- `Task<IReadOnlyList<ComplianceIqQuickQuestionView>> GetQuickQuestionsAsync(bool isRegulatorContext, CancellationToken ct = default);`
- `Task<IReadOnlyList<ComplianceIqConversationTurnView>> GetConversationHistoryAsync(Guid conversationId, Guid tenantId, CancellationToken ct = default);`
- `Task<IReadOnlyList<ComplianceIqHistoryEntry>> GetQueryHistoryAsync(Guid tenantId, string? userId = null, int limit = 50, CancellationToken ct = default);`
- `Task<IReadOnlyList<ComplianceIqTemplateCatalogItem>> GetTemplateCatalogAsync(CancellationToken ct = default);`
- `Task SubmitFeedbackAsync(int turnId, Guid tenantId, string userId, short rating, string? feedbackText, CancellationToken ct = default);`
- `Task<byte[]> ExportConversationPdfAsync(Guid conversationId, Guid tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IConsentService`

File: [IConsentService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IConsentService.cs)

- `string GetCurrentPolicyVersion();`
- `Task RecordConsent(ConsentCaptureRequest request, CancellationToken ct = default);`
- `Task<bool> HasCurrentRequiredConsent(Guid tenantId, int userId, string userType, CancellationToken ct = default);`
- `Task<IReadOnlyList<ConsentRecord>> GetConsentHistory(Guid tenantId, int userId, string userType, CancellationToken ct = default);`
- `Task WithdrawConsent(Guid tenantId, int userId, string userType, ConsentType consentType, string? ipAddress, string? userAgent, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IConsultationService`

File: [IConsultationService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IConsultationService.cs)

- `Task<long> CreateConsultationAsync(long scenarioId, int regulatorId, string title, string? coverNote, DateOnly deadline, IReadOnlyList<ConsultationProvisionInput> provisions, int userId, CancellationToken ct = default);`
- `Task PublishConsultationAsync(long consultationId, int regulatorId, int userId, CancellationToken ct = default);`
- `Task CloseConsultationAsync(long consultationId, int regulatorId, int userId, CancellationToken ct = default);`
- `Task<FeedbackAggregationResult> AggregateFeedbackAsync(long consultationId, int regulatorId, int userId, CancellationToken ct = default);`
- `Task<ConsultationDetail> GetConsultationAsync(long consultationId, int regulatorId, CancellationToken ct = default);`
- `Task<IReadOnlyList<ConsultationSummary>> GetOpenConsultationsAsync(int institutionId, CancellationToken ct = default);`
- `Task<long> SubmitFeedbackAsync(long consultationId, int institutionId, FeedbackPosition overallPosition, string? generalComments, IReadOnlyList<ProvisionFeedbackInput> provisionFeedback, int submittedByUserId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDashboardService`

File: [IDashboardService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDashboardService.cs)

- `Task<DashboardSummary> GetSummary(Guid tenantId, CancellationToken ct = default);`
- `Task<ModuleDashboardData> GetModuleDashboard(Guid tenantId, string moduleCode, CancellationToken ct = default);`
- `Task<ComplianceSummaryData> GetComplianceSummary(Guid tenantId, CancellationToken ct = default);`
- `Task<TrendData> GetSubmissionTrend(Guid tenantId, string moduleCode, int periods = 6, CancellationToken ct = default);`
- `Task<TrendData> GetValidationErrorTrend(Guid tenantId, string moduleCode, int periods = 6, CancellationToken ct = default);`
- `Task<AdminDashboardData> GetAdminDashboard(Guid tenantId, CancellationToken ct = default);`
- `Task<PartnerDashboardData> GetPartnerDashboard(Guid partnerTenantId, CancellationToken ct = default);`
- `Task<PlatformDashboardData> GetPlatformDashboard(CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDataBreachService`

File: [IDataBreachService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDataBreachService.cs)

- `Task<DataBreachIncident> ReportBreach(DataBreachReport report, CancellationToken ct = default);`
- `Task<DataBreachIncident> MarkNitdaNotified(int incidentId, int processedByUserId, string? notes, CancellationToken ct = default);`
- `Task<IReadOnlyList<DataBreachIncident>> GetOpenIncidents(Guid? tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDataFeedService`

File: [IDataFeedService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDataFeedService.cs)

- `Task<DataFeedResult?> GetByIdempotencyKey(Guid tenantId, string idempotencyKey, CancellationToken ct = default);`
- `Task<DataFeedResult> ProcessFeed(Guid tenantId, string returnCode, DataFeedRequest request, string? idempotencyKey, CancellationToken ct = default);`
- `Task UpsertFieldMapping(Guid tenantId, string integrationName, string returnCode, string externalFieldName, string templateFieldName, CancellationToken ct = default);`
- `Task<IReadOnlyList<TenantFieldMappingEntry>> GetFieldMappings(Guid tenantId, string integrationName, string returnCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDataProtectionService`

File: [IDataProtectionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDataProtectionService.cs)

- `Task<DataSourceSummary> UpsertDataSourceAsync(Guid tenantId, DataSourceRegistrationRequest request, CancellationToken ct = default);`
- `Task<DataPipelineSummary> UpsertPipelineAsync(Guid tenantId, DataPipelineDefinitionRequest request, CancellationToken ct = default);`
- `Task<CyberAssetSummary> UpsertAssetAsync(Guid tenantId, CyberAssetRegistrationRequest request, CancellationToken ct = default);`
- `Task AddAssetDependencyAsync(Guid tenantId, Guid assetId, Guid dependsOnAssetId, CancellationToken ct = default);`
- `Task<DspmAlertSummary> ReportSecurityAlertAsync(Guid tenantId, SecurityAlertReport report, CancellationToken ct = default);`
- `Task RecordSecurityEventAsync(Guid tenantId, SecurityEventReport report, CancellationToken ct = default);`
- `Task<DataPipelineExecutionSummary> RecordPipelineEventAsync(Guid tenantId, PipelineEventReport report, CancellationToken ct = default);`
- `Task HandlePipelineLifecycleEventAsync(DataPipelineLifecycleEvent pipelineEvent, CancellationToken ct = default);`
- `Task<IReadOnlyList<DspmScanSummary>> GetScanHistoryAsync(Guid tenantId, Guid? sourceId = null, CancellationToken ct = default);`
- `Task<IReadOnlyList<ShadowCopyMatch>> GetShadowCopiesAsync(Guid tenantId, CancellationToken ct = default);`
- `Task<IReadOnlyList<DspmAlertSummary>> GetSecurityAlertsAsync(Guid tenantId, string? alertType = null, CancellationToken ct = default);`
- `Task RunAtRestScanAsync(Guid? tenantId = null, CancellationToken ct = default);`
- `Task RunShadowCopyDetectionAsync(Guid? tenantId = null, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDigitalSignatureService`

File: [IDigitalSignatureService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDigitalSignatureService.cs)

- `Task<DigitalSignatureResult> SignPackageAsync(byte[] packageBytes, string regulatorCode, CancellationToken ct = default);`
- `Task<bool> VerifySignatureAsync(byte[] packageBytes, byte[] signature, string certificateThumbprint, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDivergenceDetectionService`

File: [IDivergenceDetectionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDivergenceDetectionService.cs)

- `Task<IReadOnlyList<DivergenceAlert>> DetectDivergencesAsync(CancellationToken ct = default);`
- `Task AcknowledgeDivergenceAsync(long divergenceId, int userId, CancellationToken ct = default);`
- `Task ResolveDivergenceAsync(long divergenceId, string resolution, int userId, CancellationToken ct = default);`
- `Task<IReadOnlyList<DivergenceAlert>> GetOpenDivergencesAsync(string? conceptDomain, DivergenceSeverity? minSeverity, CancellationToken ct = default);`
- `Task<IReadOnlyList<DivergenceAlert>> GetGroupDivergencesAsync(int groupId, CancellationToken ct = default);`
- `Task NotifyGroupsAsync(long divergenceId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDraftDataService`

File: [IDraftDataService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDraftDataService.cs)

- `Task<int> GetOrCreateDraftSubmission(Guid tenantId, string returnCode, int institutionId, int returnPeriodId, int? submittedByUserId = null, CancellationToken ct = default);`
- `Task SaveDraftRows(Guid tenantId, int submissionId, string returnCode, IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IDsarService`

File: [IDsarService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IDsarService.cs)

- `Task<DataSubjectRequest> CreateRequest(Guid tenantId, DataSubjectRequestType requestType, int requestedBy, string requestedByUserType, string? description, CancellationToken ct = default);`
- `Task<IReadOnlyList<DataSubjectRequest>> GetRequests(Guid tenantId, CancellationToken ct = default);`
- `Task<string> GenerateAccessPackage(int dsarId, int processedByUserId, CancellationToken ct = default);`
- `Task ProcessErasure(int dsarId, int approvedByDpoId, CancellationToken ct = default);`
- `Task<DataSubjectRequest> UpdateStatus(int dsarId, DataSubjectRequestStatus status, int processedByUserId, string? responseNotes, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IEarlyWarningService`

File: [IEarlyWarningService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEarlyWarningService.cs)

- `Task<List<EarlyWarningFlag>> ComputeFlags(string regulatorCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IEntitlementService`

File: [IEntitlementService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEntitlementService.cs)

- `Task<TenantEntitlement> ResolveEntitlements(Guid tenantId, CancellationToken ct = default);`
- `Task<bool> HasModuleAccess(Guid tenantId, string moduleCode, CancellationToken ct = default);`
- `Task<bool> HasFeatureAccess(Guid tenantId, string featureCode, CancellationToken ct = default);`
- `Task InvalidateCache(Guid tenantId);`

### `FC.Engine.Domain.Abstractions.IEntityBenchmarkingService`

File: [IEntityBenchmarkingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEntityBenchmarkingService.cs)

- `Task<EntityBenchmarkResult?> GetEntityBenchmark(string regulatorCode, int institutionId, string? periodCode = null, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IEquivalenceMappingService`

File: [IEquivalenceMappingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEquivalenceMappingService.cs)

- `Task<long> CreateMappingAsync(string mappingCode, string mappingName, string conceptDomain, string? description, IReadOnlyList<EquivalenceEntryInput> entries, int userId, CancellationToken ct = default);`
- `Task AddEntryAsync(long mappingId, EquivalenceEntryInput entry, int userId, CancellationToken ct = default);`
- `Task UpdateThresholdAsync(long mappingId, string jurisdictionCode, decimal newThreshold, int userId, CancellationToken ct = default);`
- `Task<EquivalenceMappingDetail?> GetMappingAsync(long mappingId, CancellationToken ct = default);`
- `Task<IReadOnlyList<EquivalenceMappingSummary>> ListMappingsAsync(string? conceptDomain, CancellationToken ct = default);`
- `Task<IReadOnlyList<JurisdictionThreshold>> GetCrossBorderComparisonAsync(string mappingCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IEvidencePackageService`

File: [IEvidencePackageService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IEvidencePackageService.cs)

- `Task<EvidencePackage> GenerateAsync(int submissionId, string generatedBy, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IExaminationWorkspaceService`

File: [IExaminationWorkspaceService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IExaminationWorkspaceService.cs)

- `Task<IReadOnlyList<ExaminationProject>> ListProjects(Guid regulatorTenantId, CancellationToken ct = default);`
- `Task<ExaminationProject> CreateProject(Guid regulatorTenantId, int createdBy, ExaminationProjectCreateRequest request, CancellationToken ct = default);`
- `Task<ExaminationWorkspaceData?> GetWorkspace(Guid regulatorTenantId, string regulatorCode, int projectId, CancellationToken ct = default);`
- `Task<ExaminationIntelligencePack?> GetIntelligencePack(Guid regulatorTenantId, string regulatorCode, int projectId, CancellationToken ct = default);`
- `Task<ExaminationAnnotation> AddAnnotation(Guid regulatorTenantId, int projectId, int submissionId, int? institutionId, string? fieldCode, string note, int createdBy, CancellationToken ct = default);`
- `Task<ExaminationFinding> CreateFinding(Guid regulatorTenantId, string regulatorCode, int projectId, ExaminationFindingCreateRequest request, int createdBy, CancellationToken ct = default);`
- `Task<ExaminationFinding?> UpdateFinding(Guid regulatorTenantId, int projectId, int findingId, ExaminationFindingUpdateRequest request, int updatedBy, CancellationToken ct = default);`
- `Task<ExaminationEvidenceRequest> CreateEvidenceRequest(Guid regulatorTenantId, int projectId, ExaminationEvidenceRequestCreateRequest request, int requestedBy, CancellationToken ct = default);`
- `Task<ExaminationEvidenceFile> UploadEvidence(Guid regulatorTenantId, int projectId, int? findingId, int? evidenceRequestId, int? submissionId, int? institutionId, string fileName, string contentType, long fileSizeBytes, Stream content, ExaminationEvidenceKind kind, ExaminationEvidenceUploaderRole uploadedByRole, string? notes, int uploadedBy, CancellationToken ct = default);`
- `Task<ExaminationEvidenceDownload?> DownloadEvidence(Guid regulatorTenantId, int projectId, int evidenceFileId, CancellationToken ct = default);`
- `Task<byte[]> GenerateIntelligencePackPdf(Guid regulatorTenantId, string regulatorCode, int projectId, CancellationToken ct = default);`
- `Task<byte[]> GenerateReportPdf(Guid regulatorTenantId, string regulatorCode, int projectId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IFeatureFlagService`

File: [IFeatureFlagService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IFeatureFlagService.cs)

- `Task<bool> IsEnabled(string flagCode, Guid? tenantId = null);`
- `Task<IReadOnlyList<FeatureFlag>> GetAll(CancellationToken ct = default);`
- `Task<FeatureFlag> Upsert(string flagCode, string description, bool isEnabled, int rolloutPercent, string? allowedTenants, string? allowedPlans, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IFieldLocalisationService`

File: [IFieldLocalisationService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IFieldLocalisationService.cs)

- `Task<IReadOnlyDictionary<int, FieldLocalisationValue>> GetLocalisations(IEnumerable<int> fieldIds, string languageCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IFileStorageService`

File: [IFileStorageService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IFileStorageService.cs)

- `Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);`
- `Task<string> UploadImmutableAsync(string path, Stream content, string contentType, CancellationToken ct = default);`
- `Task<Stream> DownloadAsync(string path, CancellationToken ct = default);`
- `Task DeleteAsync(string path, CancellationToken ct = default);`
- `string GetPublicUrl(string path);`

### `FC.Engine.Domain.Abstractions.IFilingCalendarService`

File: [IFilingCalendarService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IFilingCalendarService.cs)

- `Task<List<RagItem>> GetRagStatus(Guid tenantId, CancellationToken ct = default);`
- `DateTime ComputeDeadline(Module module, ReturnPeriod period);`
- `Task OverrideDeadline(Guid tenantId, int periodId, DateTime newDeadline, string reason, int overrideByUserId, CancellationToken ct = default);`
- `Task RecordSla(int periodId, int submissionId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IForeSightService`

File: [IForeSightService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IForeSightService.cs)

- `Task<ForeSightDashboardData> GetTenantDashboardAsync(Guid tenantId, CancellationToken ct = default);`
- `Task<IReadOnlyList<ForeSightPredictionSummary>> GetPredictionsAsync(Guid tenantId, string? modelCode = null, CancellationToken ct = default);`
- `Task<IReadOnlyList<ForeSightAlertItem>> GetAlertsAsync(Guid tenantId, bool unreadOnly = true, CancellationToken ct = default);`
- `Task MarkAlertReadAsync(int alertId, string userId, CancellationToken ct = default);`
- `Task DismissAlertAsync(int alertId, string userId, CancellationToken ct = default);`
- `Task RunAllPredictionsAsync(Guid tenantId, string performedBy = "FORESIGHT", CancellationToken ct = default);`
- `Task<IReadOnlyList<RegulatoryActionForecast>> GetRegulatoryRiskRankingAsync(string regulatorCode, string? licenceType = null, CancellationToken ct = default);`
- `Task<IReadOnlyList<ChurnRiskAssessment>> GetChurnRiskDashboardAsync(CancellationToken ct = default);`
- `Task<byte[]> ExportFilingRiskReportAsync(Guid tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IFormDataService`

File: [IFormDataService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IFormDataService.cs)

- `Task SaveDraftAsync(Guid tenantId, int institutionId, string returnCode, string period, List<Dictionary<string, string>> rows, string savedBy, CancellationToken ct = default);`
- `Task<ReturnDraft?> GetDraftAsync(Guid tenantId, int institutionId, string returnCode, string period, CancellationToken ct = default);`
- `Task DeleteDraftAsync(Guid tenantId, int institutionId, string returnCode, string period, CancellationToken ct = default);`
- `Task<List<ReturnDraft>> GetAllDraftsForTenantAsync(Guid tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IHeatmapQueryService`

File: [IHeatmapQueryService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IHeatmapQueryService.cs)

- `Task<IReadOnlyList<HeatmapCell>> GetSectorHeatmapAsync(string regulatorCode, string periodCode, string? institutionTypeFilter, CancellationToken ct = default);`
- `Task<IReadOnlyList<EWITriggerRow>> GetInstitutionEWIHistoryAsync(int institutionId, string regulatorCode, int periods, CancellationToken ct = default);`
- `Task<double[][]> GetCorrelationMatrixAsync(string regulatorCode, string institutionType, string periodCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IHistoricalMigrationService`

File: [IHistoricalMigrationService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IHistoricalMigrationService.cs)

- `Task<IReadOnlyList<ImportJobDto>> GetJobs(Guid tenantId, int? institutionId = null, CancellationToken ct = default);`
- `Task<ImportJobDto> UploadAndParse(Guid tenantId, int institutionId, string returnCode, int returnPeriodId, string fileName, Stream fileStream, int importedBy, CancellationToken ct = default);`
- `Task<ImportJobDto?> GetJob(Guid tenantId, int importJobId, CancellationToken ct = default);`
- `Task<ImportMappingEditorDto> GetMappingEditor(Guid tenantId, int importJobId, CancellationToken ct = default);`
- `Task SaveMapping(Guid tenantId, int importJobId, IReadOnlyList<ImportMappingUpdate> updates, string? sourceIdentifier, CancellationToken ct = default);`
- `Task<ImportJobDto> ValidateJob(Guid tenantId, int importJobId, CancellationToken ct = default);`
- `Task<ImportJobDto> StageJob(Guid tenantId, int importJobId, CancellationToken ct = default);`
- `Task<ImportJobDto> CommitJob(Guid tenantId, int importJobId, CancellationToken ct = default);`
- `Task<ImportStagedReviewDto> GetStagedReview(Guid tenantId, int importJobId, int take = 200, CancellationToken ct = default);`
- `Task SaveStagedReview(Guid tenantId, int importJobId, IReadOnlyList<ImportStagedRecordDto> records, CancellationToken ct = default);`
- `Task<MigrationTrackerDto> GetTracker(Guid tenantId, CancellationToken ct = default);`
- `Task SetModuleSignOff(Guid tenantId, int moduleId, bool signedOff, int signedOffByUserId, string? notes, CancellationToken ct = default);`
- `Task<ImportJobDto> RollbackJob(Guid tenantId, int importJobId, int rolledBackByUserId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IJurisdictionConsolidationService`

File: [IJurisdictionConsolidationService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IJurisdictionConsolidationService.cs)

- `Task<CrossJurisdictionConsolidation> GetConsolidation(Guid tenantId, string reportingCurrency = "NGN", CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IJwtTokenService`

File: [IJwtTokenService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IJwtTokenService.cs)

- `Task<TokenResponse> GenerateTokenPair(AuthenticatedUser user);`
- `Task<TokenResponse> RefreshToken(string refreshToken, string? ipAddress);`
- `Task RevokeToken(string refreshToken, string? ipAddress);`
- `ClaimsPrincipal ValidateAccessToken(string accessToken);`

### `FC.Engine.Domain.Abstractions.IMfaService`

File: [IMfaService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IMfaService.cs)

- `Task<MfaSetupResult> InitiateSetup(int userId, string userType, string email);`
- `Task<MfaActivationResult> ActivateWithVerification(int userId, string userType, string code);`
- `Task<bool> VerifyCode(int userId, string code, string? userType = null);`
- `Task<bool> VerifyBackupCode(int userId, string backupCode, string? userType = null);`
- `Task<bool> SendMfaCodeSms(int userId, string userType, CancellationToken ct = default);`
- `Task Disable(int userId, string userType);`
- `Task<bool> IsMfaEnabled(int userId, string userType);`
- `Task<bool> IsMfaRequired(Guid tenantId, string role);`

### `FC.Engine.Domain.Abstractions.IModuleImportService`

File: [IModuleImportService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IModuleImportService.cs)

- `Task<ModuleValidationResult> ValidateDefinition(string jsonDefinition, CancellationToken ct = default);`
- `Task<ModuleImportResult> ImportModule(string jsonDefinition, string performedBy, CancellationToken ct = default);`
- `Task<ModulePublishResult> PublishModule(string moduleCode, string approvedBy, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IPanAfricanDashboardService`

File: [IPanAfricanDashboardService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPanAfricanDashboardService.cs)

- `Task<GroupComplianceOverview?> GetGroupOverviewAsync(int groupId, CancellationToken ct = default);`
- `Task<IReadOnlyList<SubsidiaryComplianceSnapshot>> GetSubsidiarySnapshotsAsync(int groupId, string? reportingPeriod, CancellationToken ct = default);`
- `Task<IReadOnlyList<RegulatoryDeadlineDto>> GetDeadlineCalendarAsync(int groupId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default);`
- `Task<CrossBorderRiskMetrics?> GetConsolidatedRiskMetricsAsync(int groupId, string reportingPeriod, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IPartnerManagementService`

File: [IPartnerManagementService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPartnerManagementService.cs)

- `Task<TenantOnboardingResult> OnboardPartner(PartnerOnboardingRequest request, CancellationToken ct = default);`
- `Task<TenantOnboardingResult> CreateSubTenant(Guid partnerTenantId, SubTenantCreateRequest request, CancellationToken ct = default);`
- `Task<List<PartnerSubTenantSummary>> GetSubTenants(Guid partnerTenantId, CancellationToken ct = default);`
- `Task<PartnerConfig?> GetPartnerConfig(Guid partnerTenantId, CancellationToken ct = default);`
- `Task<PartnerConfig> UpdatePartnerConfig(Guid partnerTenantId, UpdatePartnerConfigRequest request, CancellationToken ct = default);`
- `Task<bool> IsPartnerTenant(Guid tenantId, CancellationToken ct = default);`
- `Task<List<Guid>> GetPartnerSubTenantIds(Guid partnerTenantId, CancellationToken ct = default);`
- `Task<List<PartnerSubTenantUserSummary>> GetSubTenantUsers(Guid partnerTenantId, Guid subTenantId, CancellationToken ct = default);`
- `Task<PartnerSubTenantUserSummary> CreateSubTenantUser(Guid partnerTenantId, Guid subTenantId, PartnerSubTenantUserCreateRequest request, CancellationToken ct = default);`
- `Task SetSubTenantUserStatus(Guid partnerTenantId, Guid subTenantId, int userId, bool isActive, CancellationToken ct = default);`
- `Task<List<PartnerSubTenantSubmissionSummary>> GetSubTenantSubmissions(Guid partnerTenantId, Guid subTenantId, int take = 20, CancellationToken ct = default);`
- `Task UpdateSubTenantBranding(Guid partnerTenantId, Guid subTenantId, BrandingConfig config, CancellationToken ct = default);`
- `Task<PartnerSupportTicket> CreateSupportTicket(Guid tenantId, int raisedByUserId, string raisedByUserName, string title, string description, PartnerSupportTicketPriority priority, CancellationToken ct = default);`
- `Task<List<PartnerSupportTicket>> GetSupportTicketsForPartner(Guid partnerTenantId, CancellationToken ct = default);`
- `Task<List<PartnerSupportTicket>> GetSupportTicketsForTenant(Guid tenantId, CancellationToken ct = default);`
- `Task<PartnerSupportTicket> EscalateSupportTicket(Guid partnerTenantId, int ticketId, int escalatedByUserId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IPermissionService`

File: [IPermissionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPermissionService.cs)

- `Task<IReadOnlyList<string>> GetPermissions(Guid? tenantId, string roleName, CancellationToken ct = default);`
- `Task<bool> HasPermission(Guid? tenantId, string roleName, string permissionCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IPolicyDecisionService`

File: [IPolicyDecisionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPolicyDecisionService.cs)

- `Task<long> RecordDecisionAsync(long scenarioId, int regulatorId, DecisionType decision, string summary, DateOnly? effectiveDate, int? phaseInMonths, string? circularReference, int userId, CancellationToken ct = default);`
- `Task<byte[]> GeneratePolicyDocumentAsync(long decisionId, int regulatorId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IPolicyScenarioService`

File: [IPolicyScenarioService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPolicyScenarioService.cs)

- `Task<long> CreateScenarioAsync(int regulatorId, string title, string? description, PolicyDomain domain, string targetEntityTypes, DateOnly baselineDate, int createdByUserId, CancellationToken ct = default);`
- `Task AddParameterAsync(long scenarioId, int regulatorId, string parameterCode, decimal proposedValue, string? applicableEntityTypes, int userId, CancellationToken ct = default);`
- `Task UpdateParameterAsync(long scenarioId, int regulatorId, string parameterCode, decimal newProposedValue, int userId, CancellationToken ct = default);`
- `Task<PolicyScenarioDetail> GetScenarioAsync(long scenarioId, int regulatorId, CancellationToken ct = default);`
- `Task<PagedResult<PolicyScenarioSummary>> ListScenariosAsync(int regulatorId, PolicyDomain? domain, PolicyStatus? status, int page, int pageSize, CancellationToken ct = default);`
- `Task<long> CloneScenarioAsync(long sourceScenarioId, int regulatorId, string newTitle, int userId, CancellationToken ct = default);`
- `Task TransitionStatusAsync(long scenarioId, int regulatorId, PolicyStatus newStatus, int userId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IPrivacyDashboardService`

File: [IPrivacyDashboardService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IPrivacyDashboardService.cs)

- `Task<DpoDashboardData> GetDashboard(Guid? tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IRegulatorInboxService`

File: [IRegulatorInboxService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IRegulatorInboxService.cs)

- `Task<IReadOnlyList<RegulatorSubmissionInboxItem>> GetInbox(Guid regulatorTenantId, string regulatorCode, RegulatorInboxFilter? filter = null, CancellationToken ct = default);`
- `Task<RegulatorSubmissionDetail?> GetSubmissionDetail(Guid regulatorTenantId, string regulatorCode, int submissionId, CancellationToken ct = default);`
- `Task<RegulatorReceipt> UpdateReceiptStatus(Guid regulatorTenantId, int submissionId, RegulatorReceiptStatus status, int reviewedBy, string? notes, CancellationToken ct = default);`
- `Task<IReadOnlyList<ExaminerQuery>> GetQueries(Guid regulatorTenantId, int submissionId, CancellationToken ct = default);`
- `Task<IReadOnlyList<ExaminerQuery>> GetSubmissionQueries(int submissionId, CancellationToken ct = default);`
- `Task<ExaminerQuery> RaiseQuery(Guid regulatorTenantId, int submissionId, string? fieldCode, string queryText, int raisedBy, ExaminerQueryPriority priority = ExaminerQueryPriority.Normal, CancellationToken ct = default);`
- `Task<ExaminerQuery?> RespondToQuery(Guid regulatorTenantId, int queryId, int respondedBy, string responseText, CancellationToken ct = default);`
- `Task<ExaminerQuery?> RespondToQueryAsInstitution(int queryId, int respondedBy, string responseText, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IRegulatorQueryService`

File: [IRegulatorQueryService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IRegulatorQueryService.cs)

- `Task<IReadOnlyList<RegulatoryQuerySummary>> GetOpenQueriesAsync(int institutionId, string? regulatorCode, int page, int pageSize, CancellationToken ct = default);`
- `Task AssignQueryAsync(int institutionId, long queryId, int assignToUserId, CancellationToken ct = default);`
- `Task<long> SubmitResponseAsync(int institutionId, long queryId, string responseText, IReadOnlyList<AttachmentPayload> attachments, int respondedByUserId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IRegulatorySubmissionService`

File: [IRegulatorySubmissionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IRegulatorySubmissionService.cs)

- `Task<DirectSubmissionResult> SubmitToRegulatorAsync(int submissionId, string regulatorCode, string submittedBy, CancellationToken ct = default);`
- `Task<DirectSubmissionResult> RetrySubmissionAsync(int directSubmissionId, CancellationToken ct = default);`
- `Task<DirectSubmissionStatusResult> CheckStatusAsync(int directSubmissionId, CancellationToken ct = default);`
- `Task<List<DirectSubmission>> GetSubmissionHistoryAsync(Guid tenantId, int submissionId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IReturnLockService`

File: [IReturnLockService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IReturnLockService.cs)

- `Task<ReturnLockResult> AcquireLock(Guid tenantId, int submissionId, int userId, string userName, CancellationToken ct = default);`
- `Task<ReturnLockResult?> GetActiveLock(Guid tenantId, int submissionId, CancellationToken ct = default);`
- `Task<ReturnLockResult> Heartbeat(Guid tenantId, int submissionId, int userId, CancellationToken ct = default);`
- `Task ReleaseLock(Guid tenantId, int submissionId, int userId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IReturnTimelineService`

File: [IReturnTimelineService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IReturnTimelineService.cs)

- `Task<List<TimelineEvent>> GetTimelineAsync(int submissionId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IRootCauseAnalysisService`

File: [IRootCauseAnalysisService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IRootCauseAnalysisService.cs)

- `Task<RootCauseAnalysis> AnalyzeAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, bool forceRefresh = false, CancellationToken ct = default);`
- `Task<RootCauseAnalysis?> GetCachedAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, CancellationToken ct = default);`
- `Task<IReadOnlyList<RcaTimelineEntry>> GetTimelineAsync(Guid tenantId, RcaIncidentType type, Guid incidentId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ISectorAnalyticsService`

File: [ISectorAnalyticsService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ISectorAnalyticsService.cs)

- `Task<SectorCarDistribution> GetCarDistribution(string regulatorCode, string periodCode, CancellationToken ct = default);`
- `Task<SectorNplTrend> GetNplTrend(string regulatorCode, int quarters = 8, CancellationToken ct = default);`
- `Task<SectorDepositStructure> GetDepositStructure(string regulatorCode, string periodCode, CancellationToken ct = default);`
- `Task<FilingTimeliness> GetFilingTimeliness(string regulatorCode, string periodCode, CancellationToken ct = default);`
- `Task<FilingHeatmap> GetFilingHeatmap(string regulatorCode, string periodCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IStressTestService`

File: [IStressTestService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IStressTestService.cs)

- `Task<StressTestReport> RunStressTestAsync(string regulatorCode, StressTestRequest request, CancellationToken ct = default);`
- `List<StressScenarioInfo> GetAvailableScenarios();`
- `Task<byte[]> GenerateReportPdfAsync(string regulatorCode, StressTestReport report, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ISubmissionSigningService`

File: [ISubmissionSigningService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ISubmissionSigningService.cs)

- `Task<BatchSignatureInfo> SignPayloadAsync(int institutionId, byte[] payloadContent, CancellationToken ct = default);`
- `Task<bool> VerifySignatureAsync(BatchSignatureInfo signature, byte[] payloadContent, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ISubscriptionService`

File: [ISubscriptionService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ISubscriptionService.cs)

- `Task<Subscription> CreateSubscription(Guid tenantId, string planCode, BillingFrequency frequency, CancellationToken ct = default);`
- `Task<Subscription> UpgradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default);`
- `Task<Subscription> DowngradePlan(Guid tenantId, string newPlanCode, CancellationToken ct = default);`
- `Task CancelSubscription(Guid tenantId, string reason, CancellationToken ct = default);`
- `Task<SubscriptionModule> ActivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default);`
- `Task DeactivateModule(Guid tenantId, string moduleCode, CancellationToken ct = default);`
- `Task<List<ModuleAvailability>> GetAvailableModules(Guid tenantId, CancellationToken ct = default);`
- `Task<Invoice> GenerateInvoice(Guid tenantId, CancellationToken ct = default);`
- `Task<Invoice> IssueInvoice(int invoiceId, CancellationToken ct = default);`
- `Task<Payment> RecordPayment(int invoiceId, RecordPaymentRequest request, CancellationToken ct = default);`
- `Task VoidInvoice(int invoiceId, string reason, CancellationToken ct = default);`
- `Task<UsageSummary> GetUsageSummary(Guid tenantId, CancellationToken ct = default);`
- `Task<bool> CheckLimit(Guid tenantId, string limitType, CancellationToken ct = default);`
- `Task<bool> HasFeature(Guid tenantId, string featureCode, CancellationToken ct = default);`
- `Task<Subscription> GetActiveSubscription(Guid tenantId, CancellationToken ct = default);`
- `Task<List<Invoice>> GetInvoices(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default);`
- `Task<List<Payment>> GetPayments(Guid tenantId, int page = 1, int pageSize = 20, CancellationToken ct = default);`
- `Task<List<SubscriptionPlan>> GetAvailablePlans(Guid tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ISystemicRiskService`

File: [ISystemicRiskService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ISystemicRiskService.cs)

- `Task<SystemicRiskDashboard> GetDashboard(string regulatorCode, CancellationToken ct = default);`
- `Task<List<CamelsScore>> ComputeCamelsScores(string regulatorCode, CancellationToken ct = default);`
- `Task<List<SystemicEwi>> ComputeSystemicIndicators(string regulatorCode, CancellationToken ct = default);`
- `Task<ContagionAnalysis> AnalyzeContagion(string regulatorCode, CancellationToken ct = default);`
- `Task<SupervisoryAction> GenerateSupervisoryAction(string regulatorCode, int institutionId, string flagCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ITemplateDownloadService`

File: [ITemplateDownloadService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ITemplateDownloadService.cs)

- `Task<byte[]> GenerateTemplateExcel(Guid tenantId, string returnCode, CancellationToken ct = default);`
- `Task<string> GenerateTemplateCsv(Guid tenantId, string returnCode, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ITenantBrandingService`

File: [ITenantBrandingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ITenantBrandingService.cs)

- `Task<BrandingConfig> GetBrandingConfig(Guid tenantId, CancellationToken ct = default);`
- `Task UpdateBrandingConfig(Guid tenantId, BrandingConfig config, CancellationToken ct = default);`
- `Task<string> UploadLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);`
- `Task<string> UploadCompactLogo(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);`
- `Task<string> UploadFavicon(Guid tenantId, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);`
- `Task InvalidateCache(Guid tenantId, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.ITenantOnboardingService`

File: [ITenantOnboardingService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/ITenantOnboardingService.cs)

- `Task<TenantOnboardingResult> OnboardTenant(TenantOnboardingRequest request, CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IUserLanguagePreferenceService`

File: [IUserLanguagePreferenceService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IUserLanguagePreferenceService.cs)

- `Task<string> GetCurrentLanguage(CancellationToken ct = default);`

### `FC.Engine.Domain.Abstractions.IWebhookService`

File: [IWebhookService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Domain/Abstractions/IWebhookService.cs)

- `Task<WebhookEndpoint> CreateEndpointAsync(Guid tenantId, string url, string? description, List<string> eventTypes, int createdBy, CancellationToken ct = default);`
- `Task<WebhookEndpoint?> GetEndpointAsync(int id, CancellationToken ct = default);`
- `Task<List<WebhookEndpoint>> GetEndpointsAsync(Guid tenantId, CancellationToken ct = default);`
- `Task UpdateEndpointAsync(int id, string? url, string? description, List<string>? eventTypes, bool? isActive, CancellationToken ct = default);`
- `Task DeleteEndpointAsync(int id, CancellationToken ct = default);`
- `Task<string> RotateSecretAsync(int id, CancellationToken ct = default);`
- `Task<List<WebhookDelivery>> GetDeliveryLogAsync(int endpointId, int take = 50, CancellationToken ct = default);`
- `Task SendTestWebhookAsync(int endpointId, CancellationToken ct = default);`
- `Task DeliverAsync(WebhookEndpoint endpoint, string eventType, object eventData, CancellationToken ct = default);`
- `Task RetryDeliveryAsync(WebhookDelivery delivery, CancellationToken ct = default);`

### `FC.Engine.Portal.Services.IAuditCommentService`

File: [IAuditCommentService.cs](/Users/mac/codes/fcs/FC Engine/src/FC.Engine.Portal/Services/IAuditCommentService.cs)

- `Task<List<TimelineReply>> GetRepliesAsync(int submissionId, string eventId, CancellationToken ct = default);`
- `Task<TimelineReply> AddReplyAsync(int submissionId, string eventId, string authorName, string content, CancellationToken ct = default);`
Have you completed