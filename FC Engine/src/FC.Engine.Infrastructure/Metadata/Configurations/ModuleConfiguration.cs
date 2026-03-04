using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.ToTable("modules");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.ModuleCode).HasMaxLength(30).IsRequired();
        builder.Property(e => e.ModuleName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.RegulatorCode).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.SheetCount).HasDefaultValue(0);
        builder.Property(e => e.DefaultFrequency).HasMaxLength(20).HasDefaultValue("Monthly");
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.Property(e => e.DisplayOrder).HasDefaultValue(0);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(e => e.ModuleCode).IsUnique();
    }
}
