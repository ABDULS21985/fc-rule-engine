using FC.Engine.Domain.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class ReturnTemplateConfiguration : IEntityTypeConfiguration<ReturnTemplate>
{
    public void Configure(EntityTypeBuilder<ReturnTemplate> builder)
    {
        builder.ToTable("return_templates", "meta");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.ReturnCode).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(255).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Frequency).HasMaxLength(20).IsRequired()
            .HasConversion<string>();
        builder.Property(t => t.StructuralCategory).HasMaxLength(20).IsRequired()
            .HasConversion<string>();
        builder.Property(t => t.PhysicalTableName).HasMaxLength(128).IsRequired();
        builder.Property(t => t.XmlRootElement).HasMaxLength(128).IsRequired();
        builder.Property(t => t.XmlNamespace).HasMaxLength(255).IsRequired();
        builder.Property(t => t.OwnerDepartment).HasMaxLength(50);
        builder.Property(t => t.InstitutionType).HasMaxLength(10);
        builder.Property(t => t.CreatedBy).HasMaxLength(100).IsRequired();
        builder.Property(t => t.UpdatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(t => t.ReturnCode).IsUnique();
        builder.HasIndex(t => t.PhysicalTableName).IsUnique();
        builder.HasIndex(t => t.TenantId);

        builder.HasOne(t => t.Module)
            .WithMany()
            .HasForeignKey(t => t.ModuleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(t => t.Versions)
            .WithOne()
            .HasForeignKey(v => v.TemplateId);
    }
}

public class TemplateVersionConfiguration : IEntityTypeConfiguration<TemplateVersion>
{
    public void Configure(EntityTypeBuilder<TemplateVersion> builder)
    {
        builder.ToTable("template_versions", "meta");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Status).HasMaxLength(20).IsRequired()
            .HasConversion<string>();
        builder.Property(v => v.ChangeSummary).HasMaxLength(1000);
        builder.Property(v => v.ApprovedBy).HasMaxLength(100);
        builder.Property(v => v.CreatedBy).HasMaxLength(100).IsRequired();

        builder.HasIndex(v => new { v.TemplateId, v.VersionNumber }).IsUnique();

        builder.HasMany(v => v.Fields)
            .WithOne()
            .HasForeignKey(f => f.TemplateVersionId);

        builder.HasMany(v => v.ItemCodes)
            .WithOne()
            .HasForeignKey(ic => ic.TemplateVersionId);

        builder.HasMany(v => v.IntraSheetFormulas)
            .WithOne()
            .HasForeignKey(f => f.TemplateVersionId);
    }
}

public class TemplateFieldConfiguration : IEntityTypeConfiguration<TemplateField>
{
    public void Configure(EntityTypeBuilder<TemplateField> builder)
    {
        builder.ToTable("template_fields", "meta");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.FieldName).HasMaxLength(128).IsRequired();
        builder.Property(f => f.DisplayName).HasMaxLength(255).IsRequired();
        builder.Property(f => f.XmlElementName).HasMaxLength(128).IsRequired();
        builder.Property(f => f.LineCode).HasMaxLength(20);
        builder.Property(f => f.SectionName).HasMaxLength(100);
        builder.Property(f => f.DataType).HasMaxLength(30).IsRequired()
            .HasConversion<string>();
        builder.Property(f => f.SqlType).HasMaxLength(50).IsRequired();
        builder.Property(f => f.DefaultValue).HasMaxLength(100);
        builder.Property(f => f.MinValue).HasMaxLength(100);
        builder.Property(f => f.MaxValue).HasMaxLength(100);
        builder.Property(f => f.ReferenceTable).HasMaxLength(128);
        builder.Property(f => f.ReferenceColumn).HasMaxLength(128);
        builder.Property(f => f.HelpText).HasMaxLength(500);

        builder.HasIndex(f => new { f.TemplateVersionId, f.FieldName }).IsUnique();
    }
}

public class TemplateItemCodeConfiguration : IEntityTypeConfiguration<TemplateItemCode>
{
    public void Configure(EntityTypeBuilder<TemplateItemCode> builder)
    {
        builder.ToTable("template_item_codes", "meta");
        builder.HasKey(ic => ic.Id);
        builder.Property(ic => ic.ItemCode).HasMaxLength(20).IsRequired();
        builder.Property(ic => ic.ItemDescription).HasMaxLength(255).IsRequired();

        builder.HasIndex(ic => new { ic.TemplateVersionId, ic.ItemCode }).IsUnique();
    }
}

public class TemplateSectionConfiguration : IEntityTypeConfiguration<TemplateSection>
{
    public void Configure(EntityTypeBuilder<TemplateSection> builder)
    {
        builder.ToTable("template_sections", "meta");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.SectionName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(500);

        builder.HasIndex(s => new { s.TemplateVersionId, s.SectionName }).IsUnique();
    }
}
