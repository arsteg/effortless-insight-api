using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemSettingsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The index was already dropped by 20260803094915_LinkGstClientsToOrganizationGstins;
            // dotnet ef re-emitted the drop because the model snapshot had regained the index
            // (snapshot merge drift). Use IF EXISTS so this is a no-op on databases where it
            // is already gone and still cleans up databases where it lingers.
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_OrganizationGstins_Gstin\";");

            migrationBuilder.AddColumn<DateTime>(
                name: "MandateExpiresAt",
                table: "PaymentMethods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MandateId",
                table: "PaymentMethods",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MandateMaxAmount",
                table: "PaymentMethods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MandateStatus",
                table: "PaymentMethods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PromptVersion",
                table: "AIAuditLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DataType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByAdminId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MandateExpiresAt",
                table: "PaymentMethods");

            migrationBuilder.DropColumn(
                name: "MandateId",
                table: "PaymentMethods");

            migrationBuilder.DropColumn(
                name: "MandateMaxAmount",
                table: "PaymentMethods");

            migrationBuilder.DropColumn(
                name: "MandateStatus",
                table: "PaymentMethods");

            migrationBuilder.AlterColumn<string>(
                name: "PromptVersion",
                table: "AIAuditLogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            // Intentionally NOT recreating IX_OrganizationGstins_Gstin: the database state
            // before this migration (after 20260803094915_LinkGstClientsToOrganizationGstins)
            // does not have the index, so rolling back must not reintroduce it.
        }
    }
}
