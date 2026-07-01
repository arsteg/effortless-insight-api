using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppBot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add WhatsApp fields to AspNetUsers
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppPhoneNumber",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppVerified",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppVerifiedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppOptedIn",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppOptedInAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppLastMessageAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            // Create WhatsAppVerifications table
            migrationBuilder.CreateTable(
                name: "WhatsAppVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerificationCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    InitiatedFrom = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "app"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppVerifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create WhatsAppSessions table
            migrationBuilder.CreateTable(
                name: "WhatsAppSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentState = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "start"),
                    PendingEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PendingVerificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Context = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    LastInteractionAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentPage = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MessageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Create WhatsAppMessageLogs table
            migrationBuilder.CreateTable(
                name: "WhatsAppMessageLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MessageType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    WamId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Content = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Command = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TemplateName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "sent"),
                    ErrorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProcessingTimeMs = table.Column<int>(type: "integer", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppMessageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessageLogs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WhatsAppMessageLogs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Create WhatsAppTemplates table
            migrationBuilder.CreateTable(
                name: "WhatsAppTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "en"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HeaderFormat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    HeaderText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BodyText = table.Column<string>(type: "text", nullable: false),
                    FooterText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Variables = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    Buttons = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    UsageCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppTemplates", x => x.Id);
                });

            // Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppVerifications_UserId",
                table: "WhatsAppVerifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppVerifications_PhoneNumber",
                table: "WhatsAppVerifications",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppSessions_PhoneNumber",
                table: "WhatsAppSessions",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppSessions_UserId",
                table: "WhatsAppSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_UserId",
                table: "WhatsAppMessageLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_OrganizationId",
                table: "WhatsAppMessageLogs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_WamId",
                table: "WhatsAppMessageLogs",
                column: "WamId");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppMessageLogs_CreatedAt",
                table: "WhatsAppMessageLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppTemplates_Name_Language",
                table: "WhatsAppTemplates",
                columns: new[] { "Name", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WhatsAppPhoneNumber",
                table: "AspNetUsers",
                column: "WhatsAppPhoneNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WhatsAppTemplates");
            migrationBuilder.DropTable(name: "WhatsAppMessageLogs");
            migrationBuilder.DropTable(name: "WhatsAppSessions");
            migrationBuilder.DropTable(name: "WhatsAppVerifications");

            migrationBuilder.DropIndex(name: "IX_AspNetUsers_WhatsAppPhoneNumber", table: "AspNetUsers");

            migrationBuilder.DropColumn(name: "WhatsAppPhoneNumber", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "WhatsAppVerified", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "WhatsAppVerifiedAt", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "WhatsAppOptedIn", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "WhatsAppOptedInAt", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "WhatsAppLastMessageAt", table: "AspNetUsers");
        }
    }
}
