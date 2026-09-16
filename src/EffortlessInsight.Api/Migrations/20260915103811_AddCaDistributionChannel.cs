using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCaDistributionChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAt",
                table: "OrganizationGstins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClaimedByUserId",
                table: "OrganizationGstins",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClaimed",
                table: "OrganizationGstins",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CaSyncedByUserId",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaged",
                table: "Notices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "StagedForClientUserId",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CaGstinAuthorizationId",
                table: "gst_clients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CaUserId",
                table: "gst_clients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCa",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ca_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    MembershipNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SuspensionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SuspendedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ca_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ca_profiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ca_client_relationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InvitedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedById = table.Column<Guid>(type: "uuid", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClientReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CaProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ca_client_relationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ca_client_relationships_AspNetUsers_CaUserId",
                        column: x => x.CaUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ca_client_relationships_AspNetUsers_ClientUserId",
                        column: x => x.ClientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ca_client_relationships_AspNetUsers_RevokedById",
                        column: x => x.RevokedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ca_client_relationships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ca_client_relationships_ca_profiles_CaProfileId",
                        column: x => x.CaProfileId,
                        principalTable: "ca_profiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ca_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InviterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviterOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    InviteeEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    InviteeEmailNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StagedNoticeCount = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SendCount = table.Column<int>(type: "integer", nullable: false),
                    LastSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledById = table.Column<Guid>(type: "uuid", nullable: true),
                    ResponseReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CaProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ca_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ca_invitations_AspNetUsers_AcceptedUserId",
                        column: x => x.AcceptedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ca_invitations_AspNetUsers_CancelledById",
                        column: x => x.CancelledById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ca_invitations_AspNetUsers_InviterUserId",
                        column: x => x.InviterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ca_invitations_Organizations_InviterOrganizationId",
                        column: x => x.InviterOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ca_invitations_ca_profiles_CaProfileId",
                        column: x => x.CaProfileId,
                        principalTable: "ca_profiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ca_gstin_authorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaClientRelationshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    OrganizationGstinId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Permissions = table.Column<List<string>>(type: "jsonb", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedById = table.Column<Guid>(type: "uuid", nullable: true),
                    RevocationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ca_gstin_authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ca_gstin_authorizations_AspNetUsers_RevokedById",
                        column: x => x.RevokedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ca_gstin_authorizations_OrganizationGstins_OrganizationGsti~",
                        column: x => x.OrganizationGstinId,
                        principalTable: "OrganizationGstins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ca_gstin_authorizations_ca_client_relationships_CaClientRel~",
                        column: x => x.CaClientRelationshipId,
                        principalTable: "ca_client_relationships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationGstins_ClaimedByUserId",
                table: "OrganizationGstins",
                column: "ClaimedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_CaSyncedByUserId",
                table: "Notices",
                column: "CaSyncedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_StagedForClientUserId",
                table: "Notices",
                column: "StagedForClientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_gst_clients_CaGstinAuthorizationId",
                table: "gst_clients",
                column: "CaGstinAuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_gst_clients_CaUserId",
                table: "gst_clients",
                column: "CaUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ca_client_relationships_CaProfileId",
                table: "ca_client_relationships",
                column: "CaProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ca_client_relationships_CaUserId",
                table: "ca_client_relationships",
                column: "CaUserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_client_relationships_CaUserId_ClientUserId",
                table: "ca_client_relationships",
                columns: new[] { "CaUserId", "ClientUserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_client_relationships_ClientUserId",
                table: "ca_client_relationships",
                column: "ClientUserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_client_relationships_OrganizationId",
                table: "ca_client_relationships",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL AND \"OrganizationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_client_relationships_RevokedById",
                table: "ca_client_relationships",
                column: "RevokedById");

            migrationBuilder.CreateIndex(
                name: "IX_ca_client_relationships_Status",
                table: "ca_client_relationships",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_gstin_authorizations_CaClientRelationshipId_Gstin",
                table: "ca_gstin_authorizations",
                columns: new[] { "CaClientRelationshipId", "Gstin" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_gstin_authorizations_Gstin",
                table: "ca_gstin_authorizations",
                column: "Gstin",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_gstin_authorizations_OrganizationGstinId",
                table: "ca_gstin_authorizations",
                column: "OrganizationGstinId",
                filter: "\"DeletedAt\" IS NULL AND \"OrganizationGstinId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_gstin_authorizations_RevokedById",
                table: "ca_gstin_authorizations",
                column: "RevokedById");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_AcceptedUserId",
                table: "ca_invitations",
                column: "AcceptedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_CancelledById",
                table: "ca_invitations",
                column: "CancelledById");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_CaProfileId",
                table: "ca_invitations",
                column: "CaProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_InviteeEmailNormalized",
                table: "ca_invitations",
                column: "InviteeEmailNormalized",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_InviterOrganizationId",
                table: "ca_invitations",
                column: "InviterOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_InviterUserId",
                table: "ca_invitations",
                column: "InviterUserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_Status",
                table: "ca_invitations",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_invitations_TokenHash",
                table: "ca_invitations",
                column: "TokenHash",
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_CaInvitations_PendingExpiry",
                table: "ca_invitations",
                column: "ExpiresAt",
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_CaInvitations_Unique_Pending",
                table: "ca_invitations",
                columns: new[] { "InviterUserId", "InviteeEmailNormalized", "Gstin" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ca_profiles_MembershipNumber",
                table: "ca_profiles",
                column: "MembershipNumber",
                filter: "\"MembershipNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ca_profiles_UserId",
                table: "ca_profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_gst_clients_AspNetUsers_CaUserId",
                table: "gst_clients",
                column: "CaUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_gst_clients_ca_gstin_authorizations_CaGstinAuthorizationId",
                table: "gst_clients",
                column: "CaGstinAuthorizationId",
                principalTable: "ca_gstin_authorizations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_AspNetUsers_CaSyncedByUserId",
                table: "Notices",
                column: "CaSyncedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_AspNetUsers_StagedForClientUserId",
                table: "Notices",
                column: "StagedForClientUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationGstins_AspNetUsers_ClaimedByUserId",
                table: "OrganizationGstins",
                column: "ClaimedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gst_clients_AspNetUsers_CaUserId",
                table: "gst_clients");

            migrationBuilder.DropForeignKey(
                name: "FK_gst_clients_ca_gstin_authorizations_CaGstinAuthorizationId",
                table: "gst_clients");

            migrationBuilder.DropForeignKey(
                name: "FK_Notices_AspNetUsers_CaSyncedByUserId",
                table: "Notices");

            migrationBuilder.DropForeignKey(
                name: "FK_Notices_AspNetUsers_StagedForClientUserId",
                table: "Notices");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationGstins_AspNetUsers_ClaimedByUserId",
                table: "OrganizationGstins");

            migrationBuilder.DropTable(
                name: "ca_gstin_authorizations");

            migrationBuilder.DropTable(
                name: "ca_invitations");

            migrationBuilder.DropTable(
                name: "ca_client_relationships");

            migrationBuilder.DropTable(
                name: "ca_profiles");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationGstins_ClaimedByUserId",
                table: "OrganizationGstins");

            migrationBuilder.DropIndex(
                name: "IX_Notices_CaSyncedByUserId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_StagedForClientUserId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_gst_clients_CaGstinAuthorizationId",
                table: "gst_clients");

            migrationBuilder.DropIndex(
                name: "IX_gst_clients_CaUserId",
                table: "gst_clients");

            migrationBuilder.DropColumn(
                name: "ClaimedAt",
                table: "OrganizationGstins");

            migrationBuilder.DropColumn(
                name: "ClaimedByUserId",
                table: "OrganizationGstins");

            migrationBuilder.DropColumn(
                name: "IsClaimed",
                table: "OrganizationGstins");

            migrationBuilder.DropColumn(
                name: "CaSyncedByUserId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "IsStaged",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "StagedForClientUserId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "CaGstinAuthorizationId",
                table: "gst_clients");

            migrationBuilder.DropColumn(
                name: "CaUserId",
                table: "gst_clients");

            migrationBuilder.DropColumn(
                name: "IsCa",
                table: "AspNetUsers");
        }
    }
}
