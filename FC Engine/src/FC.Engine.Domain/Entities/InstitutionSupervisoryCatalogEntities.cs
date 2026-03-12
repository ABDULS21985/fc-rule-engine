namespace FC.Engine.Domain.Entities;

public class InstitutionSupervisoryScorecardRecord
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public int OverdueObligations { get; set; }
    public int DueSoonObligations { get; set; }
    public decimal? CapitalScore { get; set; }
    public int OpenResilienceIncidents { get; set; }
    public int OpenSecurityAlerts { get; set; }
    public int ModelReviewItems { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class InstitutionSupervisoryDetailRecord
{
    public int Id { get; set; }
    public int InstitutionId { get; set; }
    public Guid TenantId { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionCode { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public decimal? CapitalScore { get; set; }
    public string CapitalAlert { get; set; } = string.Empty;
    public int OverdueObligations { get; set; }
    public int DueSoonObligations { get; set; }
    public int OpenResilienceIncidents { get; set; }
    public int OpenSecurityAlerts { get; set; }
    public int ModelReviewItems { get; set; }
    public string TopObligationsJson { get; set; } = "[]";
    public string RecentSubmissionsJson { get; set; } = "[]";
    public string RecentActivityJson { get; set; } = "[]";
    public DateTime MaterializedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
