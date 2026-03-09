using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEarlyWarningSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── PrudentialMetrics ─────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "prudential_metrics",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    InstitutionType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "date", nullable: false),
                    PeriodCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),

                    // Capital
                    CAR = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    Tier1Ratio = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    Tier2Capital = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    RWA = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalAssets = table.Column<decimal>(type: "decimal(18,2)", nullable: true),

                    // Asset Quality
                    NPLRatio = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    GrossNPL = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    GrossLoans = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ProvisioningCoverage = table.Column<decimal>(type: "decimal(8,4)", nullable: true),

                    // Earnings
                    ROA = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    ROE = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    NIM = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    CIR = table.Column<decimal>(type: "decimal(8,4)", nullable: true),

                    // Liquidity
                    LCR = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    NSFR = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    LiquidAssetsRatio = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    DepositConcentration = table.Column<decimal>(type: "decimal(8,4)", nullable: true),

                    // Market / Sensitivity
                    FXExposureRatio = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    InterestRateSensitivity = table.Column<decimal>(type: "decimal(8,4)", nullable: true),

                    // Management / Compliance
                    ComplianceScore = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    LateFilingCount = table.Column<int>(nullable: true),
                    AuditOpinionCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    RelatedPartyLendingRatio = table.Column<decimal>(type: "decimal(8,4)", nullable: true),

                    SourceReturnInstanceId = table.Column<long>(nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prudential_metrics", x => x.Id);
                    table.UniqueConstraint("UQ_prudential_metrics_entity_period",
                        x => new { x.InstitutionId, x.PeriodCode });
                });

            migrationBuilder.CreateIndex(
                name: "IX_prudential_metrics_regulator",
                schema: "meta",
                table: "prudential_metrics",
                columns: ["RegulatorCode", "AsOfDate"],
                descending: [false, true]);

            migrationBuilder.CreateIndex(
                name: "IX_prudential_metrics_type",
                schema: "meta",
                table: "prudential_metrics",
                columns: ["InstitutionType", "AsOfDate"],
                descending: [false, true]);

            // ── EWIDefinitions ────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ewi_definitions",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EWICode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    EWIName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Category = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    CAMELSComponent = table.Column<string>(type: "varchar(1)", maxLength: 1, nullable: false),
                    DefaultSeverity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RemediationGuidance = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ewi_definitions", x => x.Id);
                    table.UniqueConstraint("UQ_ewi_definitions_code", x => x.EWICode);
                });

            // ── EWITriggers ───────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ewi_triggers",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EWICode = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    PeriodCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Severity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    TriggerValue = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    ThresholdValue = table.Column<decimal>(type: "decimal(18,6)", nullable: true),
                    TrendData = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                    IsSystemic = table.Column<bool>(nullable: false, defaultValue: false),
                    ComputationRunId = table.Column<Guid>(nullable: false),
                    TriggeredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    ClearedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ClearedByRunId = table.Column<Guid>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ewi_triggers", x => x.Id);
                });

            migrationBuilder.CreateIndex("IX_ewi_triggers_institution", schema: "meta", table: "ewi_triggers",
                columns: ["InstitutionId", "IsActive", "TriggeredAt"], descending: [false, false, true]);
            migrationBuilder.CreateIndex("IX_ewi_triggers_regulator", schema: "meta", table: "ewi_triggers",
                columns: ["RegulatorCode", "IsActive", "TriggeredAt"], descending: [false, false, true]);
            migrationBuilder.CreateIndex("IX_ewi_triggers_code", schema: "meta", table: "ewi_triggers",
                columns: ["EWICode", "TriggeredAt"], descending: [false, true]);
            migrationBuilder.CreateIndex("IX_ewi_triggers_runid", schema: "meta", table: "ewi_triggers",
                column: "ComputationRunId");

            // ── CAMELSRatings ─────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "camels_ratings",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    PeriodCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "date", nullable: false),
                    CapitalScore = table.Column<byte>(nullable: false),
                    AssetQualityScore = table.Column<byte>(nullable: false),
                    ManagementScore = table.Column<byte>(nullable: false),
                    EarningsScore = table.Column<byte>(nullable: false),
                    LiquidityScore = table.Column<byte>(nullable: false),
                    SensitivityScore = table.Column<byte>(nullable: false),
                    CompositeScore = table.Column<decimal>(type: "decimal(4,2)", nullable: false),
                    RiskBand = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    TotalAssets = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ComputationRunId = table.Column<Guid>(nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_camels_ratings", x => x.Id);
                    table.UniqueConstraint("UQ_camels_ratings_entity_period",
                        x => new { x.InstitutionId, x.PeriodCode });
                });

            migrationBuilder.CreateIndex("IX_camels_ratings_regulator", schema: "meta", table: "camels_ratings",
                columns: ["RegulatorCode", "AsOfDate"], descending: [false, true]);
            migrationBuilder.CreateIndex("IX_camels_ratings_band", schema: "meta", table: "camels_ratings",
                columns: ["RiskBand", "ComputedAt"], descending: [false, true]);

            // ── SystemicRiskIndicators ────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "systemic_risk_indicators",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    InstitutionType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    PeriodCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "date", nullable: false),
                    EntityCount = table.Column<int>(nullable: false),
                    SectorAvgCAR = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    SectorAvgNPL = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    SectorAvgLCR = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    SectorAvgROA = table.Column<decimal>(type: "decimal(8,4)", nullable: true),
                    EntitiesBreachingCAR = table.Column<int>(nullable: false, defaultValue: 0),
                    EntitiesBreachingNPL = table.Column<int>(nullable: false, defaultValue: 0),
                    EntitiesBreachingLCR = table.Column<int>(nullable: false, defaultValue: 0),
                    HighRiskEntityCount = table.Column<int>(nullable: false, defaultValue: 0),
                    SystemicRiskScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    SystemicRiskBand = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    AggregateInterbankExposure = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ComputationRunId = table.Column<Guid>(nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_systemic_risk_indicators", x => x.Id);
                    table.UniqueConstraint("UQ_systemic_risk_indicators_type_period",
                        x => new { x.RegulatorCode, x.InstitutionType, x.PeriodCode });
                });

            migrationBuilder.CreateIndex("IX_systemic_risk_indicators_regulator", schema: "meta",
                table: "systemic_risk_indicators",
                columns: ["RegulatorCode", "AsOfDate"], descending: [false, true]);

            // ── InterbankExposures ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "interbank_exposures",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LendingInstitutionId = table.Column<int>(nullable: false),
                    BorrowingInstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    PeriodCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ExposureAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExposureType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interbank_exposures", x => x.Id);
                    table.UniqueConstraint("UQ_interbank_exposures_pair_period",
                        x => new { x.LendingInstitutionId, x.BorrowingInstitutionId, x.ExposureType, x.PeriodCode });
                });

            migrationBuilder.CreateIndex("IX_interbank_exposures_lender", schema: "meta",
                table: "interbank_exposures", columns: ["LendingInstitutionId", "PeriodCode"]);
            migrationBuilder.CreateIndex("IX_interbank_exposures_borrower", schema: "meta",
                table: "interbank_exposures", columns: ["BorrowingInstitutionId", "PeriodCode"]);

            // ── ContagionAnalysisResults ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "contagion_analysis_results",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    PeriodCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    EigenvectorCentrality = table.Column<decimal>(type: "decimal(12,8)", nullable: false),
                    BetweennessCentrality = table.Column<decimal>(type: "decimal(12,8)", nullable: false),
                    TotalOutboundExposure = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalInboundExposure = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DirectCounterparties = table.Column<int>(nullable: false),
                    ContagionRiskScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    IsSystemicallyImportant = table.Column<bool>(nullable: false, defaultValue: false),
                    ComputationRunId = table.Column<Guid>(nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contagion_analysis_results", x => x.Id);
                    table.UniqueConstraint("UQ_contagion_analysis_entity_period",
                        x => new { x.InstitutionId, x.PeriodCode });
                });

            migrationBuilder.CreateIndex("IX_contagion_analysis_dsib", schema: "meta",
                table: "contagion_analysis_results",
                columns: ["IsSystemicallyImportant", "PeriodCode"]);

            // ── SupervisoryActions ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "supervisory_actions",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    EWITriggerId = table.Column<long>(nullable: false),
                    ActionType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Severity = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LetterContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false,
                        defaultValue: "DRAFT"),
                    IssuedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IssuedByUserId = table.Column<int>(nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    DueDate = table.Column<DateTime>(type: "date", nullable: true),
                    EscalationLevel = table.Column<byte>(nullable: false, defaultValue: (byte)1),
                    CurrentAssigneeUserId = table.Column<int>(nullable: true),
                    RemediationPlanJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervisory_actions", x => x.Id);
                    table.ForeignKey("FK_supervisory_actions_ewi_trigger",
                        x => x.EWITriggerId,
                        principalSchema: "meta",
                        principalTable: "ewi_triggers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex("IX_supervisory_actions_institution", schema: "meta",
                table: "supervisory_actions", columns: ["InstitutionId", "Status"]);
            migrationBuilder.CreateIndex("IX_supervisory_actions_regulator", schema: "meta",
                table: "supervisory_actions", columns: ["RegulatorCode", "Status", "Severity"]);

            // ── SupervisoryActionAuditLog ─────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "supervisory_action_audit_log",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupervisoryActionId = table.Column<long>(nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    EventType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedByUserId = table.Column<int>(nullable: true),
                    PerformedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supervisory_action_audit_log", x => x.Id);
                });

            migrationBuilder.CreateIndex("IX_supervisory_action_audit_log_action", schema: "meta",
                table: "supervisory_action_audit_log", column: "SupervisoryActionId");
            migrationBuilder.CreateIndex("IX_supervisory_action_audit_log_time", schema: "meta",
                table: "supervisory_action_audit_log", column: "PerformedAt",
                descending: [true]);

            // ── EWIComputationRuns ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "ewi_computation_runs",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComputationRunId = table.Column<Guid>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    PeriodCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false,
                        defaultValue: "RUNNING"),
                    EntitiesEvaluated = table.Column<int>(nullable: false, defaultValue: 0),
                    EWIsTriggered = table.Column<int>(nullable: false, defaultValue: 0),
                    EWIsCleared = table.Column<int>(nullable: false, defaultValue: 0),
                    ActionsGenerated = table.Column<int>(nullable: false, defaultValue: 0),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ewi_computation_runs", x => x.Id);
                    table.UniqueConstraint("UQ_ewi_computation_runs_runid", x => x.ComputationRunId);
                });

            migrationBuilder.CreateIndex("IX_ewi_computation_runs_regulator", schema: "meta",
                table: "ewi_computation_runs", columns: ["RegulatorCode", "StartedAt"],
                descending: [false, true]);

            // ── Seed EWI Definitions ──────────────────────────────────────────
            migrationBuilder.InsertData(
                schema: "meta",
                table: "ewi_definitions",
                columns: ["EWICode", "EWIName", "Category", "CAMELSComponent", "DefaultSeverity",
                          "Description", "RemediationGuidance"],
                values: new object[,]
                {
                    // Capital
                    { "CAR_DECLINING_3Q", "CAR Declining 3+ Consecutive Quarters", "INSTITUTIONAL", "C", "HIGH",
                      "Capital Adequacy Ratio has declined for three or more consecutive quarters, indicating sustained capital erosion.",
                      "Submit capital restoration plan within 30 days. Consider rights issue or retained earnings strategy." },
                    { "CAR_BREACH_MINIMUM", "CAR Below Regulatory Minimum", "INSTITUTIONAL", "C", "CRITICAL",
                      "Capital Adequacy Ratio has fallen below the regulatory minimum threshold.",
                      "Immediate capital injection required. Suspend dividend payments. Submit emergency capital plan within 7 days." },
                    { "TIER1_RATIO_LOW", "Tier 1 Ratio Below Warning Threshold", "INSTITUTIONAL", "C", "MEDIUM",
                      "Tier 1 capital ratio has fallen to within 2 percentage points of the minimum requirement.",
                      "Review capital planning. Restrict discretionary spending. Engage shareholders on capital support." },
                    // Asset Quality
                    { "NPL_THRESHOLD_BREACH", "NPL Ratio Exceeds 5%", "INSTITUTIONAL", "A", "HIGH",
                      "Non-Performing Loan ratio has breached the 5% regulatory warning threshold.",
                      "Submit NPL resolution plan. Increase provisioning. Suspend new lending in affected segments." },
                    { "NPL_RAPID_RISE", "NPL Ratio Risen >2pp in Single Quarter", "INSTITUTIONAL", "A", "HIGH",
                      "NPL ratio increased by more than 2 percentage points within a single reporting quarter.",
                      "Immediate loan book review. Identify concentration of new NPLs. Engage board risk committee." },
                    { "PROVISIONING_LOW", "Provisioning Coverage Below 50%", "INSTITUTIONAL", "A", "MEDIUM",
                      "Provision coverage ratio has fallen below 50%, indicating inadequate loss absorption buffer.",
                      "Increase provisions to minimum 60% within two quarters. Review classification methodology." },
                    // Liquidity
                    { "LCR_WARNING_ZONE", "LCR Below 110% (Approaching Minimum)", "INSTITUTIONAL", "L", "MEDIUM",
                      "Liquidity Coverage Ratio has entered the warning zone below 110%, approaching the 100% regulatory minimum.",
                      "Reduce short-term liabilities. Build HQLA buffer. Review funding concentration." },
                    { "LCR_BREACH", "LCR Below 100% (Regulatory Minimum)", "INSTITUTIONAL", "L", "CRITICAL",
                      "LCR has fallen below the 100% regulatory minimum, indicating a liquidity stress event.",
                      "Immediate access to CBN Standing Lending Facility. Submit liquidity recovery plan within 24 hours." },
                    { "DEPOSIT_CONCENTRATION", "Top-20 Depositors Exceed 30% of Total", "INSTITUTIONAL", "L", "MEDIUM",
                      "Deposit concentration risk: the top 20 depositors account for more than 30% of total deposits.",
                      "Diversify funding base. Implement depositor concentration limits. Review wholesale funding reliance." },
                    { "NSFR_BREACH", "NSFR Below 100%", "INSTITUTIONAL", "L", "HIGH",
                      "Net Stable Funding Ratio has breached 100%, indicating structural liquidity vulnerability.",
                      "Extend liability maturity profile. Reduce reliance on short-term wholesale funding." },
                    // Management
                    { "LATE_FILINGS_2PLUS", "2+ Consecutive Late Filings", "INSTITUTIONAL", "M", "MEDIUM",
                      "Institution has filed regulatory returns late for two or more consecutive periods.",
                      "Review internal reporting processes. Designate dedicated regulatory reporting officer." },
                    { "RELATED_PARTY_EXCESS", "Related-Party Lending Exceeds Limit", "INSTITUTIONAL", "M", "HIGH",
                      "Related-party lending has exceeded the regulatory limit as a percentage of capital.",
                      "Immediate cessation of new related-party facilities. Develop wind-down schedule for excess." },
                    { "AUDIT_ADVERSE", "Adverse or Disclaimer Audit Opinion", "INSTITUTIONAL", "M", "CRITICAL",
                      "External auditors have issued an adverse or disclaimer of opinion on financial statements.",
                      "Immediate disclosure to CBN. Engage new auditors. Submit remediation plan within 14 days." },
                    // Earnings
                    { "ROA_NEGATIVE", "Return on Assets Negative", "INSTITUTIONAL", "E", "HIGH",
                      "Institution is operating at a loss, with negative Return on Assets.",
                      "Submit earnings recovery plan. Review cost structure. Identify non-core asset disposals." },
                    { "CIR_CRITICAL", "Cost-to-Income Ratio Exceeds 80%", "INSTITUTIONAL", "E", "MEDIUM",
                      "Operating efficiency has deteriorated with CIR above 80%, threatening long-term viability.",
                      "Implement cost reduction plan. Review branch rationalisation. Automate manual processes." },
                    // Sensitivity
                    { "FX_EXPOSURE_EXCESS", "Net FX Position Exceeds 20% of Capital", "INSTITUTIONAL", "S", "HIGH",
                      "Net open foreign exchange position has exceeded 20% of shareholders funds.",
                      "Immediately reduce FX exposure. Submit FX risk management plan. Review hedging strategy." },
                    { "SUDDEN_ASSET_GROWTH", "Assets Grew >30% Quarter-on-Quarter", "INSTITUTIONAL", "C", "MEDIUM",
                      "Total assets have grown by more than 30% in a single quarter, raising capital adequacy concerns.",
                      "Ensure CAR adequacy for new asset base. Review asset quality of new growth. Submit growth plan." },
                    // Systemic
                    { "SYSTEMIC_NPL_RISING", "Sector NPL Rising Across Multiple Types", "SYSTEMIC", "A", "HIGH",
                      "Aggregate NPL is rising simultaneously across multiple supervised institution types.",
                      "Sector-wide stress test. Macroprudential policy review. Consider countercyclical buffer." },
                    { "SYSTEMIC_LCR_STRESS", "Multiple Entities Breaching LCR", "SYSTEMIC", "L", "CRITICAL",
                      "Three or more institutions have simultaneously breached the LCR minimum.",
                      "Activate systemic liquidity support framework. Consider Emergency Liquidity Assistance." },
                    { "CONTAGION_DSIB_RISK", "D-SIB Contagion Risk Elevated", "SYSTEMIC", "S", "CRITICAL",
                      "A Domestic Systemically Important Bank shows elevated contagion risk via interbank network.",
                      "Immediate supervisory engagement with D-SIB board. Consider enhanced supervision mandate." },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "supervisory_action_audit_log", schema: "meta");
            migrationBuilder.DropTable(name: "supervisory_actions", schema: "meta");
            migrationBuilder.DropTable(name: "contagion_analysis_results", schema: "meta");
            migrationBuilder.DropTable(name: "interbank_exposures", schema: "meta");
            migrationBuilder.DropTable(name: "systemic_risk_indicators", schema: "meta");
            migrationBuilder.DropTable(name: "camels_ratings", schema: "meta");
            migrationBuilder.DropTable(name: "ewi_triggers", schema: "meta");
            migrationBuilder.DropTable(name: "ewi_definitions", schema: "meta");
            migrationBuilder.DropTable(name: "ewi_computation_runs", schema: "meta");
            migrationBuilder.DropTable(name: "prudential_metrics", schema: "meta");
        }
    }
}
