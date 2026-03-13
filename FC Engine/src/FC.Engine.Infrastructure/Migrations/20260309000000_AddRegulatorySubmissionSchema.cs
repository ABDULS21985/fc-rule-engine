using System;
using FC.Engine.Infrastructure.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FC.Engine.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MetadataDbContext))]
    [Migration("20260309000000_AddRegulatorySubmissionSchema")]
    public partial class AddRegulatorySubmissionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── RegulatoryChannels ────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "regulatory_channels",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    RegulatorName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    PortalName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IntegrationMethod = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                    RequiresCertificate = table.Column<bool>(nullable: false, defaultValue: true),
                    MaxRetriesOverride = table.Column<int>(nullable: true),
                    TimeoutSeconds = table.Column<int>(nullable: false, defaultValue: 120),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regulatory_channels", x => x.Id);
                    table.UniqueConstraint("UQ_regulatory_channels_code", x => x.RegulatorCode);
                });

            // ── SubmissionBatches ─────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "submission_batches",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstitutionId = table.Column<int>(nullable: false),
                    BatchReference = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ChannelId = table.Column<int>(nullable: false),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false,
                        defaultValue: "PENDING"),
                    SubmittedBy = table.Column<int>(nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    FinalStatusAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CorrelationId = table.Column<Guid>(nullable: false),
                    RetryCount = table.Column<int>(nullable: false, defaultValue: 0),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_batches", x => x.Id);
                    table.UniqueConstraint("UQ_submission_batches_ref", x => x.BatchReference);
                    table.ForeignKey(
                        name: "FK_submission_batches_channel",
                        column: x => x.ChannelId,
                        principalSchema: "meta",
                        principalTable: "regulatory_channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_submission_batches_institution_status",
                schema: "meta",
                table: "submission_batches",
                columns: ["InstitutionId", "Status"]);

            migrationBuilder.CreateIndex(
                name: "IX_submission_batches_regulator",
                schema: "meta",
                table: "submission_batches",
                columns: ["RegulatorCode", "Status"]);

            // ── SubmissionItems ───────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "submission_items",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<long>(nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    SubmissionId = table.Column<int>(nullable: false),
                    ReturnCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    ReturnVersion = table.Column<int>(nullable: false),
                    ReportingPeriod = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ExportFormat = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ExportPayloadHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ExportPayloadSize = table.Column<long>(nullable: false),
                    EvidencePackageId = table.Column<long>(nullable: true),
                    Status = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false,
                        defaultValue: "PENDING"),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    RegulatorReference = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_items", x => x.Id);
                    table.UniqueConstraint("UQ_submission_items_idempotent",
                        x => new { x.SubmissionId, x.RegulatorCode, x.ReturnVersion });
                    table.ForeignKey(
                        name: "FK_submission_items_batch",
                        column: x => x.BatchId,
                        principalSchema: "meta",
                        principalTable: "submission_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_submission_items_submission",
                schema: "meta",
                table: "submission_items",
                column: "SubmissionId");

            // ── SubmissionSignatures ──────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "submission_signatures",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubmissionItemId = table.Column<long>(nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    CertificateThumbprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SignatureAlgorithm = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    SignatureValue = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    SignedDataHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    SignedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    TimestampToken = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    IsValid = table.Column<bool>(nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_signatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_signatures_item",
                        column: x => x.SubmissionItemId,
                        principalSchema: "meta",
                        principalTable: "submission_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_submission_signatures_cert",
                schema: "meta",
                table: "submission_signatures",
                column: "CertificateThumbprint");

            // ── SubmissionBatchReceipts ───────────────────────────────────
            migrationBuilder.CreateTable(
                name: "submission_batch_receipts",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<long>(nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    ReceiptReference = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    ReceiptTimestamp = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    RawResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpStatusCode = table.Column<int>(nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_batch_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submission_batch_receipts_batch",
                        column: x => x.BatchId,
                        principalSchema: "meta",
                        principalTable: "submission_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_submission_batch_receipts_ref",
                schema: "meta",
                table: "submission_batch_receipts",
                column: "ReceiptReference");

            // ── RegulatoryQueryRecords ────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "regulatory_query_records",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<long>(nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    RegulatorCode = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false),
                    QueryReference = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    QueryType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    QueryText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DueDate = table.Column<DateOnly>(nullable: true),
                    Priority = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false,
                        defaultValue: "NORMAL"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false,
                        defaultValue: "OPEN"),
                    AssignedToUserId = table.Column<int>(nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    RespondedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regulatory_query_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_regulatory_query_records_batch",
                        column: x => x.BatchId,
                        principalSchema: "meta",
                        principalTable: "submission_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_regulatory_query_records_institution",
                schema: "meta",
                table: "regulatory_query_records",
                columns: ["InstitutionId", "Status"]);

            migrationBuilder.CreateIndex(
                name: "IX_regulatory_query_records_due",
                schema: "meta",
                table: "regulatory_query_records",
                columns: ["DueDate", "Status"]);

            // ── QueryResponses ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "query_responses",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueryId = table.Column<long>(nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttachmentCount = table.Column<int>(nullable: false, defaultValue: 0),
                    SubmittedToRegulator = table.Column<bool>(nullable: false, defaultValue: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RegulatorAckRef = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    CreatedBy = table.Column<int>(nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_query_responses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_query_responses_query",
                        column: x => x.QueryId,
                        principalSchema: "meta",
                        principalTable: "regulatory_query_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── QueryResponseAttachments ──────────────────────────────────
            migrationBuilder.CreateTable(
                name: "query_response_attachments",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QueryResponseId = table.Column<long>(nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(nullable: false),
                    BlobStoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_query_response_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_query_response_attachments_response",
                        column: x => x.QueryResponseId,
                        principalSchema: "meta",
                        principalTable: "query_responses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── SubmissionBatchAuditLog ───────────────────────────────────
            migrationBuilder.CreateTable(
                name: "submission_batch_audit_log",
                schema: "meta",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<long>(nullable: false),
                    InstitutionId = table.Column<int>(nullable: false),
                    CorrelationId = table.Column<Guid>(nullable: false),
                    Action = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PerformedBy = table.Column<int>(nullable: true),
                    PerformedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_batch_audit_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_submission_batch_audit_log_batch",
                schema: "meta",
                table: "submission_batch_audit_log",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_batch_audit_log_correlation",
                schema: "meta",
                table: "submission_batch_audit_log",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_submission_batch_audit_log_time",
                schema: "meta",
                table: "submission_batch_audit_log",
                column: "PerformedAt",
                descending: [true]);

            // ── Seed: RegulatoryChannels ──────────────────────────────────
            migrationBuilder.Sql(
                """
                SET IDENTITY_INSERT [meta].[regulatory_channels] ON;

                IF NOT EXISTS (SELECT 1 FROM [meta].[regulatory_channels] WHERE [RegulatorCode] = 'CBN')
                INSERT INTO [meta].[regulatory_channels]
                    ([Id], [RegulatorCode], [RegulatorName], [PortalName], [IntegrationMethod], [RequiresCertificate], [TimeoutSeconds], [IsActive], [CreatedAt], [UpdatedAt])
                VALUES
                    (1, 'CBN', 'Central Bank of Nigeria', 'eFASS', 'REST', 1, 120, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [meta].[regulatory_channels] WHERE [RegulatorCode] = 'NDIC')
                INSERT INTO [meta].[regulatory_channels]
                    ([Id], [RegulatorCode], [RegulatorName], [PortalName], [IntegrationMethod], [RequiresCertificate], [TimeoutSeconds], [IsActive], [CreatedAt], [UpdatedAt])
                VALUES
                    (2, 'NDIC', 'Nigeria Deposit Insurance Corporation', 'NDIC Portal', 'REST', 1, 120, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [meta].[regulatory_channels] WHERE [RegulatorCode] = 'NFIU')
                INSERT INTO [meta].[regulatory_channels]
                    ([Id], [RegulatorCode], [RegulatorName], [PortalName], [IntegrationMethod], [RequiresCertificate], [TimeoutSeconds], [IsActive], [CreatedAt], [UpdatedAt])
                VALUES
                    (3, 'NFIU', 'Nigerian Financial Intelligence Unit', 'goAML', 'XML_UPLOAD', 1, 180, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [meta].[regulatory_channels] WHERE [RegulatorCode] = 'SEC')
                INSERT INTO [meta].[regulatory_channels]
                    ([Id], [RegulatorCode], [RegulatorName], [PortalName], [IntegrationMethod], [RequiresCertificate], [TimeoutSeconds], [IsActive], [CreatedAt], [UpdatedAt])
                VALUES
                    (4, 'SEC', 'Securities and Exchange Commission Nigeria', 'SEC e-Filing', 'REST', 1, 90, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [meta].[regulatory_channels] WHERE [RegulatorCode] = 'NAICOM')
                INSERT INTO [meta].[regulatory_channels]
                    ([Id], [RegulatorCode], [RegulatorName], [PortalName], [IntegrationMethod], [RequiresCertificate], [TimeoutSeconds], [IsActive], [CreatedAt], [UpdatedAt])
                VALUES
                    (5, 'NAICOM', 'National Insurance Commission', 'NAICOM Portal', 'REST', 1, 120, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

                IF NOT EXISTS (SELECT 1 FROM [meta].[regulatory_channels] WHERE [RegulatorCode] = 'PENCOM')
                INSERT INTO [meta].[regulatory_channels]
                    ([Id], [RegulatorCode], [RegulatorName], [PortalName], [IntegrationMethod], [RequiresCertificate], [TimeoutSeconds], [IsActive], [CreatedAt], [UpdatedAt])
                VALUES
                    (6, 'PENCOM', 'National Pension Commission', 'PenCom Portal', 'REST', 1, 90, 1, SYSUTCDATETIME(), SYSUTCDATETIME());

                SET IDENTITY_INSERT [meta].[regulatory_channels] OFF;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "submission_batch_audit_log",   schema: "meta");
            migrationBuilder.DropTable(name: "query_response_attachments",   schema: "meta");
            migrationBuilder.DropTable(name: "query_responses",              schema: "meta");
            migrationBuilder.DropTable(name: "regulatory_query_records",     schema: "meta");
            migrationBuilder.DropTable(name: "submission_batch_receipts",    schema: "meta");
            migrationBuilder.DropTable(name: "submission_signatures",        schema: "meta");
            migrationBuilder.DropTable(name: "submission_items",             schema: "meta");
            migrationBuilder.DropTable(name: "submission_batches",           schema: "meta");
            migrationBuilder.DropTable(name: "regulatory_channels",         schema: "meta");
        }
    }
}
