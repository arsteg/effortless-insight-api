using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookEventDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The original AddWebhookEventsTable migration created "WebhookEvents" without the
            // BaseEntity "DeletedAt" column, and AddMissingBillingTables2 guarded its (correct)
            // definition behind IF NOT EXISTS, so the column was never added to existing databases.
            // Add it idempotently so both drifted and freshly-migrated databases match the model.
            //
            // ALTER TABLE **IF EXISTS**: this migration's timestamp (2026-07-01) sorts BEFORE
            // 20260702082724_InitialMigration, so on a FRESH database it runs before the table
            // exists and must be a no-op (InitialMigration creates WebhookEvents with DeletedAt
            // already included). On pre-reset databases the table exists and the column is added.
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""WebhookEvents"" ADD COLUMN IF NOT EXISTS ""DeletedAt"" timestamp with time zone;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""WebhookEvents"" DROP COLUMN IF EXISTS ""DeletedAt"";");
        }
    }
}
