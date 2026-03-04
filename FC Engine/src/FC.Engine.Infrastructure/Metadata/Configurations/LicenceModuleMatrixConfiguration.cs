using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class LicenceModuleMatrixConfiguration : IEntityTypeConfiguration<LicenceModuleMatrix>
{
    public void Configure(EntityTypeBuilder<LicenceModuleMatrix> builder)
    {
        builder.ToTable("licence_module_matrix");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.IsRequired).HasDefaultValue(false);
        builder.Property(e => e.IsOptional).HasDefaultValue(true);

        builder.HasOne(e => e.LicenceType)
            .WithMany(lt => lt.LicenceModuleEntries)
            .HasForeignKey(e => e.LicenceTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Module)
            .WithMany(m => m.LicenceModuleEntries)
            .HasForeignKey(e => e.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.LicenceTypeId, e.ModuleId }).IsUnique();
    }
}
