using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenEmailSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailImportKey",
                table: "Notices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CursorStart",
                table: "EmailSyncLogs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CursorEnd",
                table: "EmailSyncLogs",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RefreshTokenEncrypted",
                table: "ConnectedEmailAccounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastSyncCursor",
                table: "ConnectedEmailAccounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AccessTokenEncrypted",
                table: "ConnectedEmailAccounts",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastScheduledSyncDay",
                table: "ConnectedEmailAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailImportReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectedEmailAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttachmentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AiUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailImportReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailImportReceipts_ConnectedEmailAccounts_ConnectedEmailAc~",
                        column: x => x.ConnectedEmailAccountId,
                        principalTable: "ConnectedEmailAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_OrganizationId_EmailImportKey",
                table: "Notices",
                columns: new[] { "OrganizationId", "EmailImportKey" },
                unique: true,
                filter: "\"EmailImportKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmailImportReceipts_ConnectedEmailAccountId_ItemKey",
                table: "EmailImportReceipts",
                columns: new[] { "ConnectedEmailAccountId", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailImportReceipts_OrganizationId_AttachmentHash",
                table: "EmailImportReceipts",
                columns: new[] { "OrganizationId", "AttachmentHash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailImportReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Notices_OrganizationId_EmailImportKey",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EmailImportKey",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "LastScheduledSyncDay",
                table: "ConnectedEmailAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "CursorStart",
                table: "EmailSyncLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CursorEnd",
                table: "EmailSyncLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RefreshTokenEncrypted",
                table: "ConnectedEmailAccounts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LastSyncCursor",
                table: "ConnectedEmailAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AccessTokenEncrypted",
                table: "ConnectedEmailAccounts",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
