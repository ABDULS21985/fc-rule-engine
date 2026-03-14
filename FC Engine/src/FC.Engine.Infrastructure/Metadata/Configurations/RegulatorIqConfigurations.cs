using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class RegIqConfigConfiguration : IEntityTypeConfiguration<RegIqConfig>
{
    public void Configure(EntityTypeBuilder<RegIqConfig> builder)
    {
        builder.ToTable("regiq_config", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConfigKey).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConfigValue).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.ConfigKey, x.EffectiveTo }).HasDatabaseName("IX_regiq_config_lookup");
        builder.HasData(RegIqSeedData.Configs);
    }
}

public sealed class RegIqConversationConfiguration : IEntityTypeConfiguration<RegIqConversation>
{
    public void Configure(EntityTypeBuilder<RegIqConversation> builder)
    {
        builder.ToTable("regiq_conversation", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RegulatorId).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RegulatorRole).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.RegulatorAgency).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.ClassificationLevel).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Scope).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.LastActivityAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasCheckConstraint("CK_regiq_conversation_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
        builder.HasCheckConstraint("CK_regiq_conversation_scope", "[Scope] IN ('SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','SYSTEMIC','HELP')");
        builder.HasIndex(x => new { x.RegulatorTenantId, x.RegulatorId, x.LastActivityAt }).HasDatabaseName("IX_regiq_conversation_regulator");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.RegulatorTenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Turns).WithOne(x => x.Conversation).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RegIqTurnConfiguration : IEntityTypeConfiguration<RegIqTurn>
{
    public void Configure(EntityTypeBuilder<RegIqTurn> builder)
    {
        builder.ToTable("regiq_turn", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RegulatorId).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RegulatorRole).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.QueryText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.IntentCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.IntentConfidence).HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(x => x.ExtractedEntitiesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.TemplateCode).HasColumnType("varchar(80)").HasMaxLength(80).IsRequired();
        builder.Property(x => x.ResolvedParametersJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ExecutedPlan).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ResponseText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ResponseDataJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.VisualizationType).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.ConfidenceLevel).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.CitationsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.FollowUpSuggestionsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.EntitiesQueriedJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DataSourcesAccessedJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ClassificationLevel).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.RegulatorAgencyFilterApplied).HasColumnType("varchar(10)").HasMaxLength(10);
        builder.Property(x => x.ErrorMessage).HasColumnType("nvarchar(500)").HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasCheckConstraint("CK_regiq_turn_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
        builder.HasCheckConstraint("CK_regiq_turn_extracted_entities_json", "ISJSON([ExtractedEntitiesJson]) = 1");
        builder.HasCheckConstraint("CK_regiq_turn_resolved_parameters_json", "ISJSON([ResolvedParametersJson]) = 1");
        builder.HasCheckConstraint("CK_regiq_turn_response_data_json", "ISJSON([ResponseDataJson]) = 1");
        builder.HasCheckConstraint("CK_regiq_turn_citations_json", "ISJSON([CitationsJson]) = 1");
        builder.HasCheckConstraint("CK_regiq_turn_followups_json", "ISJSON([FollowUpSuggestionsJson]) = 1");
        builder.HasCheckConstraint("CK_regiq_turn_entities_queried_json", "ISJSON([EntitiesQueriedJson]) = 1");
        builder.HasCheckConstraint("CK_regiq_turn_data_sources_json", "ISJSON([DataSourcesAccessedJson]) = 1");
        builder.HasIndex(x => new { x.ConversationId, x.TurnNumber }).HasDatabaseName("IX_regiq_turn_conversation");
        builder.HasIndex(x => new { x.RegulatorTenantId, x.CreatedAt }).HasDatabaseName("IX_regiq_turn_regulator");
        builder.HasIndex(x => x.PrimaryEntityTenantId).HasDatabaseName("IX_regiq_turn_primary_entity");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.RegulatorTenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RegIqIntentConfiguration : IEntityTypeConfiguration<RegIqIntent>
{
    public void Configure(EntityTypeBuilder<RegIqIntent> builder)
    {
        builder.ToTable("regiq_intent", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IntentCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Category).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnType("nvarchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        builder.Property(x => x.ExampleQuery).HasColumnType("nvarchar(250)").HasMaxLength(250).IsRequired();
        builder.Property(x => x.PrimaryDataSource).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => x.IntentCode).IsUnique().HasDatabaseName("UX_regiq_intent_code");
        builder.HasData(RegIqSeedData.Intents);
    }
}

public sealed class RegIqQueryTemplateConfiguration : IEntityTypeConfiguration<RegIqQueryTemplate>
{
    public void Configure(EntityTypeBuilder<RegIqQueryTemplate> builder)
    {
        builder.ToTable("regiq_query_template", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IntentCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.TemplateCode).HasColumnType("varchar(80)").HasMaxLength(80).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnType("nvarchar(150)").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(500)").HasMaxLength(500).IsRequired();
        builder.Property(x => x.SqlTemplate).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ParameterSchema).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ResultFormat).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.VisualizationType).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Scope).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ClassificationLevel).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.DataSourcesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasCheckConstraint("CK_regiq_query_template_scope", "[Scope] IN ('SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','SYSTEMIC','HELP')");
        builder.HasCheckConstraint("CK_regiq_query_template_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
        builder.HasCheckConstraint("CK_regiq_query_template_parameter_schema_json", "ISJSON([ParameterSchema]) = 1");
        builder.HasCheckConstraint("CK_regiq_query_template_data_sources_json", "ISJSON([DataSourcesJson]) = 1");
        builder.HasIndex(x => x.TemplateCode).IsUnique().HasDatabaseName("UX_regiq_query_template_code");
        builder.HasIndex(x => new { x.IntentCode, x.IsActive }).HasDatabaseName("IX_regiq_query_template_intent");
        builder.HasData(RegIqSeedData.QueryTemplates);
    }
}

public sealed class RegIqEntityAliasConfiguration : IEntityTypeConfiguration<RegIqEntityAlias>
{
    public void Configure(EntityTypeBuilder<RegIqEntityAlias> builder)
    {
        builder.ToTable("regulatoriq_entity_aliases", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CanonicalName).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Alias).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(x => x.NormalizedAlias).HasColumnType("varchar(200)").HasMaxLength(200).IsRequired();
        builder.Property(x => x.AliasType).HasColumnType("varchar(30)").HasMaxLength(30).HasDefaultValue("NAME").IsRequired();
        builder.Property(x => x.LicenceCategory).HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
        builder.Property(x => x.RegulatorAgency).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.InstitutionType).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.HoldingCompanyName).HasColumnType("nvarchar(200)").HasMaxLength(200);
        builder.Property(x => x.GeoTag).HasColumnType("nvarchar(100)").HasMaxLength(100);
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasCheckConstraint("CK_regulatoriq_entity_aliases_alias_type", "[AliasType] IN ('NAME','ABBREVIATION','HOLDING_COMPANY','COMMON')");
        builder.HasIndex(x => new { x.NormalizedAlias, x.IsActive }).HasDatabaseName("IX_regulatoriq_entity_aliases_lookup");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("IX_regulatoriq_entity_aliases_tenant");
        builder.HasIndex(x => new { x.RegulatorAgency, x.LicenceCategory }).HasDatabaseName("IX_regulatoriq_entity_aliases_regulator");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.SetNull);
        builder.HasData(RegIqSeedData.EntityAliases);
    }
}

public sealed class RegIqAccessLogConfiguration : IEntityTypeConfiguration<RegIqAccessLog>
{
    public void Configure(EntityTypeBuilder<RegIqAccessLog> builder)
    {
        builder.ToTable("regiq_access_log", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RegulatorId).HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.RegulatorAgency).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.RegulatorRole).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.QueryText).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ResponseSummary).HasColumnType("nvarchar(1000)").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ClassificationLevel).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.EntitiesAccessedJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DataSourcesAccessedJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.FilterContextJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.IpAddress).HasColumnType("varchar(45)").HasMaxLength(45);
        builder.Property(x => x.SessionId).HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.AccessedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.RetainUntil).HasColumnType("datetime2(3)").IsRequired();
        builder.HasCheckConstraint("CK_regiq_access_log_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
        builder.HasCheckConstraint("CK_regiq_access_log_entities_json", "ISJSON([EntitiesAccessedJson]) = 1");
        builder.HasCheckConstraint("CK_regiq_access_log_data_sources_json", "ISJSON([DataSourcesAccessedJson]) = 1");
        builder.HasIndex(x => new { x.RegulatorTenantId, x.AccessedAt }).HasDatabaseName("IX_regiq_access_log_regulator");
        builder.HasIndex(x => x.PrimaryEntityTenantId).HasDatabaseName("IX_regiq_access_log_primary_entity");
        builder.HasIndex(x => x.RetainUntil).HasDatabaseName("IX_regiq_access_log_retention");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.RegulatorTenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
