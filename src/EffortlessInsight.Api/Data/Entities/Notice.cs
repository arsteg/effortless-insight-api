using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a GST notice uploaded to the system for AI analysis and management.
/// </summary>
public class Notice : BaseEntity
{
    // ============================================================================
    // Organization & User Relationships
    // ============================================================================

    [Required]
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required]
    public Guid UploadedById { get; set; }
    public ApplicationUser UploadedBy { get; set; } = null!;

    public Guid? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public Guid? AssignedById { get; set; }
    public ApplicationUser? AssignedBy { get; set; }

    public DateTime? AssignedAt { get; set; }

    // ============================================================================
    // Notice Identification
    // ============================================================================

    [MaxLength(100)]
    public string? NoticeNumber { get; set; }

    [MaxLength(20)]
    public string? NoticeType { get; set; } // DRC-01, ASMT-10, REG-17, etc.

    [MaxLength(50)]
    public string? NoticeCategory { get; set; } // assessment, demand, registration, refund, audit, etc.

    [MaxLength(100)]
    public string? NoticeSubCategory { get; set; } // itc_mismatch, turnover_mismatch, etc.

    // ============================================================================
    // GSTIN
    // ============================================================================

    [MaxLength(15)]
    public string? Gstin { get; set; }

    public Guid? GstinId { get; set; }
    public OrganizationGstin? GstinNavigation { get; set; }

    // ============================================================================
    // Dates
    // ============================================================================

    public DateOnly? IssueDate { get; set; }

    public DateOnly? ResponseDeadline { get; set; }

    public DateOnly? ExtendedDeadline { get; set; }

    public DateOnly? HearingDate { get; set; }

    // ============================================================================
    // Financial Amounts
    // ============================================================================

    [Column(TypeName = "decimal(15,2)")]
    public decimal? TaxAmount { get; set; }

    [Column(TypeName = "decimal(15,2)")]
    public decimal? PenaltyAmount { get; set; }

    [Column(TypeName = "decimal(15,2)")]
    public decimal? InterestAmount { get; set; }

    /// <summary>
    /// Computed total demand (Tax + Penalty + Interest).
    /// This is stored as a computed column in the database.
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    [Column(TypeName = "decimal(15,2)")]
    public decimal? TotalDemand { get; private set; }

    // ============================================================================
    // Period Covered
    // ============================================================================

    public DateOnly? PeriodFrom { get; set; }

    public DateOnly? PeriodTo { get; set; }

    [MaxLength(10)]
    public string? FinancialYear { get; set; } // 2025-26

    // ============================================================================
    // Issuing Authority
    // ============================================================================

    [MaxLength(255)]
    public string? IssuingAuthority { get; set; }

    [MaxLength(255)]
    public string? IssuingOfficer { get; set; }

    [MaxLength(100)]
    public string? OfficerDesignation { get; set; }

    [MaxLength(100)]
    public string? Jurisdiction { get; set; }

    // ============================================================================
    // Status & Priority
    // ============================================================================

    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = NoticeStatus.Uploaded;

    [Required]
    [MaxLength(20)]
    public string Priority { get; set; } = NoticePriority.Medium;

    // ============================================================================
    // File Information
    // ============================================================================

    [Required]
    [MaxLength(500)]
    public string FileUrl { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    public int FileSize { get; set; }

    [MaxLength(100)]
    public string? FileMimeType { get; set; }

    [MaxLength(64)]
    public string? FileHash { get; set; } // SHA-256 for duplicate detection

    public int? PageCount { get; set; }

    // ============================================================================
    // OCR Information
    // ============================================================================

    public string? OcrText { get; set; }

    [Column(TypeName = "decimal(5,4)")]
    public decimal? OcrConfidence { get; set; }

    [MaxLength(10)]
    public string? OcrLanguage { get; set; } // en, hi

    // ============================================================================
    // AI Processing
    // ============================================================================

    [Required]
    [MaxLength(30)]
    public string ProcessingStatus { get; set; } = NoticeProcessingStatus.Queued;

    public DateTime? ProcessingStartedAt { get; set; }

    public DateTime? ProcessingCompletedAt { get; set; }

    public string? ProcessingError { get; set; }

    public int ProcessingAttempts { get; set; }

    // ============================================================================
    // Metadata
    // ============================================================================

    public List<string>? Tags { get; set; }

    public Dictionary<string, object>? Metadata { get; set; }

    // ============================================================================
    // Soft Delete
    // ============================================================================

    public Guid? DeletedById { get; set; }
    public ApplicationUser? DeletedBy { get; set; }

    [MaxLength(500)]
    public string? DeletionReason { get; set; }

    // ============================================================================
    // Navigation Properties
    // ============================================================================

    public NoticeAiReport? AiReport { get; set; }
    public ICollection<Comment> Comments { get; set; } = [];
    public ICollection<NoticeTask> Tasks { get; set; } = [];
    public ICollection<Attachment> Attachments { get; set; } = [];
    public ICollection<NoticeResponse> Responses { get; set; } = [];
    public ICollection<DeadlineReminder> Reminders { get; set; } = [];
}

/// <summary>
/// Notice status constants following the defined workflow.
/// </summary>
public static class NoticeStatus
{
    public const string Uploaded = "uploaded";
    public const string Processing = "processing";
    public const string Analyzed = "analyzed";
    public const string InProgress = "in_progress";
    public const string Responded = "responded";
    public const string Closed = "closed";
    public const string Archived = "archived";
    public const string Failed = "failed";

    public static readonly string[] All =
    [
        Uploaded, Processing, Analyzed, InProgress, Responded, Closed, Archived, Failed
    ];

    public static bool IsValid(string status) => All.Contains(status);
}

/// <summary>
/// Notice processing status constants.
/// </summary>
public static class NoticeProcessingStatus
{
    public const string Queued = "queued";
    public const string OcrProcessing = "ocr_processing";
    public const string Extracting = "extracting";
    public const string Classifying = "classifying";
    public const string Analyzing = "analyzing";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static readonly string[] All =
    [
        Queued, OcrProcessing, Extracting, Classifying, Analyzing, Completed, Failed
    ];
}

/// <summary>
/// Notice priority constants.
/// </summary>
public static class NoticePriority
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Critical = "critical";

    public static readonly string[] All = [Low, Medium, High, Critical];

    public static bool IsValid(string priority) =>
        All.Contains(priority, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Document type constants for attachments.
/// </summary>
public static class DocumentType
{
    public const string Gstr2a = "gstr2a";
    public const string Gstr3b = "gstr3b";
    public const string PurchaseRegister = "purchase_register";
    public const string Invoices = "invoices";
    public const string BankStatement = "bank_statement";
    public const string Other = "other";

    public static readonly string[] All =
    [
        Gstr2a, Gstr3b, PurchaseRegister, Invoices, BankStatement, Other
    ];
}
