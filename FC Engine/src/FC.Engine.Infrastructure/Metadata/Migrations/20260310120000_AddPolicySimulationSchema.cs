using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Metadata.Migrations;

/// <inheritdoc />
public partial class AddPolicySimulationSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── PolicyScenarios ────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "policy_scenarios",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RegulatorId = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PolicyDomain = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                TargetEntityTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                BaselineDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_policy_scenarios", x => x.Id);
            });

        migrationBuilder.CreateIndex("IX_policy_scenarios_regulator", "policy_scenarios", new[] { "RegulatorId", "Status" });
        migrationBuilder.CreateIndex("IX_policy_scenarios_domain", "policy_scenarios", new[] { "PolicyDomain", "Status" });

        // ── PolicyParameters ───────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "policy_parameters",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScenarioId = table.Column<long>(type: "bigint", nullable: false),
                ParameterCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                ParameterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CurrentValue = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                ProposedValue = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                Unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                ApplicableEntityTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                ReturnLineReference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_policy_parameters", x => x.Id);
                table.ForeignKey("FK_policy_parameters_scenario", x => x.ScenarioId,
                    "policy_scenarios", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_policy_parameters_scenario", "policy_parameters", "ScenarioId");
        migrationBuilder.AddUniqueConstraint("UQ_policy_parameters_code", "policy_parameters", new[] { "ScenarioId", "ParameterCode" });

        // ── PolicyParameterPresets ─────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "policy_parameter_presets",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ParameterCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                ParameterName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                PolicyDomain = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                CurrentBaseline = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                Unit = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                ReturnLineReference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_policy_parameter_presets", x => x.Id);
            });

        migrationBuilder.AddUniqueConstraint("UQ_policy_parameter_presets_code", "policy_parameter_presets", new[] { "ParameterCode", "RegulatorCode" });

        // ── ImpactAssessmentRuns ───────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "impact_assessment_runs",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScenarioId = table.Column<long>(type: "bigint", nullable: false),
                RegulatorId = table.Column<int>(type: "int", nullable: false),
                RunNumber = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                SnapshotDate = table.Column<DateOnly>(type: "date", nullable: false),
                TotalEntitiesEvaluated = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                EntitiesCurrentlyCompliant = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                EntitiesWouldBreach = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                EntitiesAlreadyBreaching = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                EntitiesNotAffected = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                AggregateCapitalShortfall = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                AggregateComplianceCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExecutionTimeMs = table.Column<long>(type: "bigint", nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                StartedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_impact_assessment_runs", x => x.Id);
                table.ForeignKey("FK_impact_assessment_runs_scenario", x => x.ScenarioId,
                    "policy_scenarios", "Id");
            });

        migrationBuilder.CreateIndex("IX_impact_assessment_runs_scenario", "impact_assessment_runs", new[] { "ScenarioId", "Status" });
        migrationBuilder.AddUniqueConstraint("UQ_impact_assessment_runs_number", "impact_assessment_runs", new[] { "ScenarioId", "RunNumber" });

        // ── EntityImpactResults ────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "entity_impact_results",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                RunId = table.Column<long>(type: "bigint", nullable: false),
                InstitutionId = table.Column<int>(type: "int", nullable: false),
                InstitutionCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                InstitutionName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                EntityType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                ImpactCategory = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                ParameterResults = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CurrentMetricValue = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                ProposedThreshold = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                GapToCompliance = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                EstimatedComplianceCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                RiskScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_entity_impact_results", x => x.Id);
                table.ForeignKey("FK_entity_impact_results_run", x => x.RunId,
                    "impact_assessment_runs", "Id");
            });

        migrationBuilder.CreateIndex("IX_entity_impact_results_run", "entity_impact_results", new[] { "RunId", "ImpactCategory" });
        migrationBuilder.CreateIndex("IX_entity_impact_results_institution", "entity_impact_results", new[] { "InstitutionId", "RunId" });

        // ── CostBenefitAnalyses ────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "cost_benefit_analyses",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScenarioId = table.Column<long>(type: "bigint", nullable: false),
                RunId = table.Column<long>(type: "bigint", nullable: false),
                TotalIndustryComplianceCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CostToSmallEntities = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CostToMediumEntities = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CostToLargeEntities = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                SectorCARImprovement = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                SectorLCRImprovement = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                EstimatedRiskReduction = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                EstimatedDepositProtection = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                ImmediateImpactSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                PhaseIn12MonthSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                PhaseIn24MonthSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                NetBenefitScore = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                Recommendation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cost_benefit_analyses", x => x.Id);
                table.ForeignKey("FK_cost_benefit_analyses_scenario", x => x.ScenarioId,
                    "policy_scenarios", "Id");
                table.ForeignKey("FK_cost_benefit_analyses_run", x => x.RunId,
                    "impact_assessment_runs", "Id");
            });

        migrationBuilder.AddUniqueConstraint("UQ_cost_benefit_analyses_run", "cost_benefit_analyses", "RunId");

        // ── ConsultationRounds ─────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "consultation_rounds",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScenarioId = table.Column<long>(type: "bigint", nullable: false),
                RegulatorId = table.Column<int>(type: "int", nullable: false),
                Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                CoverNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PublishedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                DeadlineDate = table.Column<DateOnly>(type: "date", nullable: false),
                Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                TargetEntityTypes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                TotalFeedbackReceived = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                AggregationCompletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consultation_rounds", x => x.Id);
                table.ForeignKey("FK_consultation_rounds_scenario", x => x.ScenarioId,
                    "policy_scenarios", "Id");
            });

        migrationBuilder.CreateIndex("IX_consultation_rounds_scenario", "consultation_rounds", "ScenarioId");
        migrationBuilder.CreateIndex("IX_consultation_rounds_deadline", "consultation_rounds", new[] { "DeadlineDate", "Status" });

        // ── ConsultationProvisions ─────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "consultation_provisions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ConsultationId = table.Column<long>(type: "bigint", nullable: false),
                ProvisionNumber = table.Column<int>(type: "int", nullable: false),
                ProvisionTitle = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                ProvisionText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RelatedParameterCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true),
                DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consultation_provisions", x => x.Id);
                table.ForeignKey("FK_consultation_provisions_round", x => x.ConsultationId,
                    "consultation_rounds", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddUniqueConstraint("UQ_consultation_provisions_number", "consultation_provisions", new[] { "ConsultationId", "ProvisionNumber" });

        // ── ConsultationFeedback ───────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "consultation_feedback",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ConsultationId = table.Column<long>(type: "bigint", nullable: false),
                InstitutionId = table.Column<int>(type: "int", nullable: false),
                InstitutionCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                EntityType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                SubmittedByUserId = table.Column<int>(type: "int", nullable: false),
                OverallPosition = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                GeneralComments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SubmittedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                IsAnonymised = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consultation_feedback", x => x.Id);
                table.ForeignKey("FK_consultation_feedback_round", x => x.ConsultationId,
                    "consultation_rounds", "Id");
            });

        migrationBuilder.CreateIndex("IX_consultation_feedback_consultation", "consultation_feedback", "ConsultationId");
        migrationBuilder.AddUniqueConstraint("UQ_consultation_feedback_one_per_institution", "consultation_feedback", new[] { "ConsultationId", "InstitutionId" });

        // ── ProvisionFeedback ──────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "provision_feedback",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                FeedbackId = table.Column<long>(type: "bigint", nullable: false),
                ProvisionId = table.Column<long>(type: "bigint", nullable: false),
                Position = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                Reasoning = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SuggestedAmendment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ImpactAssessment = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provision_feedback", x => x.Id);
                table.ForeignKey("FK_provision_feedback_feedback", x => x.FeedbackId,
                    "consultation_feedback", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_provision_feedback_provision", x => x.ProvisionId,
                    "consultation_provisions", "Id");
            });

        migrationBuilder.AddUniqueConstraint("UQ_provision_feedback_one_per_provision", "provision_feedback", new[] { "FeedbackId", "ProvisionId" });

        // ── FeedbackAggregations ───────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "feedback_aggregations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ConsultationId = table.Column<long>(type: "bigint", nullable: false),
                ProvisionId = table.Column<long>(type: "bigint", nullable: false),
                TotalResponses = table.Column<int>(type: "int", nullable: false),
                SupportCount = table.Column<int>(type: "int", nullable: false),
                OpposeCount = table.Column<int>(type: "int", nullable: false),
                NeutralCount = table.Column<int>(type: "int", nullable: false),
                AmendCount = table.Column<int>(type: "int", nullable: false),
                SupportPercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                OpposePercentage = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                ByEntityType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                TopConcerns = table.Column<string>(type: "nvarchar(max)", nullable: true),
                TopSuggestedAmendments = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ComputedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_feedback_aggregations", x => x.Id);
                table.ForeignKey("FK_feedback_aggregations_consultation", x => x.ConsultationId,
                    "consultation_rounds", "Id");
                table.ForeignKey("FK_feedback_aggregations_provision", x => x.ProvisionId,
                    "consultation_provisions", "Id");
            });

        migrationBuilder.AddUniqueConstraint("UQ_feedback_aggregations_provision", "feedback_aggregations", new[] { "ConsultationId", "ProvisionId" });

        // ── PolicyDecisions ────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "policy_decisions",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScenarioId = table.Column<long>(type: "bigint", nullable: false),
                RegulatorId = table.Column<int>(type: "int", nullable: false),
                DecisionType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                DecisionSummary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                FinalParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                PhaseInMonths = table.Column<int>(type: "int", nullable: true),
                CircularReference = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                DocumentBlobPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                DecidedByUserId = table.Column<int>(type: "int", nullable: false),
                DecidedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_policy_decisions", x => x.Id);
                table.ForeignKey("FK_policy_decisions_scenario", x => x.ScenarioId,
                    "policy_scenarios", "Id");
            });

        migrationBuilder.AddUniqueConstraint("UQ_policy_decisions_scenario", "policy_decisions", "ScenarioId");

        // ── HistoricalImpactTracking ───────────────────────────────────
        migrationBuilder.CreateTable(
            name: "historical_impact_tracking",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                DecisionId = table.Column<long>(type: "bigint", nullable: false),
                ScenarioId = table.Column<long>(type: "bigint", nullable: false),
                TrackingDate = table.Column<DateOnly>(type: "date", nullable: false),
                MonthsSinceEnactment = table.Column<int>(type: "int", nullable: false),
                PredictedBreachCount = table.Column<int>(type: "int", nullable: false),
                PredictedCapitalShortfall = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                PredictedComplianceCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                ActualBreachCount = table.Column<int>(type: "int", nullable: false),
                ActualCapitalShortfall = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                ActualComplianceCost = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                BreachCountVariance = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                ShortfallVariance = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                AccuracyScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_historical_impact_tracking", x => x.Id);
                table.ForeignKey("FK_historical_impact_tracking_decision", x => x.DecisionId,
                    "policy_decisions", "Id");
            });

        migrationBuilder.CreateIndex("IX_historical_impact_tracking_decision", "historical_impact_tracking", new[] { "DecisionId", "TrackingDate" });
        migrationBuilder.AddUniqueConstraint("UQ_historical_impact_tracking_snapshot", "historical_impact_tracking", new[] { "DecisionId", "TrackingDate" });

        // ── PolicyAuditLog ─────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "policy_audit_log",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ScenarioId = table.Column<long>(type: "bigint", nullable: true),
                RegulatorId = table.Column<int>(type: "int", nullable: false),
                CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                PerformedByUserId = table.Column<int>(type: "int", nullable: false),
                PerformedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_policy_audit_log", x => x.Id);
            });

        migrationBuilder.CreateIndex("IX_policy_audit_log_scenario", "policy_audit_log", "ScenarioId");
        migrationBuilder.CreateIndex("IX_policy_audit_log_correlation", "policy_audit_log", "CorrelationId");
        migrationBuilder.CreateIndex("IX_policy_audit_log_time", "policy_audit_log", "PerformedAt", descending: new[] { true });

        // ── Seed: Parameter Presets (CBN) ──────────────────────────────
        migrationBuilder.Sql("""
            INSERT INTO policy_parameter_presets
                (ParameterCode, ParameterName, PolicyDomain, CurrentBaseline, Unit, ReturnLineReference, Description, RegulatorCode)
            VALUES
                ('MIN_CAR',            'Minimum Capital Adequacy Ratio',              'CapitalAdequacy', 10.000000, 'Percentage', 'SRF-001.L45',   'Basel II/III minimum CAR for DMBs',            'CBN'),
                ('MIN_CAR_DSIB',       'Minimum CAR for D-SIBs',                     'CapitalAdequacy', 15.000000, 'Percentage', 'SRF-001.L45',   'Higher CAR for domestic systemically important banks', 'CBN'),
                ('MIN_CET1',           'Minimum Common Equity Tier 1 Ratio',         'CapitalAdequacy',  6.000000, 'Percentage', 'SRF-001.L12',   'CET1 floor',                                   'CBN'),
                ('MIN_LCR',            'Minimum Liquidity Coverage Ratio',           'Liquidity',       100.000000, 'Percentage', 'LCR-001.L30',   'Basel III LCR minimum',                        'CBN'),
                ('MIN_NSFR',           'Minimum Net Stable Funding Ratio',           'Liquidity',       100.000000, 'Percentage', 'NSFR-001.L25',  'Basel III NSFR minimum',                       'CBN'),
                ('MIN_LEVERAGE',       'Minimum Leverage Ratio',                     'Leverage',          3.000000, 'Percentage', 'LEV-001.L10',   'Basel III leverage ratio floor',                'CBN'),
                ('MAX_SINGLE_OBLIGOR', 'Maximum Single Obligor Limit',               'RiskManagement',   20.000000, 'Percentage', 'CRD-001.L08',   'Max exposure to single borrower as % of S/H funds', 'CBN'),
                ('MAX_FX_SPREAD',      'Maximum BDC FX Spread',                      'FX',                5.000000, 'Percentage', NULL,            'Cap on BDC buy-sell spread',                    'CBN'),
                ('MIN_RESERVE_REQ',    'Cash Reserve Requirement',                   'Liquidity',        32.500000, 'Percentage', NULL,            'CBN CRR for DMBs',                             'CBN'),
                ('MIN_LIQUIDITY_RATIO','Minimum Liquidity Ratio',                    'Liquidity',        30.000000, 'Percentage', 'LIQ-001.L15',   'CBN minimum liquidity ratio',                  'CBN'),
                ('MIN_CAR_MFB',        'Minimum CAR for Microfinance Banks',         'CapitalAdequacy', 10.000000, 'Percentage', 'MFB-CAR.L20',   'CAR floor for MFBs',                           'CBN'),
                ('MIN_CAR_PMB',        'Minimum CAR for Payment Service Banks',      'CapitalAdequacy', 10.000000, 'Percentage', NULL,            'CAR floor for PSBs',                           'CBN');
        """);

        // ── Seed: Parameter Presets (NDIC) ─────────────────────────────
        migrationBuilder.Sql("""
            INSERT INTO policy_parameter_presets
                (ParameterCode, ParameterName, PolicyDomain, CurrentBaseline, Unit, ReturnLineReference, Description, RegulatorCode)
            VALUES
                ('MAX_INSURED_DEPOSIT', 'Maximum Insured Deposit',                   'CapitalAdequacy', 500000.00, 'Absolute', NULL, 'NDIC deposit insurance cap per depositor (NGN)', 'NDIC'),
                ('PREMIUM_RATE_DMB',    'DIS Premium Rate for DMBs',                 'CapitalAdequacy',  0.350000, 'Percentage', NULL, 'Annual DIS premium as % of total deposits',    'NDIC');
        """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "policy_audit_log");
        migrationBuilder.DropTable(name: "historical_impact_tracking");
        migrationBuilder.DropTable(name: "policy_decisions");
        migrationBuilder.DropTable(name: "feedback_aggregations");
        migrationBuilder.DropTable(name: "provision_feedback");
        migrationBuilder.DropTable(name: "consultation_feedback");
        migrationBuilder.DropTable(name: "consultation_provisions");
        migrationBuilder.DropTable(name: "consultation_rounds");
        migrationBuilder.DropTable(name: "cost_benefit_analyses");
        migrationBuilder.DropTable(name: "entity_impact_results");
        migrationBuilder.DropTable(name: "impact_assessment_runs");
        migrationBuilder.DropTable(name: "policy_parameter_presets");
        migrationBuilder.DropTable(name: "policy_parameters");
        migrationBuilder.DropTable(name: "policy_scenarios");
    }
}
