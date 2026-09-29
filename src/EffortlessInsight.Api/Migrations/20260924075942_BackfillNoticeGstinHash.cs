using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <summary>
    /// Backfills GstinHash for existing notices that have a Gstin value but null GstinHash.
    /// This is required for cross-organization notice visibility to work correctly.
    /// The GstinHash is computed as SHA-256 of the uppercase-trimmed GSTIN.
    /// </summary>
    public partial class BackfillNoticeGstinHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill GstinHash for existing notices with Gstin but missing GstinHash
            // Uses SHA-256 hash of the normalized (uppercase, trimmed) GSTIN
            // This matches the computation in ICrossOrgNoticeVisibilityService.ComputeGstinHash
            migrationBuilder.Sql(@"
                UPDATE ""Notices""
                SET ""GstinHash"" = encode(sha256(upper(trim(""Gstin""))::bytea), 'hex')
                WHERE ""Gstin"" IS NOT NULL
                  AND ""Gstin"" != ''
                  AND ""GstinHash"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot safely reverse this - we don't know which GstinHash values
            // were set by this migration vs. set by application code.
            // Leaving GstinHash values in place is safe (they're just redundant).
        }
    }
}
