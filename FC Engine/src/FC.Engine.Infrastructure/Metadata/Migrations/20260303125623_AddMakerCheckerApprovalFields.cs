using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Metadata.Migrations
{
    /// <inheritdoc />
    public partial class AddMakerCheckerApprovalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OriginalSubmissionId",
                schema: "meta",
                table: "submission_approvals",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubmitterNotes",
                schema: "meta",
                table: "submission_approvals",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MakerCheckerEnabled",
                table: "institutions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalSubmissionId",
                schema: "meta",
                table: "submission_approvals");

            migrationBuilder.DropColumn(
                name: "SubmitterNotes",
                schema: "meta",
                table: "submission_approvals");

            migrationBuilder.DropColumn(
                name: "MakerCheckerEnabled",
                table: "institutions");
        }
    }
}
