using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class LicenceTypeConfiguration : IEntityTypeConfiguration<LicenceType>
{
    public void Configure(EntityTypeBuilder<LicenceType> builder)
    {
        builder.ToTable("licence_types");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Code).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Regulator).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.Property(e => e.DisplayOrder).HasDefaultValue(0);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(e => e.Code).IsUnique();
    }
}
