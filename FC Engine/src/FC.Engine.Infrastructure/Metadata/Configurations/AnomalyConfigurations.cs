using FC.Engine.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class AnomalyThresholdConfigConfiguration : IEntityTypeConfiguration<AnomalyThresholdConfig>
{
    public void Configure(EntityTypeBuilder<AnomalyThresholdConfig> builder)
    {
        var seedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("anomaly_threshold_config", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConfigKey).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConfigValue).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnType("datetime2(3)");
        builder.Property(x => x.EffectiveTo).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)");

        builder.HasIndex(x => new { x.ConfigKey, x.EffectiveFrom }).IsUnique();
        builder.HasIndex(x => x.ConfigKey)
            .HasFilter("[EffectiveTo] IS NULL")
            .HasDatabaseName("IX_anomaly_threshold_config_key_active");

        builder.HasData(
            new AnomalyThresholdConfig { Id = 1, ConfigKey = "zscore.alert_threshold", ConfigValue = 3.0m, Description = "Absolute z-score threshold for alert findings.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 2, ConfigKey = "zscore.warning_threshold", ConfigValue = 2.0m, Description = "Absolute z-score threshold for warning findings.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 3, ConfigKey = "zscore.info_threshold", ConfigValue = 1.5m, Description = "Absolute z-score threshold for informational findings.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 4, ConfigKey = "correlation.deviation_threshold", ConfigValue = 0.30m, Description = "Allowed percentage deviation from expected correlated value before a finding is raised.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 5, ConfigKey = "correlation.min_r_squared", ConfigValue = 0.60m, Description = "Minimum R-squared for learned correlation rules.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 6, ConfigKey = "temporal.jump_pct_alert", ConfigValue = 50.0m, Description = "Period-over-period percentage jump that maps to alert severity.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 7, ConfigKey = "temporal.jump_pct_warning", ConfigValue = 30.0m, Description = "Period-over-period percentage jump that maps to warning severity.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 8, ConfigKey = "temporal.min_periods", ConfigValue = 3m, Description = "Minimum historical periods required before temporal analysis activates.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 9, ConfigKey = "peer.iqr_multiplier", ConfigValue = 2.5m, Description = "IQR multiplier used to compute peer outlier fences.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 10, ConfigKey = "peer.min_peers", ConfigValue = 5m, Description = "Minimum peer submissions required before peer comparison activates.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 11, ConfigKey = "coldstart.min_observations", ConfigValue = 30m, Description = "Minimum observations required before a field uses learned statistics instead of rule baselines.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 12, ConfigKey = "quality.anomaly_weight_alert", ConfigValue = 10.0m, Description = "Penalty points for each unacknowledged alert finding.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 13, ConfigKey = "quality.anomaly_weight_warning", ConfigValue = 5.0m, Description = "Penalty points for each unacknowledged warning finding.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 14, ConfigKey = "quality.anomaly_weight_info", ConfigValue = 2.0m, Description = "Penalty points for each unacknowledged informational finding.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 15, ConfigKey = "quality.max_penalty", ConfigValue = 100.0m, Description = "Maximum capped quality penalty.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate },
            new AnomalyThresholdConfig { Id = 16, ConfigKey = "model.retraining_day_of_month", ConfigValue = 1m, Description = "Configured day of month for scheduled anomaly model retraining.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate, UpdatedAt = seedDate });
    }
}

public sealed class AnomalyModelVersionConfiguration : IEntityTypeConfiguration<AnomalyModelVersion>
{
    public void Configure(EntityTypeBuilder<AnomalyModelVersion> builder)
    {
        builder.ToTable("anomaly_model_versions", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.RegulatorCode).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.PromotedBy).HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.TrainingStartedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.TrainingCompletedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.PromotedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.RetiredAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasIndex(x => new { x.ModuleCode, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => new { x.ModuleCode, x.Status }).HasDatabaseName("IX_anomaly_model_versions_module_status");
    }
}

public sealed class AnomalyFieldModelConfiguration : IEntityTypeConfiguration<AnomalyFieldModel>
{
    public void Configure(EntityTypeBuilder<AnomalyFieldModel> builder)
    {
        builder.ToTable("anomaly_field_models", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.FieldCode).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.FieldLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DistributionType).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.MeanValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.StdDev).HasColumnType("decimal(28,8)");
        builder.Property(x => x.MedianValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.Q1Value).HasColumnType("decimal(28,8)");
        builder.Property(x => x.Q3Value).HasColumnType("decimal(28,8)");
        builder.Property(x => x.MinObserved).HasColumnType("decimal(28,8)");
        builder.Property(x => x.MaxObserved).HasColumnType("decimal(28,8)");
        builder.Property(x => x.Percentile05).HasColumnType("decimal(28,8)");
        builder.Property(x => x.Percentile95).HasColumnType("decimal(28,8)");
        builder.Property(x => x.RuleBasedMin).HasColumnType("decimal(28,8)");
        builder.Property(x => x.RuleBasedMax).HasColumnType("decimal(28,8)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(x => x.ModelVersion)
            .WithMany(x => x.FieldModels)
            .HasForeignKey(x => x.ModelVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ModelVersionId, x.FieldCode }).IsUnique();
        builder.HasIndex(x => new { x.ModuleCode, x.FieldCode }).HasDatabaseName("IX_anomaly_field_models_lookup");
    }
}

public sealed class AnomalyCorrelationRuleConfiguration : IEntityTypeConfiguration<AnomalyCorrelationRule>
{
    public void Configure(EntityTypeBuilder<AnomalyCorrelationRule> builder)
    {
        builder.ToTable("anomaly_correlation_rules", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.FieldCodeA).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.FieldLabelA).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FieldCodeB).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.FieldLabelB).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CorrelationCoefficient).HasColumnType("decimal(10,6)");
        builder.Property(x => x.RSquared).HasColumnType("decimal(10,6)");
        builder.Property(x => x.Slope).HasColumnType("decimal(18,8)");
        builder.Property(x => x.Intercept).HasColumnType("decimal(18,8)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(x => x.ModelVersion)
            .WithMany(x => x.CorrelationRules)
            .HasForeignKey(x => x.ModelVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ModelVersionId, x.FieldCodeA, x.FieldCodeB }).IsUnique();
        builder.HasIndex(x => new { x.ModuleCode, x.IsActive }).HasDatabaseName("IX_anomaly_correlation_rules_module_active");
    }
}

public sealed class AnomalyPeerGroupStatisticConfiguration : IEntityTypeConfiguration<AnomalyPeerGroupStatistic>
{
    public void Configure(EntityTypeBuilder<AnomalyPeerGroupStatistic> builder)
    {
        builder.ToTable("anomaly_peer_group_stats", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.FieldCode).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.LicenceCategory).HasColumnType("varchar(60)").HasMaxLength(60).IsRequired();
        builder.Property(x => x.InstitutionSizeBand).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.PeerMean).HasColumnType("decimal(28,8)");
        builder.Property(x => x.PeerMedian).HasColumnType("decimal(28,8)");
        builder.Property(x => x.PeerStdDev).HasColumnType("decimal(28,8)");
        builder.Property(x => x.PeerQ1).HasColumnType("decimal(28,8)");
        builder.Property(x => x.PeerQ3).HasColumnType("decimal(28,8)");
        builder.Property(x => x.PeerMin).HasColumnType("decimal(28,8)");
        builder.Property(x => x.PeerMax).HasColumnType("decimal(28,8)");
        builder.Property(x => x.PeriodCode).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(x => x.ModelVersion)
            .WithMany(x => x.PeerStatistics)
            .HasForeignKey(x => x.ModelVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ModelVersionId, x.FieldCode, x.LicenceCategory, x.PeriodCode }).IsUnique();
        builder.HasIndex(x => new { x.ModuleCode, x.LicenceCategory, x.PeriodCode }).HasDatabaseName("IX_anomaly_peer_group_stats_lookup");
    }
}

public sealed class AnomalyRuleBaselineConfiguration : IEntityTypeConfiguration<AnomalyRuleBaseline>
{
    public void Configure(EntityTypeBuilder<AnomalyRuleBaseline> builder)
    {
        var seedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("anomaly_rule_baselines", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RegulatorCode).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(40)").HasMaxLength(40);
        builder.Property(x => x.FieldCode).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.FieldLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.MinimumValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.MaximumValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.Notes).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasIndex(x => new { x.RegulatorCode, x.ModuleCode, x.FieldCode }).HasDatabaseName("IX_anomaly_rule_baselines_lookup");

        builder.HasData(
            new AnomalyRuleBaseline { Id = 1, RegulatorCode = "CBN", FieldCode = "carratio", FieldLabel = "Capital Adequacy Ratio", MinimumValue = 10m, MaximumValue = 50m, Notes = "CBN prudential baseline for capital adequacy.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 2, RegulatorCode = "CBN", FieldCode = "nplratio", FieldLabel = "Non-Performing Loan Ratio", MinimumValue = 0m, MaximumValue = 35m, Notes = "CBN prudential baseline for NPL ratio.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 3, RegulatorCode = "CBN", FieldCode = "liquidityratio", FieldLabel = "Liquidity Ratio", MinimumValue = 20m, MaximumValue = 80m, Notes = "CBN prudential baseline for liquidity ratio.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 4, RegulatorCode = "CBN", FieldCode = "loandepositratio", FieldLabel = "Loan to Deposit Ratio", MinimumValue = 30m, MaximumValue = 85m, Notes = "CBN prudential baseline for loan to deposit ratio.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 5, RegulatorCode = "CBN", FieldCode = "costincomeratio", FieldLabel = "Cost to Income Ratio", MinimumValue = 30m, MaximumValue = 90m, Notes = "CBN prudential baseline for cost to income ratio.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 6, RegulatorCode = "CBN", FieldCode = "roa", FieldLabel = "Return on Assets", MinimumValue = -5m, MaximumValue = 10m, Notes = "CBN prudential baseline for return on assets.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 7, RegulatorCode = "CBN", FieldCode = "roe", FieldLabel = "Return on Equity", MinimumValue = -20m, MaximumValue = 40m, Notes = "CBN prudential baseline for return on equity.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 8, RegulatorCode = "NDIC", FieldCode = "insureddeposits", FieldLabel = "Insured Deposits", MinimumValue = 0m, MaximumValue = 5000000000000m, Notes = "NDIC status return cold-start range.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 9, RegulatorCode = "NDIC", FieldCode = "depositpremiumdue", FieldLabel = "Deposit Insurance Premium Due", MinimumValue = 0m, MaximumValue = 50000000000m, Notes = "NDIC premium baseline.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 10, RegulatorCode = "NAICOM", FieldCode = "grosspremium", FieldLabel = "Gross Premium Written", MinimumValue = 0m, MaximumValue = 500000000000m, Notes = "NAICOM quarterly return baseline.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 11, RegulatorCode = "NAICOM", FieldCode = "combinedratio", FieldLabel = "Combined Ratio", MinimumValue = 30m, MaximumValue = 200m, Notes = "NAICOM combined ratio baseline.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 12, RegulatorCode = "NAICOM", FieldCode = "solvencymargin", FieldLabel = "Solvency Margin", MinimumValue = 100m, MaximumValue = 500m, Notes = "NAICOM solvency margin baseline.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 13, RegulatorCode = "SEC", FieldCode = "netcapital", FieldLabel = "Net Capital", MinimumValue = 0m, MaximumValue = 500000000000m, Notes = "SEC capital market operator baseline.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 14, RegulatorCode = "SEC", FieldCode = "liquidcapital", FieldLabel = "Liquid Capital", MinimumValue = 0m, MaximumValue = 500000000000m, Notes = "SEC liquid capital baseline.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 15, RegulatorCode = "SEC", FieldCode = "clientassetsaum", FieldLabel = "Client Assets Under Management", MinimumValue = 0m, MaximumValue = 10000000000000m, Notes = "SEC AUM baseline.", CreatedAt = seedDate },
            new AnomalyRuleBaseline { Id = 16, RegulatorCode = "SEC", FieldCode = "capitaladequacy", FieldLabel = "Capital Adequacy", MinimumValue = 10m, MaximumValue = 100m, Notes = "SEC capital adequacy ratio baseline.", CreatedAt = seedDate });
    }
}

public sealed class AnomalySeedCorrelationRuleConfiguration : IEntityTypeConfiguration<AnomalySeedCorrelationRule>
{
    public void Configure(EntityTypeBuilder<AnomalySeedCorrelationRule> builder)
    {
        var seedDate = new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("anomaly_seed_correlation_rules", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RegulatorCode).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(40)").HasMaxLength(40);
        builder.Property(x => x.FieldCodeA).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.FieldLabelA).HasMaxLength(200).IsRequired();
        builder.Property(x => x.FieldCodeB).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.FieldLabelB).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CorrelationCoefficient).HasColumnType("decimal(10,6)");
        builder.Property(x => x.RSquared).HasColumnType("decimal(10,6)");
        builder.Property(x => x.Slope).HasColumnType("decimal(18,8)");
        builder.Property(x => x.Intercept).HasColumnType("decimal(18,8)");
        builder.Property(x => x.Notes).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasIndex(x => new { x.RegulatorCode, x.ModuleCode, x.FieldCodeA, x.FieldCodeB })
            .HasDatabaseName("IX_anomaly_seed_correlation_rules_lookup");

        builder.HasData(
            new AnomalySeedCorrelationRule { Id = 1, RegulatorCode = "CBN", FieldCodeA = "totalassets", FieldLabelA = "Total Assets", FieldCodeB = "totalliabilities", FieldLabelB = "Total Liabilities", CorrelationCoefficient = 0.98m, RSquared = 0.96m, Slope = 0.85m, Intercept = 1000000m, Notes = "Total liabilities should track total assets closely.", CreatedAt = seedDate },
            new AnomalySeedCorrelationRule { Id = 2, RegulatorCode = "CBN", FieldCodeA = "totalassets", FieldLabelA = "Total Assets", FieldCodeB = "shareholdersfunds", FieldLabelB = "Shareholders Funds", CorrelationCoefficient = 0.92m, RSquared = 0.85m, Slope = 0.15m, Intercept = 500000m, Notes = "Shareholders funds should scale with total assets.", CreatedAt = seedDate },
            new AnomalySeedCorrelationRule { Id = 3, RegulatorCode = "CBN", FieldCodeA = "totalloans", FieldLabelA = "Total Loans", FieldCodeB = "nplamount", FieldLabelB = "NPL Amount", CorrelationCoefficient = 0.75m, RSquared = 0.56m, Slope = 0.05m, Intercept = 100000m, Notes = "NPL amount tends to rise with loan book size.", CreatedAt = seedDate },
            new AnomalySeedCorrelationRule { Id = 4, RegulatorCode = "CBN", FieldCodeA = "totaldeposits", FieldLabelA = "Total Deposits", FieldCodeB = "totalloans", FieldLabelB = "Total Loans", CorrelationCoefficient = 0.88m, RSquared = 0.77m, Slope = 0.65m, Intercept = 2000000m, Notes = "Loan book should remain directionally aligned with deposits.", CreatedAt = seedDate },
            new AnomalySeedCorrelationRule { Id = 5, RegulatorCode = "CBN", FieldCodeA = "riskweightedassets", FieldLabelA = "Risk Weighted Assets", FieldCodeB = "carratio", FieldLabelB = "Capital Adequacy Ratio", CorrelationCoefficient = -0.35m, RSquared = 0.12m, Slope = -0.0001m, Intercept = 20m, Notes = "CAR usually compresses as RWA rises.", CreatedAt = seedDate },
            new AnomalySeedCorrelationRule { Id = 6, RegulatorCode = "CBN", FieldCodeA = "interestincome", FieldLabelA = "Interest Income", FieldCodeB = "interestexpense", FieldLabelB = "Interest Expense", CorrelationCoefficient = 0.90m, RSquared = 0.81m, Slope = 0.45m, Intercept = 50000m, Notes = "Interest income and expense should show a stable relationship.", CreatedAt = seedDate },
            new AnomalySeedCorrelationRule { Id = 7, RegulatorCode = "CBN", FieldCodeA = "totalloans", FieldLabelA = "Total Loans", FieldCodeB = "provisionamount", FieldLabelB = "Provision Amount", CorrelationCoefficient = 0.80m, RSquared = 0.64m, Slope = 0.03m, Intercept = 50000m, Notes = "Provisioning should broadly move with loans.", CreatedAt = seedDate },
            new AnomalySeedCorrelationRule { Id = 8, RegulatorCode = "CBN", FieldCodeA = "totaldeposits", FieldLabelA = "Total Deposits", FieldCodeB = "liquidassets", FieldLabelB = "Liquid Assets", CorrelationCoefficient = 0.85m, RSquared = 0.72m, Slope = 0.30m, Intercept = 1000000m, Notes = "Liquidity buffers should scale with deposits.", CreatedAt = seedDate });
    }
}

public sealed class AnomalyReportConfiguration : IEntityTypeConfiguration<AnomalyReport>
{
    public void Configure(EntityTypeBuilder<AnomalyReport> builder)
    {
        builder.ToTable("anomaly_reports", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.InstitutionName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ModuleCode).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.RegulatorCode).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.PeriodCode).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.OverallQualityScore).HasColumnType("decimal(6,2)");
        builder.Property(x => x.TrafficLight).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.NarrativeSummary).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AnalysedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");

        builder.HasOne(x => x.ModelVersion)
            .WithMany()
            .HasForeignKey(x => x.ModelVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Findings)
            .WithOne(x => x.Report)
            .HasForeignKey(x => x.AnomalyReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.SubmissionId, x.ModelVersionId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.PeriodCode }).HasDatabaseName("IX_anomaly_reports_tenant_period");
        builder.HasIndex(x => new { x.ModuleCode, x.PeriodCode }).HasDatabaseName("IX_anomaly_reports_module_period");
    }
}

public sealed class AnomalyFindingConfiguration : IEntityTypeConfiguration<AnomalyFinding>
{
    public void Configure(EntityTypeBuilder<AnomalyFinding> builder)
    {
        builder.ToTable("anomaly_findings", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.FindingType).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Severity).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.DetectionMethod).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.FieldCode).HasColumnType("varchar(120)").HasMaxLength(120).IsRequired();
        builder.Property(x => x.FieldLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RelatedFieldCode).HasColumnType("varchar(120)").HasMaxLength(120);
        builder.Property(x => x.RelatedFieldLabel).HasMaxLength(200);
        builder.Property(x => x.ReportedValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.RelatedValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.ExpectedValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.ExpectedRangeLow).HasColumnType("decimal(28,8)");
        builder.Property(x => x.ExpectedRangeHigh).HasColumnType("decimal(28,8)");
        builder.Property(x => x.HistoricalMean).HasColumnType("decimal(28,8)");
        builder.Property(x => x.HistoricalStdDev).HasColumnType("decimal(28,8)");
        builder.Property(x => x.BaselineValue).HasColumnType("decimal(28,8)");
        builder.Property(x => x.DeviationPercent).HasColumnType("decimal(10,4)");
        builder.Property(x => x.Explanation).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.AcknowledgedBy).HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.AcknowledgementReason).HasMaxLength(1000);
        builder.Property(x => x.PeerGroup).HasMaxLength(100);
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.AcknowledgedAt).HasColumnType("datetime2(3)");

        builder.HasIndex(x => x.AnomalyReportId).HasDatabaseName("IX_anomaly_findings_report");
        builder.HasIndex(x => new { x.TenantId, x.IsAcknowledged, x.Severity }).HasDatabaseName("IX_anomaly_findings_tenant_ack");
        builder.HasIndex(x => new { x.SubmissionId, x.FieldCode }).HasDatabaseName("IX_anomaly_findings_submission_field");
    }
}
