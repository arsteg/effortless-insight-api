using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class PreserveSessionOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Some databases already contain the column without this migration's
            // history entry. Preserve it, but do not silently accept an incompatible schema.
            migrationBuilder.Sql("""
                ALTER TABLE "UserSessions"
                    ADD COLUMN IF NOT EXISTS "OrganizationId" uuid NULL;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_attribute
                        WHERE attrelid = '"UserSessions"'::regclass
                          AND attname = 'OrganizationId'
                          AND atttypid = 'uuid'::regtype
                          AND NOT attnotnull
                          AND NOT attisdropped
                    ) THEN
                        RAISE EXCEPTION 'UserSessions.OrganizationId must be a nullable uuid column';
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OrganizationId", table: "UserSessions");
        }
    }
}
