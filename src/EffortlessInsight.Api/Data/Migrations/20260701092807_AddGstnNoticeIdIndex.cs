using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGstnNoticeIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Notices_Org_GstnNoticeId",
                table: "Notices",
                columns: new[] { "OrganizationId", "GstnNoticeId" },
                filter: "\"DeletedAt\" IS NULL AND \"GstnNoticeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notices_Org_GstnNoticeId",
                table: "Notices");
        }
    }
}
