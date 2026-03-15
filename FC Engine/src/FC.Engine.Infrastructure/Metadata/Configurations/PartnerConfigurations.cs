using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class PartnerConfigConfiguration : IEntityTypeConfiguration<PartnerConfig>
{
    public void Configure(EntityTypeBuilder<PartnerConfig> builder)
    {
        builder.ToTable("partner_configs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PartnerTier)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(PartnerTier.Silver);
        builder.Property(x => x.BillingModel)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(PartnerBillingModel.Direct);
        builder.Property(x => x.CommissionRate).HasColumnType("decimal(5,4)");
        builder.Property(x => x.WholesaleDiscount).HasColumnType("decimal(5,4)");
        builder.Property(x => x.MaxSubTenants).HasDefaultValue(10);
        builder.Property(x => x.AgreementVersion).HasMaxLength(20);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId).IsUnique();
    }
}

public class PartnerRevenueRecordConfiguration : IEntityTypeConfiguration<PartnerRevenueRecord>
{
    public void Configure(EntityTypeBuilder<PartnerRevenueRecord> builder)
    {
        builder.ToTable("partner_revenue_records");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PartnerTenantId).IsRequired();
        builder.Property(x => x.BillingModel)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(PartnerBillingModel.Direct);
        builder.Property(x => x.GrossAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CommissionRate).HasColumnType("decimal(5,4)");
        builder.Property(x => x.CommissionAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.WholesaleDiscountRate).HasColumnType("decimal(5,4)");
        builder.Property(x => x.WholesaleDiscountAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Invoice)
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.PartnerTenant)
            .WithMany()
            .HasForeignKey(x => x.PartnerTenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.PartnerTenantId);
        builder.HasIndex(x => new { x.PartnerTenantId, x.InvoiceId }).IsUnique();
    }
}

public class PartnerSupportTicketConfiguration : IEntityTypeConfiguration<PartnerSupportTicket>
{
    public void Configure(EntityTypeBuilder<PartnerSupportTicket> builder)
    {
        builder.ToTable("partner_support_tickets");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.PartnerTenantId).IsRequired();
        builder.Property(x => x.RaisedByUserName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Priority)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(PartnerSupportTicketPriority.Normal);
        builder.Property(x => x.Priority)
            .HasSentinel((PartnerSupportTicketPriority)(-1));
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(PartnerSupportTicketStatus.Open);
        builder.Property(x => x.EscalationLevel).HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.PartnerTenantId);
        builder.HasIndex(x => new { x.PartnerTenantId, x.Status, x.Priority });
    }
}
