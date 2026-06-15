using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Admin;

/// <summary>
/// Content pages for FAQs, help articles, and notice templates.
/// Supports versioning and publishing workflow.
/// </summary>
public class ContentPage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Content type: faq, help_article, notice_template, announcement
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ContentType { get; set; } = "faq";

    /// <summary>
    /// URL-friendly slug
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Page title
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Short description/excerpt
    /// </summary>
    [MaxLength(500)]
    public string? Excerpt { get; set; }

    /// <summary>
    /// Main content (HTML or Markdown)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Content format: html, markdown
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string ContentFormat { get; set; } = "markdown";

    /// <summary>
    /// Category for grouping
    /// </summary>
    [MaxLength(100)]
    public string? Category { get; set; }

    /// <summary>
    /// Tags for filtering
    /// </summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Status: draft, published, archived
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "draft";

    /// <summary>
    /// Version number (incremented on publish)
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Display order within category
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether this is featured/highlighted
    /// </summary>
    public bool IsFeatured { get; set; }

    /// <summary>
    /// Whether comments/feedback are enabled
    /// </summary>
    public bool AllowFeedback { get; set; } = true;

    /// <summary>
    /// View count
    /// </summary>
    public int ViewCount { get; set; }

    /// <summary>
    /// Helpful count (for FAQs)
    /// </summary>
    public int HelpfulCount { get; set; }

    /// <summary>
    /// Not helpful count
    /// </summary>
    public int NotHelpfulCount { get; set; }

    /// <summary>
    /// Admin who created the content
    /// </summary>
    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public AdminUser CreatedBy { get; set; } = null!;

    /// <summary>
    /// Admin who last updated
    /// </summary>
    public Guid? UpdatedById { get; set; }

    [ForeignKey(nameof(UpdatedById))]
    public AdminUser? UpdatedBy { get; set; }

    /// <summary>
    /// Admin who published
    /// </summary>
    public Guid? PublishedById { get; set; }

    [ForeignKey(nameof(PublishedById))]
    public AdminUser? PublishedBy { get; set; }

    /// <summary>
    /// When published
    /// </summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>
    /// SEO meta title
    /// </summary>
    [MaxLength(255)]
    public string? MetaTitle { get; set; }

    /// <summary>
    /// SEO meta description
    /// </summary>
    [MaxLength(500)]
    public string? MetaDescription { get; set; }

    /// <summary>
    /// Language code (default: en)
    /// </summary>
    [MaxLength(10)]
    public string Language { get; set; } = "en";

    /// <summary>
    /// Timestamps
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Version history
    /// </summary>
    public ICollection<ContentPageVersion> Versions { get; set; } = [];
}

/// <summary>
/// Version history for content pages
/// </summary>
public class ContentPageVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ContentPageId { get; set; }

    [ForeignKey(nameof(ContentPageId))]
    public ContentPage ContentPage { get; set; } = null!;

    /// <summary>
    /// Version number
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Title at this version
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Content at this version
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Admin who made this version
    /// </summary>
    public Guid CreatedById { get; set; }

    [ForeignKey(nameof(CreatedById))]
    public AdminUser CreatedBy { get; set; } = null!;

    /// <summary>
    /// Change notes
    /// </summary>
    [MaxLength(500)]
    public string? ChangeNotes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Content type constants
/// </summary>
public static class ContentTypes
{
    public const string Faq = "faq";
    public const string HelpArticle = "help_article";
    public const string NoticeTemplate = "notice_template";
    public const string Announcement = "announcement";
    public const string Legal = "legal";
    public const string Tutorial = "tutorial";
}

/// <summary>
/// Content status constants
/// </summary>
public static class ContentStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}

/// <summary>
/// Content format constants
/// </summary>
public static class ContentFormat
{
    public const string Html = "html";
    public const string Markdown = "markdown";
}
