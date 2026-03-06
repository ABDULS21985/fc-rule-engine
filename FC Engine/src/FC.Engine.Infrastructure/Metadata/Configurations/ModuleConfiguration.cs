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
        builder.Property(e => e.JurisdictionId);
        builder.Property(e => e.ModuleCode).HasMaxLength(30).IsRequired();
        builder.Property(e => e.ModuleName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.RegulatorCode).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(1000);
        builder.Property(e => e.SheetCount).HasDefaultValue(0);
        builder.Property(e => e.DefaultFrequency).HasMaxLength(20).HasDefaultValue("Monthly");
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.Property(e => e.DisplayOrder).HasDefaultValue(0);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(e => new { e.ModuleCode, e.JurisdictionId }).IsUnique();

        builder.HasOne(e => e.Jurisdiction)
            .WithMany(j => j.Modules)
            .HasForeignKey(e => e.JurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ModuleVersionConfiguration : IEntityTypeConfiguration<ModuleVersion>
{
    public void Configure(EntityTypeBuilder<ModuleVersion> builder)
    {
        builder.ToTable("module_versions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.VersionCode).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Status).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ReleaseNotes);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(e => new { e.ModuleId, e.VersionCode }).IsUnique();

        builder.HasOne(e => e.Module)
            .WithMany(m => m.Versions)
            .HasForeignKey(e => e.ModuleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InterModuleDataFlowConfiguration : IEntityTypeConfiguration<InterModuleDataFlow>
{
    public void Configure(EntityTypeBuilder<InterModuleDataFlow> builder)
    {
        builder.ToTable("inter_module_data_flows");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.SourceTemplateCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.SourceFieldCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TargetModuleCode).HasMaxLength(30).IsRequired();
        builder.Property(e => e.TargetTemplateCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TargetFieldCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TransformationType).HasMaxLength(20).IsRequired();
        builder.Property(e => e.TransformFormula).HasMaxLength(500);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.IsActive).HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(e => new
        {
            e.SourceModuleId,
            e.SourceTemplateCode,
            e.SourceFieldCode,
            e.TargetModuleCode,
            e.TargetTemplateCode,
            e.TargetFieldCode
        }).IsUnique();

        builder.HasOne(e => e.SourceModule)
            .WithMany(m => m.OutboundDataFlows)
            .HasForeignKey(e => e.SourceModuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.TargetModule)
            .WithMany(m => m.InboundDataFlows)
            .HasPrincipalKey(m => m.ModuleCode)
            .HasForeignKey(e => e.TargetModuleCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
