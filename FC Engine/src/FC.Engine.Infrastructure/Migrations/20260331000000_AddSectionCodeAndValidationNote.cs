using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations;

/// <summary>
/// Resolves two field-meaning mismatches found in the Modules CRUD audit:
///
/// 1. meta.template_sections — adds SectionCode (the machine-readable identifier
///    referenced by TemplateField.SectionName for section grouping). Previously the
///    import service stored the code in the Description column, making it impossible
///    to distinguish a section's identifier from its human-readable description.
///    The unique index is moved from (TemplateVersionId, SectionName) to
///    (TemplateVersionId, SectionCode) since SectionCode is the natural key.
///
/// 2. meta.template_fields — adds ValidationNote (the operator-facing validation rule
///    description from the module definition JSON, e.g. "Must be ≥ 0"). Previously
///    ValidationNote was coalesced into HelpText (?? fallback), silently losing the
///    original HelpText value when both were supplied.
///
/// Both columns are nullable so existing rows are not affected.
/// </summary>
[DbContext(typeof(MetadataDbContext))]
[Migration("20260331000000_AddSectionCodeAndValidationNote")]
public partial class AddSectionCodeAndValidationNote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── template_sections ────────────────────────────────────────────────

        migrationBuilder.AddColumn<string>(
            name: "SectionCode",
            schema: "meta",
            table: "template_sections",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);   // nullable so existing rows are not broken

        // Back-fill existing rows: use SectionName as a placeholder code.
        // Operators importing new modules will always get a proper SectionCode.
        migrationBuilder.Sql("""
            UPDATE [meta].[template_sections]
            SET [SectionCode] = [SectionName]
            WHERE [SectionCode] IS NULL;
            """);

        // Now make it non-nullable after back-fill.
        migrationBuilder.AlterColumn<string>(
            name: "SectionCode",
            schema: "meta",
            table: "template_sections",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100,
            oldNullable: true);

        // Drop the old unique index on SectionName; SectionCode is now the semantic key.
        migrationBuilder.DropIndex(
            name: "IX_template_sections_TemplateVersionId_SectionName",
            schema: "meta",
            table: "template_sections");

        migrationBuilder.CreateIndex(
            name: "IX_template_sections_TemplateVersionId_SectionCode",
            schema: "meta",
            table: "template_sections",
            columns: new[] { "TemplateVersionId", "SectionCode" },
            unique: true);

        // ── template_fields ──────────────────────────────────────────────────

        migrationBuilder.AddColumn<string>(
            name: "ValidationNote",
            schema: "meta",
            table: "template_fields",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ── template_fields ──────────────────────────────────────────────────
        migrationBuilder.DropColumn(
            name: "ValidationNote",
            schema: "meta",
            table: "template_fields");

        // ── template_sections ────────────────────────────────────────────────
        migrationBuilder.DropIndex(
            name: "IX_template_sections_TemplateVersionId_SectionCode",
            schema: "meta",
            table: "template_sections");

        migrationBuilder.CreateIndex(
            name: "IX_template_sections_TemplateVersionId_SectionName",
            schema: "meta",
            table: "template_sections",
            columns: new[] { "TemplateVersionId", "SectionName" },
            unique: true);

        migrationBuilder.DropColumn(
            name: "SectionCode",
            schema: "meta",
            table: "template_sections");
    }
}
