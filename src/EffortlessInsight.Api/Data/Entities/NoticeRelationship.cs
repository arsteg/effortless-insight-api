using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a relationship between two notices.
/// </summary>
public class NoticeRelationship
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The source notice (the one initiating the relationship).
    /// </summary>
    [Required]
    public Guid SourceNoticeId { get; set; }
    public Notice SourceNotice { get; set; } = null!;

    /// <summary>
    /// The target notice (the one being linked to).
    /// </summary>
    [Required]
    public Guid TargetNoticeId { get; set; }
    public Notice TargetNotice { get; set; } = null!;

    /// <summary>
    /// Type of relationship between the notices.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string RelationshipType { get; set; } = NoticeRelationshipType.Related;

    /// <summary>
    /// Optional note explaining the relationship.
    /// </summary>
    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// User who created this relationship.
    /// </summary>
    [Required]
    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Notice relationship type constants.
/// </summary>
public static class NoticeRelationshipType
{
    /// <summary>
    /// Source is the parent of target (target is a follow-up or sub-notice).
    /// </summary>
    public const string Parent = "parent";

    /// <summary>
    /// Source is a child of target (source is a follow-up or sub-notice).
    /// </summary>
    public const string Child = "child";

    /// <summary>
    /// Notices are related but no hierarchy.
    /// </summary>
    public const string Related = "related";

    /// <summary>
    /// Source supersedes/replaces target.
    /// </summary>
    public const string Supersedes = "supersedes";

    /// <summary>
    /// Source is superseded/replaced by target.
    /// </summary>
    public const string SupersededBy = "superseded_by";

    /// <summary>
    /// Source references target notice.
    /// </summary>
    public const string References = "references";

    /// <summary>
    /// Source is referenced by target notice.
    /// </summary>
    public const string ReferencedBy = "referenced_by";

    public static readonly string[] All =
    [
        Parent, Child, Related, Supersedes, SupersededBy, References, ReferencedBy
    ];

    public static bool IsValid(string type) =>
        All.Contains(type, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Get the inverse relationship type for bidirectional linking.
    /// </summary>
    public static string? GetInverse(string type) => type switch
    {
        Parent => Child,
        Child => Parent,
        Supersedes => SupersededBy,
        SupersededBy => Supersedes,
        References => ReferencedBy,
        ReferencedBy => References,
        Related => Related,
        _ => null
    };
}
