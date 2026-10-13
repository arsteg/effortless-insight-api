using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EffortlessInsight.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261013100000_UniqueNoticeIdentity")]
public class UniqueNoticeIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"Notices\" SET \"NoticeNumber\" = COALESCE(NULLIF(btrim(\"GstnReferenceNumber\"), ''), NULLIF(btrim(\"GstnNoticeId\"), '')) WHERE NULLIF(btrim(\"NoticeNumber\"), '') IS NULL AND \"Source\" = 'gstn_portal'");
        // Fail atomically if legacy duplicates exist; never silently delete customer data.
        migrationBuilder.AddColumn<string>("IdentityGstin", "Notices", type: "text", nullable: true,
            computedColumnSql: "NULLIF(upper(btrim(\"Gstin\")), '')", stored: true);
        migrationBuilder.AddColumn<string>("IdentityReference", "Notices", type: "text", nullable: true,
            computedColumnSql: "NULLIF(upper(btrim(\"NoticeNumber\")), '')", stored: true);
        migrationBuilder.AddColumn<string>("IdentityPortal", "Notices", type: "text", nullable: true,
            computedColumnSql: "NULLIF(upper(btrim(\"GstnNoticeId\")), '')", stored: true);
        migrationBuilder.CreateIndex("UX_Notices_Identity", "Notices",
            new[] { "OrganizationId", "IdentityGstin", "IdentityReference" }, unique: true,
            filter: "\"DeletedAt\" IS NULL");
        migrationBuilder.CreateIndex("UX_Notices_Portal", "Notices",
            new[] { "OrganizationId", "IdentityGstin", "IdentityPortal" }, unique: true,
            filter: "\"DeletedAt\" IS NULL");
        migrationBuilder.CreateIndex("UX_Notices_Content", "Notices",
            new[] { "OrganizationId", "FileHash" }, unique: true,
            filter: "\"DeletedAt\" IS NULL AND \"FileHash\" IS NOT NULL AND \"FileHash\" <> ''");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("UX_Notices_Content", "Notices");
        migrationBuilder.DropIndex("UX_Notices_Portal", "Notices");
        migrationBuilder.DropIndex("UX_Notices_Identity", "Notices");
        migrationBuilder.DropColumn("IdentityPortal", "Notices");
        migrationBuilder.DropColumn("IdentityReference", "Notices");
        migrationBuilder.DropColumn("IdentityGstin", "Notices");
    }
}
