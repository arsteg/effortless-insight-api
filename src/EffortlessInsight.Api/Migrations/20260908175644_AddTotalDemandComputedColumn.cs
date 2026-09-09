using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTotalDemandComputedColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop existing TotalDemand column (regular nullable decimal)
            migrationBuilder.DropColumn(
                name: "TotalDemand",
                table: "Notices");

            // Add TotalDemand as a PostgreSQL STORED GENERATED column
            // This computes the sum of TaxAmount, PenaltyAmount, and InterestAmount
            migrationBuilder.Sql(@"
                ALTER TABLE ""Notices""
                ADD COLUMN ""TotalDemand"" decimal(15,2)
                GENERATED ALWAYS AS (
                    COALESCE(""TaxAmount"", 0) +
                    COALESCE(""PenaltyAmount"", 0) +
                    COALESCE(""InterestAmount"", 0)
                ) STORED;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop the computed column
            migrationBuilder.DropColumn(
                name: "TotalDemand",
                table: "Notices");

            // Recreate as a regular nullable decimal column
            migrationBuilder.AddColumn<decimal>(
                name: "TotalDemand",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true);
        }
    }
}
