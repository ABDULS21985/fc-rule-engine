namespace FC.Engine.Domain.Entities;

public class JurisdictionFxRate
{
    public int Id { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string QuoteCurrency { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public DateOnly RateDate { get; set; }
    public string Source { get; set; } = "Manual";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
