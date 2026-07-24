using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppMessageLogColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "WhatsAppMessageLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullPhoneNumber",
                table: "WhatsAppMessageLogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRetryable",
                table: "WhatsAppMessageLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxRetryAttempts",
                table: "WhatsAppMessageLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRetryAt",
                table: "WhatsAppMessageLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReferenceId",
                table: "WhatsAppMessageLogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceType",
                table: "WhatsAppMessageLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateLanguage",
                table: "WhatsAppMessageLogs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "TemplateParameters",
                table: "WhatsAppMessageLogs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsAppWebhookEvents",
                columns: table => new
                {
                    PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EventType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingResult = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppWebhookEvents", x => x.PayloadHash);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppWebhookEvents");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "FullPhoneNumber",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "IsRetryable",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "MaxRetryAttempts",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "NextRetryAt",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "ReferenceId",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "ReferenceType",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "TemplateLanguage",
                table: "WhatsAppMessageLogs");

            migrationBuilder.DropColumn(
                name: "TemplateParameters",
                table: "WhatsAppMessageLogs");
        }
    }
}
