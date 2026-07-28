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
            // Use conditional SQL to add columns only if they don't exist
            // This makes the migration idempotent and avoids errors from partial previous runs
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'CorrelationId') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""CorrelationId"" character varying(50);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'FullPhoneNumber') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""FullPhoneNumber"" character varying(20);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'IsRetryable') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""IsRetryable"" boolean NOT NULL DEFAULT false;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'MaxRetryAttempts') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""MaxRetryAttempts"" integer NOT NULL DEFAULT 0;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'NextRetryAt') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""NextRetryAt"" timestamp with time zone;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'ReferenceId') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""ReferenceId"" uuid;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'ReferenceType') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""ReferenceType"" character varying(50);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'TemplateLanguage') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""TemplateLanguage"" character varying(10);
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'WhatsAppMessageLogs' AND column_name = 'TemplateParameters') THEN
                        ALTER TABLE ""WhatsAppMessageLogs"" ADD COLUMN ""TemplateParameters"" jsonb;
                    END IF;
                END $$;
            ");

            // Create table only if it doesn't exist
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""WhatsAppWebhookEvents"" (
                    ""PayloadHash"" character varying(64) NOT NULL,
                    ""EntryId"" character varying(100),
                    ""EventType"" character varying(20) NOT NULL,
                    ""ReceivedAt"" timestamp with time zone NOT NULL,
                    ""ProcessedAt"" timestamp with time zone,
                    ""ProcessingResult"" character varying(20),
                    ""ErrorMessage"" character varying(500),
                    CONSTRAINT ""PK_WhatsAppWebhookEvents"" PRIMARY KEY (""PayloadHash"")
                );
            ");
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
