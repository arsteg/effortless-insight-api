using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGstSyncModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AIAuditLogs_NoticeConversations_ConversationId",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_NoticeMessages_Conversation_CreatedAt",
                table: "NoticeMessages");

            migrationBuilder.DropIndex(
                name: "IX_NoticeMessages_ConversationId",
                table: "NoticeMessages");

            migrationBuilder.DropIndex(
                name: "IX_NoticeMessages_CreatedAt",
                table: "NoticeMessages");

            migrationBuilder.DropIndex(
                name: "IX_NoticeMessages_Role",
                table: "NoticeMessages");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_CreatedAt",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_LastMessageAt",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_Notice_User_LastMessage",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_NoticeId",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_OrganizationId",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_UserId",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_MessageFeedbacks_Message_User_Unique",
                table: "MessageFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_MessageFeedbacks_MessageId",
                table: "MessageFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_MessageFeedbacks_Rating",
                table: "MessageFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_MessageFeedbacks_UserId",
                table: "MessageFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_ConversationSummaries_Conversation_CreatedAt",
                table: "ConversationSummaries");

            migrationBuilder.DropIndex(
                name: "IX_ConversationSummaries_ConversationId",
                table: "ConversationSummaries");

            migrationBuilder.DropIndex(
                name: "IX_ConversationSummaries_CreatedAt",
                table: "ConversationSummaries");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_ConversationId",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_CreatedAt",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_ModelId",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_Organization_CreatedAt",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_OrganizationId",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_Status",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_UserId",
                table: "AIAuditLogs");

            migrationBuilder.CreateTable(
                name: "gst_clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    TradeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LegalName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StateCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    SyncEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SyncFrequencyHours = table.Column<int>(type: "integer", nullable: false),
                    AutoImportToNotices = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StatusMessage = table.Column<string>(type: "text", nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    LastSuccessfulSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextSyncDueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    TotalNoticesSynced = table.Column<int>(type: "integer", nullable: false),
                    TotalSyncsPerformed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gst_clients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gst_clients_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gst_clients_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gst_extension_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EventData = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    ExtensionVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BrowserInfo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ErrorType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ErrorStack = table.Column<string>(type: "text", nullable: true),
                    PageUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gst_extension_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gst_extension_events_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gst_sync_reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GstClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReminderType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClickedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MessageContent = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gst_sync_reminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gst_sync_reminders_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gst_sync_reminders_gst_clients_GstClientId",
                        column: x => x.GstClientId,
                        principalTable: "gst_clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gst_sync_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GstClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    SyncSource = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SourceMetadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NoticesFound = table.Column<int>(type: "integer", nullable: false),
                    NoticesNew = table.Column<int>(type: "integer", nullable: false),
                    NoticesUpdated = table.Column<int>(type: "integer", nullable: false),
                    NoticesUnchanged = table.Column<int>(type: "integer", nullable: false),
                    PdfsDownloaded = table.Column<int>(type: "integer", nullable: false),
                    PdfsFailed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gst_sync_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gst_sync_sessions_gst_clients_GstClientId",
                        column: x => x.GstClientId,
                        principalTable: "gst_clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gst_notices_raw",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GstClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    PortalNoticeId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NoticeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NoticeCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    StatusOnPortal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DemandAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    InterestAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    PenaltyAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    TaxPeriod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FinancialYear = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SectionRule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OfficerName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OfficerDesignation = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Jurisdiction = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    PdfAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    PdfS3Key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PdfSizeBytes = table.Column<int>(type: "integer", nullable: true),
                    PdfDownloadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawData = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    ImportedToNotices = table.Column<bool>(type: "boolean", nullable: false),
                    ImportedNoticeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SyncCount = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gst_notices_raw", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gst_notices_raw_gst_clients_GstClientId",
                        column: x => x.GstClientId,
                        principalTable: "gst_clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gst_notices_raw_gst_sync_sessions_LastSyncSessionId",
                        column: x => x.LastSyncSessionId,
                        principalTable: "gst_sync_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeMessages_ConversationId",
                table: "NoticeMessages",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_NoticeId",
                table: "NoticeConversations",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_OrganizationId",
                table: "NoticeConversations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_UserId",
                table: "NoticeConversations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedbacks_MessageId",
                table: "MessageFeedbacks",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedbacks_UserId",
                table: "MessageFeedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_ConversationId",
                table: "ConversationSummaries",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_ConversationId",
                table: "AIAuditLogs",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_OrganizationId",
                table: "AIAuditLogs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_UserId",
                table: "AIAuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_gst_clients_CreatedByUserId",
                table: "gst_clients",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_gst_clients_Gstin",
                table: "gst_clients",
                column: "Gstin",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_clients_OrganizationId",
                table: "gst_clients",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GstClients_Active",
                table: "gst_clients",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_GstClients_NextSyncDue",
                table: "gst_clients",
                column: "NextSyncDueAt",
                filter: "\"DeletedAt\" IS NULL AND \"SyncEnabled\" = true AND \"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_GstClients_Org_Gstin_Unique",
                table: "gst_clients",
                columns: new[] { "OrganizationId", "Gstin" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_extension_events_EventType",
                table: "gst_extension_events",
                column: "EventType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_extension_events_OrganizationId",
                table: "gst_extension_events",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_extension_events_UserId",
                table: "gst_extension_events",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GstExtensionEvents_CreatedAt_Desc",
                table: "gst_extension_events",
                column: "CreatedAt",
                descending: new bool[0],
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_notices_raw_GstClientId",
                table: "gst_notices_raw",
                column: "GstClientId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_notices_raw_Gstin",
                table: "gst_notices_raw",
                column: "Gstin",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_notices_raw_LastSyncSessionId",
                table: "gst_notices_raw",
                column: "LastSyncSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_gst_notices_raw_NoticeType",
                table: "gst_notices_raw",
                column: "NoticeType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_notices_raw_OrganizationId",
                table: "gst_notices_raw",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_notices_raw_PortalNoticeId",
                table: "gst_notices_raw",
                column: "PortalNoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GstNoticesRaw_Client_PortalId_Unique",
                table: "gst_notices_raw",
                columns: new[] { "GstClientId", "PortalNoticeId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GstNoticesRaw_DueDate",
                table: "gst_notices_raw",
                column: "DueDate",
                filter: "\"DeletedAt\" IS NULL AND \"DueDate\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GstNoticesRaw_NotImported",
                table: "gst_notices_raw",
                column: "ImportedToNotices",
                filter: "\"DeletedAt\" IS NULL AND \"ImportedToNotices\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_gst_sync_reminders_GstClientId",
                table: "gst_sync_reminders",
                column: "GstClientId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_sync_reminders_OrganizationId",
                table: "gst_sync_reminders",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_sync_reminders_UserId",
                table: "gst_sync_reminders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GstSyncReminders_Pending",
                table: "gst_sync_reminders",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_gst_sync_sessions_GstClientId",
                table: "gst_sync_sessions",
                column: "GstClientId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_sync_sessions_OrganizationId",
                table: "gst_sync_sessions",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gst_sync_sessions_Status",
                table: "gst_sync_sessions",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GstSyncSessions_StartedAt_Desc",
                table: "gst_sync_sessions",
                column: "StartedAt",
                descending: new bool[0],
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AIAuditLogs_NoticeConversations_ConversationId",
                table: "AIAuditLogs",
                column: "ConversationId",
                principalTable: "NoticeConversations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AIAuditLogs_NoticeConversations_ConversationId",
                table: "AIAuditLogs");

            migrationBuilder.DropTable(
                name: "gst_extension_events");

            migrationBuilder.DropTable(
                name: "gst_notices_raw");

            migrationBuilder.DropTable(
                name: "gst_sync_reminders");

            migrationBuilder.DropTable(
                name: "gst_sync_sessions");

            migrationBuilder.DropTable(
                name: "gst_clients");

            migrationBuilder.DropIndex(
                name: "IX_NoticeMessages_ConversationId",
                table: "NoticeMessages");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_NoticeId",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_OrganizationId",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_NoticeConversations_UserId",
                table: "NoticeConversations");

            migrationBuilder.DropIndex(
                name: "IX_MessageFeedbacks_MessageId",
                table: "MessageFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_MessageFeedbacks_UserId",
                table: "MessageFeedbacks");

            migrationBuilder.DropIndex(
                name: "IX_ConversationSummaries_ConversationId",
                table: "ConversationSummaries");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_ConversationId",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_OrganizationId",
                table: "AIAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AIAuditLogs_UserId",
                table: "AIAuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeMessages_Conversation_CreatedAt",
                table: "NoticeMessages",
                columns: new[] { "ConversationId", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeMessages_ConversationId",
                table: "NoticeMessages",
                column: "ConversationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeMessages_CreatedAt",
                table: "NoticeMessages",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeMessages_Role",
                table: "NoticeMessages",
                column: "Role",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_CreatedAt",
                table: "NoticeConversations",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_LastMessageAt",
                table: "NoticeConversations",
                column: "LastMessageAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_Notice_User_LastMessage",
                table: "NoticeConversations",
                columns: new[] { "NoticeId", "UserId", "LastMessageAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_NoticeId",
                table: "NoticeConversations",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_OrganizationId",
                table: "NoticeConversations",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeConversations_UserId",
                table: "NoticeConversations",
                column: "UserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedbacks_Message_User_Unique",
                table: "MessageFeedbacks",
                columns: new[] { "MessageId", "UserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedbacks_MessageId",
                table: "MessageFeedbacks",
                column: "MessageId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedbacks_Rating",
                table: "MessageFeedbacks",
                column: "Rating",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedbacks_UserId",
                table: "MessageFeedbacks",
                column: "UserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_Conversation_CreatedAt",
                table: "ConversationSummaries",
                columns: new[] { "ConversationId", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_ConversationId",
                table: "ConversationSummaries",
                column: "ConversationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_CreatedAt",
                table: "ConversationSummaries",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_ConversationId",
                table: "AIAuditLogs",
                column: "ConversationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_CreatedAt",
                table: "AIAuditLogs",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_ModelId",
                table: "AIAuditLogs",
                column: "ModelId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_Organization_CreatedAt",
                table: "AIAuditLogs",
                columns: new[] { "OrganizationId", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_OrganizationId",
                table: "AIAuditLogs",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_Status",
                table: "AIAuditLogs",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AIAuditLogs_UserId",
                table: "AIAuditLogs",
                column: "UserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_AIAuditLogs_NoticeConversations_ConversationId",
                table: "AIAuditLogs",
                column: "ConversationId",
                principalTable: "NoticeConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
