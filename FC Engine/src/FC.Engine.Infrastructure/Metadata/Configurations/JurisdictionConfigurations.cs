using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class JurisdictionConfiguration : IEntityTypeConfiguration<Jurisdiction>
{
    public void Configure(EntityTypeBuilder<Jurisdiction> builder)
    {
        builder.ToTable("jurisdictions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.CountryName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Timezone).HasMaxLength(100).IsRequired();
        builder.Property(x => x.RegulatoryBodies).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DateFormat).HasMaxLength(20).IsRequired().HasDefaultValue("dd/MM/yyyy");
        builder.Property(x => x.DataProtectionLaw).HasMaxLength(100);
        builder.Property(x => x.DataResidencyRegion).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(false);

        builder.HasIndex(x => x.CountryCode).IsUnique();
    }
}

public class JurisdictionFxRateConfiguration : IEntityTypeConfiguration<JurisdictionFxRate>
{
    public void Configure(EntityTypeBuilder<JurisdictionFxRate> builder)
    {
        builder.ToTable("jurisdiction_fx_rates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.QuoteCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Rate).HasColumnType("decimal(18,8)").IsRequired();
        builder.Property(x => x.RateDate).HasConversion<DateOnlyConverter, DateOnlyComparer>();
        builder.Property(x => x.Source).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => new { x.BaseCurrency, x.QuoteCurrency, x.RateDate }).IsUnique();
    }
}

public class ConsolidationAdjustmentConfiguration : IEntityTypeConfiguration<ConsolidationAdjustment>
{
    public void Configure(EntityTypeBuilder<ConsolidationAdjustment> builder)
    {
        builder.ToTable("consolidation_adjustments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AdjustmentType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired().HasDefaultValue("NGN");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.EffectiveDate).HasConversion<DateOnlyConverter, DateOnlyComparer>();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.SourceInstitution)
            .WithMany()
            .HasForeignKey(x => x.SourceInstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TargetInstitution)
            .WithMany()
            .HasForeignKey(x => x.TargetInstitutionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.EffectiveDate);
    }
}

public class FieldLocalisationConfiguration : IEntityTypeConfiguration<FieldLocalisation>
{
    public void Configure(EntityTypeBuilder<FieldLocalisation> builder)
    {
        builder.ToTable("field_localisations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FieldId).IsRequired();
        builder.Property(x => x.LanguageCode).HasMaxLength(5).IsRequired();
        builder.Property(x => x.LocalisedLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LocalisedHelpText).HasMaxLength(500);

        builder.HasIndex(x => new { x.FieldId, x.LanguageCode }).IsUnique();

        builder.HasOne(x => x.Field)
            .WithMany()
            .HasForeignKey(x => x.FieldId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

