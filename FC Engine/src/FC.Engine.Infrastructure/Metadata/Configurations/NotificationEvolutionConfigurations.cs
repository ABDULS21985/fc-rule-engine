using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("email_templates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TemplateCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HtmlBody).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.PlainTextBody).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Variables).HasColumnType("nvarchar(max)");
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new { x.TemplateCode, x.TenantId }).IsUnique();
        builder.HasIndex(x => x.TenantId);
    }
}

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.InAppEnabled).HasDefaultValue(true);
        builder.Property(x => x.EmailEnabled).HasDefaultValue(true);
        builder.Property(x => x.SmsEnabled).HasDefaultValue(false);
        builder.Property(x => x.SmsQuietHoursStart).HasColumnType("time");
        builder.Property(x => x.SmsQuietHoursEnd).HasColumnType("time");

        builder.HasIndex(x => new { x.UserId, x.EventType }).IsUnique();
        builder.HasIndex(x => x.TenantId);
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.NotificationEventType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Channel).HasMaxLength(10).IsRequired().HasConversion<string>();
        builder.Property(x => x.RecipientAddress).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().HasConversion<string>();
        builder.Property(x => x.AttemptCount).HasDefaultValue(0);
        builder.Property(x => x.MaxAttempts).HasDefaultValue(3);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.Status, x.NextRetryAt });
        builder.HasIndex(x => new { x.RecipientId, x.CreatedAt });
    }
}
