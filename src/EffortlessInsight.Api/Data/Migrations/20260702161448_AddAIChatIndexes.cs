using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAIChatIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AIAuditLogs_NoticeConversations_ConversationId",
                table: "AIAuditLogs");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                column: "MessageId",
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_AIAuditLogs_NoticeConversations_ConversationId",
                table: "AIAuditLogs",
                column: "ConversationId",
                principalTable: "NoticeConversations",
                principalColumn: "Id");
        }
    }
}
