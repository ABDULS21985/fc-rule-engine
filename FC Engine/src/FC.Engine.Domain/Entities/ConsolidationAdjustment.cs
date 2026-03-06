namespace FC.Engine.Domain.Entities;

public class ConsolidationAdjustment
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public int? SourceInstitutionId { get; set; }
    public int? TargetInstitutionId { get; set; }
    public string AdjustmentType { get; set; } = "Elimination";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "NGN";
    public string? Description { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Institution? SourceInstitution { get; set; }
    public Institution? TargetInstitution { get; set; }
}
