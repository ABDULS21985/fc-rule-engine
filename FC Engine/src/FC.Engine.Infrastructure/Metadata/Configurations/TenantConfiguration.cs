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

        builder.Property(t => t.ParentTenantId);

        builder.Property(t => t.TenantType)
            .HasMaxLength(30)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(Domain.Enums.TenantType.Institution);

        builder.Property(t => t.Status)
            .HasColumnName("TenantStatus")
            .HasMaxLength(30)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(Domain.Enums.TenantStatus.PendingActivation);

        builder.Property(t => t.ContactEmail).HasMaxLength(255);
        builder.Property(t => t.ContactPhone).HasMaxLength(50);
        builder.Property(t => t.Address).HasMaxLength(500);
        builder.Property(t => t.TaxId).HasMaxLength(50);
        builder.Property(t => t.RcNumber).HasMaxLength(50);
        builder.Property(t => t.FiscalYearStartMonth).HasDefaultValue(1);
        builder.Property(t => t.Timezone).HasMaxLength(100).HasDefaultValue("Africa/Lagos");
        builder.Property(t => t.DefaultCurrency).HasMaxLength(3).HasDefaultValue("NGN");
        builder.Property(t => t.BrandingConfig).HasColumnType("nvarchar(max)");
        builder.Property(t => t.CustomDomain).HasMaxLength(255);
        builder.Property(t => t.MaxInstitutions).HasDefaultValue(1);
        builder.Property(t => t.MaxUsersPerEntity).HasDefaultValue(10);
        builder.Property(t => t.SuspensionReason).HasMaxLength(500);

        builder.Property(t => t.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(t => t.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(t => t.TenantSlug).IsUnique();
        builder.HasIndex(t => t.ParentTenantId);

        builder.HasMany(t => t.Institutions)
            .WithOne(i => i.Tenant)
            .HasForeignKey(i => i.TenantId);

        builder.HasMany(t => t.TenantLicenceTypes)
            .WithOne(tlt => tlt.Tenant)
            .HasForeignKey(tlt => tlt.TenantId);

        builder.HasMany(t => t.Subscriptions)
            .WithOne(s => s.Tenant)
            .HasForeignKey(s => s.TenantId);

        builder.HasOne(t => t.ParentTenant)
            .WithMany(t => t.ChildTenants)
            .HasForeignKey(t => t.ParentTenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.PartnerConfig)
            .WithOne(pc => pc.Tenant)
            .HasForeignKey<PartnerConfig>(pc => pc.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
