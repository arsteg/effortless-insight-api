using System.Text.Json.Serialization;

namespace EffortlessInsight.Api.DTOs;

// =============================================================================
// TASK DTOs
// =============================================================================

public record CreateTaskDto(
    string Title,
    string? Description,
    List<Guid>? Assignees,
    string? Priority,
    DateTime? DueDate,
    decimal? EstimatedHours,
    List<string>? Labels,
    Guid? ParentTaskId,
    Guid? TemplateId
);

public record UpdateTaskDto(
    string? Title,
    string? Description,
    List<Guid>? Assignees,
    string? Priority,
    DateTime? DueDate,
    decimal? EstimatedHours,
    decimal? ActualHours,
    List<string>? Labels,
    string? Status,
    string? CompletionNote
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
    DateTime AssignedAt
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
    string Content,
    string? Visibility,
    Guid? ParentCommentId,
    List<string>? AttachmentUrls
);

public record UpdateCommentDto(
    string Content
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
    string Emoji
);

public record ReactionResponseDto(
    Guid CommentId,
    List<ReactionSummaryDto> Reactions
);

// =============================================================================
// DOCUMENT REQUEST DTOs
// =============================================================================

public record CreateDocumentRequestDto(
    string Title,
    string Description,
    Guid RequestedFrom,
    DateOnly DueDate,
    string? Priority,
    List<string>? AcceptedFormats,
    Guid? TemplateId
);

public record UpdateDocumentRequestDto(
    string? Title,
    string? Description,
    DateOnly? DueDate,
    string? Priority,
    List<string>? AcceptedFormats,
    string? Status,
    string? ReviewNote
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
    string? Note
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
    string Name,
    string? Description,
    string DefaultTitle,
    string? DefaultDescription,
    string? DefaultPriority,
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
    string Name,
    string TitleTemplate,
    string DescriptionTemplate,
    string? DefaultPriority,
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
    string Name,
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
