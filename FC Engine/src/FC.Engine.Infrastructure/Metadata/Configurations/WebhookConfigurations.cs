using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.Url).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(200);
        builder.Property(e => e.SecretKey).HasMaxLength(128).IsRequired();
        builder.Property(e => e.EventTypes).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(e => e.DisabledReason).HasMaxLength(200);

        builder.HasIndex(e => e.TenantId);
        builder.HasIndex(e => new { e.TenantId, e.IsActive });
    }
}

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.EventType).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Payload).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(d => d.ResponseBody).HasColumnType("nvarchar(max)");
        builder.Property(d => d.Status).HasMaxLength(20).IsRequired();

        builder.HasOne(d => d.Endpoint)
            .WithMany()
            .HasForeignKey(d => d.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.Status, d.NextRetryAt });
        builder.HasIndex(d => new { d.EndpointId, d.CreatedAt });
    }
}
