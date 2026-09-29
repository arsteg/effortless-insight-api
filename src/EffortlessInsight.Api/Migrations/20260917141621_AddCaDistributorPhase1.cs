using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCaDistributorPhase1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCA",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CaFreeAccessGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    GrantedByAdminId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GrantReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RevokedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaFreeAccessGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaFreeAccessGrants_AdminUsers_GrantedByAdminId",
                        column: x => x.GrantedByAdminId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaFreeAccessGrants_AdminUsers_RevokedByAdminId",
                        column: x => x.RevokedByAdminId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaFreeAccessGrants_AspNetUsers_CaUserId",
                        column: x => x.CaUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaFreeAccessGrants_ActivePerCaUser",
                table: "CaFreeAccessGrants",
                column: "CaUserId",
                unique: true,
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_CaFreeAccessGrants_GrantedByAdminId",
                table: "CaFreeAccessGrants",
                column: "GrantedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_CaFreeAccessGrants_RevokedByAdminId",
                table: "CaFreeAccessGrants",
                column: "RevokedByAdminId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaFreeAccessGrants");

            migrationBuilder.DropColumn(
                name: "IsCA",
                table: "AspNetUsers");
        }
    }
}
