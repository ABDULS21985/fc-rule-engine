using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class ComplianceIqConfigConfiguration : IEntityTypeConfiguration<ComplianceIqConfig>
{
    public void Configure(EntityTypeBuilder<ComplianceIqConfig> builder)
    {
        var seedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("complianceiq_config", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConfigKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConfigValue).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(500)").IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.ConfigKey, x.EffectiveTo }).HasDatabaseName("IX_complianceiq_config_lookup");

        builder.HasData(
            new ComplianceIqConfig { Id = 1, ConfigKey = "rate.queries_per_minute", ConfigValue = "10", Description = "Maximum ComplianceIQ queries per user per minute.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 2, ConfigKey = "rate.queries_per_hour", ConfigValue = "100", Description = "Maximum ComplianceIQ queries per user per hour.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 3, ConfigKey = "rate.queries_per_day", ConfigValue = "500", Description = "Maximum ComplianceIQ queries per user per day.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 4, ConfigKey = "response.max_rows", ConfigValue = "25", Description = "Maximum grounded rows returned in a ComplianceIQ answer.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 5, ConfigKey = "trend.default_periods", ConfigValue = "8", Description = "Default lookback window for trend questions when the user does not specify one.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 6, ConfigKey = "confidence.high_threshold", ConfigValue = "0.85", Description = "Minimum confidence for a HIGH-confidence response label.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 7, ConfigKey = "confidence.medium_threshold", ConfigValue = "0.60", Description = "Minimum confidence for a MEDIUM-confidence response label.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 8, ConfigKey = "help.welcome_message", ConfigValue = "Welcome to ComplianceIQ. Ask about returns, deadlines, anomalies, peer benchmarks, compliance health, or regulator knowledge.", Description = "Welcome message shown in the ComplianceIQ chat surface.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ComplianceIqConfig { Id = 9, ConfigKey = "scenario.default_npl_multiplier", ConfigValue = "2.0", Description = "Fallback NPL multiplier for scenario analysis when a user says doubled without a numeric multiplier.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate });
    }
}

public sealed class ComplianceIqIntentConfiguration : IEntityTypeConfiguration<ComplianceIqIntent>
{
    public void Configure(EntityTypeBuilder<ComplianceIqIntent> builder)
    {
        var seedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("complianceiq_intents", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IntentCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(500)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => x.IntentCode).IsUnique().HasDatabaseName("UX_complianceiq_intents_code");

        builder.HasData(
            new ComplianceIqIntent { Id = 1, IntentCode = "CURRENT_VALUE", Category = "DATA", DisplayName = "Current Value", Description = "Retrieve the latest grounded value for a regulatory field.", SortOrder = 1, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 2, IntentCode = "TREND", Category = "DATA", DisplayName = "Trend", Description = "Show historical movement for a field across recent filing periods.", SortOrder = 2, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 3, IntentCode = "COMPARISON_PEER", Category = "BENCHMARK", DisplayName = "Peer Comparison", Description = "Compare a metric with the peer median or peer band.", SortOrder = 3, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 4, IntentCode = "COMPARISON_PERIOD", Category = "BENCHMARK", DisplayName = "Period Comparison", Description = "Compare a metric between two filing periods.", SortOrder = 4, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 5, IntentCode = "DEADLINE", Category = "CALENDAR", DisplayName = "Deadline", Description = "List upcoming or overdue filing deadlines.", SortOrder = 5, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 6, IntentCode = "REGULATORY_LOOKUP", Category = "KNOWLEDGE", DisplayName = "Regulatory Lookup", Description = "Search regulatory guidance, circulars, and knowledge-base content.", SortOrder = 6, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 7, IntentCode = "COMPLIANCE_STATUS", Category = "STATUS", DisplayName = "Compliance Status", Description = "Summarise compliance health and key compliance posture indicators.", SortOrder = 7, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 8, IntentCode = "ANOMALY_STATUS", Category = "QUALITY", DisplayName = "Anomaly Status", Description = "Summarise anomaly findings and data quality signals.", SortOrder = 8, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 9, IntentCode = "SCENARIO", Category = "ANALYSIS", DisplayName = "Scenario", Description = "Project simple prudential what-if scenarios such as an NPL shock.", SortOrder = 9, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 10, IntentCode = "SEARCH", Category = "DISCOVERY", DisplayName = "Search", Description = "Search validation history and filing evidence.", SortOrder = 10, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 11, IntentCode = "SECTOR_AGGREGATE", Category = "REGULATOR", DisplayName = "Sector Aggregate", Description = "Aggregate a metric across supervised institutions.", RequiresRegulatorContext = true, SortOrder = 11, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 12, IntentCode = "ENTITY_COMPARE", Category = "REGULATOR", DisplayName = "Entity Compare", Description = "Compare named institutions on a selected metric.", RequiresRegulatorContext = true, SortOrder = 12, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 13, IntentCode = "RISK_RANKING", Category = "REGULATOR", DisplayName = "Risk Ranking", Description = "Rank institutions by anomaly pressure or data quality weakness.", RequiresRegulatorContext = true, SortOrder = 13, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 14, IntentCode = "HELP", Category = "SYSTEM", DisplayName = "Help", Description = "Explain the kinds of questions ComplianceIQ can answer.", SortOrder = 14, CreatedAt = seedDate },
            new ComplianceIqIntent { Id = 15, IntentCode = "UNCLEAR", Category = "SYSTEM", DisplayName = "Clarification", Description = "Fallback when a question is ambiguous or unsupported.", SortOrder = 15, CreatedAt = seedDate });
    }
}

public sealed class ComplianceIqTemplateConfiguration : IEntityTypeConfiguration<ComplianceIqTemplate>
{
    public void Configure(EntityTypeBuilder<ComplianceIqTemplate> builder)
    {
        var seedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("complianceiq_templates", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IntentCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.TemplateCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(500)").IsRequired();
        builder.Property(x => x.TemplateBody).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ParameterSchema).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ResultFormat).HasMaxLength(20).IsRequired();
        builder.Property(x => x.VisualizationType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.IntentCode, x.IsActive }).HasDatabaseName("IX_complianceiq_templates_intent");
        builder.HasIndex(x => x.TemplateCode).IsUnique().HasDatabaseName("UX_complianceiq_templates_code");

        builder.HasData(
            new ComplianceIqTemplate { Id = 1, IntentCode = "CURRENT_VALUE", TemplateCode = "CV_SINGLE_FIELD", DisplayName = "Latest Field Value", Description = "Use the latest accepted submission that contains the requested field.", TemplateBody = "Latest accepted submission -> extract requested metric -> cite module and period.", ParameterSchema = "{\"fieldCode\":\"string\"}", ResultFormat = "SCALAR", VisualizationType = "number", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 2, IntentCode = "CURRENT_VALUE", TemplateCode = "CV_KEY_RATIOS", DisplayName = "Latest Key Ratios", Description = "Return the latest prudential key ratio bundle for the institution.", TemplateBody = "Latest accepted submission -> extract key ratios -> return tabular bundle.", ParameterSchema = "{\"moduleCode\":\"string\"}", ResultFormat = "TABLE", VisualizationType = "table", SortOrder = 2, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 3, IntentCode = "TREND", TemplateCode = "TR_FIELD_HISTORY", DisplayName = "Field Trend", Description = "Return the requested metric across multiple periods.", TemplateBody = "Accepted submissions ordered by period -> extract metric -> return timeseries.", ParameterSchema = "{\"fieldCode\":\"string\",\"periodCount\":\"int\"}", ResultFormat = "TIMESERIES", VisualizationType = "lineChart", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 4, IntentCode = "COMPARISON_PEER", TemplateCode = "CP_PEER_METRIC", DisplayName = "Peer Comparison", Description = "Compare an institution metric with peer aggregates.", TemplateBody = "Latest accepted submission + active peer stats -> compute deviation from peer median.", ParameterSchema = "{\"fieldCode\":\"string\",\"licenceCategory\":\"string\"}", ResultFormat = "SCALAR", VisualizationType = "gauge", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 5, IntentCode = "COMPARISON_PERIOD", TemplateCode = "CPR_TWO_PERIODS", DisplayName = "Two Period Comparison", Description = "Compare the same metric between two periods.", TemplateBody = "Locate two accepted periods for tenant -> extract same field -> compute deltas.", ParameterSchema = "{\"fieldCode\":\"string\",\"periodA\":\"string\",\"periodB\":\"string\"}", ResultFormat = "TABLE", VisualizationType = "barChart", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 6, IntentCode = "DEADLINE", TemplateCode = "DL_CALENDAR", DisplayName = "Filing Calendar", Description = "Return upcoming and overdue filing calendar items.", TemplateBody = "Tenant return periods with module deadlines -> classify due, overdue, upcoming.", ParameterSchema = "{\"regulatorCode\":\"string?\",\"overdueOnly\":\"bool\"}", ResultFormat = "TABLE", VisualizationType = "table", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 7, IntentCode = "REGULATORY_LOOKUP", TemplateCode = "RL_KNOWLEDGE", DisplayName = "Knowledge Lookup", Description = "Search knowledge-base and knowledge-graph records.", TemplateBody = "Knowledge articles + knowledge graph nodes/edges -> rank top regulatory matches.", ParameterSchema = "{\"keyword\":\"string\"}", ResultFormat = "LIST", VisualizationType = "table", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 8, IntentCode = "COMPLIANCE_STATUS", TemplateCode = "CS_HEALTH_SCORE", DisplayName = "Compliance Health", Description = "Return the latest Compliance Health Score and pillars.", TemplateBody = "Current CHS snapshot -> summarise overall and pillar scores.", ParameterSchema = "{}", ResultFormat = "SCALAR", VisualizationType = "gauge", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 9, IntentCode = "ANOMALY_STATUS", TemplateCode = "AS_LATEST_REPORT", DisplayName = "Latest Anomaly Report", Description = "Return the latest anomaly report or detailed findings for a module.", TemplateBody = "Latest anomaly report -> optionally expand findings for requested module.", ParameterSchema = "{\"moduleCode\":\"string?\"}", ResultFormat = "TABLE", VisualizationType = "table", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 10, IntentCode = "SCENARIO", TemplateCode = "SC_CAR_NPL", DisplayName = "CAR NPL Scenario", Description = "Project CAR, NPL ratio, and LDR from an NPL shock.", TemplateBody = "Latest prudential submission -> apply NPL multiplier -> recompute CAR/NPL/LDR.", ParameterSchema = "{\"scenarioMultiplier\":\"decimal\"}", ResultFormat = "SCALAR", VisualizationType = "number", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 11, IntentCode = "SEARCH", TemplateCode = "SR_VALIDATION_ERRORS", DisplayName = "Validation Error Search", Description = "Return submissions with validation errors or keyword matches.", TemplateBody = "Validation reports joined to submissions -> count errors and warnings.", ParameterSchema = "{\"keyword\":\"string?\"}", ResultFormat = "TABLE", VisualizationType = "table", SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 12, IntentCode = "SECTOR_AGGREGATE", TemplateCode = "SA_FIELD_AGGREGATE", DisplayName = "Sector Aggregate", Description = "Aggregate a field across supervised institutions.", TemplateBody = "Cross-tenant accepted submissions for regulator context -> aggregate metric.", ParameterSchema = "{\"fieldCode\":\"string\",\"periodCode\":\"string?\",\"licenceCategory\":\"string?\"}", ResultFormat = "AGGREGATE", VisualizationType = "barChart", RequiresRegulatorContext = true, SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 13, IntentCode = "ENTITY_COMPARE", TemplateCode = "EC_ENTITY_COMPARE", DisplayName = "Entity Compare", Description = "Compare named institutions for a selected metric.", TemplateBody = "Cross-tenant accepted submissions -> extract requested metric for named institutions.", ParameterSchema = "{\"fieldCode\":\"string\",\"entityNames\":\"string[]\"}", ResultFormat = "TABLE", VisualizationType = "barChart", RequiresRegulatorContext = true, SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate },
            new ComplianceIqTemplate { Id = 14, IntentCode = "RISK_RANKING", TemplateCode = "RR_ANOMALY_RANKING", DisplayName = "Anomaly Ranking", Description = "Rank institutions by anomaly density or quality score.", TemplateBody = "Cross-tenant anomaly reports -> order by quality score ascending.", ParameterSchema = "{\"periodCode\":\"string?\",\"moduleCode\":\"string?\"}", ResultFormat = "TABLE", VisualizationType = "ranking", RequiresRegulatorContext = true, SortOrder = 1, CreatedAt = seedDate, UpdatedAt = seedDate });
    }
}

public sealed class ComplianceIqFieldSynonymConfiguration : IEntityTypeConfiguration<ComplianceIqFieldSynonym>
{
    public void Configure(EntityTypeBuilder<ComplianceIqFieldSynonym> builder)
    {
        var seedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("complianceiq_field_synonyms", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Synonym).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FieldCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.ModuleCode).HasMaxLength(40);
        builder.Property(x => x.RegulatorCode).HasMaxLength(20);
        builder.Property(x => x.ConfidenceBoost).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.Synonym, x.IsActive }).HasDatabaseName("IX_complianceiq_field_synonyms_lookup");
        builder.HasIndex(x => new { x.FieldCode, x.ModuleCode }).HasDatabaseName("IX_complianceiq_field_synonyms_field");

        builder.HasData(
            new ComplianceIqFieldSynonym { Id = 1, Synonym = "car", FieldCode = "carratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 2, Synonym = "capital adequacy", FieldCode = "carratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 3, Synonym = "capital adequacy ratio", FieldCode = "carratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 4, Synonym = "npl", FieldCode = "nplratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 5, Synonym = "npl ratio", FieldCode = "nplratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 6, Synonym = "non performing loans", FieldCode = "nplratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 7, Synonym = "npl amount", FieldCode = "nplamount", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 8, Synonym = "liquidity", FieldCode = "liquidityratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 9, Synonym = "liquidity ratio", FieldCode = "liquidityratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 10, Synonym = "lcr", FieldCode = "liquidityratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 11, Synonym = "loan to deposit ratio", FieldCode = "loandepositratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 12, Synonym = "ldr", FieldCode = "loandepositratio", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 13, Synonym = "roa", FieldCode = "roa", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 14, Synonym = "return on assets", FieldCode = "roa", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 15, Synonym = "roe", FieldCode = "roe", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 16, Synonym = "return on equity", FieldCode = "roe", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 17, Synonym = "nim", FieldCode = "netinterestmargin", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 18, Synonym = "net interest margin", FieldCode = "netinterestmargin", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 19, Synonym = "total assets", FieldCode = "totalassets", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 20, Synonym = "total liabilities", FieldCode = "totalliabilities", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 21, Synonym = "shareholders funds", FieldCode = "shareholdersfunds", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 22, Synonym = "risk weighted assets", FieldCode = "riskweightedassets", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 23, Synonym = "rwa", FieldCode = "riskweightedassets", ModuleCode = "CBN_PRUDENTIAL", RegulatorCode = "CBN", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 24, Synonym = "insured deposits", FieldCode = "insureddeposits", ModuleCode = "NDIC_SRF", RegulatorCode = "NDIC", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 25, Synonym = "deposit premium", FieldCode = "depositpremiumdue", ModuleCode = "NDIC_SRF", RegulatorCode = "NDIC", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 26, Synonym = "gross premium", FieldCode = "grosspremium", ModuleCode = "NAICOM_QR", RegulatorCode = "NAICOM", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 27, Synonym = "loss ratio", FieldCode = "lossratio", ModuleCode = "NAICOM_QR", RegulatorCode = "NAICOM", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 28, Synonym = "combined ratio", FieldCode = "combinedratio", ModuleCode = "NAICOM_QR", RegulatorCode = "NAICOM", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 29, Synonym = "solvency margin", FieldCode = "solvencymargin", ModuleCode = "NAICOM_QR", RegulatorCode = "NAICOM", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 30, Synonym = "net capital", FieldCode = "netcapital", ModuleCode = "SEC_CMO", RegulatorCode = "SEC", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 31, Synonym = "liquid capital", FieldCode = "liquidcapital", ModuleCode = "SEC_CMO", RegulatorCode = "SEC", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 32, Synonym = "assets under management", FieldCode = "clientassetsaum", ModuleCode = "SEC_CMO", RegulatorCode = "SEC", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 33, Synonym = "aum", FieldCode = "clientassetsaum", ModuleCode = "SEC_CMO", RegulatorCode = "SEC", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 34, Synonym = "trade volume", FieldCode = "tradevolume", ModuleCode = "SEC_CMO", RegulatorCode = "SEC", CreatedAt = seedDate },
            new ComplianceIqFieldSynonym { Id = 35, Synonym = "segregation ratio", FieldCode = "segregationratio", ModuleCode = "SEC_CMO", RegulatorCode = "SEC", CreatedAt = seedDate });
    }
}

public sealed class ComplianceIqConversationConfiguration : IEntityTypeConfiguration<ComplianceIqConversation>
{
    public void Configure(EntityTypeBuilder<ComplianceIqConversation> builder)
    {
        builder.ToTable("complianceiq_conversations", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UserRole).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.LastActivityAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.LastActivityAt }).HasDatabaseName("IX_complianceiq_conversations_tenant_user");
        builder.HasMany(x => x.Turns).WithOne(x => x.Conversation).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ComplianceIqTurnConfiguration : IEntityTypeConfiguration<ComplianceIqTurn>
{
    public void Configure(EntityTypeBuilder<ComplianceIqTurn> builder)
    {
        builder.ToTable("complianceiq_turns", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UserRole).HasMaxLength(60).IsRequired();
        builder.Property(x => x.QueryText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.IntentCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.IntentConfidence).HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(x => x.ExtractedEntitiesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.TemplateCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ResolvedParametersJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ExecutedPlan).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ResponseText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ResponseDataJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.VisualizationType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ConfidenceLevel).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CitationsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.FollowUpSuggestionsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnType("nvarchar(500)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.ConversationId, x.TurnNumber }).HasDatabaseName("IX_complianceiq_turns_conversation");
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt }).HasDatabaseName("IX_complianceiq_turns_tenant");
        builder.HasMany(x => x.Feedback).WithOne(x => x.Turn).HasForeignKey(x => x.TurnId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ComplianceIqQuickQuestionConfiguration : IEntityTypeConfiguration<ComplianceIqQuickQuestion>
{
    public void Configure(EntityTypeBuilder<ComplianceIqQuickQuestion> builder)
    {
        builder.ToTable("complianceiq_quick_questions", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QuestionText).HasMaxLength(220).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(40).IsRequired();
        builder.Property(x => x.IconClass).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.RequiresRegulatorContext, x.IsActive, x.SortOrder }).HasDatabaseName("IX_complianceiq_quick_questions_lookup");

        builder.HasData(
            new ComplianceIqQuickQuestion { Id = 1, QuestionText = "What is our current CAR?", Category = "DATA", IconClass = "bi-bank", SortOrder = 1 },
            new ComplianceIqQuickQuestion { Id = 2, QuestionText = "Show NPL trend over the last 8 quarters", Category = "TREND", IconClass = "bi-graph-up", SortOrder = 2 },
            new ComplianceIqQuickQuestion { Id = 3, QuestionText = "When is our next filing due?", Category = "CALENDAR", IconClass = "bi-calendar-event", SortOrder = 3 },
            new ComplianceIqQuickQuestion { Id = 4, QuestionText = "Do we have any anomalies in our latest return?", Category = "QUALITY", IconClass = "bi-exclamation-triangle", SortOrder = 4 },
            new ComplianceIqQuickQuestion { Id = 5, QuestionText = "What is our compliance health score?", Category = "STATUS", IconClass = "bi-heart-pulse", SortOrder = 5 },
            new ComplianceIqQuickQuestion { Id = 6, QuestionText = "How does our liquidity compare to peers?", Category = "BENCHMARK", IconClass = "bi-people", SortOrder = 6 },
            new ComplianceIqQuickQuestion { Id = 7, QuestionText = "Rank institutions by anomaly density", Category = "REGULATOR", IconClass = "bi-sort-numeric-down", RequiresRegulatorContext = true, SortOrder = 7 },
            new ComplianceIqQuickQuestion { Id = 8, QuestionText = "What is aggregate CAR across commercial banks?", Category = "REGULATOR", IconClass = "bi-bar-chart", RequiresRegulatorContext = true, SortOrder = 8 },
            new ComplianceIqQuickQuestion { Id = 9, QuestionText = "Compare Access Bank and GTBank on NPL ratio", Category = "REGULATOR", IconClass = "bi-diagram-3", RequiresRegulatorContext = true, SortOrder = 9 },
            new ComplianceIqQuickQuestion { Id = 10, QuestionText = "What does BSD/DIR/2024/003 require?", Category = "KNOWLEDGE", IconClass = "bi-journal-text", SortOrder = 10 });
    }
}

public sealed class ComplianceIqFeedbackConfiguration : IEntityTypeConfiguration<ComplianceIqFeedback>
{
    public void Configure(EntityTypeBuilder<ComplianceIqFeedback> builder)
    {
        builder.ToTable("complianceiq_feedback", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Rating).IsRequired();
        builder.Property(x => x.FeedbackText).HasColumnType("nvarchar(1000)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.TurnId, x.UserId }).HasDatabaseName("IX_complianceiq_feedback_turn_user");
    }
}
