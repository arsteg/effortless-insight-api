using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCaSubscriptionIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "SubscriptionPlans",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminGrantedAt",
                table: "BillingSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GrantedByAdminId",
                table: "BillingSubscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAdminGranted",
                table: "BillingSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Set CA operator plans to non-public
            migrationBuilder.Sql(
                "UPDATE \"SubscriptionPlans\" SET \"IsPublic\" = false WHERE \"IsCaOperatorPlan\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "AdminGrantedAt",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "GrantedByAdminId",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "IsAdminGranted",
                table: "BillingSubscriptions");
        }
    }
}
