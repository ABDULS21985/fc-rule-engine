using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public class RegulatoryJurisdictionConfiguration : IEntityTypeConfiguration<RegulatoryJurisdiction>
{
    public void Configure(EntityTypeBuilder<RegulatoryJurisdiction> builder)
    {
        builder.ToTable("regulatory_jurisdictions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JurisdictionCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CountryName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.RegulatorCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.RegulatorName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CurrencySymbol).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RegulatoryFramework).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.JurisdictionCode).IsUnique();
        builder.HasIndex(x => x.RegulatorCode).IsUnique();
    }
}

public class FinancialGroupConfiguration : IEntityTypeConfiguration<FinancialGroup>
{
    public void Configure(EntityTypeBuilder<FinancialGroup> builder)
    {
        builder.ToTable("financial_groups");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.GroupCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.GroupName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.HeadquarterJurisdiction).HasMaxLength(3).IsRequired();
        builder.Property(x => x.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.GroupCode).IsUnique();

        builder.HasOne(x => x.HeadquarterJurisdictionNav)
            .WithMany()
            .HasForeignKey(x => x.HeadquarterJurisdiction)
            .HasPrincipalKey(j => j.JurisdictionCode)
            .OnDelete(DeleteBehavior.NoAction);
    }
}

public class GroupSubsidiaryConfiguration : IEntityTypeConfiguration<GroupSubsidiary>
{
    public void Configure(EntityTypeBuilder<GroupSubsidiary> builder)
    {
        builder.ToTable("group_subsidiaries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JurisdictionCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.SubsidiaryCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SubsidiaryName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LocalCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.OwnershipPercentage).HasPrecision(5, 2);
        builder.Property(x => x.ConsolidationMethod)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ConsolidationMethod.Full)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Group)
            .WithMany(g => g.Subsidiaries)
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.JurisdictionNav)
            .WithMany()
            .HasForeignKey(x => x.JurisdictionCode)
            .HasPrincipalKey(j => j.JurisdictionCode)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.GroupId, x.SubsidiaryCode }).IsUnique();
        builder.HasIndex(x => new { x.GroupId, x.JurisdictionCode });
    }
}

public class RegulatoryEquivalenceMappingConfiguration : IEntityTypeConfiguration<RegulatoryEquivalenceMapping>
{
    public void Configure(EntityTypeBuilder<RegulatoryEquivalenceMapping> builder)
    {
        builder.ToTable("regulatory_equivalence_mappings");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.MappingCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.MappingName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ConceptDomain).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => new { x.MappingCode, x.Version }).IsUnique();
    }
}

public class EquivalenceMappingEntryConfiguration : IEntityTypeConfiguration<EquivalenceMappingEntry>
{
    public void Configure(EntityTypeBuilder<EquivalenceMappingEntry> builder)
    {
        builder.ToTable("equivalence_mapping_entries");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JurisdictionCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.RegulatorCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.LocalParameterCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.LocalParameterName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LocalThreshold).HasPrecision(18, 6);
        builder.Property(x => x.ThresholdUnit).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CalculationBasis).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ReturnFormCode).HasMaxLength(30);
        builder.Property(x => x.ReturnLineReference).HasMaxLength(60);
        builder.Property(x => x.RegulatoryFramework).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Notes).HasColumnType("nvarchar(max)");

        builder.HasOne(x => x.Mapping)
            .WithMany(m => m.Entries)
            .HasForeignKey(x => x.MappingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.JurisdictionNav)
            .WithMany()
            .HasForeignKey(x => x.JurisdictionCode)
            .HasPrincipalKey(j => j.JurisdictionCode)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.MappingId, x.JurisdictionCode }).IsUnique();
        builder.HasIndex(x => x.MappingId);
    }
}

public class CrossBorderFxRateConfiguration : IEntityTypeConfiguration<CrossBorderFxRate>
{
    public void Configure(EntityTypeBuilder<CrossBorderFxRate> builder)
    {
        builder.ToTable("cross_border_fx_rates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.QuoteCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Rate).HasPrecision(18, 8);
        builder.Property(x => x.InverseRate).HasPrecision(18, 8);
        builder.Property(x => x.RateSource).HasMaxLength(40).IsRequired();
        builder.Property(x => x.RateType)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(FxRateType.PeriodEnd)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => new { x.BaseCurrency, x.QuoteCurrency, x.RateDate, x.RateType }).IsUnique();
        builder.HasIndex(x => x.RateDate).IsDescending();
        builder.HasIndex(x => new { x.BaseCurrency, x.RateDate }).IsDescending();
    }
}

public class ConsolidationRunConfiguration : IEntityTypeConfiguration<ConsolidationRun>
{
    public void Configure(EntityTypeBuilder<ConsolidationRun> builder)
    {
        builder.ToTable("consolidation_runs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReportingPeriod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.BaseCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(ConsolidationRunStatus.Pending)
            .IsRequired();
        builder.Property(x => x.ConsolidatedTotalAssets).HasPrecision(18, 2);
        builder.Property(x => x.ConsolidatedTotalCapital).HasPrecision(18, 2);
        builder.Property(x => x.ConsolidatedCAR).HasPrecision(8, 4);
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.GroupId, x.RunNumber }).IsUnique();
        builder.HasIndex(x => new { x.GroupId, x.ReportingPeriod });
    }
}

public class ConsolidationSubsidiarySnapshotConfiguration : IEntityTypeConfiguration<ConsolidationSubsidiarySnapshot>
{
    public void Configure(EntityTypeBuilder<ConsolidationSubsidiarySnapshot> builder)
    {
        builder.ToTable("consolidation_subsidiary_snapshots");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JurisdictionCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.LocalCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.LocalTotalAssets).HasPrecision(18, 2);
        builder.Property(x => x.LocalTotalLiabilities).HasPrecision(18, 2);
        builder.Property(x => x.LocalTotalCapital).HasPrecision(18, 2);
        builder.Property(x => x.LocalRWA).HasPrecision(18, 2);
        builder.Property(x => x.LocalCAR).HasPrecision(8, 4);
        builder.Property(x => x.LocalLCR).HasPrecision(8, 4);
        builder.Property(x => x.LocalNSFR).HasPrecision(8, 4);
        builder.Property(x => x.FxRateUsed).HasPrecision(18, 8);
        builder.Property(x => x.FxRateSource).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ConvertedTotalAssets).HasPrecision(18, 2);
        builder.Property(x => x.ConvertedTotalLiabilities).HasPrecision(18, 2);
        builder.Property(x => x.ConvertedTotalCapital).HasPrecision(18, 2);
        builder.Property(x => x.ConvertedRWA).HasPrecision(18, 2);
        builder.Property(x => x.OwnershipPercentage).HasPrecision(5, 2);
        builder.Property(x => x.ConsolidationMethodUsed).HasMaxLength(20).IsRequired();
        builder.Property(x => x.AdjustedTotalAssets).HasPrecision(18, 2);
        builder.Property(x => x.AdjustedTotalCapital).HasPrecision(18, 2);
        builder.Property(x => x.AdjustedRWA).HasPrecision(18, 2);
        builder.Property(x => x.DataCollectedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Run)
            .WithMany(r => r.Snapshots)
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Subsidiary)
            .WithMany()
            .HasForeignKey(x => x.SubsidiaryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.RunId);
    }
}

public class GroupConsolidationAdjustmentConfiguration : IEntityTypeConfiguration<GroupConsolidationAdjustment>
{
    public void Configure(EntityTypeBuilder<GroupConsolidationAdjustment> builder)
    {
        builder.ToTable("group_consolidation_adjustments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AdjustmentType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DebitAccount).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreditAccount).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Run)
            .WithMany(r => r.Adjustments)
            .HasForeignKey(x => x.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AffectedSubsidiary)
            .WithMany()
            .HasForeignKey(x => x.AffectedSubsidiaryId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.RunId);
    }
}

public class CrossBorderDataFlowConfiguration : IEntityTypeConfiguration<CrossBorderDataFlow>
{
    public void Configure(EntityTypeBuilder<CrossBorderDataFlow> builder)
    {
        builder.ToTable("cross_border_data_flows");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FlowCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.FlowName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceJurisdiction).HasMaxLength(3).IsRequired();
        builder.Property(x => x.SourceReturnCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.SourceLineCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.TargetJurisdiction).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TargetReturnCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.TargetLineCode).HasMaxLength(60).IsRequired();
        builder.Property(x => x.TransformationType)
            .HasMaxLength(30)
            .HasConversion<string>()
            .HasDefaultValue(DataFlowTransformation.Direct)
            .IsRequired();
        builder.Property(x => x.TransformationFormula).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Group)
            .WithMany()
            .HasForeignKey(x => x.GroupId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.GroupId, x.FlowCode }).IsUnique();
        builder.HasIndex(x => new { x.SourceJurisdiction, x.SourceReturnCode });
    }
}

public class DataFlowExecutionConfiguration : IEntityTypeConfiguration<DataFlowExecution>
{
    public void Configure(EntityTypeBuilder<DataFlowExecution> builder)
    {
        builder.ToTable("data_flow_executions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReportingPeriod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.SourceValue).HasPrecision(18, 6);
        builder.Property(x => x.SourceCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.FxRateApplied).HasPrecision(18, 8);
        builder.Property(x => x.ConvertedValue).HasPrecision(18, 6);
        builder.Property(x => x.TargetValue).HasPrecision(18, 6);
        builder.Property(x => x.TargetCurrency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);
        builder.Property(x => x.ExecutedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Flow)
            .WithMany(f => f.Executions)
            .HasForeignKey(x => x.FlowId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.FlowId, x.ReportingPeriod });
        builder.HasIndex(x => x.CorrelationId);
    }
}

public class RegulatoryDivergenceConfiguration : IEntityTypeConfiguration<RegulatoryDivergence>
{
    public void Configure(EntityTypeBuilder<RegulatoryDivergence> builder)
    {
        builder.ToTable("regulatory_divergences");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConceptDomain).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DivergenceType)
            .HasMaxLength(30)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(x => x.SourceJurisdiction).HasMaxLength(3).IsRequired();
        builder.Property(x => x.AffectedJurisdictions).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PreviousValue).HasMaxLength(200);
        builder.Property(x => x.NewValue).HasMaxLength(200);
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Severity)
            .HasMaxLength(10)
            .HasConversion<string>()
            .IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(DivergenceStatus.Open)
            .IsRequired();
        builder.Property(x => x.DetectedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Mapping)
            .WithMany()
            .HasForeignKey(x => x.MappingId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.Status, x.Severity });
        builder.HasIndex(x => new { x.ConceptDomain, x.Status });
    }
}

public class DivergenceNotificationConfiguration : IEntityTypeConfiguration<DivergenceNotification>
{
    public void Configure(EntityTypeBuilder<DivergenceNotification> builder)
    {
        builder.ToTable("divergence_notifications");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.NotificationChannel).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SentAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.Divergence)
            .WithMany(d => d.Notifications)
            .HasForeignKey(x => x.DivergenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.GroupId, x.Status });
    }
}

public class AfcftaProtocolTrackingConfiguration : IEntityTypeConfiguration<AfcftaProtocolTracking>
{
    public void Configure(EntityTypeBuilder<AfcftaProtocolTracking> builder)
    {
        builder.ToTable("afcfta_protocol_tracking");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProtocolCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ProtocolName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(AfcftaProtocolStatus.Proposed)
            .IsRequired();
        builder.Property(x => x.ParticipatingJurisdictions).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Description).HasColumnType("nvarchar(max)");
        builder.Property(x => x.ImpactOnRegOS).HasColumnType("nvarchar(max)");
        builder.Property(x => x.LastUpdated).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.ProtocolCode).IsUnique();
    }
}

public class RegulatoryDeadlineConfiguration : IEntityTypeConfiguration<RegulatoryDeadline>
{
    public void Configure(EntityTypeBuilder<RegulatoryDeadline> builder)
    {
        builder.ToTable("regulatory_deadlines");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JurisdictionCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.RegulatorCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.ReturnCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ReturnName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ReportingPeriod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.LocalTimeZone).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Frequency).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .HasDefaultValue(DeadlineStatus.Upcoming)
            .IsRequired();
        builder.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasOne(x => x.JurisdictionNav)
            .WithMany()
            .HasForeignKey(x => x.JurisdictionCode)
            .HasPrincipalKey(j => j.JurisdictionCode)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.GroupId, x.DeadlineUtc });
        builder.HasIndex(x => new { x.JurisdictionCode, x.DeadlineUtc });
    }
}

public class HarmonisationAuditLogConfiguration : IEntityTypeConfiguration<HarmonisationAuditLog>
{
    public void Configure(EntityTypeBuilder<HarmonisationAuditLog> builder)
    {
        builder.ToTable("harmonisation_audit_log");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.JurisdictionCode).HasMaxLength(3);
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Detail).HasColumnType("nvarchar(max)");
        builder.Property(x => x.PerformedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        builder.HasIndex(x => x.GroupId);
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => x.PerformedAt).IsDescending();
    }
}
