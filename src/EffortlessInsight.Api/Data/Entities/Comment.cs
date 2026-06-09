using System.ComponentModel.DataAnnotations;

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

    public List<Guid>? Mentions { get; set; }

    public bool IsInternal { get; set; }

    public List<string>? AttachmentUrls { get; set; }

    // Navigation
    public ICollection<Comment> Replies { get; set; } = [];
}

public class NoticeTask : BaseEntity
{
    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    [Required]
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;

    public Guid? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }

    [MaxLength(20)]
    public string Priority { get; set; } = "medium";

    [MaxLength(20)]
    public string Status { get; set; } = "pending"; // pending, in_progress, completed, cancelled

    public DateTime? CompletedAt { get; set; }

    public Guid? CompletedById { get; set; }
}

public class Attachment : BaseEntity
{
    public Guid? NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public Guid? ResponseId { get; set; }
    public NoticeResponse? Response { get; set; }

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
    public string? DocumentType { get; set; } // invoice, gstr, bank_statement, etc.
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
