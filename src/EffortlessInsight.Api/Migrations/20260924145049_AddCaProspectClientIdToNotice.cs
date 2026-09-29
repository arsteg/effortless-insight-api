using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCaProspectClientIdToNotice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CaProspectClientId",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notices_CaProspectClientId",
                table: "Notices",
                column: "CaProspectClientId",
                filter: "\"DeletedAt\" IS NULL AND \"CaProspectClientId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_CaProspectClients_CaProspectClientId",
                table: "Notices",
                column: "CaProspectClientId",
                principalTable: "CaProspectClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notices_CaProspectClients_CaProspectClientId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_CaProspectClientId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "CaProspectClientId",
                table: "Notices");
        }
    }
}
