using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationGstinSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "OrganizationGstins",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "manual");

            // Backfill existing rows: the primary GSTIN was registered during
            // org creation ('onboarding'); the rest were added via Settings
            // ('manual', already the column default). Sync-created entries
            // ('gst_sync') only exist from this release onward.
            migrationBuilder.Sql(
                "UPDATE \"OrganizationGstins\" SET \"Source\" = 'onboarding' WHERE \"IsPrimary\" = true;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Source",
                table: "OrganizationGstins");
        }
    }
}
