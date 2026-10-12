using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EffortlessInsight.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261012130000_AllowDeletedEmailNoticeReimport")]
public class AllowDeletedEmailNoticeReimport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_Notices_OrganizationId_EmailImportKey", "Notices");
        migrationBuilder.CreateIndex("IX_Notices_OrganizationId_EmailImportKey", "Notices",
            new[] { "OrganizationId", "EmailImportKey" }, unique: true,
            filter: "\"EmailImportKey\" IS NOT NULL AND \"DeletedAt\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reimports may now share a key with deleted rows. Keep historical rows,
        // but release their idempotency keys before restoring the old index.
        migrationBuilder.Sql("UPDATE \"Notices\" SET \"EmailImportKey\" = NULL WHERE \"DeletedAt\" IS NOT NULL");
        migrationBuilder.DropIndex("IX_Notices_OrganizationId_EmailImportKey", "Notices");
        migrationBuilder.CreateIndex("IX_Notices_OrganizationId_EmailImportKey", "Notices",
            new[] { "OrganizationId", "EmailImportKey" }, unique: true,
            filter: "\"EmailImportKey\" IS NOT NULL");
    }
}
