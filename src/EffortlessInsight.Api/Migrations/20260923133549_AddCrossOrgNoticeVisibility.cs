using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCrossOrgNoticeVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GstinHash",
                table: "Notices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Gstin",
                table: "CaProspectClients",
                type: "character varying(225)",
                maxLength: 225,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(15)",
                oldMaxLength: 15);

            migrationBuilder.AlterColumn<string>(
                name: "Gstin",
                table: "CaClientInvitations",
                type: "character varying(225)",
                maxLength: 225,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(15)",
                oldMaxLength: 15);

            migrationBuilder.CreateTable(
                name: "CaBoGstinLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GstinHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CaUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeactivationReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaBoGstinLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaBoGstinLinks_AspNetUsers_CaUserId",
                        column: x => x.CaUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaBoGstinLinks_OrganizationMembers_CaMembershipId",
                        column: x => x.CaMembershipId,
                        principalTable: "OrganizationMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaBoGstinLinks_Organizations_BoOrganizationId",
                        column: x => x.BoOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaBoGstinLinks_Organizations_CaOrganizationId",
                        column: x => x.CaOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_GstinHash_OrganizationId",
                table: "Notices",
                columns: new[] { "GstinHash", "OrganizationId" },
                filter: "\"DeletedAt\" IS NULL AND \"GstinHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CaBoGstinLinks_BoOrg_Active",
                table: "CaBoGstinLinks",
                columns: new[] { "BoOrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CaBoGstinLinks_CaMembershipId",
                table: "CaBoGstinLinks",
                column: "CaMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_CaBoGstinLinks_CaOrg_Active",
                table: "CaBoGstinLinks",
                columns: new[] { "CaOrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CaBoGstinLinks_CaUserId",
                table: "CaBoGstinLinks",
                column: "CaUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaBoGstinLinks_GstinHash_Active",
                table: "CaBoGstinLinks",
                columns: new[] { "GstinHash", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CaBoGstinLinks_Unique",
                table: "CaBoGstinLinks",
                columns: new[] { "CaOrganizationId", "BoOrganizationId", "GstinHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaBoGstinLinks");

            migrationBuilder.DropIndex(
                name: "IX_Notices_GstinHash_OrganizationId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "GstinHash",
                table: "Notices");

            migrationBuilder.AlterColumn<string>(
                name: "Gstin",
                table: "CaProspectClients",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(225)",
                oldMaxLength: 225);

            migrationBuilder.AlterColumn<string>(
                name: "Gstin",
                table: "CaClientInvitations",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(225)",
                oldMaxLength: 225);
        }
    }
}
