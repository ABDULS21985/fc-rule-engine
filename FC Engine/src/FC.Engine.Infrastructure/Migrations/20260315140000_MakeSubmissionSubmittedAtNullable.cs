using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations;

/// <inheritdoc />
public partial class MakeSubmissionSubmittedAtNullable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Make SubmittedAt nullable so Draft submissions do not carry a spurious
        // "submitted at creation" timestamp. The column is populated by
        // Submission.MarkSubmitted() when the institution formally submits.
        migrationBuilder.AlterColumn<DateTime>(
            name: "SubmittedAt",
            table: "return_submissions",
            type: "datetime2(3)",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Back-fill nulls with the row's CreatedAt before re-adding the NOT NULL constraint.
        migrationBuilder.Sql(
            "UPDATE return_submissions SET SubmittedAt = CreatedAt WHERE SubmittedAt IS NULL");

        migrationBuilder.AlterColumn<DateTime>(
            name: "SubmittedAt",
            table: "return_submissions",
            type: "datetime2(3)",
            nullable: false,
            defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
            oldClrType: typeof(DateTime),
            oldNullable: true,
            oldType: "datetime2(3)");
    }
}
