using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("login_attempts", "meta");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Username).HasMaxLength(100).IsRequired();
        builder.Property(e => e.IpAddress).HasMaxLength(50);
        builder.Property(e => e.FailureReason).HasMaxLength(200);
        builder.HasIndex(e => new { e.Username, e.AttemptedAt });
        builder.HasIndex(e => e.TenantId);
    }
}

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens", "meta");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Token).HasMaxLength(128).IsRequired();
        builder.HasIndex(e => e.Token).IsUnique();
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
