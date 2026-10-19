using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoticeAiReportAnalysisFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CessAmount",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LateFeeAmount",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherChargesAmount",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true);

            migrationBuilder.AddColumn<List<Dictionary<string, object>>>(
                name: "CalculationDiscrepancies",
                table: "NoticeAiReports",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<Dictionary<string, object>>>(
                name: "PrefilingChecklist",
                table: "NoticeAiReports",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "RiskAssessment",
                table: "NoticeAiReports",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "RiskReasons",
                table: "NoticeAiReports",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CessAmount",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "LateFeeAmount",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "OtherChargesAmount",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "CalculationDiscrepancies",
                table: "NoticeAiReports");

            migrationBuilder.DropColumn(
                name: "PrefilingChecklist",
                table: "NoticeAiReports");

            migrationBuilder.DropColumn(
                name: "RiskAssessment",
                table: "NoticeAiReports");

            migrationBuilder.DropColumn(
                name: "RiskReasons",
                table: "NoticeAiReports");
        }
    }
}
