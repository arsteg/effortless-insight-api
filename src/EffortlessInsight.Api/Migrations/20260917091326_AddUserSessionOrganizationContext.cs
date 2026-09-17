using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSessionOrganizationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CaClientRelationshipId",
                table: "UserSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExternal",
                table: "UserSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "UserSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "UserSessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_CaClientRelationshipId",
                table: "UserSessions",
                column: "CaClientRelationshipId",
                filter: "\"CaClientRelationshipId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSessions_CaClientRelationshipId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "CaClientRelationshipId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "IsExternal",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "UserSessions");
        }
    }
}
