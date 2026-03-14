using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatorIqSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "regiq_access_log",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegulatorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TurnId = table.Column<int>(type: "int", nullable: true),
                    RegulatorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RegulatorAgency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    RegulatorRole = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    QueryText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ClassificationLevel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    EntitiesAccessedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PrimaryEntityTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DataSourcesAccessedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilterContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", maxLength: 45, nullable: true),
                    SessionId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    AccessedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RetainUntil = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regiq_access_log", x => x.Id);
                    table.CheckConstraint("CK_regiq_access_log_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
                    table.CheckConstraint("CK_regiq_access_log_data_sources_json", "ISJSON([DataSourcesAccessedJson]) = 1");
                    table.CheckConstraint("CK_regiq_access_log_entities_json", "ISJSON([EntitiesAccessedJson]) = 1");
                    table.ForeignKey(
                        name: "FK_regiq_access_log_tenants_RegulatorTenantId",
                        column: x => x.RegulatorTenantId,
                        principalTable: "tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regiq_config",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConfigKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ConfigValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regiq_config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "regiq_conversation",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegulatorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegulatorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RegulatorRole = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    RegulatorAgency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ClassificationLevel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Scope = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TurnCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regiq_conversation", x => x.Id);
                    table.CheckConstraint("CK_regiq_conversation_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
                    table.CheckConstraint("CK_regiq_conversation_scope", "[Scope] IN ('SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','SYSTEMIC','HELP')");
                    table.ForeignKey(
                        name: "FK_regiq_conversation_tenants_RegulatorTenantId",
                        column: x => x.RegulatorTenantId,
                        principalTable: "tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regiq_intent",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntentCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExampleQuery = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    PrimaryDataSource = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    RequiresRegulatorContext = table.Column<bool>(type: "bit", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regiq_intent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "regiq_query_template",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IntentCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    TemplateCode = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SqlTemplate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParameterSchema = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResultFormat = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    VisualizationType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Scope = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ClassificationLevel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    DataSourcesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CrossTenantEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RequiresEntityContext = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regiq_query_template", x => x.Id);
                    table.CheckConstraint("CK_regiq_query_template_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
                    table.CheckConstraint("CK_regiq_query_template_data_sources_json", "ISJSON([DataSourcesJson]) = 1");
                    table.CheckConstraint("CK_regiq_query_template_parameter_schema_json", "ISJSON([ParameterSchema]) = 1");
                    table.CheckConstraint("CK_regiq_query_template_scope", "[Scope] IN ('SECTOR_WIDE','ENTITY_SPECIFIC','COMPARATIVE','SYSTEMIC','HELP')");
                });

            migrationBuilder.CreateTable(
                name: "regulatoriq_entity_aliases",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CanonicalName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Alias = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NormalizedAlias = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    AliasType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, defaultValue: "NAME"),
                    LicenceCategory = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    RegulatorAgency = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    InstitutionType = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    HoldingCompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    GeoTag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MatchPriority = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regulatoriq_entity_aliases", x => x.Id);
                    table.CheckConstraint("CK_regulatoriq_entity_aliases_alias_type", "[AliasType] IN ('NAME','ABBREVIATION','HOLDING_COMPANY','COMMON')");
                    table.ForeignKey(
                        name: "FK_regulatoriq_entity_aliases_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "regiq_turn",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegulatorTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegulatorId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RegulatorRole = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    TurnNumber = table.Column<int>(type: "int", nullable: false),
                    QueryText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IntentCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    IntentConfidence = table.Column<decimal>(type: "decimal(5,4)", nullable: false),
                    ExtractedEntitiesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TemplateCode = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    ResolvedParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExecutedPlan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ExecutionTimeMs = table.Column<int>(type: "int", nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseDataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VisualizationType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    ConfidenceLevel = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    CitationsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FollowUpSuggestionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalTimeMs = table.Column<int>(type: "int", nullable: false),
                    EntitiesQueriedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataSourcesAccessedJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClassificationLevel = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    RegulatorAgencyFilterApplied = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: true),
                    PrimaryEntityTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regiq_turn", x => x.Id);
                    table.CheckConstraint("CK_regiq_turn_citations_json", "ISJSON([CitationsJson]) = 1");
                    table.CheckConstraint("CK_regiq_turn_classification", "[ClassificationLevel] IN ('UNCLASSIFIED','RESTRICTED','CONFIDENTIAL')");
                    table.CheckConstraint("CK_regiq_turn_data_sources_json", "ISJSON([DataSourcesAccessedJson]) = 1");
                    table.CheckConstraint("CK_regiq_turn_entities_queried_json", "ISJSON([EntitiesQueriedJson]) = 1");
                    table.CheckConstraint("CK_regiq_turn_extracted_entities_json", "ISJSON([ExtractedEntitiesJson]) = 1");
                    table.CheckConstraint("CK_regiq_turn_followups_json", "ISJSON([FollowUpSuggestionsJson]) = 1");
                    table.CheckConstraint("CK_regiq_turn_resolved_parameters_json", "ISJSON([ResolvedParametersJson]) = 1");
                    table.CheckConstraint("CK_regiq_turn_response_data_json", "ISJSON([ResponseDataJson]) = 1");
                    table.ForeignKey(
                        name: "FK_regiq_turn_regiq_conversation_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "meta",
                        principalTable: "regiq_conversation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_regiq_turn_tenants_RegulatorTenantId",
                        column: x => x.RegulatorTenantId,
                        principalTable: "tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_regiq_access_log_primary_entity",
                schema: "meta",
                table: "regiq_access_log",
                column: "PrimaryEntityTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_regiq_access_log_regulator",
                schema: "meta",
                table: "regiq_access_log",
                columns: new[] { "RegulatorTenantId", "AccessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_regiq_access_log_retention",
                schema: "meta",
                table: "regiq_access_log",
                column: "RetainUntil");

            migrationBuilder.CreateIndex(
                name: "IX_regiq_config_lookup",
                schema: "meta",
                table: "regiq_config",
                columns: new[] { "ConfigKey", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_regiq_conversation_regulator",
                schema: "meta",
                table: "regiq_conversation",
                columns: new[] { "RegulatorTenantId", "RegulatorId", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "UX_regiq_intent_code",
                schema: "meta",
                table: "regiq_intent",
                column: "IntentCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regiq_query_template_intent",
                schema: "meta",
                table: "regiq_query_template",
                columns: new[] { "IntentCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "UX_regiq_query_template_code",
                schema: "meta",
                table: "regiq_query_template",
                column: "TemplateCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_regiq_turn_conversation",
                schema: "meta",
                table: "regiq_turn",
                columns: new[] { "ConversationId", "TurnNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_regiq_turn_primary_entity",
                schema: "meta",
                table: "regiq_turn",
                column: "PrimaryEntityTenantId");

            migrationBuilder.CreateIndex(
                name: "IX_regiq_turn_regulator",
                schema: "meta",
                table: "regiq_turn",
                columns: new[] { "RegulatorTenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_regulatoriq_entity_aliases_lookup",
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                columns: new[] { "NormalizedAlias", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_regulatoriq_entity_aliases_regulator",
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                columns: new[] { "RegulatorAgency", "LicenceCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_regulatoriq_entity_aliases_tenant",
                schema: "meta",
                table: "regulatoriq_entity_aliases",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "regiq_access_log",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "regiq_config",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "regiq_intent",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "regiq_query_template",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "regiq_turn",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "regulatoriq_entity_aliases",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "regiq_conversation",
                schema: "meta");
        }
    }
}
