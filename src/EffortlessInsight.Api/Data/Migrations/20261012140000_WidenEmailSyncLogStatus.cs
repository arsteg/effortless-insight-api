using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EffortlessInsight.Api.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261012140000_WidenEmailSyncLogStatus")]
public class WidenEmailSyncLogStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(name: "Status", table: "EmailSyncLogs",
            type: "character varying(30)", maxLength: 30, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        // PostgreSQL will reject the downgrade if longer statuses remain;
        // do not silently truncate or rewrite historical outcomes.
        migrationBuilder.AlterColumn<string>(name: "Status", table: "EmailSyncLogs",
            type: "character varying(20)", maxLength: 20, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(30)", oldMaxLength: 30);
}
