using FC.Engine.Domain.Enums;

namespace FC.Engine.Domain.Entities;

public class Invoice
{
    public int Id { get; set; }
    public Guid TenantId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int SubscriptionId { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatRate { get; set; } = 0.0750m;
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "NGN";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public DateTime? IssuedAt { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Subscription? Subscription { get; set; }
    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();

    public void RecalculateTotals()
    {
        Subtotal = LineItems.Sum(i => i.LineTotal);
        VatAmount = decimal.Round(Subtotal * VatRate, 2, MidpointRounding.AwayFromZero);
        TotalAmount = Subtotal + VatAmount;
    }

    public void Issue(DateOnly dueDate)
    {
        if (Status != InvoiceStatus.Draft)
            throw new InvalidOperationException($"Only draft invoices can be issued. Current status: {Status}");

        Status = InvoiceStatus.Issued;
        IssuedAt = DateTime.UtcNow;
        DueDate = dueDate;
    }

    public void MarkOverdue()
    {
        if (Status != InvoiceStatus.Issued)
            return;

        Status = InvoiceStatus.Overdue;
    }

    public void MarkPaid()
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Voided)
            throw new InvalidOperationException($"Cannot mark {Status} invoice as paid");

        Status = InvoiceStatus.Paid;
        PaidAt = DateTime.UtcNow;
    }

    public void Void(string reason)
    {
        if (Status is InvoiceStatus.Paid or InvoiceStatus.Voided)
            throw new InvalidOperationException($"Cannot void a {Status.ToString().ToLowerInvariant()} invoice");

        Status = InvoiceStatus.Voided;
        VoidedAt = DateTime.UtcNow;
        VoidReason = reason;
    }
}
