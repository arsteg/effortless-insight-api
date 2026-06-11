using System;
using System.Collections.Generic;
using EffortlessInsight.Api.Data.Entities;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EffortlessInsight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_AssignedToId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_NoticeId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Notices_AssignedToId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Gstin",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_OrganizationId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_ResponseDeadline",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Status",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_NoticeResponses_NoticeId",
                table: "NoticeResponses");

            migrationBuilder.DropIndex(
                name: "IX_DeadlineReminders_NoticeId",
                table: "DeadlineReminders");

            migrationBuilder.DropIndex(
                name: "IX_DeadlineReminders_UserId",
                table: "DeadlineReminders");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAmount",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PenaltyAmount",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "OcrConfidence",
                table: "Notices",
                type: "numeric(5,4)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "InterestAmount",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedById",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedById",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "Notices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileMimeType",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "Notices",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FileSize",
                table: "Notices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FinancialYear",
                table: "Notices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GstinId",
                table: "Notices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "HearingDate",
                table: "Notices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Jurisdiction",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoticeSubCategory",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OcrLanguage",
                table: "Notices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficerDesignation",
                table: "Notices",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PageCount",
                table: "Notices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessingAttempts",
                table: "Notices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingCompletedAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                table: "Notices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDemand",
                table: "Notices",
                type: "numeric(15,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Attachments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileHash",
                table: "Attachments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NoticeDeadlines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeadlineType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OriginalDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ExtractionConfidence = table.Column<int>(type: "integer", nullable: true),
                    ExtractedText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReminderEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderDaysBefore = table.Column<List<int>>(type: "jsonb", nullable: false),
                    LastReminderSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeDeadlines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeDeadlines_AspNetUsers_VerifiedById",
                        column: x => x.VerifiedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NoticeDeadlines_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ApplicableNoticeTypes = table.Column<List<string>>(type: "jsonb", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplates_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplates_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeadlineExtensions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeDeadlineId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NewDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DaysExtended = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExtensionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ExternalReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SupportingDocumentIds = table.Column<List<string>>(type: "jsonb", nullable: true),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadlineExtensions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeadlineExtensions_AspNetUsers_RequestedById",
                        column: x => x.RequestedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeadlineExtensions_AspNetUsers_ReviewedById",
                        column: x => x.ReviewedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DeadlineExtensions_NoticeDeadlines_NoticeDeadlineId",
                        column: x => x.NoticeDeadlineId,
                        principalTable: "NoticeDeadlines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeadlineExtensions_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowAssignmentRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Conditions = table.Column<List<RuleCondition>>(type: "jsonb", nullable: false),
                    Actions = table.Column<List<RuleAction>>(type: "jsonb", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowAssignmentRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowAssignmentRules_WorkflowTemplates_WorkflowTemplateId",
                        column: x => x.WorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowEscalationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TriggerPercent = table.Column<int>(type: "integer", nullable: false),
                    Actions = table.Column<List<EscalationAction>>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowEscalationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowEscalationRules_WorkflowTemplates_WorkflowTemplateId",
                        column: x => x.WorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowSlaMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PeriodType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalNotices = table.Column<int>(type: "integer", nullable: false),
                    NoticesEntered = table.Column<int>(type: "integer", nullable: false),
                    NoticesCompleted = table.Column<int>(type: "integer", nullable: false),
                    NoticesInProgress = table.Column<int>(type: "integer", nullable: false),
                    SlaMetCount = table.Column<int>(type: "integer", nullable: false),
                    SlaBreachedCount = table.Column<int>(type: "integer", nullable: false),
                    SlaWarningCount = table.Column<int>(type: "integer", nullable: false),
                    SlaComplianceRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalProcessingTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    AverageProcessingTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    MedianProcessingTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    MinProcessingTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaxProcessingTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    EscalationCount = table.Column<int>(type: "integer", nullable: false),
                    ReassignmentCount = table.Column<int>(type: "integer", nullable: false),
                    UniqueAssignees = table.Column<int>(type: "integer", nullable: false),
                    AverageNoticesPerAssignee = table.Column<decimal>(type: "numeric", nullable: false),
                    AssigneeBreakdown = table.Column<Dictionary<string, AssigneeMetrics>>(type: "jsonb", nullable: true),
                    NoticeTypeBreakdown = table.Column<Dictionary<string, int>>(type: "jsonb", nullable: true),
                    PriorityBreakdown = table.Column<Dictionary<string, int>>(type: "jsonb", nullable: true),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSlaMetrics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowSlaMetrics_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowSlaMetrics_WorkflowTemplates_WorkflowTemplateId",
                        column: x => x.WorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StageKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StageOrder = table.Column<int>(type: "integer", nullable: false),
                    SlaHours = table.Column<int>(type: "integer", nullable: true),
                    SlaWarningPercent = table.Column<int>(type: "integer", nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AllowedTransitions = table.Column<List<string>>(type: "jsonb", nullable: false),
                    EntryActions = table.Column<List<WorkflowAction>>(type: "jsonb", nullable: false),
                    ExitActions = table.Column<List<WorkflowAction>>(type: "jsonb", nullable: false),
                    AutoTransitionRules = table.Column<List<AutoTransitionRule>>(type: "jsonb", nullable: false),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStages_WorkflowTemplates_WorkflowTemplateId",
                        column: x => x.WorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NoticeWorkflowInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStageKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CurrentStageId = table.Column<Guid>(type: "uuid", nullable: true),
                    StageEnteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SlaDeadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SlaStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SlaPercentConsumed = table.Column<int>(type: "integer", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PreviousAssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletionOutcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TotalTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    SlaBreachCount = table.Column<int>(type: "integer", nullable: false),
                    TransitionCount = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeWorkflowInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeWorkflowInstances_AspNetUsers_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NoticeWorkflowInstances_AspNetUsers_PreviousAssigneeId",
                        column: x => x.PreviousAssigneeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NoticeWorkflowInstances_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoticeWorkflowInstances_WorkflowStages_CurrentStageId",
                        column: x => x.CurrentStageId,
                        principalTable: "WorkflowStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_NoticeWorkflowInstances_WorkflowTemplates_WorkflowTemplateId",
                        column: x => x.WorkflowTemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoticeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FromStageKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ToStageKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PerformedById = table.Column<Guid>(type: "uuid", nullable: true),
                    PerformedBySystem = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PreviousAssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewAssigneeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TimeInStageMinutes = table.Column<int>(type: "integer", nullable: true),
                    SlaStatusAtEvent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EventData = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowHistories_AspNetUsers_PerformedById",
                        column: x => x.PerformedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkflowHistories_NoticeWorkflowInstances_WorkflowInstanceId",
                        column: x => x.WorkflowInstanceId,
                        principalTable: "NoticeWorkflowInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowHistories_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeTasks_NoticeId_Status",
                table: "Tasks",
                columns: new[] { "NoticeId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedToId",
                table: "Tasks",
                column: "AssignedToId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CompletedById",
                table: "Tasks",
                column: "CompletedById");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_NoticeId",
                table: "Tasks",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_AssignedById",
                table: "Notices",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_AssignedToId",
                table: "Notices",
                column: "AssignedToId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_CreatedAt",
                table: "Notices",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_DeletedById",
                table: "Notices",
                column: "DeletedById");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_FileHash",
                table: "Notices",
                column: "FileHash",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Gstin",
                table: "Notices",
                column: "Gstin",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_GstinId",
                table: "Notices",
                column: "GstinId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Org_Status_Deadline",
                table: "Notices",
                columns: new[] { "OrganizationId", "Status", "ResponseDeadline" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_OrganizationId",
                table: "Notices",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Priority",
                table: "Notices",
                column: "Priority",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_ResponseDeadline",
                table: "Notices",
                column: "ResponseDeadline",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Status",
                table: "Notices",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeResponses_NoticeId",
                table: "NoticeResponses",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeResponses_NoticeId_CreatedAt",
                table: "NoticeResponses",
                columns: new[] { "NoticeId", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeResponses_NoticeId_Status",
                table: "NoticeResponses",
                columns: new[] { "NoticeId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineReminders_NoticeId",
                table: "DeadlineReminders",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineReminders_Pending",
                table: "DeadlineReminders",
                columns: new[] { "RemindAt", "IsSent" },
                filter: "\"DeletedAt\" IS NULL AND \"IsSent\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineReminders_UserId",
                table: "DeadlineReminders",
                column: "UserId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineExtensions_NoticeDeadlineId",
                table: "DeadlineExtensions",
                column: "NoticeDeadlineId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineExtensions_NoticeId",
                table: "DeadlineExtensions",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineExtensions_RequestedById",
                table: "DeadlineExtensions",
                column: "RequestedById",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineExtensions_ReviewedById",
                table: "DeadlineExtensions",
                column: "ReviewedById");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineExtensions_Status",
                table: "DeadlineExtensions",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeDeadlines_ActiveDeadlines",
                table: "NoticeDeadlines",
                columns: new[] { "EffectiveDeadline", "Status" },
                filter: "\"DeletedAt\" IS NULL AND \"Status\" IN ('pending', 'in_progress')");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeDeadlines_DeadlineType",
                table: "NoticeDeadlines",
                column: "DeadlineType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeDeadlines_EffectiveDeadline",
                table: "NoticeDeadlines",
                column: "EffectiveDeadline",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeDeadlines_NoticeId",
                table: "NoticeDeadlines",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeDeadlines_Status",
                table: "NoticeDeadlines",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeDeadlines_VerifiedById",
                table: "NoticeDeadlines",
                column: "VerifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_ActiveSlaDeadline",
                table: "NoticeWorkflowInstances",
                column: "SlaDeadline",
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_AssignedToId",
                table: "NoticeWorkflowInstances",
                column: "AssignedToId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_CurrentStageId",
                table: "NoticeWorkflowInstances",
                column: "CurrentStageId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_Notice_Status",
                table: "NoticeWorkflowInstances",
                columns: new[] { "NoticeId", "Status" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_PreviousAssigneeId",
                table: "NoticeWorkflowInstances",
                column: "PreviousAssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_SingleActivePerNotice",
                table: "NoticeWorkflowInstances",
                column: "NoticeId",
                unique: true,
                filter: "\"DeletedAt\" IS NULL AND \"Status\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_SlaStatus",
                table: "NoticeWorkflowInstances",
                column: "SlaStatus",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_Status",
                table: "NoticeWorkflowInstances",
                column: "Status",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeWorkflowInstances_WorkflowTemplateId",
                table: "NoticeWorkflowInstances",
                column: "WorkflowTemplateId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAssignmentRules_Template_Priority",
                table: "WorkflowAssignmentRules",
                columns: new[] { "WorkflowTemplateId", "Priority" },
                filter: "\"DeletedAt\" IS NULL AND \"IsEnabled\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowAssignmentRules_WorkflowTemplateId",
                table: "WorkflowAssignmentRules",
                column: "WorkflowTemplateId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEscalationRules_Template_Trigger",
                table: "WorkflowEscalationRules",
                columns: new[] { "WorkflowTemplateId", "TriggerPercent" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEscalationRules_WorkflowTemplateId",
                table: "WorkflowEscalationRules",
                column: "WorkflowTemplateId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistories_CreatedAt",
                table: "WorkflowHistories",
                column: "CreatedAt",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistories_EventType",
                table: "WorkflowHistories",
                column: "EventType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistories_NoticeId",
                table: "WorkflowHistories",
                column: "NoticeId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistories_PerformedById",
                table: "WorkflowHistories",
                column: "PerformedById",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistories_WorkflowInstanceId",
                table: "WorkflowHistories",
                column: "WorkflowInstanceId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowHistory_Instance_CreatedAt",
                table: "WorkflowHistories",
                columns: new[] { "WorkflowInstanceId", "CreatedAt" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSlaMetrics_Org_Template_Period_Unique",
                table: "WorkflowSlaMetrics",
                columns: new[] { "OrganizationId", "WorkflowTemplateId", "PeriodType", "PeriodStart" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSlaMetrics_OrganizationId",
                table: "WorkflowSlaMetrics",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSlaMetrics_PeriodType",
                table: "WorkflowSlaMetrics",
                column: "PeriodType",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSlaMetrics_WorkflowTemplateId",
                table: "WorkflowSlaMetrics",
                column: "WorkflowTemplateId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_Template_Order",
                table: "WorkflowStages",
                columns: new[] { "WorkflowTemplateId", "StageOrder" },
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_Template_StageKey_Unique",
                table: "WorkflowStages",
                columns: new[] { "WorkflowTemplateId", "StageKey" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStages_WorkflowTemplateId",
                table: "WorkflowStages",
                column: "WorkflowTemplateId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_CreatedById",
                table: "WorkflowTemplates",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_IsActive",
                table: "WorkflowTemplates",
                column: "IsActive",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_IsSystem",
                table: "WorkflowTemplates",
                column: "IsSystem",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_Org_Name_Unique",
                table: "WorkflowTemplates",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_OrganizationId",
                table: "WorkflowTemplates",
                column: "OrganizationId",
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_AspNetUsers_AssignedById",
                table: "Notices",
                column: "AssignedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_AspNetUsers_DeletedById",
                table: "Notices",
                column: "DeletedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notices_OrganizationGstins_GstinId",
                table: "Notices",
                column: "GstinId",
                principalTable: "OrganizationGstins",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_AspNetUsers_CompletedById",
                table: "Tasks",
                column: "CompletedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notices_AspNetUsers_AssignedById",
                table: "Notices");

            migrationBuilder.DropForeignKey(
                name: "FK_Notices_AspNetUsers_DeletedById",
                table: "Notices");

            migrationBuilder.DropForeignKey(
                name: "FK_Notices_OrganizationGstins_GstinId",
                table: "Notices");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_AspNetUsers_CompletedById",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "DeadlineExtensions");

            migrationBuilder.DropTable(
                name: "WorkflowAssignmentRules");

            migrationBuilder.DropTable(
                name: "WorkflowEscalationRules");

            migrationBuilder.DropTable(
                name: "WorkflowHistories");

            migrationBuilder.DropTable(
                name: "WorkflowSlaMetrics");

            migrationBuilder.DropTable(
                name: "NoticeDeadlines");

            migrationBuilder.DropTable(
                name: "NoticeWorkflowInstances");

            migrationBuilder.DropTable(
                name: "WorkflowStages");

            migrationBuilder.DropTable(
                name: "WorkflowTemplates");

            migrationBuilder.DropIndex(
                name: "IX_NoticeTasks_NoticeId_Status",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_AssignedToId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_CompletedById",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_NoticeId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Notices_AssignedById",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_AssignedToId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_CreatedAt",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_DeletedById",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_FileHash",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Gstin",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_GstinId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Org_Status_Deadline",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_OrganizationId",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Priority",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_ResponseDeadline",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Status",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_NoticeResponses_NoticeId",
                table: "NoticeResponses");

            migrationBuilder.DropIndex(
                name: "IX_NoticeResponses_NoticeId_CreatedAt",
                table: "NoticeResponses");

            migrationBuilder.DropIndex(
                name: "IX_NoticeResponses_NoticeId_Status",
                table: "NoticeResponses");

            migrationBuilder.DropIndex(
                name: "IX_DeadlineReminders_NoticeId",
                table: "DeadlineReminders");

            migrationBuilder.DropIndex(
                name: "IX_DeadlineReminders_Pending",
                table: "DeadlineReminders");

            migrationBuilder.DropIndex(
                name: "IX_DeadlineReminders_UserId",
                table: "DeadlineReminders");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "AssignedById",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "DeletedById",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "FileMimeType",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "FileSize",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "FinancialYear",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "GstinId",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "HearingDate",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "Jurisdiction",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "NoticeSubCategory",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "OcrLanguage",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "OfficerDesignation",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "PageCount",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "ProcessingAttempts",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "ProcessingCompletedAt",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "TotalDemand",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "FileHash",
                table: "Attachments");

            migrationBuilder.AlterColumn<decimal>(
                name: "TaxAmount",
                table: "Notices",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "PenaltyAmount",
                table: "Notices",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "OcrConfidence",
                table: "Notices",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,4)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "InterestAmount",
                table: "Notices",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(15,2)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedToId",
                table: "Tasks",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_NoticeId",
                table: "Tasks",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_AssignedToId",
                table: "Notices",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Gstin",
                table: "Notices",
                column: "Gstin");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_OrganizationId",
                table: "Notices",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_ResponseDeadline",
                table: "Notices",
                column: "ResponseDeadline");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Status",
                table: "Notices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeResponses_NoticeId",
                table: "NoticeResponses",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineReminders_NoticeId",
                table: "DeadlineReminders",
                column: "NoticeId");

            migrationBuilder.CreateIndex(
                name: "IX_DeadlineReminders_UserId",
                table: "DeadlineReminders",
                column: "UserId");
        }
    }
}
