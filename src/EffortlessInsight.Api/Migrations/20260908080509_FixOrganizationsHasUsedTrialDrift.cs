using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <summary>
    /// Schema-drift repair: Organization.HasUsedTrial exists in the entity and
    /// model snapshot, but the migration that should have added the column was
    /// lost (same snapshot merge drift as the IX_OrganizationGstins_Gstin
    /// incident). Databases built purely from migrations therefore lack the
    /// column and login/subscription queries fail with 42703. IF NOT EXISTS
    /// makes this a no-op on databases where the column was added out-of-band.
    /// </summary>
    public partial class FixOrganizationsHasUsedTrialDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"Organizations\" ADD COLUMN IF NOT EXISTS \"HasUsedTrial\" boolean NOT NULL DEFAULT FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally left as a no-op: rolling back must not drop a
            // column that may have pre-existed this repair on some databases.
        }
    }
}
