using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class SanctionsStrDraftRecordConfiguration : IEntityTypeConfiguration<SanctionsStrDraftRecord>
{
    public void Configure(EntityTypeBuilder<SanctionsStrDraftRecord> builder)
    {
        builder.ToTable("sanctions_str_drafts", "meta");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DraftId).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Subject).HasMaxLength(240).IsRequired();
        builder.Property(x => x.MatchedName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.SourceCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.SourceName).HasMaxLength(240).IsRequired();
        builder.Property(x => x.RiskLevel).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Decision).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Priority).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ScorePercent).HasColumnType("decimal(9,2)").IsRequired();
        builder.Property(x => x.FreezeRecommended).IsRequired();
        builder.Property(x => x.ScreenedAtUtc).IsRequired();
        builder.Property(x => x.ReviewDueAtUtc).IsRequired();
        builder.Property(x => x.SuspicionBasis).HasMaxLength(1600).IsRequired();
        builder.Property(x => x.GoAmlPayloadSummary).HasMaxLength(1600).IsRequired();
        builder.Property(x => x.Narrative).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.RecommendedActionsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.MaterializedAt).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.DraftId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.Priority);
        builder.HasIndex(x => x.MaterializedAt);
    }
}
