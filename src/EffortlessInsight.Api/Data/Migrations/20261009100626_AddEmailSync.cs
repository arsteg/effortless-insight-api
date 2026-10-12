using System;
using System.Collections.Generic;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentHash",
                table: "Notices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectedEmailAccountId",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailMessageId",
                table: "Notices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailProvider",
                table: "Notices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailProviderMessageId",
                table: "Notices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailReceivedAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSender",
                table: "Notices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailSubject",
                table: "Notices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConnectedEmailAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AccessTokenEncrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RefreshTokenEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Scopes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncCursor = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastSyncResult = table.Column<EmailSyncResult>(type: "jsonb", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    AutoSyncEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectedEmailAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConnectedEmailAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConnectedEmailAccounts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailSyncLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedEmailAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggerSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggeredById = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    EmailsScanned = table.Column<int>(type: "integer", nullable: false),
                    NoticesImported = table.Column<int>(type: "integer", nullable: false),
                    AttachmentsSkipped = table.Column<int>(type: "integer", nullable: false),
                    DuplicatesSkipped = table.Column<int>(type: "integer", nullable: false),
                    NoticesFailed = table.Column<int>(type: "integer", nullable: false),
                    AiDetectionUsed = table.Column<int>(type: "integer", nullable: false),
                    SyncPeriodFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncPeriodTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CursorStart = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CursorEnd = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ErrorDetails = table.Column<string>(type: "text", nullable: true),
                    ImportedNoticeIds = table.Column<List<Guid>>(type: "jsonb", nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSyncLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSyncLogs_AspNetUsers_TriggeredById",
                        column: x => x.TriggeredById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EmailSyncLogs_ConnectedEmailAccounts_ConnectedEmailAccountId",
                        column: x => x.ConnectedEmailAccountId,
                        principalTable: "ConnectedEmailAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_AttachmentHash",
                table: "Notices",
                column: "AttachmentHash",
                filter: "\"AttachmentHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_ConnectedEmailAccountId",
                table: "Notices",
                column: "ConnectedEmailAccountId",
                filter: "\"ConnectedEmailAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_EmailProvider_MessageId",
                table: "Notices",
                columns: new[] { "EmailProviderMessageId", "EmailProvider" },
                filter: "\"EmailProviderMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedEmailAccounts_AutoSyncEnabled",
                table: "ConnectedEmailAccounts",
                column: "AutoSyncEnabled",
                filter: "\"Status\" = 'connected' AND \"AutoSyncEnabled\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedEmailAccounts_Org_Email_Provider_Unique",
                table: "ConnectedEmailAccounts",
                columns: new[] { "OrganizationId", "Email", "Provider" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedEmailAccounts_Org_Status",
                table: "ConnectedEmailAccounts",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedEmailAccounts_Status",
                table: "ConnectedEmailAccounts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedEmailAccounts_TokenExpiresAt",
                table: "ConnectedEmailAccounts",
                column: "TokenExpiresAt",
                filter: "\"Status\" = 'connected'");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedEmailAccounts_UserId",
                table: "ConnectedEmailAccounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncLogs_ConnectedEmailAccountId",
                table: "EmailSyncLogs",
                column: "ConnectedEmailAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncLogs_CreatedAt",
                table: "EmailSyncLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncLogs_StartedAt",
                table: "EmailSyncLogs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncLogs_Status",
                table: "EmailSyncLogs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSyncLogs_TriggeredById",
                table: "EmailSyncLogs",
                column: "TriggeredById");

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_ConnectedEmailAccounts_ConnectedEmailAccountId",
                table: "Notices",
                column: "ConnectedEmailAccountId",
                principalTable: "ConnectedEmailAccounts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notices_ConnectedEmailAccounts_ConnectedEmailAccountId",
                table: "Notices");

            migrationBuilder.DropTable(
                name: "EmailSyncLogs");

            migrationBuilder.DropTable(
                name: "ConnectedEmailAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Notices_AttachmentHash",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_ConnectedEmailAccountId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_EmailProvider_MessageId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "AttachmentHash",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "ConnectedEmailAccountId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EmailMessageId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EmailProvider",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EmailProviderMessageId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EmailReceivedAt",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EmailSender",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EmailSubject",
                table: "Notices");
        }
    }
}
