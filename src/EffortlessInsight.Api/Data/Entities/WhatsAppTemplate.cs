using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Local cache of Meta-approved WhatsApp message templates.
/// </summary>
public class WhatsAppTemplate : BaseEntity
{
    /// <summary>
    /// Meta template ID.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    /// Template name (used in API calls).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Template category: MARKETING, UTILITY, AUTHENTICATION.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Template language code (en, hi).
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string Language { get; set; } = "en";

    /// <summary>
    /// Approval status: APPROVED, PENDING, REJECTED.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Header format: TEXT, IMAGE, DOCUMENT, VIDEO, or null.
    /// </summary>
    [MaxLength(20)]
    public string? HeaderFormat { get; set; }

    /// <summary>
    /// Header text if format is TEXT.
    /// </summary>
    [MaxLength(500)]
    public string? HeaderText { get; set; }

    /// <summary>
    /// Template body text with {{1}}, {{2}} placeholders.
    /// </summary>
    [Required]
    public string BodyText { get; set; } = string.Empty;

    /// <summary>
    /// Optional footer text.
    /// </summary>
    [MaxLength(200)]
    public string? FooterText { get; set; }

    /// <summary>
    /// Variable names for the template (for documentation).
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<string> Variables { get; set; } = [];

    /// <summary>
    /// Button definitions if any.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public List<WhatsAppTemplateButton> Buttons { get; set; } = [];

    /// <summary>
    /// Whether this template is active for use.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Usage count for analytics.
    /// </summary>
    public int UsageCount { get; set; }

    /// <summary>
    /// Last time this template was used.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// When the template was last synced from Meta.
    /// </summary>
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Rejection reason if status is REJECTED.
    /// </summary>
    [MaxLength(500)]
    public string? RejectionReason { get; set; }
}

/// <summary>
/// WhatsApp template button definition.
/// </summary>
public class WhatsAppTemplateButton
{
    /// <summary>
    /// Button type: QUICK_REPLY, URL, PHONE_NUMBER.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Button text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// URL for URL buttons.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Phone number for phone buttons.
    /// </summary>
    public string? PhoneNumber { get; set; }
}

/// <summary>
/// Template category constants.
/// </summary>
public static class WhatsAppTemplateCategory
{
    public const string Marketing = "MARKETING";
    public const string Utility = "UTILITY";
    public const string Authentication = "AUTHENTICATION";
}

/// <summary>
/// Template status constants.
/// </summary>
public static class WhatsAppTemplateStatus
{
    public const string Approved = "APPROVED";
    public const string Pending = "PENDING";
    public const string Rejected = "REJECTED";
}
