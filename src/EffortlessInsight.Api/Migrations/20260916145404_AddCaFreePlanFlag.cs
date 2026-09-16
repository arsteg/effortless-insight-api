using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCaFreePlanFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowFreePlan",
                table: "ca_profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FreePlanGrantedAt",
                table: "ca_profiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FreePlanRevokedAt",
                table: "ca_profiles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowFreePlan",
                table: "ca_profiles");

            migrationBuilder.DropColumn(
                name: "FreePlanGrantedAt",
                table: "ca_profiles");

            migrationBuilder.DropColumn(
                name: "FreePlanRevokedAt",
                table: "ca_profiles");
        }
    }
}
