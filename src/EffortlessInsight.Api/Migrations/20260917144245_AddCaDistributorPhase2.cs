using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCaDistributorPhase2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaClientInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaOrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ClientDisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultingOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccessDurationDays = table.Column<int>(type: "integer", nullable: true),
                    LastSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SendCount = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaClientInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaClientInvitations_AspNetUsers_AcceptedUserId",
                        column: x => x.AcceptedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CaClientInvitations_AspNetUsers_CaUserId",
                        column: x => x.CaUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaClientInvitations_Organizations_CaOrganizationId",
                        column: x => x.CaOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaClientInvitations_Organizations_ResultingOrganizationId",
                        column: x => x.ResultingOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CaProspectClients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    GstinHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientDisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CaClientInvitationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MergedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MergedIntoOrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaProspectClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaProspectClients_AspNetUsers_CaUserId",
                        column: x => x.CaUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CaProspectClients_CaClientInvitations_CaClientInvitationId",
                        column: x => x.CaClientInvitationId,
                        principalTable: "CaClientInvitations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CaProspectClients_Organizations_MergedIntoOrganizationId",
                        column: x => x.MergedIntoOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CaStagedNotices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaProspectClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NoticeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    NoticeCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ResponseDeadline = table.Column<DateOnly>(type: "date", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    PenaltyAmount = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    InterestAmount = table.Column<decimal>(type: "numeric(15,2)", nullable: true),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: true),
                    FinancialYear = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    FileUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FileSize = table.Column<int>(type: "integer", nullable: true),
                    FileMimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MergedToNotices = table.Column<bool>(type: "boolean", nullable: false),
                    MergedNoticeId = table.Column<Guid>(type: "uuid", nullable: true),
                    MergedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaStagedNotices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CaStagedNotices_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CaStagedNotices_CaProspectClients_CaProspectClientId",
                        column: x => x.CaProspectClientId,
                        principalTable: "CaProspectClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaClientInvitations_AcceptedUserId",
                table: "CaClientInvitations",
                column: "AcceptedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaClientInvitations_CaOrganizationId",
                table: "CaClientInvitations",
                column: "CaOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CaClientInvitations_CaUserId",
                table: "CaClientInvitations",
                column: "CaUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CaClientInvitations_EmailNormalized",
                table: "CaClientInvitations",
                column: "EmailNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_CaClientInvitations_ResultingOrganizationId",
                table: "CaClientInvitations",
                column: "ResultingOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CaClientInvitations_TokenHash",
                table: "CaClientInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaProspectClients_CaClientInvitationId",
                table: "CaProspectClients",
                column: "CaClientInvitationId");

            migrationBuilder.CreateIndex(
                name: "IX_CaProspectClients_CaUser_GstinHash",
                table: "CaProspectClients",
                columns: new[] { "CaUserId", "GstinHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CaProspectClients_MergedIntoOrganizationId",
                table: "CaProspectClients",
                column: "MergedIntoOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CaStagedNotices_CaProspectClientId",
                table: "CaStagedNotices",
                column: "CaProspectClientId");

            migrationBuilder.CreateIndex(
                name: "IX_CaStagedNotices_MergedToNotices",
                table: "CaStagedNotices",
                column: "MergedToNotices");

            migrationBuilder.CreateIndex(
                name: "IX_CaStagedNotices_UploadedByUserId",
                table: "CaStagedNotices",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaStagedNotices");

            migrationBuilder.DropTable(
                name: "CaProspectClients");

            migrationBuilder.DropTable(
                name: "CaClientInvitations");
        }
    }
}
