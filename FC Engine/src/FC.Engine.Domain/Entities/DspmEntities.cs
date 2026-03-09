namespace FC.Engine.Domain.Entities;

public class DspmScanRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SourceDataSourceId { get; set; }
    public Guid? PipelineId { get; set; }
    public Guid? PipelineExecutionId { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = "running";
    public int FindingsCount { get; set; }
    public int NewPiiCount { get; set; }
    public int DriftCount { get; set; }
    public decimal PostureScore { get; set; }
    public bool EncryptionAtRestEnabled { get; set; } = true;
    public string ScopeTablesJson { get; set; } = "[]";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public class DspmColumnFinding
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string DetectedPiiTypesJson { get; set; } = "[]";
    public string? PrimaryPiiType { get; set; }
    public string Sensitivity { get; set; } = "Internal";
    public string ComplianceTagsJson { get; set; } = "[]";
    public bool IsNewPii { get; set; }
    public bool IsDrift { get; set; }
    public string? PreviousSensitivity { get; set; }
}

public class ShadowCopyRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SourceDataSourceId { get; set; }
    public Guid TargetDataSourceId { get; set; }
    public string SourceTable { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string DetectionType { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public decimal SimilarityScore { get; set; }
    public bool IsLegitimate { get; set; }
    public bool RequiresReview { get; set; }
    public string? EvidenceJson { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}
