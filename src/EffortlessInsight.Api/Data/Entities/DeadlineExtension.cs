using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks deadline extension requests and approvals.
/// </summary>
public class DeadlineExtension : BaseEntity
{
    [Required]
    public Guid NoticeDeadlineId { get; set; }
    public NoticeDeadline NoticeDeadline { get; set; } = null!;

    [Required]
    public Guid NoticeId { get; set; }
    public Notice Notice { get; set; } = null!;

    /// <summary>
    /// Deadline before this extension.
    /// </summary>
    [Required]
    public DateTime PreviousDeadline { get; set; }

    /// <summary>
    /// New deadline after extension.
    /// </summary>
    [Required]
    public DateTime NewDeadline { get; set; }

    /// <summary>
    /// Number of days extended.
    /// </summary>
    public int DaysExtended { get; set; }

    /// <summary>
    /// Reason for extension request.
    /// </summary>
    [Required]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Type of extension: internal, external (filed with authority)
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string ExtensionType { get; set; } = ExtensionTypes.Internal;

    /// <summary>
    /// Current status of the extension request.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = ExtensionStatuses.Pending;

    /// <summary>
    /// User who requested the extension.
    /// </summary>
    [Required]
    public Guid RequestedById { get; set; }
    public ApplicationUser RequestedBy { get; set; } = null!;

    public DateTime RequestedAt { get; set; }

    /// <summary>
    /// User who approved/rejected the extension.
    /// </summary>
    public Guid? ReviewedById { get; set; }
    public ApplicationUser? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>
    /// Review decision notes.
    /// </summary>
    [MaxLength(1000)]
    public string? ReviewNotes { get; set; }

    /// <summary>
    /// Reference number for external extension requests.
    /// </summary>
    [MaxLength(100)]
    public string? ExternalReferenceNumber { get; set; }

    /// <summary>
    /// Supporting documents for extension request.
    /// </summary>
    public List<string>? SupportingDocumentIds { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Extension type constants.
/// </summary>
public static class ExtensionTypes
{
    public const string Internal = "internal";
    public const string External = "external";

    public static readonly string[] All = [Internal, External];

    public static bool IsValid(string type) => All.Contains(type, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Extension status constants.
/// </summary>
public static class ExtensionStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = [Pending, Approved, Rejected, Cancelled];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.OrdinalIgnoreCase);
}
