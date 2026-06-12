using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCollaborationModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============================================================================
            // Extend Comments table
            // ============================================================================
            migrationBuilder.AddColumn<string>(
                name: "ContentHtml",
                table: "Comments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                table: "Comments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "all");

            migrationBuilder.AddColumn<bool>(
                name: "IsEdited",
                table: "Comments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EditCount",
                table: "Comments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Comments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Depth",
                table: "Comments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Visibility",
                table: "Comments",
                column: "Visibility",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_IsDeleted",
                table: "Comments",
                column: "IsDeleted",
                filter: "\"DeletedAt\" IS NULL");

            // ============================================================================
            // Extend Tasks table
            // ============================================================================
            migrationBuilder.AddColumn<Guid>(
                name: "ParentTaskId",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedHours",
                table: "Tasks",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ActualHours",
                table: "Tasks",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionNote",
                table: "Tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "Labels",
                table: "Tasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Tasks",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ParentTaskId",
                table: "Tasks",
                column: "ParentTaskId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeTasks_Notice_Parent",
                table: "Tasks",
                columns: new[] { "NoticeId", "ParentTaskId" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeTasks_ActiveDueDate",
                table: "Tasks",
                column: "DueDate",
                filter: "\"DeletedAt\" IS NULL AND \"Status\" NOT IN ('done', 'archived')");

            // ============================================================================
            // Create TaskTemplates table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "TaskTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DefaultTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DefaultDescription = table.Column<string>(type: "text", nullable: true),
                    DefaultPriority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "medium"),
                    DefaultEstimatedHours = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    DefaultLabels = table.Column<List<string>>(type: "jsonb", nullable: true),
                    ApplicableNoticeTypes = table.Column<List<string>>(type: "jsonb", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskTemplates_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskTemplates_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_OrganizationId",
                table: "TaskTemplates",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_IsActive",
                table: "TaskTemplates",
                column: "IsActive",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_Org_Name_Unique",
                table: "TaskTemplates",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_CreatedById",
                table: "TaskTemplates",
                column: "CreatedById");

            // Add FK from Tasks to TaskTemplates
            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_TaskTemplates_TemplateId",
                table: "Tasks",
                column: "TemplateId",
                principalTable: "TaskTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TemplateId",
                table: "Tasks",
                column: "TemplateId");

            // Add FK for parent task
            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Tasks_ParentTaskId",
                table: "Tasks",
                column: "ParentTaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // ============================================================================
            // Create TaskAssignees table (many-to-many)
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "TaskAssignees",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignees", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TaskAssignees_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskAssignees_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskAssignees_AspNetUsers_AssignedById",
                        column: x => x.AssignedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignees_UserId",
                table: "TaskAssignees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignees_AssignedById",
                table: "TaskAssignees",
                column: "AssignedById");

            // ============================================================================
            // Create CommentEditHistory table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "CommentEditHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousContent = table.Column<string>(type: "text", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentEditHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentEditHistory_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommentEditHistory_CommentId",
                table: "CommentEditHistory",
                column: "CommentId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommentEditHistory_Comment_EditedAt",
                table: "CommentEditHistory",
                columns: new[] { "CommentId", "EditedAt" },
                filter: "\"DeletedAt\" IS NULL");

            // ============================================================================
            // Create CommentReactions table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "CommentReactions",
                columns: table => new
                {
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentReactions", x => new { x.CommentId, x.UserId, x.Emoji });
                    table.ForeignKey(
                        name: "FK_CommentReactions_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommentReactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommentReactions_CommentId",
                table: "CommentReactions",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_CommentReactions_UserId",
                table: "CommentReactions",
                column: "UserId");

            // ============================================================================
            // Create DocumentRequestTemplates table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "DocumentRequestTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TitleTemplate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DescriptionTemplate = table.Column<string>(type: "text", nullable: false),
                    DefaultPriority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "medium"),
                    DefaultDueDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 7),
                    AcceptedFormats = table.Column<List<string>>(type: "jsonb", nullable: true),
                    ApplicableNoticeTypes = table.Column<List<string>>(type: "jsonb", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequestTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRequestTemplates_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestTemplates_OrganizationId",
                table: "DocumentRequestTemplates",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestTemplates_IsActive",
                table: "DocumentRequestTemplates",
                column: "IsActive",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestTemplates_Org_Name_Unique",
                table: "DocumentRequestTemplates",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            // ============================================================================
            // Create FileFolders table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "FileFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentFolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileFolders_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileFolders_FileFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "FileFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FileFolders_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileFolders_NoticeId",
                table: "FileFolders",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileFolders_ParentFolderId",
                table: "FileFolders",
                column: "ParentFolderId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileFolders_Notice_Name_Parent_Unique",
                table: "FileFolders",
                columns: new[] { "NoticeId", "Name", "ParentFolderId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FileFolders_CreatedById",
                table: "FileFolders",
                column: "CreatedById");

            // ============================================================================
            // Create NoticeFiles table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "NoticeFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: true),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OriginalFilename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    StorageProvider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "s3"),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    UploadedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeFiles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoticeFiles_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NoticeFiles_FileFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "FileFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NoticeFiles_AspNetUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeFiles_NoticeId",
                table: "NoticeFiles",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeFiles_OrganizationId",
                table: "NoticeFiles",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeFiles_FolderId",
                table: "NoticeFiles",
                column: "FolderId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeFiles_Checksum",
                table: "NoticeFiles",
                column: "Checksum",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeFiles_UploadedById",
                table: "NoticeFiles",
                column: "UploadedById");

            // ============================================================================
            // Create DocumentRequests table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "DocumentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "medium"),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AcceptedFormats = table.Column<List<string>>(type: "jsonb", nullable: true),
                    RequestedFromId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    FulfilledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRequests_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentRequests_AspNetUsers_RequestedFromId",
                        column: x => x.RequestedFromId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequests_AspNetUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequests_AspNetUsers_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DocumentRequests_DocumentRequestTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "DocumentRequestTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_NoticeId",
                table: "DocumentRequests",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_Status",
                table: "DocumentRequests",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_RequestedFromId",
                table: "DocumentRequests",
                column: "RequestedFromId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_DueDate",
                table: "DocumentRequests",
                column: "DueDate",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_Notice_Status",
                table: "DocumentRequests",
                columns: new[] { "NoticeId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_RequestedById",
                table: "DocumentRequests",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_ReviewedById",
                table: "DocumentRequests",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequests_TemplateId",
                table: "DocumentRequests",
                column: "TemplateId");

            // ============================================================================
            // Create DocumentRequestDocuments table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "DocumentRequestDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedById = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRequestDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRequestDocuments_DocumentRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "DocumentRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentRequestDocuments_NoticeFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "NoticeFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentRequestDocuments_AspNetUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestDocuments_RequestId",
                table: "DocumentRequestDocuments",
                column: "RequestId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestDocuments_FileId",
                table: "DocumentRequestDocuments",
                column: "FileId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRequestDocuments_UploadedById",
                table: "DocumentRequestDocuments",
                column: "UploadedById");

            // ============================================================================
            // Create ActivityLogs table
            // ============================================================================
            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "user"),
                    Data = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityLogs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityLogs_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityLogs_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_NoticeId",
                table: "ActivityLogs",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_OrganizationId",
                table: "ActivityLogs",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_ActivityType",
                table: "ActivityLogs",
                column: "ActivityType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_ActorId",
                table: "ActivityLogs",
                column: "ActorId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_CreatedAt",
                table: "ActivityLogs",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_Notice_CreatedAt",
                table: "ActivityLogs",
                columns: new[] { "NoticeId", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_Org_CreatedAt",
                table: "ActivityLogs",
                columns: new[] { "OrganizationId", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tables in reverse dependency order
            migrationBuilder.DropTable(name: "ActivityLogs");
            migrationBuilder.DropTable(name: "DocumentRequestDocuments");
            migrationBuilder.DropTable(name: "DocumentRequests");
            migrationBuilder.DropTable(name: "NoticeFiles");
            migrationBuilder.DropTable(name: "FileFolders");
            migrationBuilder.DropTable(name: "DocumentRequestTemplates");
            migrationBuilder.DropTable(name: "CommentReactions");
            migrationBuilder.DropTable(name: "CommentEditHistory");
            migrationBuilder.DropTable(name: "TaskAssignees");

            // Drop foreign keys
            migrationBuilder.DropForeignKey(name: "FK_Tasks_TaskTemplates_TemplateId", table: "Tasks");
            migrationBuilder.DropForeignKey(name: "FK_Tasks_Tasks_ParentTaskId", table: "Tasks");

            migrationBuilder.DropTable(name: "TaskTemplates");

            // Drop indexes
            migrationBuilder.DropIndex(name: "IX_Tasks_TemplateId", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_Tasks_ParentTaskId", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_NoticeTasks_Notice_Parent", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_NoticeTasks_ActiveDueDate", table: "Tasks");
            migrationBuilder.DropIndex(name: "IX_Comments_Visibility", table: "Comments");
            migrationBuilder.DropIndex(name: "IX_Comments_IsDeleted", table: "Comments");

            // Drop columns from Tasks
            migrationBuilder.DropColumn(name: "ParentTaskId", table: "Tasks");
            migrationBuilder.DropColumn(name: "EstimatedHours", table: "Tasks");
            migrationBuilder.DropColumn(name: "ActualHours", table: "Tasks");
            migrationBuilder.DropColumn(name: "CompletionNote", table: "Tasks");
            migrationBuilder.DropColumn(name: "Labels", table: "Tasks");
            migrationBuilder.DropColumn(name: "TemplateId", table: "Tasks");

            // Revert Title/Description column sizes
            migrationBuilder.AlterColumn<string>(
                name: "Title",
                table: "Tasks",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            // Drop columns from Comments
            migrationBuilder.DropColumn(name: "ContentHtml", table: "Comments");
            migrationBuilder.DropColumn(name: "Visibility", table: "Comments");
            migrationBuilder.DropColumn(name: "IsEdited", table: "Comments");
            migrationBuilder.DropColumn(name: "EditCount", table: "Comments");
            migrationBuilder.DropColumn(name: "IsDeleted", table: "Comments");
            migrationBuilder.DropColumn(name: "Depth", table: "Comments");
        }
    }
}
