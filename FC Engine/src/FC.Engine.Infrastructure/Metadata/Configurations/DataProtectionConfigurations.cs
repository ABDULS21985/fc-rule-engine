using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class DataSourceRegistrationConfiguration : IEntityTypeConfiguration<DataSourceRegistration>
{
    public void Configure(EntityTypeBuilder<DataSourceRegistration> builder)
    {
        builder.ToTable("data_source_registrations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ConnectionIdentifier).HasMaxLength(200);
        builder.Property(x => x.FilesystemRootPath).HasMaxLength(500);
        builder.Property(x => x.SchemaJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
        builder.Property(x => x.PostureScore).HasPrecision(5, 2);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.SourceName }).IsUnique();
    }
}

public class CyberAssetConfiguration : IEntityTypeConfiguration<CyberAsset>
{
    public void Configure(EntityTypeBuilder<CyberAsset> builder)
    {
        builder.ToTable("cyber_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AssetKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AssetType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Criticality).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DataClassificationsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AssetKey }).IsUnique();
    }
}

public class CyberAssetDependencyConfiguration : IEntityTypeConfiguration<CyberAssetDependency>
{
    public void Configure(EntityTypeBuilder<CyberAssetDependency> builder)
    {
        builder.ToTable("cyber_asset_dependencies");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AssetId, x.DependsOnAssetId }).IsUnique();
    }
}

public class DataPipelineDefinitionConfiguration : IEntityTypeConfiguration<DataPipelineDefinition>
{
    public void Configure(EntityTypeBuilder<DataPipelineDefinition> builder)
    {
        builder.ToTable("data_pipeline_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PipelineName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.UpstreamPipelineIdsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.PipelineName }).IsUnique();
    }
}

public class DataPipelineExecutionConfiguration : IEntityTypeConfiguration<DataPipelineExecution>
{
    public void Configure(EntityTypeBuilder<DataPipelineExecution> builder)
    {
        builder.ToTable("data_pipeline_executions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Phase).HasMaxLength(100);
        builder.Property(x => x.SourceTablesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.TargetTablesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");
        builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.PipelineId, x.StartedAt });
    }
}

public class DspmScanRecordConfiguration : IEntityTypeConfiguration<DspmScanRecord>
{
    public void Configure(EntityTypeBuilder<DspmScanRecord> builder)
    {
        builder.ToTable("dspm_scan_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Trigger).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ScopeTablesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.PostureScore).HasPrecision(5, 2);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.SourceDataSourceId, x.StartedAt });
    }
}

public class DspmColumnFindingConfiguration : IEntityTypeConfiguration<DspmColumnFinding>
{
    public void Configure(EntityTypeBuilder<DspmColumnFinding> builder)
    {
        builder.ToTable("dspm_column_findings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TableName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ColumnName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DataType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DetectedPiiTypesJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.PrimaryPiiType).HasMaxLength(50);
        builder.Property(x => x.Sensitivity).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ComplianceTagsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.PreviousSensitivity).HasMaxLength(30);
        builder.HasIndex(x => x.ScanId);
    }
}

public class ShadowCopyRecordConfiguration : IEntityTypeConfiguration<ShadowCopyRecord>
{
    public void Configure(EntityTypeBuilder<ShadowCopyRecord> builder)
    {
        builder.ToTable("shadow_copy_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceTable).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TargetTable).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DetectionType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Fingerprint).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SimilarityScore).HasPrecision(5, 2);
        builder.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.SourceDataSourceId, x.TargetDataSourceId, x.SourceTable, x.TargetTable });
    }
}

public class SecurityAlertConfiguration : IEntityTypeConfiguration<SecurityAlert>
{
    public void Configure(EntityTypeBuilder<SecurityAlert> builder)
    {
        builder.ToTable("security_alerts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AlertType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.AffectedAssetIdsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(100);
        builder.Property(x => x.Username).HasMaxLength(200);
        builder.Property(x => x.SourceIp).HasMaxLength(64);
        builder.Property(x => x.MitreTechnique).HasMaxLength(32);
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AlertType, x.CreatedAt });
    }
}

public class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("security_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventSource).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(100);
        builder.Property(x => x.Username).HasMaxLength(200);
        builder.Property(x => x.SourceIp).HasMaxLength(64);
        builder.Property(x => x.MitreTechnique).HasMaxLength(32);
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RelatedEntityType).HasMaxLength(100);
        builder.Property(x => x.RelatedEntityId).HasMaxLength(100);
        builder.Property(x => x.EvidenceJson).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
        builder.HasIndex(x => new { x.TenantId, x.SourceIp });
        builder.HasIndex(x => new { x.TenantId, x.UserId });
    }
}

public class RootCauseAnalysisRecordConfiguration : IEntityTypeConfiguration<RootCauseAnalysisRecord>
{
    public void Configure(EntityTypeBuilder<RootCauseAnalysisRecord> builder)
    {
        builder.ToTable("root_cause_analysis_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IncidentType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.RootCauseType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RootCauseSummary).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Confidence).HasPrecision(5, 2);
        builder.Property(x => x.TimelineJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.CausalChainJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ImpactJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RecommendationsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.ModelName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ModelType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExplainabilityMode).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.IncidentType, x.IncidentId }).IsUnique();
    }
}
