namespace FC.Engine.Domain.Entities;

public class ReturnPeriod
{
    public int Id { get; set; }

    /// <summary>FK to Tenant for RLS.</summary>
    public Guid TenantId { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }
    public string Frequency { get; set; } = string.Empty;
    public DateTime ReportingDate { get; set; }
    public bool IsOpen { get; set; }
    public DateTime CreatedAt { get; set; }
}
