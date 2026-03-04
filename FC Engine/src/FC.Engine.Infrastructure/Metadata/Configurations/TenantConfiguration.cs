using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.TenantId);

        builder.Property(t => t.TenantId)
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        builder.Property(t => t.TenantName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.TenantSlug)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.TenantStatus)
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue("PendingActivation");

        builder.Property(t => t.ContactEmail)
            .HasMaxLength(255);

        builder.Property(t => t.ContactPhone)
            .HasMaxLength(50);

        builder.Property(t => t.CreatedAt)
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property(t => t.UpdatedAt)
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(t => t.TenantSlug).IsUnique();

        builder.HasMany(t => t.Institutions)
            .WithOne(i => i.Tenant)
            .HasForeignKey(i => i.TenantId);
    }
}
