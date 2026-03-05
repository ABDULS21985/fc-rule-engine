using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Token).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedByIp).HasMaxLength(45);
        builder.Property(x => x.RevokedByIp).HasMaxLength(45);
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.IsRevoked).HasDefaultValue(false);
        builder.Property(x => x.IsUsed).HasDefaultValue(false);

        builder.HasIndex(x => x.Token).IsUnique();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.UserId, x.UserType });
    }
}

public class UserMfaConfigConfiguration : IEntityTypeConfiguration<UserMfaConfig>
{
    public void Configure(EntityTypeBuilder<UserMfaConfig> builder)
    {
        builder.ToTable("user_mfa_configs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SecretKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.BackupCodes).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.IsEnabled).HasDefaultValue(false);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.UserId, x.UserType }).IsUnique();
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PermissionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.PermissionCode).IsUnique();
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(200);
        builder.Property(x => x.IsSystemRole).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.RoleName }).IsUnique();
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(x => new { x.RoleId, x.PermissionId });

        builder.HasOne(x => x.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(x => x.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TenantSsoConfigConfiguration : IEntityTypeConfiguration<TenantSsoConfig>
{
    public void Configure(EntityTypeBuilder<TenantSsoConfig> builder)
    {
        builder.ToTable("tenant_sso_configs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.SsoEnabled).HasDefaultValue(false);
        builder.Property(x => x.IdpEntityId).HasMaxLength(500).IsRequired();
        builder.Property(x => x.IdpSsoUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.IdpSloUrl).HasMaxLength(500);
        builder.Property(x => x.IdpCertificate).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.SpEntityId).HasMaxLength(500).IsRequired();
        builder.Property(x => x.AttributeMapping).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DefaultRole).HasMaxLength(50).HasDefaultValue("Viewer");
        builder.Property(x => x.JitProvisioningEnabled).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId).IsUnique();
        builder.HasOne(x => x.Tenant)
            .WithOne(t => t.SsoConfig)
            .HasForeignKey<TenantSsoConfig>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.KeyHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.KeyPrefix).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Permissions).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RateLimitPerMinute).HasDefaultValue(100);
        builder.Property(x => x.LastUsedIp).HasMaxLength(45);
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.KeyPrefix);
        builder.HasIndex(x => x.ExpiresAt);
    }
}
