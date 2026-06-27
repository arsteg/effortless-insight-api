using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a team or department within an organization.
/// Supports hierarchical structure with parent-child relationships.
/// </summary>
public class Team : BaseEntity
{
    /// <summary>
    /// The organization this team belongs to.
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Display name of the team.
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
    /// Optional description of the team's purpose.
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Parent team ID for hierarchical structure.
    /// Null for top-level teams/departments.
    /// </summary>
    public Guid? ParentTeamId { get; set; }

    [ForeignKey(nameof(ParentTeamId))]
    public Team? ParentTeam { get; set; }

    /// <summary>
    /// Team lead user ID.
    /// </summary>
    public Guid? LeaderId { get; set; }

    [ForeignKey(nameof(LeaderId))]
    public ApplicationUser? Leader { get; set; }

    /// <summary>
    /// Display color for the team badge in UI (hex format).
    /// </summary>
    [MaxLength(7)]
    public string? Color { get; set; }

    /// <summary>
    /// Team icon or emoji.
    /// </summary>
    [MaxLength(50)]
    public string? Icon { get; set; }

    /// <summary>
    /// Hierarchical path for efficient queries (e.g., "/parent-id/child-id/").
    /// </summary>
    [MaxLength(1000)]
    public string? HierarchyPath { get; set; }

    /// <summary>
    /// Depth level in the hierarchy (0 for top-level).
    /// </summary>
    public int HierarchyLevel { get; set; }

    /// <summary>
    /// Whether the team is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Team-specific settings stored as JSONB.
    /// </summary>
    public Dictionary<string, object>? Settings { get; set; }

    /// <summary>
    /// Members of this team.
    /// </summary>
    public ICollection<TeamMember> Members { get; set; } = [];

    /// <summary>
    /// Sub-teams (children) of this team.
    /// </summary>
    [InverseProperty(nameof(ParentTeam))]
    public ICollection<Team> SubTeams { get; set; } = [];
}

/// <summary>
/// Represents a user's membership in a team.
/// </summary>
public class TeamMember : BaseEntity
{
    /// <summary>
    /// The team this membership belongs to.
    /// </summary>
    [Required]
    public Guid TeamId { get; set; }

    [ForeignKey(nameof(TeamId))]
    public Team Team { get; set; } = null!;

    /// <summary>
    /// The user who is a member of this team.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Role within the team: member, lead, manager.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Role { get; set; } = "member";

    /// <summary>
    /// Job title or position within the team.
    /// </summary>
    [MaxLength(100)]
    public string? Title { get; set; }

    /// <summary>
    /// Whether this is a primary team for the user.
    /// Users can belong to multiple teams but have one primary.
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// When the user joined this team.
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
