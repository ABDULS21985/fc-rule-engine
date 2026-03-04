using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class TenantLicenceTypeConfiguration : IEntityTypeConfiguration<TenantLicenceType>
{
    public void Configure(EntityTypeBuilder<TenantLicenceType> builder)
    {
        builder.ToTable("tenant_licence_types");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.LicenceTypeId).IsRequired();
        builder.Property(e => e.RegistrationNumber).HasMaxLength(100);
        builder.Property(e => e.EffectiveDate).IsRequired();
        builder.Property(e => e.IsActive).HasDefaultValue(true);

        builder.HasOne(e => e.Tenant)
            .WithMany(t => t.TenantLicenceTypes)
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.LicenceType)
            .WithMany(lt => lt.TenantLicenceTypes)
            .HasForeignKey(e => e.LicenceTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.LicenceTypeId }).IsUnique();
        builder.HasIndex(e => e.TenantId);
    }
}
