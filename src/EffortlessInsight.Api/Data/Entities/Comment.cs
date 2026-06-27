using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

public class Comment : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public Guid? ParentId { get; set; }
    public Comment? Parent { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// HTML-rendered content with sanitized markdown output.
    /// </summary>
    public string? ContentHtml { get; set; }

    /// <summary>
    /// Visibility: 'all' (visible to everyone) or 'internal' (hidden from clients).
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string Visibility { get; set; } = "all";

    public List<Guid>? Mentions { get; set; }

    public bool IsInternal { get; set; }

    /// <summary>
    /// Whether this comment has been edited.
    /// </summary>
    public bool IsEdited { get; set; }

    /// <summary>
    /// Number of times this comment has been edited (max 10 stored).
    /// </summary>
    public int EditCount { get; set; }

    /// <summary>
    /// Soft delete flag for showing "This comment was deleted" placeholder.
    /// </summary>
    public bool IsDeleted { get; set; }

    public List<string>? AttachmentUrls { get; set; }

    /// <summary>
    /// Max nesting depth (for validation, up to 3 levels).
    /// </summary>
    public int Depth { get; set; }

    // Navigation
    public ICollection<Comment> Replies { get; set; } = [];
    public ICollection<CommentEditHistory> EditHistory { get; set; } = [];
    public ICollection<CommentReaction> Reactions { get; set; } = [];
}

public class NoticeTask : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    /// <summary>
    /// Parent task for subtask support.
    /// </summary>
    public Guid? ParentTaskId { get; set; }
    public NoticeTask? ParentTask { get; set; }

    [Required]
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;

    /// <summary>
    /// Legacy single assignee (kept for backward compatibility).
    /// Use Assignees collection for multi-assignee support.
    /// </summary>
    public Guid? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    /// <summary>
    /// Team assigned to this task (optional).
    /// When set, all team members are considered assignees.
    /// </summary>
    public Guid? AssignedTeamId { get; set; }
    public Team? AssignedTeam { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>
    /// Estimated hours to complete the task.
    /// </summary>
    public decimal? EstimatedHours { get; set; }

    /// <summary>
    /// Actual hours spent on the task.
    /// </summary>
    public decimal? ActualHours { get; set; }

    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = TaskPriorityValues.Medium;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = TaskStatusValues.Todo;

    /// <summary>
    /// Note added when completing the task.
    /// </summary>
    public string? CompletionNote { get; set; }

    public DateTime? CompletedAt { get; set; }

    public Guid? CompletedById { get; set; }
    public ApplicationUser? CompletedBy { get; set; }

    /// <summary>
    /// Task labels/tags for categorization.
    /// </summary>
    public List<string>? Labels { get; set; }

    /// <summary>
    /// Template used to create this task.
    /// </summary>
    public Guid? TemplateId { get; set; }
    public TaskTemplate? Template { get; set; }

    // Navigation
    public ICollection<TaskAssignee> Assignees { get; set; } = [];
    public ICollection<NoticeTask> Subtasks { get; set; } = [];
    public ICollection<Attachment> Attachments { get; set; } = [];

    /// <summary>
    /// Dependencies where this task blocks other tasks (this task must complete first).
    /// </summary>
    public ICollection<TaskDependency> Dependencies { get; set; } = [];

    /// <summary>
    /// Dependencies where this task is blocked by other tasks (other tasks must complete first).
    /// </summary>
    public ICollection<TaskDependency> DependsOn { get; set; } = [];

    /// <summary>
    /// Reminders set for this task.
    /// </summary>
    public ICollection<TaskReminder> Reminders { get; set; } = [];

    /// <summary>
    /// Time entries logged for this task.
    /// </summary>
    public ICollection<TimeEntry> TimeEntries { get; set; } = [];

    /// <summary>
    /// Soft delete flag.
    /// </summary>
    [NotMapped]
    public bool IsDeleted => DeletedAt != null;

    /// <summary>
    /// Organization ID (derived from Notice).
    /// </summary>
    [NotMapped]
    public Guid OrganizationId => Notice?.OrganizationId ?? Guid.Empty;
}

public class Attachment : BaseEntity
{
    public Guid? NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public Guid? ResponseId { get; set; }
    public NoticeResponse? Response { get; set; }

    /// <summary>
    /// Task this attachment belongs to (for task attachments).
    /// </summary>
    public Guid? TaskId { get; set; }
    public NoticeTask? Task { get; set; }

    [Required]
    public Guid UploadedById { get; set; }
    public ApplicationUser UploadedBy { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string FileUrl { get; set; } = string.Empty;

    public int? FileSize { get; set; }

    [MaxLength(50)]
    public string? FileType { get; set; }

    [MaxLength(50)]
    public string? DocumentType { get; set; } // gstr2a, gstr3b, purchase_register, invoices, bank_statement, other

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(64)]
    public string? FileHash { get; set; }

    // ============================================================================
    // Versioning
    // ============================================================================

    /// <summary>
    /// Version number of this attachment (starts at 1).
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Reference to the previous version of this attachment.
    /// </summary>
    public Guid? PreviousVersionId { get; set; }
    public Attachment? PreviousVersion { get; set; }

    /// <summary>
    /// Whether this is the current (latest) version.
    /// </summary>
    public bool IsCurrentVersion { get; set; } = true;

    /// <summary>
    /// Optional note explaining what changed in this version.
    /// </summary>
    [MaxLength(500)]
    public string? VersionNote { get; set; }

    /// <summary>
    /// Original attachment ID (first version). Used to group all versions together.
    /// </summary>
    public Guid? OriginalAttachmentId { get; set; }
}

public class NoticeResponse : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;

    public Guid? ApprovedById { get; set; }
    public ApplicationUser? ApprovedBy { get; set; }

    public string? DraftContent { get; set; }

    public string? FinalContent { get; set; }

    [MaxLength(20)]
    public string Status { get; set; } = "draft"; // draft, review, approved, submitted

    [MaxLength(100)]
    public string? SubmissionReference { get; set; }

    public DateTime? SubmittedAt { get; set; }

    [MaxLength(500)]
    public string? SubmissionProofUrl { get; set; }

    public int Version { get; set; } = 1;

    // Navigation
    public ICollection<Attachment> Attachments { get; set; } = [];
}

public class DeadlineReminder : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string ReminderType { get; set; } = string.Empty; // email, sms, push, whatsapp

    [Required]
    public DateTime RemindAt { get; set; }

    public int? DaysBefore { get; set; }

    public bool IsSent { get; set; }

    public DateTime? SentAt { get; set; }
}

/// <summary>
/// Stores audit trail of all significant actions in the system.
/// Used for compliance, security auditing, and forensic analysis.
/// </summary>
public class AuditLog : BaseEntity
{
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }

    [Required]
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public Dictionary<string, object>? OldValues { get; set; }

    public Dictionary<string, object>? NewValues { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>
    /// Additional metadata about the action
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

public class Plan : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Code { get; set; } = string.Empty;

    public decimal? PriceMonthly { get; set; }

    public decimal? PriceYearly { get; set; }

    public int? NoticeLimit { get; set; }

    public int? UserLimit { get; set; }

    public int? GstinLimit { get; set; }

    public int? StorageLimitGb { get; set; }

    public Dictionary<string, object>? Features { get; set; }

    public bool IsActive { get; set; } = true;
}

public class Subscription : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid PlanId { get; set; }
    public Plan Plan { get; set; } = null!;

    [MaxLength(20)]
    public string Status { get; set; } = "active";

    [MaxLength(20)]
    public string? BillingCycle { get; set; } // monthly, yearly

    public DateOnly? CurrentPeriodStart { get; set; }

    public DateOnly? CurrentPeriodEnd { get; set; }

    public bool CancelAtPeriodEnd { get; set; }

    [MaxLength(50)]
    public string? PaymentProvider { get; set; }

    [MaxLength(100)]
    public string? ProviderSubscriptionId { get; set; }
}

public class Embedding : BaseEntity
{
    [Required]
    [MaxLength(50)]
    public string SourceType { get; set; } = string.Empty; // notice, gst_rule, circular, case_law, template

    public Guid? SourceId { get; set; }

    [MaxLength(64)]
    public string? ContentHash { get; set; }

    public int ChunkIndex { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    public Pgvector.Vector Vector { get; set; } = null!;

    public Dictionary<string, object>? Metadata { get; set; }
}

public class KnowledgeBase : BaseEntity
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // gst_rule, circular, notification, case_law

    [MaxLength(100)]
    public string? ReferenceNumber { get; set; }

    [MaxLength(500)]
    public string? Title { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateOnly? EffectiveDate { get; set; }

    public bool IsActive { get; set; } = true;

    public Dictionary<string, object>? Metadata { get; set; }
}
