namespace FC.Engine.Domain.Models;

public enum DataSensitivityLevel
{
    Public = 0,
    Internal = 1,
    Confidential = 2,
    Restricted = 3
}

public static class PiiCatalog
{
    public const string Email = "email";
    public const string Phone = "phone";
    public const string Name = "name";
    public const string Address = "address";
    public const string DateOfBirth = "dob";
    public const string Ssn = "ssn";
    public const string NationalId = "national_id";
    public const string Health = "health";
    public const string Medical = "medical";
    public const string CreditCard = "credit_card";
    public const string BankAccount = "bank_account";
    public const string Salary = "salary";
    public const string Credential = "credential";
    public const string Gender = "gender";
    public const string Ethnicity = "ethnicity";
    public const string Religion = "religion";
    public const string Biometric = "biometric";
    public const string IpAddress = "ip_address";
    public const string Bvn = "bvn";

    public static readonly IReadOnlyList<string> AllTypes =
    [
        Email,
        Phone,
        Name,
        Address,
        DateOfBirth,
        Ssn,
        NationalId,
        Health,
        Medical,
        CreditCard,
        BankAccount,
        Salary,
        Credential,
        Gender,
        Ethnicity,
        Religion,
        Biometric,
        IpAddress,
        Bvn
    ];
}

public sealed class ComplianceTag
{
    public string Framework { get; set; } = string.Empty;
    public string Article { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Requirement { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
}

public sealed class DataSourceSchema
{
    public List<DataTableSchema> Tables { get; set; } = [];
}

public sealed class DataTableSchema
{
    public string TableName { get; set; } = string.Empty;
    public string? SchemaName { get; set; }
    public List<DataColumnSchema> Columns { get; set; } = [];
}

public sealed class DataColumnSchema
{
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public List<string> SampleValues { get; set; } = [];
}

public sealed class DataSourceRegistrationRequest
{
    public Guid? SourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? ConnectionIdentifier { get; set; }
    public bool EncryptionAtRestEnabled { get; set; } = true;
    public bool TlsRequired { get; set; } = true;
    public string? FilesystemRootPath { get; set; }
    public DataSourceSchema Schema { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DataPipelineDefinitionRequest
{
    public Guid? PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public Guid SourceDataSourceId { get; set; }
    public Guid TargetDataSourceId { get; set; }
    public bool SourceTlsEnabled { get; set; } = true;
    public bool TargetTlsEnabled { get; set; } = true;
    public bool IsApproved { get; set; }
    public long? MemoryLimitRows { get; set; }
    public List<Guid> UpstreamPipelineIds { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class PipelineEventReport
{
    public Guid PipelineId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Phase { get; set; }
    public List<string> TargetTables { get; set; } = [];
    public List<string> SourceTables { get; set; } = [];
    public long ProcessedRows { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CyberAssetRegistrationRequest
{
    public Guid? AssetId { get; set; }
    public string AssetKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Criticality { get; set; } = "medium";
    public Guid? LinkedDataSourceId { get; set; }
    public List<string> DataClassifications { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SecurityAlertReport
{
    public Guid? AlertId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Guid> AffectedAssetIds { get; set; } = [];
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? SourceIp { get; set; }
    public string? MitreTechnique { get; set; }
    public Dictionary<string, string> Evidence { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SecurityEventReport
{
    public Guid? EventId { get; set; }
    public string EventSource { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public Guid? AlertId { get; set; }
    public Guid? AssetId { get; set; }
    public string? UserId { get; set; }
    public string? Username { get; set; }
    public string? SourceIp { get; set; }
    public string? MitreTechnique { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? RelatedEntityType { get; set; }
    public string? RelatedEntityId { get; set; }
    public DateTime? OccurredAt { get; set; }
    public Dictionary<string, string> Evidence { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DspmColumnClassification
{
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public List<string> DetectedPiiTypes { get; set; } = [];
    public DataSensitivityLevel Sensitivity { get; set; } = DataSensitivityLevel.Internal;
    public List<ComplianceTag> ComplianceTags { get; set; } = [];
    public bool IsNewPii { get; set; }
}

public sealed class DspmScanSummary
{
    public Guid ScanId { get; set; }
    public Guid SourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int FindingsCount { get; set; }
    public int NewPiiCount { get; set; }
    public int DriftCount { get; set; }
    public decimal PostureScore { get; set; }
    public bool EncryptionAtRestEnabled { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<DspmColumnClassification> Columns { get; set; } = [];
}

public sealed class ShadowCopyMatch
{
    public Guid ShadowCopyId { get; set; }
    public Guid SourceDataSourceId { get; set; }
    public Guid TargetDataSourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public string SourceTable { get; set; } = string.Empty;
    public string TargetTable { get; set; } = string.Empty;
    public string DetectionType { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public decimal SimilarityScore { get; set; }
    public bool IsLegitimate { get; set; }
    public bool RequiresReview { get; set; }
    public DateTime DetectedAt { get; set; }
}

public sealed class DspmAlertSummary
{
    public Guid AlertId { get; set; }
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? MitreTechnique { get; set; }
    public string? SourceIp { get; set; }
    public string? UserId { get; set; }
    public List<Guid> AffectedAssetIds { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}

public sealed class DataPipelineExecutionSummary
{
    public Guid ExecutionId { get; set; }
    public Guid PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Phase { get; set; }
    public string? ErrorMessage { get; set; }
    public long ProcessedRows { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class DataSourceSummary
{
    public Guid SourceId { get; set; }
    public string SourceName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public bool EncryptionAtRestEnabled { get; set; }
    public bool TlsRequired { get; set; }
    public decimal PostureScore { get; set; }
    public DateTime? LastScannedAt { get; set; }
}

public sealed class DataPipelineSummary
{
    public Guid PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public Guid SourceDataSourceId { get; set; }
    public Guid TargetDataSourceId { get; set; }
    public bool SourceTlsEnabled { get; set; }
    public bool TargetTlsEnabled { get; set; }
    public bool IsApproved { get; set; }
    public long? MemoryLimitRows { get; set; }
    public List<Guid> UpstreamPipelineIds { get; set; } = [];
}

public sealed class CyberAssetSummary
{
    public Guid AssetId { get; set; }
    public string AssetKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string Criticality { get; set; } = string.Empty;
    public Guid? LinkedDataSourceId { get; set; }
    public List<string> DataClassifications { get; set; } = [];
}
