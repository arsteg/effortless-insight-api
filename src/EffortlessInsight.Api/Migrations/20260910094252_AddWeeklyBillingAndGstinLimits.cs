using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyBillingAndGstinLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Add columns as nullable first
            migrationBuilder.AddColumn<List<string>>(
                name: "AllowedBillingCycles",
                table: "SubscriptionPlans",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultBillingCycle",
                table: "SubscriptionPlans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCaOperatorPlan",
                table: "SubscriptionPlans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PerSeatWeekly",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricingWeekly",
                table: "SubscriptionPlans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RazorpayPlanIdWeekly",
                table: "SubscriptionPlans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // Step 2: Set default values for existing rows
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""AllowedBillingCycles"" = ARRAY['monthly', 'annually']::text[],
                    ""DefaultBillingCycle"" = 'annually'
                WHERE ""AllowedBillingCycles"" IS NULL;
            ");

            // Step 3: Update Limits JSON to include GstinsAllowed if not present
            migrationBuilder.Sql(@"
                UPDATE ""SubscriptionPlans""
                SET ""Limits"" = ""Limits"" || '{""GstinsAllowed"": 1}'::jsonb
                WHERE ""Limits"" IS NOT NULL
                  AND NOT (""Limits"" ? 'GstinsAllowed');
            ");

            // Step 4: Make columns NOT NULL after data is populated
            migrationBuilder.AlterColumn<List<string>>(
                name: "AllowedBillingCycles",
                table: "SubscriptionPlans",
                type: "text[]",
                nullable: false,
                oldClrType: typeof(List<string>),
                oldType: "text[]",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DefaultBillingCycle",
                table: "SubscriptionPlans",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "annually",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedBillingCycles",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "DefaultBillingCycle",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "IsCaOperatorPlan",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PerSeatWeekly",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "PricingWeekly",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "RazorpayPlanIdWeekly",
                table: "SubscriptionPlans");
        }
    }
}
