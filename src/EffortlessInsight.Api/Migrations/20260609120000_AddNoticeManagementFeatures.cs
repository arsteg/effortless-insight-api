using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNoticeManagementFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============================================================================
            // Add new columns to Notices table
            // ============================================================================

            // Notice sub-category
            migrationBuilder.AddColumn<string>(
                name: "NoticeSubCategory",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // GSTIN relationship
            migrationBuilder.AddColumn<Guid>(
                name: "GstinId",
                table: "Notices",
                type: "uuid",
                nullable: true);

            // Hearing date
            migrationBuilder.AddColumn<DateOnly>(
                name: "HearingDate",
                table: "Notices",
                type: "date",
                nullable: true);

            // Financial year
            migrationBuilder.AddColumn<string>(
                name: "FinancialYear",
                table: "Notices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // Officer details
            migrationBuilder.AddColumn<string>(
                name: "OfficerDesignation",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Jurisdiction",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Assignment tracking
            migrationBuilder.AddColumn<Guid>(
                name: "AssignedById",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            // File details
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "Notices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FileSize",
                table: "Notices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FileMimeType",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "Notices",
                type: "integer",
                nullable: true);

            // OCR language
            migrationBuilder.AddColumn<string>(
                name: "OcrLanguage",
                table: "Notices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // Processing tracking
            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingCompletedAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "Notices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Soft delete tracking
            migrationBuilder.AddColumn<Guid>(
                name: "DeletedById",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "Notices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // ============================================================================
            // Add computed column for TotalDemand
            // ============================================================================
            migrationBuilder.Sql(@"
                ALTER TABLE ""Notices""
                ADD COLUMN ""TotalDemand"" decimal(15,2)
                GENERATED ALWAYS AS (
                    COALESCE(""TaxAmount"", 0) + COALESCE(""PenaltyAmount"", 0) + COALESCE(""InterestAmount"", 0)
                ) STORED;
            ");

            // ============================================================================
            // Add full-text search vector column
            // ============================================================================
            migrationBuilder.Sql(@"
                ALTER TABLE ""Notices""
                ADD COLUMN ""SearchVector"" tsvector
                GENERATED ALWAYS AS (
                    setweight(to_tsvector('english', COALESCE(""NoticeNumber"", '')), 'A') ||
                    setweight(to_tsvector('english', COALESCE(""Gstin"", '')), 'A') ||
                    setweight(to_tsvector('english', COALESCE(""NoticeType"", '')), 'B') ||
                    setweight(to_tsvector('english', COALESCE(""NoticeCategory"", '')), 'B') ||
                    setweight(to_tsvector('english', COALESCE(""OcrText"", '')), 'C')
                ) STORED;
            ");

            // ============================================================================
            // Add indexes
            // ============================================================================

            // Foreign key indexes
            migrationBuilder.CreateIndex(
                name: "IX_Notices_GstinId",
                table: "Notices",
                column: "GstinId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_AssignedById",
                table: "Notices",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_DeletedById",
                table: "Notices",
                column: "DeletedById");

            // Priority index with filter
            migrationBuilder.CreateIndex(
                name: "IX_Notices_Priority",
                table: "Notices",
                column: "Priority",
                filter: "\"DeletedAt\" IS NULL");

            // File hash index for duplicate detection
            migrationBuilder.CreateIndex(
                name: "IX_Notices_FileHash",
                table: "Notices",
                column: "FileHash",
                filter: "\"DeletedAt\" IS NULL AND \"FileHash\" IS NOT NULL");

            // CreatedAt index with filter
            migrationBuilder.CreateIndex(
                name: "IX_Notices_CreatedAt",
                table: "Notices",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            // Composite index for common queries
            migrationBuilder.CreateIndex(
                name: "IX_Notices_Org_Status_Deadline",
                table: "Notices",
                columns: new[] { "OrganizationId", "Status", "ResponseDeadline" },
                filter: "\"DeletedAt\" IS NULL");

            // GIN index for full-text search
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Notices_SearchVector""
                ON ""Notices"" USING gin(""SearchVector"")
                WHERE ""DeletedAt"" IS NULL;
            ");

            // GIN index for tags array
            migrationBuilder.Sql(@"
                CREATE INDEX ""IX_Notices_Tags""
                ON ""Notices"" USING gin(""Tags"")
                WHERE ""DeletedAt"" IS NULL;
            ");

            // ============================================================================
            // Add foreign key constraints
            // ============================================================================

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_AspNetUsers_AssignedById",
                table: "Notices",
                column: "AssignedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_AspNetUsers_DeletedById",
                table: "Notices",
                column: "DeletedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_OrganizationGstins_GstinId",
                table: "Notices",
                column: "GstinId",
                principalTable: "OrganizationGstins",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ============================================================================
            // Add description column to Attachments table
            // ============================================================================

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Attachments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "Attachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop foreign keys
            migrationBuilder.DropForeignKey(
                name: "FK_Notices_AspNetUsers_AssignedById",
                table: "Notices");

            migrationBuilder.DropForeignKey(
                name: "FK_Notices_AspNetUsers_DeletedById",
                table: "Notices");

            migrationBuilder.DropForeignKey(
                name: "FK_Notices_OrganizationGstins_GstinId",
                table: "Notices");

            // Drop indexes
            migrationBuilder.DropIndex(name: "IX_Notices_GstinId", table: "Notices");
            migrationBuilder.DropIndex(name: "IX_Notices_AssignedById", table: "Notices");
            migrationBuilder.DropIndex(name: "IX_Notices_DeletedById", table: "Notices");
            migrationBuilder.DropIndex(name: "IX_Notices_Priority", table: "Notices");
            migrationBuilder.DropIndex(name: "IX_Notices_FileHash", table: "Notices");
            migrationBuilder.DropIndex(name: "IX_Notices_CreatedAt", table: "Notices");
            migrationBuilder.DropIndex(name: "IX_Notices_Org_Status_Deadline", table: "Notices");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Notices_SearchVector"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Notices_Tags"";");

            // Drop computed columns
            migrationBuilder.Sql(@"ALTER TABLE ""Notices"" DROP COLUMN IF EXISTS ""SearchVector"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Notices"" DROP COLUMN IF EXISTS ""TotalDemand"";");

            // Drop new columns from Notices
            migrationBuilder.DropColumn(name: "NoticeSubCategory", table: "Notices");
            migrationBuilder.DropColumn(name: "GstinId", table: "Notices");
            migrationBuilder.DropColumn(name: "HearingDate", table: "Notices");
            migrationBuilder.DropColumn(name: "FinancialYear", table: "Notices");
            migrationBuilder.DropColumn(name: "OfficerDesignation", table: "Notices");
            migrationBuilder.DropColumn(name: "Jurisdiction", table: "Notices");
            migrationBuilder.DropColumn(name: "AssignedById", table: "Notices");
            migrationBuilder.DropColumn(name: "AssignedAt", table: "Notices");
            migrationBuilder.DropColumn(name: "FileName", table: "Notices");
            migrationBuilder.DropColumn(name: "FileSize", table: "Notices");
            migrationBuilder.DropColumn(name: "FileMimeType", table: "Notices");
            migrationBuilder.DropColumn(name: "PageCount", table: "Notices");
            migrationBuilder.DropColumn(name: "OcrLanguage", table: "Notices");
            migrationBuilder.DropColumn(name: "ProcessingStartedAt", table: "Notices");
            migrationBuilder.DropColumn(name: "ProcessingCompletedAt", table: "Notices");
            migrationBuilder.DropColumn(name: "ProcessingAttempts", table: "Notices");
            migrationBuilder.DropColumn(name: "DeletedById", table: "Notices");
            migrationBuilder.DropColumn(name: "DeletionReason", table: "Notices");

            // Drop new columns from Attachments
            migrationBuilder.DropColumn(name: "Description", table: "Attachments");
            migrationBuilder.DropColumn(name: "FileHash", table: "Attachments");
        }
    }
}
