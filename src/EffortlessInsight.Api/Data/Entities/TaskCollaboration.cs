using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

// =============================================================================
// TASK ENTITIES (Extended from NoticeTask)
// =============================================================================

/// <summary>
/// Multi-assignee support for tasks (many-to-many relationship).
/// </summary>
public class TaskAssignee
{
    [Required]
    public Guid TaskId { get; set; }
    public NoticeTask Task { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    public Guid? AssignedById { get; set; }
    public ApplicationUser? AssignedBy { get; set; }
}

/// <summary>
/// Reusable task templates for common task types.
/// </summary>
public class TaskTemplate : BaseEntity
{
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(200)]
    public string DefaultTitle { get; set; } = string.Empty;

    public string? DefaultDescription { get; set; }

    [Required]
    [MaxLength(20)]
    public string DefaultPriority { get; set; } = TaskPriorityValues.Medium;

    public decimal? DefaultEstimatedHours { get; set; }

    /// <summary>
    /// Default labels to apply when using this template.
    /// </summary>
    public List<string>? DefaultLabels { get; set; }

    /// <summary>
    /// Notice types this template applies to. ["*"] means all types.
    /// </summary>
    public List<string>? ApplicableNoticeTypes { get; set; }

    public bool IsActive { get; set; } = true;

    public Guid? CreatedById { get; set; }
    public ApplicationUser? CreatedBy { get; set; }
}

// =============================================================================
// COMMENT ENTITIES (Extended)
// =============================================================================

/// <summary>
/// Stores edit history for comments (max 10 edits stored).
/// </summary>
public class CommentEditHistory : BaseEntity
{
    [Required]
    public Guid CommentId { get; set; }
    public Comment Comment { get; set; } = null!;

    [Required]
    public string PreviousContent { get; set; } = string.Empty;

    public DateTime EditedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Emoji reactions on comments.
/// </summary>
public class CommentReaction
{
    [Required]
    public Guid CommentId { get; set; }
    public Comment Comment { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Required]
    [MaxLength(10)]
    public string Emoji { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// =============================================================================
// DOCUMENT REQUEST ENTITIES
// =============================================================================

/// <summary>
/// Document request from CA/team to client for specific documents.
/// </summary>
public class DocumentRequest : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = DocumentRequestStatus.Pending;

    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = TaskPriorityValues.Medium;

    [Required]
    public DateOnly DueDate { get; set; }

    /// <summary>
    /// Accepted file formats (e.g., ["pdf", "zip", "doc"]).
    /// </summary>
    public List<string>? AcceptedFormats { get; set; }

    [Required]
    public Guid RequestedFromId { get; set; }
    public ApplicationUser RequestedFrom { get; set; } = null!;

    [Required]
    public Guid RequestedById { get; set; }
    public ApplicationUser RequestedBy { get; set; } = null!;

    public DateTime? FulfilledAt { get; set; }

    public Guid? ReviewedById { get; set; }
    public ApplicationUser? ReviewedBy { get; set; }

    [MaxLength(1000)]
    public string? ReviewNote { get; set; }

    public Guid? TemplateId { get; set; }
    public DocumentRequestTemplate? Template { get; set; }

    // Navigation
    public ICollection<DocumentRequestDocument> Documents { get; set; } = [];
}

/// <summary>
/// Documents uploaded to fulfill a document request.
/// </summary>
public class DocumentRequestDocument : BaseEntity
{
    [Required]
    public Guid RequestId { get; set; }
    public DocumentRequest Request { get; set; } = null!;

    [Required]
    public Guid FileId { get; set; }
    public NoticeFile File { get; set; } = null!;

    [MaxLength(500)]
    public string? Note { get; set; }

    [Required]
    public Guid UploadedById { get; set; }
    public ApplicationUser UploadedBy { get; set; } = null!;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Reusable document request templates.
/// </summary>
public class DocumentRequestTemplate : BaseEntity
{
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string TitleTemplate { get; set; } = string.Empty;

    [Required]
    public string DescriptionTemplate { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string DefaultPriority { get; set; } = TaskPriorityValues.Medium;

    public int DefaultDueDays { get; set; } = 7;

    public List<string>? AcceptedFormats { get; set; }

    /// <summary>
    /// Notice types this template applies to. ["*"] means all types.
    /// </summary>
    public List<string>? ApplicableNoticeTypes { get; set; }

    public bool IsActive { get; set; } = true;
}

// =============================================================================
// FILE MANAGEMENT ENTITIES
// =============================================================================

/// <summary>
/// General file storage entity for all uploaded files.
/// </summary>
public class NoticeFile : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public Guid? NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public Guid? FolderId { get; set; }
    public FileFolder? Folder { get; set; }

    [Required]
    [MaxLength(255)]
    public string Filename { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string OriginalFilename { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string MimeType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    [Required]
    [MaxLength(500)]
    public string StoragePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string StorageProvider { get; set; } = "s3";

    [MaxLength(64)]
    public string? Checksum { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    [Required]
    public Guid UploadedById { get; set; }
    public ApplicationUser UploadedBy { get; set; } = null!;
}

/// <summary>
/// Folder organization for files within a notice.
/// </summary>
public class FileFolder : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentFolderId { get; set; }
    public FileFolder? ParentFolder { get; set; }

    [Required]
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;

    // Navigation
    public ICollection<FileFolder> SubFolders { get; set; } = [];
    public ICollection<NoticeFile> Files { get; set; } = [];
}

// =============================================================================
// ACTIVITY LOG ENTITY
// =============================================================================

/// <summary>
/// Activity log for notice collaboration feed.
/// </summary>
public class ActivityLog : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public Guid? NoticeId { get; set; }
    public Notice? Notice { get; set; }

    [Required]
    [MaxLength(50)]
    public string ActivityType { get; set; } = string.Empty;

    public Guid? ActorId { get; set; }
    public ApplicationUser? Actor { get; set; }

    [Required]
    [MaxLength(20)]
    public string ActorType { get; set; } = "user";

    [Required]
    public Dictionary<string, object> Data { get; set; } = new();

    [Required]
    public string Message { get; set; } = string.Empty;
}

// =============================================================================
// CONSTANTS
// =============================================================================

/// <summary>
/// Task status constants with expanded workflow.
/// </summary>
public static class TaskStatusValues
{
    public const string Todo = "todo";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Blocked = "blocked";
    public const string OnHold = "on_hold";
    public const string Archived = "archived";

    public static readonly string[] All =
    [
        Todo, InProgress, Done, Blocked, OnHold, Archived
    ];

    public static readonly string[] ActiveStatuses =
    [
        Todo, InProgress, Blocked, OnHold
    ];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Task priority constants.
/// </summary>
public static class TaskPriorityValues
{
    public const string Critical = "critical";
    public const string High = "high";
    public const string Medium = "medium";
    public const string Low = "low";

    public static readonly string[] All = [Critical, High, Medium, Low];

    public static bool IsValid(string priority) => All.Contains(priority, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Document request status constants.
/// </summary>
public static class DocumentRequestStatus
{
    public const string Pending = "pending";
    public const string Submitted = "submitted";
    public const string Reviewing = "reviewing";
    public const string Fulfilled = "fulfilled";
    public const string ResubmitNeeded = "resubmit_needed";
    public const string Cancelled = "cancelled";

    public static readonly string[] All =
    [
        Pending, Submitted, Reviewing, Fulfilled, ResubmitNeeded, Cancelled
    ];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Comment visibility constants.
/// </summary>
public static class CommentVisibility
{
    public const string All = "all";
    public const string Internal = "internal";

    public static readonly string[] Values = [All, Internal];

    public static bool IsValid(string visibility) => Values.Contains(visibility, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Activity type constants.
/// </summary>
public static class ActivityTypes
{
    // Task activities
    public const string TaskCreated = "task_created";
    public const string TaskUpdated = "task_updated";
    public const string TaskCompleted = "task_completed";
    public const string TaskAssigned = "task_assigned";
    public const string TaskStatusChanged = "task_status_changed";

    // Comment activities
    public const string CommentAdded = "comment_added";
    public const string CommentEdited = "comment_edited";
    public const string CommentDeleted = "comment_deleted";
    public const string CommentReaction = "comment_reaction";
    public const string UserMentioned = "user_mentioned";

    // Document activities
    public const string DocumentRequested = "document_requested";
    public const string DocumentUploaded = "document_uploaded";
    public const string DocumentReviewed = "document_reviewed";
    public const string DocumentOverdue = "document_overdue";

    // System activities
    public const string NoticeAssigned = "notice_assigned";
    public const string NoticeStatusChanged = "notice_status_changed";
    public const string AiAnalysisCompleted = "ai_analysis_completed";
    public const string WorkflowStageChanged = "workflow_stage_changed";
}

/// <summary>
/// Allowed emoji reactions for comments.
/// </summary>
public static class AllowedReactions
{
    public const string ThumbsUp = "👍";
    public const string Heart = "❤️";
    public const string Smile = "😊";
    public const string Celebration = "🎉";

    public static readonly string[] All = [ThumbsUp, Heart, Smile, Celebration];

    public static bool IsValid(string emoji) => All.Contains(emoji);
}
