namespace FC.Engine.Domain.Entities;

public class DataPipelineDefinition
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public Guid SourceDataSourceId { get; set; }
    public Guid TargetDataSourceId { get; set; }
    public bool SourceTlsEnabled { get; set; } = true;
    public bool TargetTlsEnabled { get; set; } = true;
    public bool IsApproved { get; set; }
    public long? MemoryLimitRows { get; set; }
    public string UpstreamPipelineIdsJson { get; set; } = "[]";
    public string? MetadataJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class DataPipelineExecution
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PipelineId { get; set; }
    public Guid SourceDataSourceId { get; set; }
    public Guid TargetDataSourceId { get; set; }
    public bool SourceTlsEnabled { get; set; }
    public bool TargetTlsEnabled { get; set; }
    public bool IsApproved { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Phase { get; set; }
    public string SourceTablesJson { get; set; } = "[]";
    public string TargetTablesJson { get; set; } = "[]";
    public long ProcessedRows { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
