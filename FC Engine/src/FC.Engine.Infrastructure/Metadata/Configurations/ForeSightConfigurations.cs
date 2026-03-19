using FC.Engine.Domain.Entities;
using FC.Engine.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FC.Engine.Infrastructure.Metadata.Configurations;

public sealed class ForeSightConfigConfiguration : IEntityTypeConfiguration<ForeSightConfig>
{
    public void Configure(EntityTypeBuilder<ForeSightConfig> builder)
    {
        var seedDate = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("foresight_config", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ConfigKey).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConfigValue).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.EffectiveFrom).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.EffectiveTo).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.ConfigKey, x.EffectiveFrom }).IsUnique().HasDatabaseName("UX_foresight_config_key_effective");
        builder.HasIndex(x => x.ConfigKey).HasFilter("[EffectiveTo] IS NULL").HasDatabaseName("IX_foresight_config_active");

        // NOTE: Seed data removed from HasData() — already seeded via raw SQL in migration
        // 20260326120000_AddForeSightSchema.cs. Keeping both causes PK conflicts on next migration generation.
        /* builder.HasData(
            new ForeSightConfig { Id = 1, ConfigKey = "filing.risk_high_threshold", ConfigValue = "0.70", Description = "Probability threshold for a high filing-risk alert.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 2, ConfigKey = "filing.risk_medium_threshold", ConfigValue = "0.40", Description = "Probability threshold for a medium filing-risk classification.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 3, ConfigKey = "filing.alert_horizon_days", ConfigValue = "14", Description = "First warning horizon, in days before deadline, for high filing-risk returns.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 4, ConfigKey = "filing.escalation_horizon_days", ConfigValue = "7", Description = "Escalation horizon, in days before deadline, when risk remains elevated.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 5, ConfigKey = "filing.critical_horizon_days", ConfigValue = "3", Description = "Critical alert horizon, in days before deadline, for unresolved filing risk.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 6, ConfigKey = "filing.min_history_periods", ConfigValue = "4", Description = "Minimum filing history periods required before unsuppressed filing-risk predictions are shown.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 7, ConfigKey = "capital.warning_buffer", ConfigValue = "2.0", Description = "Buffer in percentage points above or below a threshold that maps to a warning.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 8, ConfigKey = "capital.forecast_quarters", ConfigValue = "2", Description = "Number of quarters to project ahead for prudential metrics.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 9, ConfigKey = "capital.min_data_points", ConfigValue = "6", Description = "Minimum quarterly observations required before capital forecasts become visible.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 10, ConfigKey = "chs.decline_alert_threshold", ConfigValue = "5.0", Description = "Projected CHS decline in points that triggers an advisory alert.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 11, ConfigKey = "chs.forecast_periods", ConfigValue = "3", Description = "Number of future scoring periods to project for CHS trend forecasting.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 12, ConfigKey = "churn.high_threshold", ConfigValue = "0.70", Description = "Probability threshold for classifying a tenant as high churn risk.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 13, ConfigKey = "churn.medium_threshold", ConfigValue = "0.40", Description = "Probability threshold for classifying a tenant as medium churn risk.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 14, ConfigKey = "churn.lookback_days", ConfigValue = "90", Description = "Lookback window in days for platform-engagement and churn features.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 15, ConfigKey = "regaction.high_threshold", ConfigValue = "0.60", Description = "Probability threshold for regulator-priority classification.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 16, ConfigKey = "regaction.forecast_months", ConfigValue = "6", Description = "Advisory horizon in months for supervisory-intervention probability.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 17, ConfigKey = "prediction.min_confidence", ConfigValue = "0.55", Description = "Predictions below this confidence score are persisted as suppressed.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 18, ConfigKey = "prediction.suppress_low_data", ConfigValue = "true", Description = "Suppress predictions when the historical dataset is below the configured minimum.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 19, ConfigKey = "alert.cooldown_hours", ConfigValue = "24", Description = "Minimum number of hours between equivalent advisory alerts for the same prediction target.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate },
            new ForeSightConfig { Id = 20, ConfigKey = "prediction.stale_after_hours", ConfigValue = "24", Description = "Hours after which a tenant dashboard run is considered stale and will be recomputed on demand.", CreatedBy = "SYSTEM", EffectiveFrom = seedDate, CreatedAt = seedDate }); */
    }
}

public sealed class ForeSightModelVersionConfiguration : IEntityTypeConfiguration<ForeSightModelVersion>
{
    public void Configure(EntityTypeBuilder<ForeSightModelVersion> builder)
    {
        var seedDate = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("foresight_model_versions", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModelCode).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status).HasColumnType("varchar(20)").HasMaxLength(20).IsRequired();
        builder.Property(x => x.AccuracyMetric).HasColumnType("decimal(5,4)");
        builder.Property(x => x.AccuracyMetricName).HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.TrainedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.ModelCode, x.VersionNumber }).IsUnique().HasDatabaseName("UX_foresight_model_version");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_foresight_model_versions_status", "[Status] IN ('ACTIVE','RETIRED','TRAINING','FAILED')");
        });

        // Seed data removed — already seeded via raw SQL in migration 20260326120000_AddForeSightSchema.cs
        /* builder.HasData(
            new ForeSightModelVersion { Id = 1, ... },
            ...
            new ForeSightModelVersion { Id = 5, ... }); */
    }
}

public sealed class ForeSightFeatureDefinitionConfiguration : IEntityTypeConfiguration<ForeSightFeatureDefinition>
{
    public void Configure(EntityTypeBuilder<ForeSightFeatureDefinition> builder)
    {
        var seedDate = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("foresight_feature_definitions", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModelCode).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.FeatureName).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.FeatureLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.DataSource).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.DefaultWeight).HasColumnType("decimal(8,4)").IsRequired();
        builder.HasIndex(x => new { x.ModelCode, x.FeatureName }).IsUnique().HasDatabaseName("UX_foresight_feature_definition");

        // Seed data removed — already seeded via raw SQL in migration 20260326120000_AddForeSightSchema.cs
        /* builder.HasData(
            new ForeSightFeatureDefinition { Id = 1, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "days_to_deadline", FeatureLabel = "Days to Deadline", Description = "Normalized countdown pressure before the regulatory deadline.", DataSource = "return_periods", DefaultWeight = 0.22m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 2, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "historical_late_rate", FeatureLabel = "Historical Late Rate", Description = "Late-filing share for the same module across recent filing periods.", DataSource = "filing_sla_records", DefaultWeight = 0.18m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 3, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "draft_completeness_gap", FeatureLabel = "Draft Completeness Gap", Description = "Share of expected template fields that are still blank in the current draft.", DataSource = "return_drafts", DefaultWeight = 0.16m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 4, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "preparation_stage", FeatureLabel = "Preparation Stage", Description = "Preparation stage from draft through submitted/accepted.", DataSource = "return_submissions", DefaultWeight = 0.14m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 5, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "login_activity_gap", FeatureLabel = "Login Activity Gap", Description = "Deficit in recent portal-login intensity for the tenant.", DataSource = "login_attempts", DefaultWeight = 0.08m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 6, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "recent_late_count", FeatureLabel = "Recent Late Count", Description = "Count of recent late or overdue filings across the last four quarters.", DataSource = "filing_sla_records", DefaultWeight = 0.08m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 7, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "anomaly_pressure", FeatureLabel = "Anomaly Pressure", Description = "Residual anomaly pressure from the latest accepted return for the module.", DataSource = "anomaly_reports", DefaultWeight = 0.07m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 8, ModelCode = ForeSightModelCodes.FilingRisk, FeatureName = "concurrent_filings", FeatureLabel = "Concurrent Filing Load", Description = "Number of open filing obligations clustered around the same deadline window.", DataSource = "return_periods", DefaultWeight = 0.07m, IsActive = true },

            new ForeSightFeatureDefinition { Id = 9, ModelCode = ForeSightModelCodes.CapitalBreach, FeatureName = "threshold_buffer", FeatureLabel = "Threshold Buffer", Description = "Distance between the current metric and its closest regulatory threshold.", DataSource = "prudential_metrics", DefaultWeight = 0.30m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 10, ModelCode = ForeSightModelCodes.CapitalBreach, FeatureName = "trend_slope", FeatureLabel = "Trend Slope", Description = "Linear direction of travel across recent prudential periods.", DataSource = "prudential_metrics", DefaultWeight = 0.24m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 11, ModelCode = ForeSightModelCodes.CapitalBreach, FeatureName = "volatility", FeatureLabel = "Volatility", Description = "Observed variability in the metric across recent periods.", DataSource = "prudential_metrics", DefaultWeight = 0.16m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 12, ModelCode = ForeSightModelCodes.CapitalBreach, FeatureName = "credit_stress_proxy", FeatureLabel = "Credit Stress Proxy", Description = "Credit-quality or liquidity co-metric stress surrounding the forecasted threshold.", DataSource = "prudential_metrics", DefaultWeight = 0.16m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 13, ModelCode = ForeSightModelCodes.CapitalBreach, FeatureName = "ewi_pressure", FeatureLabel = "EWI Pressure", Description = "Active prudential early warnings already signalling weakness.", DataSource = "ewi_triggers", DefaultWeight = 0.14m, IsActive = true },

            new ForeSightFeatureDefinition { Id = 14, ModelCode = ForeSightModelCodes.ComplianceTrend, FeatureName = "overall_trend_slope", FeatureLabel = "Overall Trend Slope", Description = "Recent CHS trajectory over the latest scoring periods.", DataSource = "chs_score_snapshots", DefaultWeight = 0.25m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 15, ModelCode = ForeSightModelCodes.ComplianceTrend, FeatureName = "filing_pillar", FeatureLabel = "Filing Timeliness Pillar", Description = "Current filing timeliness pillar score.", DataSource = "chs_score_snapshots", DefaultWeight = 0.18m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 16, ModelCode = ForeSightModelCodes.ComplianceTrend, FeatureName = "data_quality_pillar", FeatureLabel = "Data Quality Pillar", Description = "Current data-quality pillar score.", DataSource = "chs_score_snapshots", DefaultWeight = 0.18m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 17, ModelCode = ForeSightModelCodes.ComplianceTrend, FeatureName = "capital_pillar", FeatureLabel = "Regulatory Capital Pillar", Description = "Current regulatory-capital pillar score.", DataSource = "chs_score_snapshots", DefaultWeight = 0.16m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 18, ModelCode = ForeSightModelCodes.ComplianceTrend, FeatureName = "engagement_pillar", FeatureLabel = "Engagement Pillar", Description = "Current engagement pillar score.", DataSource = "chs_score_snapshots", DefaultWeight = 0.13m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 19, ModelCode = ForeSightModelCodes.ComplianceTrend, FeatureName = "consecutive_declines", FeatureLabel = "Consecutive Declines", Description = "Count of consecutive score periods with decline.", DataSource = "chs_score_snapshots", DefaultWeight = 0.10m, IsActive = true },

            new ForeSightFeatureDefinition { Id = 20, ModelCode = ForeSightModelCodes.ChurnRisk, FeatureName = "login_trend", FeatureLabel = "Login Trend", Description = "Trend in successful portal-login activity.", DataSource = "login_attempts", DefaultWeight = 0.23m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 21, ModelCode = ForeSightModelCodes.ChurnRisk, FeatureName = "usage_drop", FeatureLabel = "Usage Drop", Description = "Drop in active users, entities, modules or return submissions across recent months.", DataSource = "usage_records", DefaultWeight = 0.20m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 22, ModelCode = ForeSightModelCodes.ChurnRisk, FeatureName = "complianceiq_gap", FeatureLabel = "ComplianceIQ Activity Gap", Description = "Deficit in recent natural-language compliance-query usage.", DataSource = "complianceiq_turns", DefaultWeight = 0.14m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 23, ModelCode = ForeSightModelCodes.ChurnRisk, FeatureName = "support_pressure", FeatureLabel = "Support Pressure", Description = "Support-ticket escalation and unresolved support burden.", DataSource = "partner_support_tickets", DefaultWeight = 0.12m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 24, ModelCode = ForeSightModelCodes.ChurnRisk, FeatureName = "payment_delay", FeatureLabel = "Payment Delay", Description = "Delayed invoice settlement and overdue billing posture.", DataSource = "invoices,payments", DefaultWeight = 0.17m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 25, ModelCode = ForeSightModelCodes.ChurnRisk, FeatureName = "filing_timeliness_gap", FeatureLabel = "Filing Timeliness Gap", Description = "Recent decline in on-time filing performance, used as an engagement signal.", DataSource = "filing_sla_records", DefaultWeight = 0.14m, IsActive = true },

            new ForeSightFeatureDefinition { Id = 26, ModelCode = ForeSightModelCodes.RegulatoryAction, FeatureName = "critical_ewi_count", FeatureLabel = "Critical EWI Count", Description = "Number of active critical prudential or conduct EWIs.", DataSource = "ewi_triggers", DefaultWeight = 0.26m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 27, ModelCode = ForeSightModelCodes.RegulatoryAction, FeatureName = "chs_deficit", FeatureLabel = "CHS Deficit", Description = "Distance between current compliance health and full-score posture.", DataSource = "chs_score_snapshots", DefaultWeight = 0.18m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 28, ModelCode = ForeSightModelCodes.RegulatoryAction, FeatureName = "anomaly_pressure", FeatureLabel = "Anomaly Pressure", Description = "Severity and density of unresolved anomaly findings.", DataSource = "anomaly_reports", DefaultWeight = 0.18m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 29, ModelCode = ForeSightModelCodes.RegulatoryAction, FeatureName = "filing_delinquency", FeatureLabel = "Filing Delinquency", Description = "Late, overdue, or deteriorating filing posture across recent obligations.", DataSource = "filing_sla_records,return_periods", DefaultWeight = 0.14m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 30, ModelCode = ForeSightModelCodes.RegulatoryAction, FeatureName = "capital_proximity", FeatureLabel = "Capital Proximity", Description = "How close prudential capital and liquidity metrics are to binding thresholds.", DataSource = "prudential_metrics", DefaultWeight = 0.16m, IsActive = true },
            new ForeSightFeatureDefinition { Id = 31, ModelCode = ForeSightModelCodes.RegulatoryAction, FeatureName = "camels_pressure", FeatureLabel = "CAMELS Pressure", Description = "Composite CAMELS severity from the latest supervisory scoring.", DataSource = "camels_ratings", DefaultWeight = 0.08m, IsActive = true }); */
    }
}

public sealed class ForeSightRegulatoryThresholdConfiguration : IEntityTypeConfiguration<ForeSightRegulatoryThreshold>
{
    public void Configure(EntityTypeBuilder<ForeSightRegulatoryThreshold> builder)
    {
        var seedDate = new DateTime(2026, 3, 13, 0, 0, 0, DateTimeKind.Utc);

        builder.ToTable("foresight_regulatory_thresholds", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Regulator).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.LicenceCategory).HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
        builder.Property(x => x.MetricCode).HasColumnType("varchar(50)").HasMaxLength(50).IsRequired();
        builder.Property(x => x.MetricLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ThresholdValue).HasColumnType("decimal(10,4)").IsRequired();
        builder.Property(x => x.ThresholdType).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.SeverityIfBreached).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.CircularReference).HasColumnType("varchar(100)").HasMaxLength(100);
        builder.HasIndex(x => new { x.Regulator, x.LicenceCategory, x.MetricCode, x.ThresholdValue }).HasDatabaseName("IX_foresight_threshold_lookup");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_foresight_thresholds_type", "[ThresholdType] IN ('MINIMUM','MAXIMUM')");
            t.HasCheckConstraint("CK_foresight_thresholds_severity", "[SeverityIfBreached] IN ('WARNING','CRITICAL')");
        });

        // Seed data removed — already seeded via raw SQL in migration 20260326120000_AddForeSightSchema.cs
        /* builder.HasData(
            new ForeSightRegulatoryThreshold { Id = 1, Regulator = "CBN", LicenceCategory = "DMB", MetricCode = "CAR", MetricLabel = "Capital Adequacy Ratio", ThresholdValue = 15.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Deposit Money Banks should maintain at least 15% CAR for international and systemically important prudential posture.", CircularReference = "BSD/1/2004", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 2, Regulator = "CBN", LicenceCategory = "MFB", MetricCode = "CAR", MetricLabel = "Capital Adequacy Ratio", ThresholdValue = 10.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Microfinance banks should maintain at least 10% CAR.", CircularReference = "RPSD/DIR/GEN/12/005", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 3, Regulator = "CBN", LicenceCategory = "PMB", MetricCode = "CAR", MetricLabel = "Capital Adequacy Ratio", ThresholdValue = 10.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Primary mortgage banks should maintain at least 10% CAR.", CircularReference = "BSD/1/2004", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 4, Regulator = "CBN", LicenceCategory = "DMB", MetricCode = "NPL", MetricLabel = "Non-Performing Loan Ratio", ThresholdValue = 5.0m, ThresholdType = "MAXIMUM", SeverityIfBreached = "WARNING", Description = "CBN prudential guidance flags NPL ratios above 5% for closer monitoring.", CircularReference = "BSD/DIR/GEN/VOL.2/06", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 5, Regulator = "CBN", LicenceCategory = "DMB", MetricCode = "NPL", MetricLabel = "Non-Performing Loan Ratio", ThresholdValue = 10.0m, ThresholdType = "MAXIMUM", SeverityIfBreached = "CRITICAL", Description = "NPL ratios above 10% require enhanced supervisory attention.", CircularReference = "BSD/DIR/GEN/VOL.2/06", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 6, Regulator = "CBN", LicenceCategory = "MFB", MetricCode = "NPL", MetricLabel = "Non-Performing Loan Ratio", ThresholdValue = 5.0m, ThresholdType = "MAXIMUM", SeverityIfBreached = "WARNING", Description = "Microfinance-bank portfolios above 5% NPL should be escalated for review.", CircularReference = "RPSD/DIR/GEN/12/005", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 7, Regulator = "CBN", LicenceCategory = "DMB", MetricCode = "LCR", MetricLabel = "Liquidity Coverage Ratio", ThresholdValue = 100.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Liquidity coverage ratio should remain at or above 100%.", CircularReference = "BSD/DIR/GEN/VOL.2/11", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 8, Regulator = "CBN", LicenceCategory = "MFB", MetricCode = "LCR", MetricLabel = "Liquidity Coverage Ratio", ThresholdValue = 100.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Microfinance-bank liquidity coverage ratio should remain at or above 100%.", CircularReference = "RPSD/DIR/GEN/12/005", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 9, Regulator = "NAICOM", LicenceCategory = "GENERAL_INSURER", MetricCode = "SOLVENCY_MARGIN", MetricLabel = "Solvency Margin", ThresholdValue = 100.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "General insurers should sustain a solvency margin above 100%.", CircularReference = "NAICOM Act 1997", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 10, Regulator = "NAICOM", LicenceCategory = "LIFE_INSURER", MetricCode = "SOLVENCY_MARGIN", MetricLabel = "Solvency Margin", ThresholdValue = 100.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Life insurers should sustain a solvency margin above 100%.", CircularReference = "NAICOM Act 1997", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 11, Regulator = "SEC", LicenceCategory = "BROKER_DEALER", MetricCode = "CAPITAL_ADEQUACY", MetricLabel = "Capital Adequacy", ThresholdValue = 10.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Broker-dealers should maintain capital adequacy above 10%.", CircularReference = "SEC Rules 2013", IsActive = true },
            new ForeSightRegulatoryThreshold { Id = 12, Regulator = "SEC", LicenceCategory = "BROKER_DEALER", MetricCode = "SEGREGATION_RATIO", MetricLabel = "Client Funds Segregation Ratio", ThresholdValue = 100.0m, ThresholdType = "MINIMUM", SeverityIfBreached = "CRITICAL", Description = "Client funds should remain fully segregated.", CircularReference = "ISA 2007 s.148", IsActive = true }); */

        _ = seedDate;
    }
}

public sealed class ForeSightPredictionConfiguration : IEntityTypeConfiguration<ForeSightPrediction>
{
    public void Configure(EntityTypeBuilder<ForeSightPrediction> builder)
    {
        builder.ToTable("foresight_predictions", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ModelCode).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.PredictionDate).HasColumnType("date").IsRequired();
        builder.Property(x => x.HorizonLabel).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.HorizonDate).HasColumnType("date");
        builder.Property(x => x.PredictedValue).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(x => x.ConfidenceLower).HasColumnType("decimal(18,6)");
        builder.Property(x => x.ConfidenceUpper).HasColumnType("decimal(18,6)");
        builder.Property(x => x.ConfidenceScore).HasColumnType("decimal(5,4)").IsRequired();
        builder.Property(x => x.RiskBand).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.TargetModuleCode).HasColumnType("varchar(40)").HasMaxLength(40).HasDefaultValue(string.Empty).IsRequired();
        builder.Property(x => x.TargetPeriodCode).HasColumnType("varchar(40)").HasMaxLength(40).HasDefaultValue(string.Empty).IsRequired();
        builder.Property(x => x.TargetMetric).HasColumnType("varchar(60)").HasMaxLength(60).HasDefaultValue(string.Empty).IsRequired();
        builder.Property(x => x.TargetLabel).HasMaxLength(200).HasDefaultValue(string.Empty).IsRequired();
        builder.Property(x => x.Explanation).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RootCauseNarrative).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Recommendation).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RootCausePillar).HasColumnType("varchar(100)").HasMaxLength(100).HasDefaultValue(string.Empty).IsRequired();
        builder.Property(x => x.FeatureImportanceJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.SuppressionReason).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnType("datetime2(3)").IsRequired();

        builder.HasOne(x => x.ModelVersion)
            .WithMany(x => x.Predictions)
            .HasForeignKey(x => x.ModelVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Features)
            .WithOne(x => x.Prediction)
            .HasForeignKey(x => x.PredictionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Alerts)
            .WithOne(x => x.Prediction)
            .HasForeignKey(x => x.PredictionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TenantId, x.ModelCode, x.PredictionDate })
            .HasDatabaseName("IX_foresight_predictions_tenant");
        builder.HasIndex(x => new { x.ModelCode, x.RiskBand, x.IsSuppressed })
            .HasDatabaseName("IX_foresight_predictions_risk");
        builder.HasIndex(x => new
        {
            x.TenantId,
            x.ModelCode,
            x.PredictionDate,
            x.HorizonLabel,
            x.TargetModuleCode,
            x.TargetPeriodCode,
            x.TargetMetric
        }).IsUnique().HasDatabaseName("UX_foresight_predictions_scope");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_foresight_predictions_risk", "[RiskBand] IN ('CRITICAL','HIGH','MEDIUM','LOW','NONE')");
        });
    }
}

public sealed class ForeSightPredictionFeatureRecordConfiguration : IEntityTypeConfiguration<ForeSightPredictionFeatureRecord>
{
    public void Configure(EntityTypeBuilder<ForeSightPredictionFeatureRecord> builder)
    {
        builder.ToTable("foresight_prediction_features", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FeatureName).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.FeatureLabel).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RawValue).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(x => x.NormalizedValue).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(x => x.Weight).HasColumnType("decimal(8,4)").IsRequired();
        builder.Property(x => x.ContributionScore).HasColumnType("decimal(18,6)").IsRequired();
        builder.Property(x => x.ImpactDirection).HasColumnType("varchar(40)").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.PredictionId, x.FeatureName }).HasDatabaseName("IX_foresight_prediction_features_prediction");
    }
}

public sealed class ForeSightAlertConfiguration : IEntityTypeConfiguration<ForeSightAlert>
{
    public void Configure(EntityTypeBuilder<ForeSightAlert> builder)
    {
        builder.ToTable("foresight_alerts", "meta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AlertType).HasColumnType("varchar(30)").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Severity).HasColumnType("varchar(10)").HasMaxLength(10).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Recommendation).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RecipientRole).HasColumnType("varchar(100)").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReadBy).HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.DismissedBy).HasColumnType("varchar(100)").HasMaxLength(100);
        builder.Property(x => x.ReadAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.DismissedAt).HasColumnType("datetime2(3)");
        builder.Property(x => x.DispatchedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnType("datetime2(3)").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.IsRead, x.IsDismissed, x.DispatchedAt }).HasDatabaseName("IX_foresight_alerts_lookup");
        builder.HasIndex(x => new { x.TenantId, x.AlertType, x.DispatchedAt }).HasDatabaseName("IX_foresight_alerts_type");
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_foresight_alerts_type", "[AlertType] IN ('FILING_RISK','CAPITAL_WARNING','CAPITAL_CRITICAL','CHS_DECLINE','CHURN_RISK','REG_ACTION_PRIORITY')");
            t.HasCheckConstraint("CK_foresight_alerts_severity", "[Severity] IN ('INFO','WARNING','CRITICAL')");
        });
    }
}
