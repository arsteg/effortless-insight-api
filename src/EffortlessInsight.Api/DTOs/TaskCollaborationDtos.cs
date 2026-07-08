using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EffortlessInsight.Api.DTOs;

// =============================================================================
// TASK DTOs
// =============================================================================

public record CreateTaskDto(
    [MaxLength(200)] string Title,
    [MaxLength(5000)] string? Description,
    List<Guid>? Assignees,
    Guid? AssignedTeamId,
    [MaxLength(20)] string? Priority,
    DateTime? DueDate,
    decimal? EstimatedHours,
    List<string>? Labels,
    Guid? ParentTaskId,
    Guid? TemplateId
);

public record UpdateTaskDto(
    [MaxLength(200)] string? Title,
    [MaxLength(5000)] string? Description,
    List<Guid>? Assignees,
    Guid? AssignedTeamId,
    bool? ClearTeamAssignment,
    [MaxLength(20)] string? Priority,
    DateTime? DueDate,
    decimal? EstimatedHours,
    decimal? ActualHours,
    List<string>? Labels,
    [MaxLength(50)] string? Status,
    [MaxLength(2000)] string? CompletionNote
);

public record TaskDto(
    Guid Id,
    Guid NoticeId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateTime? DueDate,
    decimal? EstimatedHours,
    decimal? ActualHours,
    bool IsOverdue,
    List<TaskAssigneeDto> Assignees,
    TaskTeamDto? AssignedTeam,
    List<string>? Labels,
    Guid? ParentTaskId,
    int SubtaskCount,
    int SubtasksCompleted,
    TaskUserDto CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? CompletedAt,
    TaskUserDto? CompletedBy,
    string? CompletionNote
);

public record TaskDetailDto(
    Guid Id,
    Guid NoticeId,
    string Title,
    string? Description,
    string Status,
    string Priority,
    DateTime? DueDate,
    decimal? EstimatedHours,
    decimal? ActualHours,
    bool IsOverdue,
    List<TaskAssigneeDto> Assignees,
    TaskTeamDto? AssignedTeam,
    List<string>? Labels,
    Guid? ParentTaskId,
    List<TaskDto>? Subtasks,
    TaskUserDto CreatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? CompletedAt,
    TaskUserDto? CompletedBy,
    string? CompletionNote,
    List<AttachmentDto>? Attachments
);

public record TaskAssigneeDto(
    Guid Id,
    string Name,
    string? Email,
    string? AvatarUrl,
    DateTime AssignedAt,
    Guid? TeamId,
    string? TeamName
);

public record TaskTeamDto(
    Guid Id,
    string Name,
    string? Color,
    string? Icon,
    int MemberCount
);

public record TaskUserDto(
    Guid Id,
    string Name,
    string? AvatarUrl
);

public record TaskListResponseDto(
    List<TaskDto> Tasks,
    TaskSummaryDto Summary
);

public record TaskSummaryDto(
    int Total,
    int Todo,
    int InProgress,
    int Done,
    int Blocked,
    int OnHold,
    int Overdue
);

public record MyTasksResponseDto(
    List<MyTaskDto> Tasks,
    PaginationDto Pagination
);

public record MyTaskDto(
    Guid Id,
    string Title,
    MyTaskNoticeDto Notice,
    string Status,
    string Priority,
    DateTime? DueDate,
    bool IsOverdue
);

public record MyTaskNoticeDto(
    Guid Id,
    string? Number,
    string? Type,
    MyTaskOrganizationDto Organization
);

public record MyTaskOrganizationDto(
    Guid Id,
    string Name
);

// =============================================================================
// COMMENT DTOs
// =============================================================================

public record CreateCommentRequestDto(
    [MaxLength(10000)] string Content,
    [MaxLength(20)] string? Visibility,
    Guid? ParentCommentId,
    List<string>? AttachmentUrls
);

public record UpdateCommentDto(
    [MaxLength(10000)] string Content
);

public record CommentResponseDto(
    Guid Id,
    Guid NoticeId,
    string Content,
    string? ContentHtml,
    string Visibility,
    List<MentionDto>? Mentions,
    List<string>? AttachmentUrls,
    List<ReactionSummaryDto> Reactions,
    int ReplyCount,
    CommentAuthorDto Author,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsEdited,
    bool IsDeleted,
    Guid? ParentCommentId,
    List<CommentResponseDto>? Replies
);

public record MentionDto(
    Guid UserId,
    string Username,
    string Name
);

public record ReactionSummaryDto(
    string Emoji,
    int Count,
    List<string> Users,
    bool HasReacted
);

public record CommentAuthorDto(
    Guid Id,
    string Name,
    string? AvatarUrl
);

public record CommentListResponseDto(
    List<CommentResponseDto> Comments,
    PaginationDto Pagination
);

// =============================================================================
// COMMENT REACTIONS DTOs
// =============================================================================

public record AddReactionDto(
    [MaxLength(10)] string Emoji
);

public record ReactionResponseDto(
    Guid CommentId,
    List<ReactionSummaryDto> Reactions
);

// =============================================================================
// DOCUMENT REQUEST DTOs
// =============================================================================

public record CreateDocumentRequestDto(
    [MaxLength(200)] string Title,
    [MaxLength(2000)] string Description,
    Guid RequestedFrom,
    DateOnly DueDate,
    [MaxLength(20)] string? Priority,
    List<string>? AcceptedFormats,
    Guid? TemplateId
);

public record UpdateDocumentRequestDto(
    [MaxLength(200)] string? Title,
    [MaxLength(2000)] string? Description,
    DateOnly? DueDate,
    [MaxLength(20)] string? Priority,
    List<string>? AcceptedFormats,
    [MaxLength(50)] string? Status,
    [MaxLength(2000)] string? ReviewNote
);

public record DocumentRequestDto(
    Guid Id,
    Guid NoticeId,
    string Title,
    string Description,
    string Status,
    string Priority,
    DateOnly DueDate,
    bool IsOverdue,
    int DaysRemaining,
    List<string>? AcceptedFormats,
    DocumentRequestUserDto RequestedFrom,
    DocumentRequestUserDto RequestedBy,
    DateTime? FulfilledAt,
    DocumentRequestUserDto? ReviewedBy,
    string? ReviewNote,
    List<DocumentRequestDocumentDto> Documents,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record DocumentRequestUserDto(
    Guid Id,
    string Name,
    string? Email,
    string? AvatarUrl
);

public record DocumentRequestDocumentDto(
    Guid Id,
    Guid FileId,
    string Filename,
    long SizeBytes,
    string MimeType,
    DocumentRequestUserDto UploadedBy,
    DateTime UploadedAt,
    string? Note
);

public record FulfillDocumentRequestDto(
    [MaxLength(2000)] string? Note
);

public record DocumentRequestListResponseDto(
    List<DocumentRequestDto> Requests,
    DocumentRequestSummaryDto Summary
);

public record DocumentRequestSummaryDto(
    int Total,
    int Pending,
    int Submitted,
    int Reviewing,
    int Fulfilled,
    int ResubmitNeeded,
    int Overdue
);

// =============================================================================
// ACTIVITY FEED DTOs
// =============================================================================

public record ActivityDto(
    Guid Id,
    string Type,
    DateTime Timestamp,
    ActivityActorDto? Actor,
    Dictionary<string, object> Data,
    string Message
);

public record ActivityActorDto(
    Guid Id,
    string Name,
    string? AvatarUrl
);

public record ActivityFeedResponseDto(
    List<ActivityDto> Activities,
    bool HasMore,
    string? NextCursor
);

// =============================================================================
// TEMPLATE DTOs
// =============================================================================

public record CreateTaskTemplateDto(
    [MaxLength(200)] string Name,
    [MaxLength(2000)] string? Description,
    [MaxLength(200)] string DefaultTitle,
    [MaxLength(5000)] string? DefaultDescription,
    [MaxLength(20)] string? DefaultPriority,
    decimal? DefaultEstimatedHours,
    List<string>? DefaultLabels,
    List<string>? ApplicableNoticeTypes
);

public record TaskTemplateDto(
    Guid Id,
    Guid? OrganizationId,
    string Name,
    string? Description,
    string DefaultTitle,
    string? DefaultDescription,
    string DefaultPriority,
    decimal? DefaultEstimatedHours,
    List<string>? DefaultLabels,
    List<string>? ApplicableNoticeTypes,
    bool IsActive,
    DateTime CreatedAt
);

public record CreateDocumentRequestTemplateDto(
    [MaxLength(200)] string Name,
    [MaxLength(500)] string TitleTemplate,
    [MaxLength(2000)] string DescriptionTemplate,
    [MaxLength(20)] string? DefaultPriority,
    int? DefaultDueDays,
    List<string>? AcceptedFormats,
    List<string>? ApplicableNoticeTypes
);

public record DocumentRequestTemplateDto(
    Guid Id,
    Guid? OrganizationId,
    string Name,
    string TitleTemplate,
    string DescriptionTemplate,
    string DefaultPriority,
    int DefaultDueDays,
    List<string>? AcceptedFormats,
    List<string>? ApplicableNoticeTypes,
    bool IsActive,
    DateTime CreatedAt
);

// =============================================================================
// FILE DTOs
// =============================================================================

public record FileDto(
    Guid Id,
    string Filename,
    string OriginalFilename,
    string MimeType,
    long SizeBytes,
    string? Checksum,
    FileUserDto UploadedBy,
    DateTime CreatedAt,
    Guid? FolderId
);

public record FileUserDto(
    Guid Id,
    string Name
);

public record FileFolderDto(
    Guid Id,
    string Name,
    Guid? ParentFolderId,
    List<FileFolderDto> SubFolders,
    List<FileDto> Files,
    DateTime CreatedAt
);

public record CreateFolderDto(
    [MaxLength(200)] string Name,
    Guid? ParentFolderId
);

public record FileListResponseDto(
    List<FileDto> Files,
    List<FileFolderDto> Folders,
    PaginationDto Pagination
);

// =============================================================================
// SHARED DTOs
// =============================================================================

public record PaginationDto(
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages
);

// Note: AttachmentDto is defined in Dtos.cs with additional DocumentType field

// =============================================================================
// TASK DEPENDENCY DTOs (GAP-TASK-001)
// =============================================================================

public record CreateTaskDependencyDto(
    Guid DependsOnTaskId,
    [MaxLength(50)] string? Type
);

public record TaskDependencyDto(
    Guid Id,
    Guid TaskId,
    Guid DependsOnTaskId,
    TaskSummaryInfoDto DependsOnTask,
    string DependencyType,
    DateTime CreatedAt
);

public record TaskSummaryInfoDto(
    Guid Id,
    string Title,
    string Status,
    string Priority,
    DateTime? DueDate,
    bool IsOverdue
);

public record TaskDependencyListDto(
    List<TaskDependencyDto> Dependencies,
    List<TaskSummaryInfoDto> BlockingTasks
);

// =============================================================================
// TASK REMINDER DTOs (GAP-TASK-002)
// =============================================================================

public record CreateTaskReminderDto(
    int DaysBeforeDue
);

public record TaskReminderDto(
    Guid Id,
    Guid TaskId,
    int DaysBeforeDue,
    bool IsSent,
    DateTime? SentAt,
    DateTime CreatedAt
);

// =============================================================================
// TIME ENTRY DTOs (GAP-TASK-004)
// =============================================================================

public record CreateTimeEntryDto(
    decimal Hours,
    DateOnly Date,
    [MaxLength(1000)] string? Description,
    bool IsBillable = true
);

public record UpdateTimeEntryDto(
    decimal? Hours,
    DateOnly? Date,
    [MaxLength(1000)] string? Description,
    bool? IsBillable
);

public record TimeEntryDto(
    Guid Id,
    Guid TaskId,
    TimeEntryUserDto User,
    DateOnly Date,
    decimal Hours,
    string? Description,
    bool IsBillable,
    DateTime? StartTime,
    DateTime? EndTime,
    bool IsTimerRunning,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record TimeEntryUserDto(
    Guid Id,
    string Name,
    string? AvatarUrl
);

public record TimeEntryListResponseDto(
    List<TimeEntryDto> Entries,
    decimal TotalHours,
    decimal TotalBillableHours
);

// =============================================================================
// TASK ATTACHMENT DTOs (GAP-TASK-006)
// =============================================================================

public record UploadTaskAttachmentDto(
    [MaxLength(500)] string? Description,
    [MaxLength(100)] string? DocumentType
);

public record TaskAttachmentDto(
    Guid Id,
    string FileName,
    string FileUrl,
    int? FileSize,
    string? FileType,
    string? DocumentType,
    string? Description,
    TaskAttachmentUserDto UploadedBy,
    DateTime CreatedAt
);

public record TaskAttachmentUserDto(
    Guid Id,
    string Name,
    string? AvatarUrl
);

public record TaskAttachmentListResponseDto(
    List<TaskAttachmentDto> Attachments,
    int TotalCount
);
