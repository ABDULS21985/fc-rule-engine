using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class ModelInventoryDefinitionRecordConfiguration : IEntityTypeConfiguration<ModelInventoryDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<ModelInventoryDefinitionRecord> builder)
    {
        builder.ToTable("model_inventory_definitions", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ModelCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ModelName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.Tier).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Owner).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ReturnHint).HasMaxLength(120).IsRequired();
        builder.Property(x => x.MatchTermsJson).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.ModelCode).IsUnique();
        builder.HasIndex(x => x.Tier);
        builder.HasIndex(x => x.MaterializedAt);
    }
}
