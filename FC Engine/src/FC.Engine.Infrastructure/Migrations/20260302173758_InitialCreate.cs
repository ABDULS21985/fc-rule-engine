using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "meta");

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "business_rules",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RuleType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Expression = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AppliesToTemplates = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppliesToFields = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cross_sheet_rules",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cross_sheet_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ddl_migrations",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    VersionFrom = table.Column<int>(type: "int", nullable: true),
                    VersionTo = table.Column<int>(type: "int", nullable: false),
                    MigrationType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DdlScript = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RollbackScript = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExecutedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExecutionDurationMs = table.Column<int>(type: "int", nullable: true),
                    IsRolledBack = table.Column<bool>(type: "bit", nullable: false),
                    RolledBackAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RolledBackBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ddl_migrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "institutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InstitutionName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    LicenseType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_institutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "portal_users",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_portal_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "return_periods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReportingDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_periods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "return_templates",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StructuralCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PhysicalTableName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    XmlRootElement = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    XmlNamespace = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsSystemTemplate = table.Column<bool>(type: "bit", nullable: false),
                    OwnerDepartment = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    InstitutionType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "template_sections",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateVersionId = table.Column<int>(type: "int", nullable: false),
                    SectionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SectionOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsRepeating = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_sections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cross_sheet_rule_expressions",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleId = table.Column<int>(type: "int", nullable: false),
                    Expression = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ToleranceAmount = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    TolerancePercent = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cross_sheet_rule_expressions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cross_sheet_rule_expressions_cross_sheet_rules_RuleId",
                        column: x => x.RuleId,
                        principalSchema: "meta",
                        principalTable: "cross_sheet_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cross_sheet_rule_operands",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleId = table.Column<int>(type: "int", nullable: false),
                    OperandAlias = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TemplateReturnCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LineCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    AggregateFunction = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    FilterItemCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cross_sheet_rule_operands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cross_sheet_rule_operands_cross_sheet_rules_RuleId",
                        column: x => x.RuleId,
                        principalSchema: "meta",
                        principalTable: "cross_sheet_rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "return_submissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(type: "int", nullable: false),
                    ReturnPeriodId = table.Column<int>(type: "int", nullable: false),
                    ReturnCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateVersionId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawXml = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParsedDataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProcessingDurationMs = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_return_submissions_institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_return_submissions_return_periods_ReturnPeriodId",
                        column: x => x.ReturnPeriodId,
                        principalTable: "return_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_versions",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ChangeSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DdlScript = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RollbackScript = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_versions_return_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "meta",
                        principalTable: "return_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "validation_reports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinalizedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_validation_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_validation_reports_return_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "return_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intra_sheet_formulas",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateVersionId = table.Column<int>(type: "int", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RuleName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FormulaType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TargetFieldName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetLineCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OperandFields = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperandLineCodes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CustomExpression = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ToleranceAmount = table.Column<decimal>(type: "decimal(20,2)", nullable: false),
                    TolerancePercent = table.Column<decimal>(type: "decimal(10,4)", nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_intra_sheet_formulas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_intra_sheet_formulas_template_versions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalSchema: "meta",
                        principalTable: "template_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_fields",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateVersionId = table.Column<int>(type: "int", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    XmlElementName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LineCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SectionName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SectionOrder = table.Column<int>(type: "int", nullable: false),
                    FieldOrder = table.Column<int>(type: "int", nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SqlType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsComputed = table.Column<bool>(type: "bit", nullable: false),
                    IsKeyField = table.Column<bool>(type: "bit", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MinValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MaxValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MaxLength = table.Column<int>(type: "int", nullable: true),
                    AllowedValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReferenceTable = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ReferenceColumn = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    HelpText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsYtdField = table.Column<bool>(type: "bit", nullable: false),
                    YtdSourceFieldId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_fields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_fields_template_versions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalSchema: "meta",
                        principalTable: "template_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_item_codes",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateVersionId = table.Column<int>(type: "int", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ItemDescription = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsTotalRow = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_template_item_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_template_item_codes_template_versions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalSchema: "meta",
                        principalTable: "template_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "validation_errors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ValidationReportId = table.Column<int>(type: "int", nullable: false),
                    RuleId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ExpectedValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ActualValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReferencedReturnCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_validation_errors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_validation_errors_validation_reports_ValidationReportId",
                        column: x => x.ValidationReportId,
                        principalTable: "validation_reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_business_rules_RuleCode",
                schema: "meta",
                table: "business_rules",
                column: "RuleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cross_sheet_rule_expressions_RuleId",
                schema: "meta",
                table: "cross_sheet_rule_expressions",
                column: "RuleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cross_sheet_rule_operands_RuleId_OperandAlias",
                schema: "meta",
                table: "cross_sheet_rule_operands",
                columns: new[] { "RuleId", "OperandAlias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cross_sheet_rules_RuleCode",
                schema: "meta",
                table: "cross_sheet_rules",
                column: "RuleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_intra_sheet_formulas_TemplateVersionId_RuleCode",
                schema: "meta",
                table: "intra_sheet_formulas",
                columns: new[] { "TemplateVersionId", "RuleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_portal_users_Email",
                schema: "meta",
                table: "portal_users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_portal_users_Username",
                schema: "meta",
                table: "portal_users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_return_submissions_InstitutionId",
                table: "return_submissions",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_return_submissions_ReturnPeriodId",
                table: "return_submissions",
                column: "ReturnPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_return_templates_PhysicalTableName",
                schema: "meta",
                table: "return_templates",
                column: "PhysicalTableName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_return_templates_ReturnCode",
                schema: "meta",
                table: "return_templates",
                column: "ReturnCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_fields_TemplateVersionId_FieldName",
                schema: "meta",
                table: "template_fields",
                columns: new[] { "TemplateVersionId", "FieldName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_item_codes_TemplateVersionId_ItemCode",
                schema: "meta",
                table: "template_item_codes",
                columns: new[] { "TemplateVersionId", "ItemCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_sections_TemplateVersionId_SectionName",
                schema: "meta",
                table: "template_sections",
                columns: new[] { "TemplateVersionId", "SectionName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_template_versions_TemplateId_VersionNumber",
                schema: "meta",
                table: "template_versions",
                columns: new[] { "TemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_validation_errors_ValidationReportId",
                table: "validation_errors",
                column: "ValidationReportId");

            migrationBuilder.CreateIndex(
                name: "IX_validation_reports_SubmissionId",
                table: "validation_reports",
                column: "SubmissionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "business_rules",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "cross_sheet_rule_expressions",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "cross_sheet_rule_operands",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "ddl_migrations",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "intra_sheet_formulas",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "portal_users",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "template_fields",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "template_item_codes",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "template_sections",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "validation_errors");

            migrationBuilder.DropTable(
                name: "cross_sheet_rules",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "template_versions",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "validation_reports");

            migrationBuilder.DropTable(
                name: "return_templates",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "return_submissions");

            migrationBuilder.DropTable(
                name: "institutions");

            migrationBuilder.DropTable(
                name: "return_periods");
        }
    }
}
