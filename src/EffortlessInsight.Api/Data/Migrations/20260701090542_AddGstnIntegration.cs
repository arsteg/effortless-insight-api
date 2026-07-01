using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGstnIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "OrganizationGstins",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FetchedFromGstnAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GspCorrelationId",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GstnNoticeId",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GstnReferenceNumber",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDocumentArchived",
                table: "Notices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Notices",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "GstnConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationGstinId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EncryptedRefreshToken = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GspProvider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GspSessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextScheduledSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AutoSyncEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SyncIntervalHours = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastSyncError = table.Column<string>(type: "text", nullable: true),
                    ConnectedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectedById = table.Column<Guid>(type: "uuid", nullable: true),
                    DisconnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisconnectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GstnConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GstnConnections_AspNetUsers_ConnectedById",
                        column: x => x.ConnectedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GstnConnections_AspNetUsers_DisconnectedById",
                        column: x => x.DisconnectedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GstnConnections_OrganizationGstins_OrganizationGstinId",
                        column: x => x.OrganizationGstinId,
                        principalTable: "OrganizationGstins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GstnOtpSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationGstinId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    GspSessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OtpDestination = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OtpDestinationType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    RequestUserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GstnOtpSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GstnOtpSessions_AspNetUsers_InitiatedById",
                        column: x => x.InitiatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GstnOtpSessions_OrganizationGstins_OrganizationGstinId",
                        column: x => x.OrganizationGstinId,
                        principalTable: "OrganizationGstins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GstnSyncLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GstnConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggerSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TriggeredById = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
                    NoticesFound = table.Column<int>(type: "integer", nullable: false),
                    NoticesImported = table.Column<int>(type: "integer", nullable: false),
                    NoticesSkipped = table.Column<int>(type: "integer", nullable: false),
                    NoticesFailed = table.Column<int>(type: "integer", nullable: false),
                    SyncPeriodFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncPeriodTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ErrorDetails = table.Column<string>(type: "text", nullable: true),
                    GspCorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ImportedNoticeIds = table.Column<List<Guid>>(type: "jsonb", nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GstnSyncLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GstnSyncLogs_AspNetUsers_TriggeredById",
                        column: x => x.TriggeredById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GstnSyncLogs_GstnConnections_GstnConnectionId",
                        column: x => x.GstnConnectionId,
                        principalTable: "GstnConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GstnConnections_ConnectedById",
                table: "GstnConnections",
                column: "ConnectedById");

            migrationBuilder.CreateIndex(
                name: "IX_GstnConnections_DisconnectedById",
                table: "GstnConnections",
                column: "DisconnectedById");

            migrationBuilder.CreateIndex(
                name: "IX_GstnConnections_NextScheduledSyncAt",
                table: "GstnConnections",
                column: "NextScheduledSyncAt",
                filter: "\"Status\" = 'connected' AND \"AutoSyncEnabled\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_GstnConnections_OrganizationGstinId",
                table: "GstnConnections",
                column: "OrganizationGstinId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GstnConnections_Status",
                table: "GstnConnections",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_GstnConnections_TokenExpiresAt",
                table: "GstnConnections",
                column: "TokenExpiresAt",
                filter: "\"Status\" = 'connected'");

            migrationBuilder.CreateIndex(
                name: "IX_GstnOtpSessions_ExpiresAt",
                table: "GstnOtpSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_GstnOtpSessions_InitiatedById",
                table: "GstnOtpSessions",
                column: "InitiatedById");

            migrationBuilder.CreateIndex(
                name: "IX_GstnOtpSessions_OrganizationGstinId_Status",
                table: "GstnOtpSessions",
                columns: new[] { "OrganizationGstinId", "Status" },
                filter: "\"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_GstnSyncLogs_CreatedAt",
                table: "GstnSyncLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GstnSyncLogs_GstnConnectionId",
                table: "GstnSyncLogs",
                column: "GstnConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_GstnSyncLogs_StartedAt",
                table: "GstnSyncLogs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GstnSyncLogs_Status",
                table: "GstnSyncLogs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_GstnSyncLogs_TriggeredById",
                table: "GstnSyncLogs",
                column: "TriggeredById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GstnOtpSessions");

            migrationBuilder.DropTable(
                name: "GstnSyncLogs");

            migrationBuilder.DropTable(
                name: "GstnConnections");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "OrganizationGstins");

            migrationBuilder.DropColumn(
                name: "FetchedFromGstnAt",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "GspCorrelationId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "GstnNoticeId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "GstnReferenceNumber",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "IsDocumentArchived",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Notices");
        }
    }
}
