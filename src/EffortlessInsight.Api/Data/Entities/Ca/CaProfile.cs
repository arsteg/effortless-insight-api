using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Ca;

/// <summary>
/// CA-specific profile data for users who are Chartered Accountants.
/// CAs use the platform free and invite Business Owners to manage their GST notices.
/// </summary>
public class CaProfile : BaseEntity
{
    /// <summary>
    /// User this CA profile belongs to. One-to-one relationship.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// CA's firm name (optional).
    /// </summary>
    [MaxLength(255)]
    public string? FirmName { get; set; }

    /// <summary>
    /// ICAI (Institute of Chartered Accountants of India) membership number.
    /// </summary>
    [MaxLength(50)]
    public string? MembershipNumber { get; set; }

    /// <summary>
    /// Whether the CA profile has been verified (ICAI membership validated).
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// When the CA profile was verified.
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// CA profile status: active, suspended.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = CaProfileStatus.Active;

    /// <summary>
    /// Reason for suspension (if status is suspended).
    /// </summary>
    [MaxLength(500)]
    public string? SuspensionReason { get; set; }

    /// <summary>
    /// When the CA profile was suspended.
    /// </summary>
    public DateTime? SuspendedAt { get; set; }

    // Navigation properties
    public ICollection<CaClientRelationship> ClientRelationships { get; set; } = [];
    public ICollection<CaInvitation> SentInvitations { get; set; } = [];
}

/// <summary>
/// Status constants for CA profiles.
/// </summary>
public static class CaProfileStatus
{
    /// <summary>
    /// CA profile is active and can operate normally.
    /// </summary>
    public const string Active = "active";

    /// <summary>
    /// CA profile is suspended (cannot send invitations or sync notices).
    /// </summary>
    public const string Suspended = "suspended";

    public static readonly string[] All = [Active, Suspended];

    public static bool IsValid(string status) => All.Contains(status);
}
