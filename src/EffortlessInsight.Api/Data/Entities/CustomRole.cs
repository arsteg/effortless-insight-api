using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a custom role within an organization.
/// Custom roles allow fine-grained permission assignment beyond the built-in roles.
/// </summary>
public class CustomRole : BaseEntity
{
    /// <summary>
    /// The organization this role belongs to.
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Display name of the role.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Normalized name for uniqueness checks (lowercase, trimmed).
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string NameNormalized { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the role's purpose.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this is a system (built-in) role that cannot be deleted.
    /// System roles: owner, admin, manager, member, ca, viewer
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// The base role this custom role extends.
    /// Used to inherit base permissions before applying custom ones.
    /// Values: owner, admin, manager, member, ca, viewer
    /// </summary>
    [MaxLength(20)]
    public string? BaseRole { get; set; }

    /// <summary>
    /// List of permission strings granted to this role.
    /// Stored as JSON array in the database.
    /// </summary>
    public List<string> Permissions { get; set; } = [];

    /// <summary>
    /// Display color for the role badge in UI (hex format).
    /// </summary>
    [MaxLength(7)]
    public string? Color { get; set; }

    /// <summary>
    /// Sort order for display purposes.
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Whether the role is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Organization members assigned to this role.
    /// </summary>
    public ICollection<OrganizationMember> Members { get; set; } = [];
}
