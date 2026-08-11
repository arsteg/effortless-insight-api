using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class LinkGstClientsToOrganizationGstins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationGstins_Gstin",
                table: "OrganizationGstins");

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationGstinId",
                table: "gst_clients",
                type: "uuid",
                nullable: true);

            // NOTE: dotnet ef also emitted AddColumn for BillingSubscriptions
            // RazorpayOrderId/RazorpayPaymentId here. Those columns were already
            // created by 20260721150651_AddRazorpayPaymentFieldsToSubscription
            // (the model snapshot had lost them, likely in a merge), so the
            // operations were removed to keep this migration re-runnable. The
            // regenerated snapshot now includes them correctly.

            migrationBuilder.CreateIndex(
                name: "IX_gst_clients_OrganizationGstinId",
                table: "gst_clients",
                column: "OrganizationGstinId");

            migrationBuilder.AddForeignKey(
                name: "FK_gst_clients_OrganizationGstins_OrganizationGstinId",
                table: "gst_clients",
                column: "OrganizationGstinId",
                principalTable: "OrganizationGstins",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gst_clients_OrganizationGstins_OrganizationGstinId",
                table: "gst_clients");

            migrationBuilder.DropIndex(
                name: "IX_gst_clients_OrganizationGstinId",
                table: "gst_clients");

            migrationBuilder.DropColumn(
                name: "OrganizationGstinId",
                table: "gst_clients");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationGstins_Gstin",
                table: "OrganizationGstins",
                column: "Gstin",
                unique: true);
        }
    }
}
