using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class ReturnDraftConfiguration : IEntityTypeConfiguration<ReturnDraft>
{
    public void Configure(EntityTypeBuilder<ReturnDraft> builder)
    {
        builder.ToTable("return_drafts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.InstitutionId).IsRequired();
        builder.Property(x => x.ReturnCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Period).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DataJson).IsRequired();
        builder.Property(x => x.LastSavedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.SavedBy).HasMaxLength(200).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.InstitutionId, x.ReturnCode, x.Period })
            .IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.LastSavedAt });
    }
}
