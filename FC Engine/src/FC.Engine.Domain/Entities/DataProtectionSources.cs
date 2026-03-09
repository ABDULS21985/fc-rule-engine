namespace FC.Engine.Domain.Entities;

public class DataSourceRegistration
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? ConnectionIdentifier { get; set; }
    public bool EncryptionAtRestEnabled { get; set; } = true;
    public bool TlsRequired { get; set; } = true;
    public string? FilesystemRootPath { get; set; }
    public string SchemaJson { get; set; } = "{\"tables\":[]}";
    public decimal PostureScore { get; set; } = 100m;
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastScannedAt { get; set; }
}

public class CyberAsset
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string AssetKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Criticality { get; set; } = "medium";
    public Guid? LinkedDataSourceId { get; set; }
    public string DataClassificationsJson { get; set; } = "[]";
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CyberAssetDependency
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid AssetId { get; set; }
    public Guid DependsOnAssetId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
