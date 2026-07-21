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
            migrationBuilder.Sql(@"ALTER TABLE ""WebhookEvents"" ADD COLUMN IF NOT EXISTS ""DeletedAt"" timestamp with time zone;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""WebhookEvents"" DROP COLUMN IF EXISTS ""DeletedAt"";");
        }
    }
}
