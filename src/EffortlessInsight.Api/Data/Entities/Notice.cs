using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

public class Notice : BaseEntity
{
    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid UploadedById { get; set; }
    public ApplicationUser UploadedBy { get; set; } = null!;

    public Guid? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    [MaxLength(20)]
    public string? NoticeType { get; set; } // DRC-01, ASMT-10, etc.

    [MaxLength(50)]
    public string? NoticeCategory { get; set; } // assessment, demand, registration, etc.

    [MaxLength(100)]
    public string? NoticeNumber { get; set; }

    [MaxLength(15)]
    public string? Gstin { get; set; }

    public DateOnly? IssueDate { get; set; }

    public DateOnly? ResponseDeadline { get; set; }

    public DateOnly? ExtendedDeadline { get; set; }

    public decimal? TaxAmount { get; set; }

    public decimal? PenaltyAmount { get; set; }

    public decimal? InterestAmount { get; set; }

    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    [MaxLength(255)]
    public string? IssuingAuthority { get; set; }

    [MaxLength(255)]
    public string? IssuingOfficer { get; set; }

    [MaxLength(30)]
    public string Status { get; set; } = "pending"; // pending, processing, analyzed, in_progress, responded, closed, archived

    [MaxLength(20)]
    public string Priority { get; set; } = "medium"; // low, medium, high, critical

    [Required]
    [MaxLength(500)]
    public string FileUrl { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? FileHash { get; set; } // For duplicate detection

    public string? OcrText { get; set; }

    public decimal? OcrConfidence { get; set; }

    [MaxLength(30)]
    public string ProcessingStatus { get; set; } = "queued"; // queued, processing, completed, failed

    public string? ProcessingError { get; set; }

    public List<string>? Tags { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    // Navigation properties
    public NoticeAiReport? AiReport { get; set; }
    public ICollection<Comment> Comments { get; set; } = [];
    public ICollection<NoticeTask> Tasks { get; set; } = [];
    public ICollection<Attachment> Attachments { get; set; } = [];
    public ICollection<NoticeResponse> Responses { get; set; } = [];
    public ICollection<DeadlineReminder> Reminders { get; set; } = [];
}
