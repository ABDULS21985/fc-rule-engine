using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceIqSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "complianceiq_config",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ConfigValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "complianceiq_conversations",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserRole = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsRegulatorContext = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TurnCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_conversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "complianceiq_field_synonyms",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Synonym = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FieldCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RegulatorCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ConfidenceBoost = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_field_synonyms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "complianceiq_intents",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntentCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", nullable: false),
                    RequiresRegulatorContext = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_intents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "complianceiq_quick_questions",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuestionText = table.Column<string>(type: "nvarchar(220)", maxLength: 220, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IconClass = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RequiresRegulatorContext = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_quick_questions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "complianceiq_templates",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntentCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", nullable: false),
                    TemplateBody = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParameterSchema = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VisualizationType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequiresRegulatorContext = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "complianceiq_turns",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserRole = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    TurnNumber = table.Column<int>(type: "int", nullable: false),
                    QueryText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntentCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IntentConfidence = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    ExtractedEntitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ResolvedParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExecutedPlan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ExecutionTimeMs = table.Column<int>(type: "int", nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VisualizationType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FollowUpSuggestionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalTimeMs = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_turns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_complianceiq_turns_complianceiq_conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "meta",
                        principalTable: "complianceiq_conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "complianceiq_feedback",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TurnId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Rating = table.Column<short>(type: "smallint", nullable: false),
                    FeedbackText = table.Column<string>(type: "nvarchar(1000)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_complianceiq_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_complianceiq_feedback_complianceiq_turns_TurnId",
                        column: x => x.TurnId,
                        principalSchema: "meta",
                        principalTable: "complianceiq_turns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "complianceiq_config",
                columns: new[] { "Id", "ConfigKey", "ConfigValue", "CreatedAt", "CreatedBy", "Description", "EffectiveFrom", "EffectiveTo" },
                values: new object[,]
                {
                    { 1, "rate.queries_per_minute", "10", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum ComplianceIQ queries per user per minute.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 2, "rate.queries_per_hour", "100", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum ComplianceIQ queries per user per hour.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 3, "rate.queries_per_day", "500", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum ComplianceIQ queries per user per day.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 4, "response.max_rows", "25", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Maximum grounded rows returned in a ComplianceIQ answer.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 5, "trend.default_periods", "8", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Default lookback window for trend questions when the user does not specify one.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 6, "confidence.high_threshold", "0.85", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Minimum confidence for a HIGH-confidence response label.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 7, "confidence.medium_threshold", "0.60", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Minimum confidence for a MEDIUM-confidence response label.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 8, "help.welcome_message", "Welcome to ComplianceIQ. Ask about returns, deadlines, anomalies, peer benchmarks, compliance health, or regulator knowledge.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Welcome message shown in the ComplianceIQ chat surface.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 9, "scenario.default_npl_multiplier", "2.0", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "SYSTEM", "Fallback NPL multiplier for scenario analysis when a user says doubled without a numeric multiplier.", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "complianceiq_field_synonyms",
                columns: new[] { "Id", "ConfidenceBoost", "CreatedAt", "FieldCode", "IsActive", "ModuleCode", "RegulatorCode", "Synonym" },
                values: new object[,]
                {
                    { 1, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "carratio", true, "CBN_PRUDENTIAL", "CBN", "car" },
                    { 2, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "carratio", true, "CBN_PRUDENTIAL", "CBN", "capital adequacy" },
                    { 3, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "carratio", true, "CBN_PRUDENTIAL", "CBN", "capital adequacy ratio" },
                    { 4, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "nplratio", true, "CBN_PRUDENTIAL", "CBN", "npl" },
                    { 5, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "nplratio", true, "CBN_PRUDENTIAL", "CBN", "npl ratio" },
                    { 6, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "nplratio", true, "CBN_PRUDENTIAL", "CBN", "non performing loans" },
                    { 7, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "nplamount", true, "CBN_PRUDENTIAL", "CBN", "npl amount" },
                    { 8, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "liquidityratio", true, "CBN_PRUDENTIAL", "CBN", "liquidity" },
                    { 9, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "liquidityratio", true, "CBN_PRUDENTIAL", "CBN", "liquidity ratio" },
                    { 10, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "liquidityratio", true, "CBN_PRUDENTIAL", "CBN", "lcr" },
                    { 11, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "loandepositratio", true, "CBN_PRUDENTIAL", "CBN", "loan to deposit ratio" },
                    { 12, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "loandepositratio", true, "CBN_PRUDENTIAL", "CBN", "ldr" },
                    { 13, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "roa", true, "CBN_PRUDENTIAL", "CBN", "roa" },
                    { 14, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "roa", true, "CBN_PRUDENTIAL", "CBN", "return on assets" },
                    { 15, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "roe", true, "CBN_PRUDENTIAL", "CBN", "roe" },
                    { 16, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "roe", true, "CBN_PRUDENTIAL", "CBN", "return on equity" },
                    { 17, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "netinterestmargin", true, "CBN_PRUDENTIAL", "CBN", "nim" },
                    { 18, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "netinterestmargin", true, "CBN_PRUDENTIAL", "CBN", "net interest margin" },
                    { 19, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "totalassets", true, "CBN_PRUDENTIAL", "CBN", "total assets" },
                    { 20, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "totalliabilities", true, "CBN_PRUDENTIAL", "CBN", "total liabilities" },
                    { 21, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "shareholdersfunds", true, "CBN_PRUDENTIAL", "CBN", "shareholders funds" },
                    { 22, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "riskweightedassets", true, "CBN_PRUDENTIAL", "CBN", "risk weighted assets" },
                    { 23, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "riskweightedassets", true, "CBN_PRUDENTIAL", "CBN", "rwa" },
                    { 24, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "insureddeposits", true, "NDIC_SRF", "NDIC", "insured deposits" },
                    { 25, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "depositpremiumdue", true, "NDIC_SRF", "NDIC", "deposit premium" },
                    { 26, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "grosspremium", true, "NAICOM_QR", "NAICOM", "gross premium" },
                    { 27, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "lossratio", true, "NAICOM_QR", "NAICOM", "loss ratio" },
                    { 28, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "combinedratio", true, "NAICOM_QR", "NAICOM", "combined ratio" },
                    { 29, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "solvencymargin", true, "NAICOM_QR", "NAICOM", "solvency margin" },
                    { 30, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "netcapital", true, "SEC_CMO", "SEC", "net capital" },
                    { 31, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "liquidcapital", true, "SEC_CMO", "SEC", "liquid capital" },
                    { 32, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "clientassetsaum", true, "SEC_CMO", "SEC", "assets under management" },
                    { 33, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "clientassetsaum", true, "SEC_CMO", "SEC", "aum" },
                    { 34, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "tradevolume", true, "SEC_CMO", "SEC", "trade volume" },
                    { 35, 1m, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "segregationratio", true, "SEC_CMO", "SEC", "segregation ratio" }
                });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "complianceiq_intents",
                columns: new[] { "Id", "Category", "CreatedAt", "Description", "DisplayName", "IntentCode", "IsEnabled", "RequiresRegulatorContext", "SortOrder" },
                values: new object[,]
                {
                    { 1, "DATA", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Retrieve the latest grounded value for a regulatory field.", "Current Value", "CURRENT_VALUE", true, false, 1 },
                    { 2, "DATA", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Show historical movement for a field across recent filing periods.", "Trend", "TREND", true, false, 2 },
                    { 3, "BENCHMARK", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Compare a metric with the peer median or peer band.", "Peer Comparison", "COMPARISON_PEER", true, false, 3 },
                    { 4, "BENCHMARK", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Compare a metric between two filing periods.", "Period Comparison", "COMPARISON_PERIOD", true, false, 4 },
                    { 5, "CALENDAR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "List upcoming or overdue filing deadlines.", "Deadline", "DEADLINE", true, false, 5 },
                    { 6, "KNOWLEDGE", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Search regulatory guidance, circulars, and knowledge-base content.", "Regulatory Lookup", "REGULATORY_LOOKUP", true, false, 6 },
                    { 7, "STATUS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Summarise compliance health and key compliance posture indicators.", "Compliance Status", "COMPLIANCE_STATUS", true, false, 7 },
                    { 8, "QUALITY", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Summarise anomaly findings and data quality signals.", "Anomaly Status", "ANOMALY_STATUS", true, false, 8 },
                    { 9, "ANALYSIS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Project simple prudential what-if scenarios such as an NPL shock.", "Scenario", "SCENARIO", true, false, 9 },
                    { 10, "DISCOVERY", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Search validation history and filing evidence.", "Search", "SEARCH", true, false, 10 },
                    { 11, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Aggregate a metric across supervised institutions.", "Sector Aggregate", "SECTOR_AGGREGATE", true, true, 11 },
                    { 12, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Compare named institutions on a selected metric.", "Entity Compare", "ENTITY_COMPARE", true, true, 12 },
                    { 13, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank institutions by anomaly pressure or data quality weakness.", "Risk Ranking", "RISK_RANKING", true, true, 13 },
                    { 14, "SYSTEM", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Explain the kinds of questions ComplianceIQ can answer.", "Help", "HELP", true, false, 14 },
                    { 15, "SYSTEM", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Fallback when a question is ambiguous or unsupported.", "Clarification", "UNCLEAR", true, false, 15 },
                    { 16, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Build a composite supervisory profile for one supervised institution.", "Entity Intelligence Profile", "ENTITY_PROFILE", true, true, 16 },
                    { 17, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Show a sector-level trend for a metric across time.", "Sector Metric Trend", "SECTOR_TREND", true, true, 17 },
                    { 18, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank supervised institutions by a requested metric.", "Top or Bottom Ranking", "TOP_N_RANKING", true, true, 18 },
                    { 19, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Show overdue, pending, or due filing status across entities.", "Filing Status Check", "FILING_STATUS", true, true, 19 },
                    { 20, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank supervised institutions by filing timeliness and delinquency.", "Filing Delinquency Ranking", "FILING_DELINQUENCY", true, true, 20 },
                    { 21, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank institutions by Compliance Health Score.", "Compliance Health Ranking", "CHS_RANKING", true, true, 21 },
                    { 22, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return the compliance health breakdown for one entity.", "Entity Compliance Health", "CHS_ENTITY", true, true, 22 },
                    { 23, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Summarise early warning flags across supervised entities.", "Early Warning Status", "EWI_STATUS", true, true, 23 },
                    { 24, "SYSTEMIC", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return a system-wide supervisory risk dashboard.", "Systemic Risk Overview", "SYSTEMIC_DASHBOARD", true, true, 24 },
                    { 25, "SYSTEMIC", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Analyse contagion effects and interbank spillovers around a named institution.", "Contagion Analysis", "CONTAGION_QUERY", true, true, 25 },
                    { 26, "SYSTEMIC", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "List available stress scenarios or return stress-test outputs.", "Stress Test Results", "STRESS_SCENARIOS", true, true, 26 },
                    { 27, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Summarise sanctions-screening and AML exposure across entities.", "Sanctions Exposure", "SANCTIONS_EXPOSURE", true, true, 27 },
                    { 28, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Generate a comprehensive supervisory briefing for one institution.", "Examination Briefing", "EXAMINATION_BRIEF", true, true, 28 },
                    { 29, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Show outstanding or overdue supervisory actions and recommendations.", "Supervisory Actions", "SUPERVISORY_ACTIONS", true, true, 29 },
                    { 30, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return cross-border, group, harmonisation, or divergence intelligence.", "Cross-Border Intelligence", "CROSS_BORDER", true, true, 30 },
                    { 31, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return policy simulation and what-if impact outputs.", "Policy Impact", "POLICY_IMPACT", true, true, 31 },
                    { 32, "REGULATOR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Aggregate validation-error hotspots across institutions and templates.", "Validation Hotspot", "VALIDATION_HOTSPOT", true, true, 32 }
                });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "complianceiq_quick_questions",
                columns: new[] { "Id", "Category", "IconClass", "IsActive", "QuestionText", "RequiresRegulatorContext", "SortOrder" },
                values: new object[,]
                {
                    { 1, "DATA", "bi-bank", true, "What is our current CAR?", false, 1 },
                    { 2, "TREND", "bi-graph-up", true, "Show NPL trend over the last 8 quarters", false, 2 },
                    { 3, "CALENDAR", "bi-calendar-event", true, "When is our next filing due?", false, 3 },
                    { 4, "QUALITY", "bi-exclamation-triangle", true, "Do we have any anomalies in our latest return?", false, 4 },
                    { 5, "STATUS", "bi-heart-pulse", true, "What is our compliance health score?", false, 5 },
                    { 6, "BENCHMARK", "bi-people", true, "How does our liquidity compare to peers?", false, 6 },
                    { 7, "REGULATOR", "bi-sort-numeric-down", true, "Rank institutions by anomaly density", true, 7 },
                    { 8, "REGULATOR", "bi-bar-chart", true, "What is aggregate CAR across commercial banks?", true, 8 },
                    { 9, "REGULATOR", "bi-diagram-3", true, "Compare Access Bank and GTBank on NPL ratio", true, 9 },
                    { 10, "KNOWLEDGE", "bi-journal-text", true, "What does BSD/DIR/2024/003 require?", false, 10 }
                });

            migrationBuilder.InsertData(
                schema: "meta",
                table: "complianceiq_templates",
                columns: new[] { "Id", "CreatedAt", "Description", "DisplayName", "IntentCode", "IsActive", "ParameterSchema", "RequiresRegulatorContext", "ResultFormat", "SortOrder", "TemplateBody", "TemplateCode", "UpdatedAt", "VisualizationType" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Use the latest accepted submission that contains the requested field.", "Latest Field Value", "CURRENT_VALUE", true, "{\"fieldCode\":\"string\"}", false, "SCALAR", 1, "Latest accepted submission -> extract requested metric -> cite module and period.", "CV_SINGLE_FIELD", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "number" },
                    { 2, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return the latest prudential key ratio bundle for the institution.", "Latest Key Ratios", "CURRENT_VALUE", true, "{\"moduleCode\":\"string\"}", false, "TABLE", 2, "Latest accepted submission -> extract key ratios -> return tabular bundle.", "CV_KEY_RATIOS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 3, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return the requested metric across multiple periods.", "Field Trend", "TREND", true, "{\"fieldCode\":\"string\",\"periodCount\":\"int\"}", false, "TIMESERIES", 1, "Accepted submissions ordered by period -> extract metric -> return timeseries.", "TR_FIELD_HISTORY", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "lineChart" },
                    { 4, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Compare an institution metric with peer aggregates.", "Peer Comparison", "COMPARISON_PEER", true, "{\"fieldCode\":\"string\",\"licenceCategory\":\"string\"}", false, "SCALAR", 1, "Latest accepted submission + active peer stats -> compute deviation from peer median.", "CP_PEER_METRIC", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "gauge" },
                    { 5, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Compare the same metric between two periods.", "Two Period Comparison", "COMPARISON_PERIOD", true, "{\"fieldCode\":\"string\",\"periodA\":\"string\",\"periodB\":\"string\"}", false, "TABLE", 1, "Locate two accepted periods for tenant -> extract same field -> compute deltas.", "CPR_TWO_PERIODS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "barChart" },
                    { 6, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return upcoming and overdue filing calendar items.", "Filing Calendar", "DEADLINE", true, "{\"regulatorCode\":\"string?\",\"overdueOnly\":\"bool\"}", false, "TABLE", 1, "Tenant return periods with module deadlines -> classify due, overdue, upcoming.", "DL_CALENDAR", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 7, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Search knowledge-base and knowledge-graph records.", "Knowledge Lookup", "REGULATORY_LOOKUP", true, "{\"keyword\":\"string\"}", false, "LIST", 1, "Knowledge articles + knowledge graph nodes/edges -> rank top regulatory matches.", "RL_KNOWLEDGE", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 8, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return the latest Compliance Health Score and pillars.", "Compliance Health", "COMPLIANCE_STATUS", true, "{}", false, "SCALAR", 1, "Current CHS snapshot -> summarise overall and pillar scores.", "CS_HEALTH_SCORE", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "gauge" },
                    { 9, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return the latest anomaly report or detailed findings for a module.", "Latest Anomaly Report", "ANOMALY_STATUS", true, "{\"moduleCode\":\"string?\"}", false, "TABLE", 1, "Latest anomaly report -> optionally expand findings for requested module.", "AS_LATEST_REPORT", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 10, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Project CAR, NPL ratio, and LDR from an NPL shock.", "CAR NPL Scenario", "SCENARIO", true, "{\"scenarioMultiplier\":\"decimal\"}", false, "SCALAR", 1, "Latest prudential submission -> apply NPL multiplier -> recompute CAR/NPL/LDR.", "SC_CAR_NPL", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "number" },
                    { 11, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return submissions with validation errors or keyword matches.", "Validation Error Search", "SEARCH", true, "{\"keyword\":\"string?\"}", false, "TABLE", 1, "Validation reports joined to submissions -> count errors and warnings.", "SR_VALIDATION_ERRORS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 12, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Aggregate a field across supervised institutions.", "Sector Aggregate", "SECTOR_AGGREGATE", true, "{\"fieldCode\":\"string\",\"periodCode\":\"string?\",\"licenceCategory\":\"string?\"}", true, "AGGREGATE", 1, "Cross-tenant accepted submissions for regulator context -> aggregate metric.", "SA_FIELD_AGGREGATE", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "barChart" },
                    { 13, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Compare named institutions for a selected metric.", "Entity Compare", "ENTITY_COMPARE", true, "{\"fieldCode\":\"string\",\"entityNames\":\"string[]\"}", true, "TABLE", 1, "Cross-tenant accepted submissions -> extract requested metric for named institutions.", "EC_ENTITY_COMPARE", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "barChart" },
                    { 14, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank institutions by anomaly density or quality score.", "Anomaly Ranking", "RISK_RANKING", true, "{\"periodCode\":\"string?\",\"moduleCode\":\"string?\"}", true, "TABLE", 1, "Cross-tenant anomaly reports -> order by quality score ascending.", "RR_ANOMALY_RANKING", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "ranking" },
                    { 15, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return the composite supervisory profile for one named institution.", "Entity Intelligence Profile", "ENTITY_PROFILE", true, "{\"entityNames\":\"string[]\",\"periodCode\":\"string?\"}", true, "PROFILE", 1, "Resolve entity -> call regulator intelligence profile service -> return profile and citations.", "EP_ENTITY_PROFILE", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "profile" },
                    { 16, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return a sector-level trend for the requested metric.", "Sector Trend", "SECTOR_TREND", true, "{\"fieldCode\":\"string\",\"periodCount\":\"int\",\"licenceCategory\":\"string?\",\"periodCode\":\"string?\"}", true, "TIMESERIES", 1, "Resolve metric and sector filter -> call sector analytics trend services -> return time-series output.", "ST_SECTOR_TREND", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "lineChart" },
                    { 17, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank entities by a requested supervisory metric.", "Metric Ranking", "TOP_N_RANKING", true, "{\"fieldCode\":\"string\",\"limit\":\"int\",\"direction\":\"string\",\"licenceCategory\":\"string?\",\"periodCode\":\"string?\"}", true, "TABLE", 1, "Resolve field, sort direction, and requested count -> run cross-tenant metric ranking.", "TN_METRIC_RANKING", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "ranking" },
                    { 18, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return overdue, due, or pending filings across supervised institutions.", "Filing Status", "FILING_STATUS", true, "{\"licenceCategory\":\"string?\",\"periodCode\":\"string?\",\"status\":\"string?\"}", true, "TABLE", 1, "Resolve regulator scope and filing status filters -> query filing calendar and SLA state cross-tenant.", "FS_FILING_STATUS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 19, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank institutions by filing lateness and timeliness.", "Filing Delinquency Ranking", "FILING_DELINQUENCY", true, "{\"licenceCategory\":\"string?\",\"periodCode\":\"string?\",\"limit\":\"int\"}", true, "TABLE", 1, "Cross-tenant filing SLA records -> aggregate late or overdue counts -> order by delinquency.", "FD_TIMELINESS_RANKING", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "ranking" },
                    { 20, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Rank supervised institutions by current Compliance Health Score.", "CHS Ranking", "CHS_RANKING", true, "{\"licenceCategory\":\"string?\",\"limit\":\"int\"}", true, "TABLE", 1, "Cross-tenant CHS watch list and sector summary -> order entities by current score.", "CR_CHS_RANKING", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "ranking" },
                    { 21, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return the compliance health scorecard for one institution.", "Entity CHS Breakdown", "CHS_ENTITY", true, "{\"entityNames\":\"string[]\"}", true, "PROFILE", 1, "Resolve entity -> load current Compliance Health Score -> return overall and pillar breakdown.", "CE_CHS_ENTITY", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "gauge" },
                    { 22, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return current early-warning flags across supervised entities.", "Early Warning Status", "EWI_STATUS", true, "{\"licenceCategory\":\"string?\",\"entityNames\":\"string[]?\"}", true, "TABLE", 1, "Compute early warning flags for the regulator scope -> optionally filter by entity or licence type.", "EWI_SECTOR_STATUS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "heatmap" },
                    { 23, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return a regulator-facing systemic risk dashboard.", "Systemic Dashboard", "SYSTEMIC_DASHBOARD", true, "{\"periodCode\":\"string?\"}", true, "PROFILE", 1, "Call systemic risk dashboard service -> return cross-entity KPIs, alerts, and component scores.", "SD_SYSTEMIC_DASHBOARD", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "dashboard" },
                    { 24, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Run contagion analysis for a named institution or scenario.", "Contagion Analysis", "CONTAGION_QUERY", true, "{\"entityNames\":\"string[]\",\"scenarioCode\":\"string?\"}", true, "PROFILE", 1, "Resolve entity and scenario context -> call contagion analysis service -> return systemic spillover view.", "CQ_CONTAGION_ANALYSIS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "network" },
                    { 25, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return available stress scenarios or results for a named scenario.", "Stress Scenarios", "STRESS_SCENARIOS", true, "{\"scenarioName\":\"string?\",\"periodCode\":\"string?\"}", true, "TABLE", 1, "List available scenarios, or route a named scenario request to the stress-testing service.", "SS_STRESS_SCENARIOS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 26, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Summarise sanctions-screening exposure for one or more entities.", "Sanctions Exposure", "SANCTIONS_EXPOSURE", true, "{\"entityNames\":\"string[]?\",\"licenceCategory\":\"string?\"}", true, "TABLE", 1, "Resolve entity scope -> query sanctions screening results -> aggregate counts and highest risk outcomes.", "SX_SANCTIONS_EXPOSURE", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 27, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Generate a multi-source supervisory briefing for one entity.", "Examination Briefing", "EXAMINATION_BRIEF", true, "{\"entityNames\":\"string[]\"}", true, "PROFILE", 1, "Resolve entity -> call regulator examination briefing service -> return composite focus areas and evidence.", "EB_EXAMINATION_BRIEF", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "profile" },
                    { 28, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return open, overdue, or recommended supervisory actions.", "Supervisory Actions", "SUPERVISORY_ACTIONS", true, "{\"entityNames\":\"string[]?\",\"status\":\"string?\"}", true, "TABLE", 1, "Call supervisory action or dashboard services -> return action backlog and status indicators.", "SV_SUPERVISORY_ACTIONS", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 29, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return pan-African group, harmonisation, or divergence intelligence.", "Cross-Border Intelligence", "CROSS_BORDER", true, "{\"entityNames\":\"string[]?\",\"jurisdiction\":\"string?\"}", true, "TABLE", 1, "Resolve group or jurisdiction scope -> call cross-border dashboard services -> return summary tables and indicators.", "CB_CROSS_BORDER", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "table" },
                    { 30, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Return policy simulation scenarios and their institution or sector impacts.", "Policy Impact", "POLICY_IMPACT", true, "{\"scenarioName\":\"string?\",\"licenceCategory\":\"string?\"}", true, "TABLE", 1, "Resolve policy scenario request -> call policy simulation services -> return impact outputs.", "PI_POLICY_IMPACT", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "barChart" },
                    { 31, new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "Aggregate recurring validation errors across supervised institutions.", "Validation Hotspot", "VALIDATION_HOTSPOT", true, "{\"fieldCode\":\"string?\",\"licenceCategory\":\"string?\",\"periodCode\":\"string?\",\"limit\":\"int\"}", true, "TABLE", 1, "Cross-tenant validation errors -> group by rule, field, or institution -> return hotspot counts.", "VH_VALIDATION_HOTSPOT", new DateTime(2026, 3, 12, 0, 0, 0, 0, DateTimeKind.Utc), "heatmap" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_config_lookup",
                schema: "meta",
                table: "complianceiq_config",
                columns: new[] { "ConfigKey", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_conversations_tenant_user",
                schema: "meta",
                table: "complianceiq_conversations",
                columns: new[] { "TenantId", "UserId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_feedback_turn_user",
                schema: "meta",
                table: "complianceiq_feedback",
                columns: new[] { "TurnId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_field_synonyms_field",
                schema: "meta",
                table: "complianceiq_field_synonyms",
                columns: new[] { "FieldCode", "ModuleCode" });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_field_synonyms_lookup",
                schema: "meta",
                table: "complianceiq_field_synonyms",
                columns: new[] { "Synonym", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_complianceiq_intents_code",
                schema: "meta",
                table: "complianceiq_intents",
                column: "IntentCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_quick_questions_lookup",
                schema: "meta",
                table: "complianceiq_quick_questions",
                columns: new[] { "RequiresRegulatorContext", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_templates_intent",
                schema: "meta",
                table: "complianceiq_templates",
                columns: new[] { "IntentCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_complianceiq_templates_code",
                schema: "meta",
                table: "complianceiq_templates",
                column: "TemplateCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_turns_conversation",
                schema: "meta",
                table: "complianceiq_turns",
                columns: new[] { "ConversationId", "TurnNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_complianceiq_turns_tenant",
                schema: "meta",
                table: "complianceiq_turns",
                columns: new[] { "TenantId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "complianceiq_config",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "complianceiq_feedback",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "complianceiq_field_synonyms",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "complianceiq_intents",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "complianceiq_quick_questions",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "complianceiq_templates",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "complianceiq_turns",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "complianceiq_conversations",
                schema: "meta");
        }
    }
}
