using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingEntityProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_Tasks_NoticeTaskId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_NoticeId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_ResponseId",
                table: "Attachments");

            migrationBuilder.RenameColumn(
                name: "NoticeTaskId",
                table: "Attachments",
                newName: "TaskId");

            migrationBuilder.RenameIndex(
                name: "IX_Attachments_NoticeTaskId",
                table: "Attachments",
                newName: "IX_Attachments_TaskId");

            migrationBuilder.AddColumn<bool>(
                name: "AutoCreateTask",
                table: "WorkflowStages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSynchronizationPoint",
                table: "WorkflowStages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "JoinType",
                table: "WorkflowStages",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinBranchesToComplete",
                table: "WorkflowStages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParallelBranchId",
                table: "WorkflowStages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TaskTemplateId",
                table: "WorkflowStages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Comment",
                table: "WorkflowHistories",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FromStage",
                table: "WorkflowHistories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToStage",
                table: "WorkflowHistories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedTeamId",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "TaskAssignees",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomRoleId",
                table: "OrganizationMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspensionExpiresAt",
                table: "OrganizationMembers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActiveBranchCount",
                table: "NoticeWorkflowInstances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HasParallelStages",
                table: "NoticeWorkflowInstances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersionUsed",
                table: "NoticeWorkflowInstances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentCode",
                table: "Notices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManualEntry",
                table: "Notices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Notices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "Notices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Notices",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "LoginAudits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuthMethod",
                table: "LoginAudits",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "LoginAudits",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "LoginAudits",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PauseReason",
                table: "BillingSubscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAt",
                table: "BillingSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledResumeAt",
                table: "BillingSubscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrentVersion",
                table: "Attachments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginalAttachmentId",
                table: "Attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousVersionId",
                table: "Attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VersionNote",
                table: "Attachments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmailVerified",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthProvider",
                table: "AspNetUsers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OAuthProviderId",
                table: "AspNetUsers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApprovalChains",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TriggerEvent = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TriggerConditions = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsParallel = table.Column<bool>(type: "boolean", nullable: false),
                    MinApprovalsRequired = table.Column<int>(type: "integer", nullable: true),
                    DefaultTimeoutHours = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalChains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalChains_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelUnsubscribes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NotificationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UnsubscribedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelUnsubscribes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelUnsubscribes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    BaseRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Permissions = table.Column<List<string>>(type: "jsonb", nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomRoles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DataExports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FileKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    FileUrl = table.Column<string>(type: "text", nullable: true),
                    Format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Options = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    Summary = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataExports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataExports_AspNetUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DataExports_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceNoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetNoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationshipType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeRelationships_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NoticeRelationships_Notices_SourceNoticeId",
                        column: x => x.SourceNoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoticeRelationships_Notices_TargetNoticeId",
                        column: x => x.TargetNoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeadLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalDeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    FirstAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ResolvedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeadLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationDeadLetters_NotificationDeliveries_OriginalDeli~",
                        column: x => x.OriginalDeliveryId,
                        principalTable: "NotificationDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NotificationDeadLetters_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskDependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependencyType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskDependencies_Tasks_DependsOnTaskId",
                        column: x => x.DependsOnTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskDependencies_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    DaysBeforeDue = table.Column<int>(type: "integer", nullable: false),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskReminders_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TaskReminders_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ParentTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HierarchyPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    HierarchyLevel = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Settings = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_AspNetUsers_LeaderId",
                        column: x => x.LeaderId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Teams_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Teams_Teams_ParentTeamId",
                        column: x => x.ParentTeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TimeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsBillable = table.Column<bool>(type: "boolean", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimeEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TimeEntries_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStageInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BranchId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EnteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SlaPercentConsumed = table.Column<int>(type: "integer", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TimeSpentMinutes = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    WorkflowStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStageInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStageInstances_AspNetUsers_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkflowStageInstances_NoticeWorkflowInstances_WorkflowInst~",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "NoticeWorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowStageInstances_WorkflowStages_StageId",
                        column: x => x.StageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowStageInstances_WorkflowStages_WorkflowStageId",
                        column: x => x.WorkflowStageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalChainId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResponseId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStep = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentStepDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestNotes = table.Column<string>(type: "text", nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_ApprovalChains_ApprovalChainId",
                        column: x => x.ApprovalChainId,
                        principalTable: "ApprovalChains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_AspNetUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_NoticeResponses_ResponseId",
                        column: x => x.ResponseId,
                        principalTable: "NoticeResponses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalChainId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApproverType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ApproverId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApproverRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsOptional = table.Column<bool>(type: "boolean", nullable: false),
                    Conditions = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    TimeoutHours = table.Column<int>(type: "integer", nullable: true),
                    EscalationUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllowDelegation = table.Column<bool>(type: "boolean", nullable: false),
                    Instructions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_ApprovalChains_ApprovalChainId",
                        column: x => x.ApprovalChainId,
                        principalTable: "ApprovalChains",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_AspNetUsers_ApproverId",
                        column: x => x.ApproverId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ApprovalSteps_AspNetUsers_EscalationUserId",
                        column: x => x.EscalationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Comments = table.Column<string>(type: "text", nullable: true),
                    DelegatedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    DelegationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsAutomatic = table.Column<bool>(type: "boolean", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalActions_ApprovalRequests_ApprovalRequestId",
                        column: x => x.ApprovalRequestId,
                        principalTable: "ApprovalRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApprovalActions_ApprovalSteps_ApprovalStepId",
                        column: x => x.ApprovalStepId,
                        principalTable: "ApprovalSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalActions_AspNetUsers_ActorId",
                        column: x => x.ActorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalActions_AspNetUsers_DelegatedToId",
                        column: x => x.DelegatedToId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedTeamId",
                table: "Tasks",
                column: "AssignedTeamId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignees_TeamId",
                table: "TaskAssignees",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMembers_CustomRoleId",
                table: "OrganizationMembers",
                column: "CustomRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_NoticeId",
                table: "Attachments",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_NoticeId_Current",
                table: "Attachments",
                columns: new[] { "NoticeId", "IsCurrentVersion" },
                filter: "\"DeletedAt\" IS NULL AND \"IsCurrentVersion\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OriginalAttachmentId",
                table: "Attachments",
                column: "OriginalAttachmentId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_PreviousVersionId",
                table: "Attachments",
                column: "PreviousVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ResponseId",
                table: "Attachments",
                column: "ResponseId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_ActorId",
                table: "ApprovalActions",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_ApprovalRequestId",
                table: "ApprovalActions",
                column: "ApprovalRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_ApprovalStepId",
                table: "ApprovalActions",
                column: "ApprovalStepId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_DelegatedToId",
                table: "ApprovalActions",
                column: "DelegatedToId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalActions_Request_Created",
                table: "ApprovalActions",
                columns: new[] { "ApprovalRequestId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalChains_Org_Active",
                table: "ApprovalChains",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalChains_OrganizationId",
                table: "ApprovalChains",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalChains_TriggerEvent",
                table: "ApprovalChains",
                column: "TriggerEvent");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_ApprovalChainId",
                table: "ApprovalRequests",
                column: "ApprovalChainId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Deadline_Pending",
                table: "ApprovalRequests",
                column: "CurrentStepDeadline",
                filter: "\"Status\" = 'pending'");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Notice_Status",
                table: "ApprovalRequests",
                columns: new[] { "NoticeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_NoticeId",
                table: "ApprovalRequests",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_RequestedById",
                table: "ApprovalRequests",
                column: "RequestedById");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_ResponseId",
                table: "ApprovalRequests",
                column: "ResponseId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status",
                table: "ApprovalRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_ApprovalChainId",
                table: "ApprovalSteps",
                column: "ApprovalChainId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_ApproverId",
                table: "ApprovalSteps",
                column: "ApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_Chain_Order",
                table: "ApprovalSteps",
                columns: new[] { "ApprovalChainId", "StepOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSteps_EscalationUserId",
                table: "ApprovalSteps",
                column: "EscalationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelUnsubscribes_UserId",
                table: "ChannelUnsubscribes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_IsActive",
                table: "CustomRoles",
                column: "IsActive",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_IsSystem",
                table: "CustomRoles",
                column: "IsSystem",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_Org_DisplayOrder",
                table: "CustomRoles",
                columns: new[] { "OrganizationId", "DisplayOrder" },
                filter: "\"DeletedAt\" IS NULL AND \"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_Org_Name_Unique",
                table: "CustomRoles",
                columns: new[] { "OrganizationId", "NameNormalized" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_OrganizationId",
                table: "CustomRoles",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DataExports_OrganizationId",
                table: "DataExports",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DataExports_RequestedById",
                table: "DataExports",
                column: "RequestedById",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DataExports_Status",
                table: "DataExports",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeRelationships_CreatedById",
                table: "NoticeRelationships",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeRelationships_SourceNoticeId",
                table: "NoticeRelationships",
                column: "SourceNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeRelationships_TargetNoticeId",
                table: "NoticeRelationships",
                column: "TargetNoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeRelationships_Unique",
                table: "NoticeRelationships",
                columns: new[] { "SourceNoticeId", "TargetNoticeId", "RelationshipType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeadLetters_NotificationId",
                table: "NotificationDeadLetters",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeadLetters_OriginalDeliveryId",
                table: "NotificationDeadLetters",
                column: "OriginalDeliveryId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_DependencyType",
                table: "TaskDependencies",
                column: "DependencyType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_DependsOnTaskId",
                table: "TaskDependencies",
                column: "DependsOnTaskId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_Task_DependsOn_Unique",
                table: "TaskDependencies",
                columns: new[] { "TaskId", "DependsOnTaskId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_TaskId",
                table: "TaskDependencies",
                column: "TaskId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReminders_CreatedById",
                table: "TaskReminders",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReminders_Pending",
                table: "TaskReminders",
                columns: new[] { "IsSent", "DaysBeforeDue" },
                filter: "\"DeletedAt\" IS NULL AND \"IsSent\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReminders_Task_Days_Unique",
                table: "TaskReminders",
                columns: new[] { "TaskId", "DaysBeforeDue" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TaskReminders_TaskId",
                table: "TaskReminders",
                column: "TaskId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_Role",
                table: "TeamMembers",
                column: "Role",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_Team_User_Unique",
                table: "TeamMembers",
                columns: new[] { "TeamId", "UserId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId",
                table: "TeamMembers",
                column: "TeamId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_User_Primary_Unique",
                table: "TeamMembers",
                columns: new[] { "UserId", "IsPrimary" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL AND \"IsPrimary\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_UserId",
                table: "TeamMembers",
                column: "UserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_HierarchyPath",
                table: "Teams",
                column: "HierarchyPath",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_IsActive",
                table: "Teams",
                column: "IsActive",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_LeaderId",
                table: "Teams",
                column: "LeaderId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Org_Name_Parent_Unique",
                table: "Teams",
                columns: new[] { "OrganizationId", "NameNormalized", "ParentTeamId" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_OrganizationId",
                table: "Teams",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_ParentTeamId",
                table: "Teams",
                column: "ParentTeamId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_TaskId",
                table: "TimeEntries",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_UserId",
                table: "TimeEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_AssignedToId",
                table: "WorkflowStageInstances",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_BranchId",
                table: "WorkflowStageInstances",
                column: "BranchId",
                filter: "\"DeletedAt\" IS NULL AND \"BranchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_Instance_Branch_Status",
                table: "WorkflowStageInstances",
                columns: new[] { "WorkflowInstanceId", "BranchId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_Instance_Status",
                table: "WorkflowStageInstances",
                columns: new[] { "WorkflowInstanceId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_StageId",
                table: "WorkflowStageInstances",
                column: "StageId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_Status",
                table: "WorkflowStageInstances",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_WorkflowInstanceId",
                table: "WorkflowStageInstances",
                column: "WorkflowInstanceId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStageInstances_WorkflowStageId",
                table: "WorkflowStageInstances",
                column: "WorkflowStageId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_Attachments_PreviousVersionId",
                table: "Attachments",
                column: "PreviousVersionId",
                principalTable: "Attachments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_Tasks_TaskId",
                table: "Attachments",
                column: "TaskId",
                principalTable: "Tasks",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationMembers_CustomRoles_CustomRoleId",
                table: "OrganizationMembers",
                column: "CustomRoleId",
                principalTable: "CustomRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskAssignees_Teams_TeamId",
                table: "TaskAssignees",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Teams_AssignedTeamId",
                table: "Tasks",
                column: "AssignedTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_Attachments_PreviousVersionId",
                table: "Attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_Tasks_TaskId",
                table: "Attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationMembers_CustomRoles_CustomRoleId",
                table: "OrganizationMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskAssignees_Teams_TeamId",
                table: "TaskAssignees");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Teams_AssignedTeamId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "ApprovalActions");

            migrationBuilder.DropTable(
                name: "ChannelUnsubscribes");

            migrationBuilder.DropTable(
                name: "CustomRoles");

            migrationBuilder.DropTable(
                name: "DataExports");

            migrationBuilder.DropTable(
                name: "NoticeRelationships");

            migrationBuilder.DropTable(
                name: "NotificationDeadLetters");

            migrationBuilder.DropTable(
                name: "TaskDependencies");

            migrationBuilder.DropTable(
                name: "TaskReminders");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "TimeEntries");

            migrationBuilder.DropTable(
                name: "WorkflowStageInstances");

            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropTable(
                name: "ApprovalSteps");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "ApprovalChains");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_AssignedTeamId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_TaskAssignees_TeamId",
                table: "TaskAssignees");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationMembers_CustomRoleId",
                table: "OrganizationMembers");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_NoticeId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_NoticeId_Current",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_OriginalAttachmentId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_PreviousVersionId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_ResponseId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "AutoCreateTask",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "IsSynchronizationPoint",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "JoinType",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "MinBranchesToComplete",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "ParallelBranchId",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "TaskTemplateId",
                table: "WorkflowStages");

            migrationBuilder.DropColumn(
                name: "Comment",
                table: "WorkflowHistories");

            migrationBuilder.DropColumn(
                name: "FromStage",
                table: "WorkflowHistories");

            migrationBuilder.DropColumn(
                name: "ToStage",
                table: "WorkflowHistories");

            migrationBuilder.DropColumn(
                name: "AssignedTeamId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "TaskAssignees");

            migrationBuilder.DropColumn(
                name: "CustomRoleId",
                table: "OrganizationMembers");

            migrationBuilder.DropColumn(
                name: "SuspensionExpiresAt",
                table: "OrganizationMembers");

            migrationBuilder.DropColumn(
                name: "ActiveBranchCount",
                table: "NoticeWorkflowInstances");

            migrationBuilder.DropColumn(
                name: "HasParallelStages",
                table: "NoticeWorkflowInstances");

            migrationBuilder.DropColumn(
                name: "TemplateVersionUsed",
                table: "NoticeWorkflowInstances");

            migrationBuilder.DropColumn(
                name: "DepartmentCode",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "IsManualEntry",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "LoginAudits");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "LoginAudits");

            migrationBuilder.DropColumn(
                name: "PauseReason",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "ScheduledResumeAt",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "IsCurrentVersion",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "OriginalAttachmentId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "PreviousVersionId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "VersionNote",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsEmailVerified",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OAuthProvider",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OAuthProviderId",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "TaskId",
                table: "Attachments",
                newName: "NoticeTaskId");

            migrationBuilder.RenameIndex(
                name: "IX_Attachments_TaskId",
                table: "Attachments",
                newName: "IX_Attachments_NoticeTaskId");

            migrationBuilder.AlterColumn<string>(
                name: "FailureReason",
                table: "LoginAudits",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuthMethod",
                table: "LoginAudits",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_NoticeId",
                table: "Attachments",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_ResponseId",
                table: "Attachments",
                column: "ResponseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_Tasks_NoticeTaskId",
                table: "Attachments",
                column: "NoticeTaskId",
                principalTable: "Tasks",
                principalColumn: "Id");
        }
    }
}
