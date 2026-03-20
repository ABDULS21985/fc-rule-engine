using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class InstitutionUserConfiguration : IEntityTypeConfiguration<InstitutionUser>
{
    public void Configure(EntityTypeBuilder<InstitutionUser> builder)
    {
        builder.ToTable("institution_users", "meta");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.TenantId).IsRequired();

        builder.Property(e => e.Username).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Email).HasMaxLength(256).IsRequired();
        builder.Property(e => e.PhoneNumber).HasMaxLength(32);
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(e => e.PermissionOverridesJson);
        builder.Property(e => e.PreferredLanguage).HasMaxLength(10).IsRequired().HasDefaultValue("en");
        builder.Property(e => e.Role).HasMaxLength(20).IsRequired()
            .HasConversion<string>();
        builder.Property(e => e.LastLoginIp).HasMaxLength(45);
        builder.Property(e => e.DeletionReason).HasMaxLength(300);
        builder.Property(e => e.FailedLoginAttempts).HasDefaultValue(0);

        // Indexes
        builder.HasIndex(e => e.Username).IsUnique();
        builder.HasIndex(e => e.Email);
        builder.HasIndex(e => e.InstitutionId);
        builder.HasIndex(e => e.TenantId);

        // Relationships
        builder.HasOne(e => e.Institution)
            .WithMany(i => i.Users)
            .HasForeignKey(e => e.InstitutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
