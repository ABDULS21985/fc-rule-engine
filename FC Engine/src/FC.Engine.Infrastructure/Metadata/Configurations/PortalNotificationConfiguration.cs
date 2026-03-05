using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class PortalNotificationConfiguration : IEntityTypeConfiguration<PortalNotification>
{
    public void Configure(EntityTypeBuilder<PortalNotification> builder)
    {
        builder.ToTable("portal_notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.TenantId).IsRequired();

        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(1000).IsRequired();
        builder.Property(n => n.Link).HasMaxLength(500);
        builder.Ignore(n => n.EventType);
        builder.Ignore(n => n.Channel);
        builder.Ignore(n => n.Priority);
        builder.Ignore(n => n.RecipientEmail);
        builder.Ignore(n => n.RecipientPhone);
        builder.Property(n => n.Metadata).HasColumnName("MetadataJson").HasMaxLength(4000);
        builder.Property(n => n.Type).HasMaxLength(30).IsRequired()
            .HasConversion<string>();

        // Primary query: user's notifications ordered by date
        builder.HasIndex(n => new { n.InstitutionId, n.UserId, n.IsRead, n.CreatedAt })
            .HasDatabaseName("IX_PortalNotification_UserQuery");

        builder.HasIndex(n => n.TenantId);
        builder.HasIndex(n => new { n.TenantId, n.CreatedAt });
    }
}
