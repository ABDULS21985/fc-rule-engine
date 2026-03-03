using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionUsersAndApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ApprovalRequired",
                table: "return_submissions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SubmittedByUserId",
                table: "return_submissions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "institutions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "institutions",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "institutions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSubmissionAt",
                table: "institutions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxUsersAllowed",
                table: "institutions",
                type: "int",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionTier",
                table: "institutions",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Basic");

            migrationBuilder.CreateTable(
                name: "institution_users",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(type: "int", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    FailedLoginAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_institution_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_institution_users_institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission_approvals",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReviewerComments = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_approvals_institution_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "meta",
                        principalTable: "institution_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_approvals_institution_users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalSchema: "meta",
                        principalTable: "institution_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submission_approvals_return_submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "return_submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_institution_users_Email",
                schema: "meta",
                table: "institution_users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_institution_users_InstitutionId",
                schema: "meta",
                table: "institution_users",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_institution_users_Username",
                schema: "meta",
                table: "institution_users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submission_approvals_RequestedByUserId",
                schema: "meta",
                table: "submission_approvals",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_approvals_ReviewedByUserId",
                schema: "meta",
                table: "submission_approvals",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_approvals_Status",
                schema: "meta",
                table: "submission_approvals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_submission_approvals_SubmissionId",
                schema: "meta",
                table: "submission_approvals",
                column: "SubmissionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "submission_approvals",
                schema: "meta");

            migrationBuilder.DropTable(
                name: "institution_users",
                schema: "meta");

            migrationBuilder.DropColumn(
                name: "ApprovalRequired",
                table: "return_submissions");

            migrationBuilder.DropColumn(
                name: "SubmittedByUserId",
                table: "return_submissions");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "LastSubmissionAt",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "MaxUsersAllowed",
                table: "institutions");

            migrationBuilder.DropColumn(
                name: "SubscriptionTier",
                table: "institutions");
        }
    }
}
