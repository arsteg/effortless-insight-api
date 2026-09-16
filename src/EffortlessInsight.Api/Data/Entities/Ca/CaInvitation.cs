using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities.Ca;

/// <summary>
/// Represents a bidirectional invitation between CA and Business Owner.
/// Supports both CA inviting BO and BO inviting CA flows.
/// </summary>
public class CaInvitation : BaseEntity
{
    /// <summary>
    /// Type of invitation: ca_invites_bo, bo_invites_ca.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string InvitationType { get; set; } = CaInvitationType.CaInvitesBo;

    /// <summary>
    /// User who sent the invitation (CA or BO).
    /// </summary>
    [Required]
    public Guid InviterUserId { get; set; }

    [ForeignKey(nameof(InviterUserId))]
    public ApplicationUser InviterUser { get; set; } = null!;

    /// <summary>
    /// Organization of the inviter (set for bo_invites_ca flow).
    /// </summary>
    public Guid? InviterOrganizationId { get; set; }

    [ForeignKey(nameof(InviterOrganizationId))]
    public Organization? InviterOrganization { get; set; }

    /// <summary>
    /// Email of the person being invited.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string InviteeEmail { get; set; } = string.Empty;

    /// <summary>
    /// Normalized (lowercase, trimmed) invitee email for lookups.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string InviteeEmailNormalized { get; set; } = string.Empty;

    /// <summary>
    /// GSTIN associated with this invitation.
    /// </summary>
    [Required]
    [MaxLength(15)]
    public string Gstin { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash of the invitation token (token itself is sent via email).
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Invitation status: pending, accepted, declined, expired, cancelled.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = CaInvitationStatus.Pending;

    /// <summary>
    /// When the invitation expires.
    /// </summary>
    [Required]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// When the invitee responded (accepted/declined).
    /// </summary>
    public DateTime? RespondedAt { get; set; }

    /// <summary>
    /// User who accepted the invitation (set after registration/login).
    /// </summary>
    public Guid? AcceptedUserId { get; set; }

    [ForeignKey(nameof(AcceptedUserId))]
    public ApplicationUser? AcceptedUser { get; set; }

    /// <summary>
    /// Count of notices staged before the BO accepted (for ca_invites_bo flow).
    /// These notices are transferred to BO's org upon acceptance.
    /// </summary>
    public int StagedNoticeCount { get; set; }

    /// <summary>
    /// Optional message from the inviter to the invitee.
    /// </summary>
    [MaxLength(1000)]
    public string? Message { get; set; }

    /// <summary>
    /// Number of times the invitation email was sent.
    /// </summary>
    public int SendCount { get; set; } = 1;

    /// <summary>
    /// When the invitation was last sent.
    /// </summary>
    public DateTime LastSentAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the invitation was cancelled (if applicable).
    /// </summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// User who cancelled the invitation.
    /// </summary>
    public Guid? CancelledById { get; set; }

    [ForeignKey(nameof(CancelledById))]
    public ApplicationUser? CancelledBy { get; set; }

    /// <summary>
    /// Reason for cancellation or decline.
    /// </summary>
    [MaxLength(500)]
    public string? ResponseReason { get; set; }
}

/// <summary>
/// Invitation type constants.
/// </summary>
public static class CaInvitationType
{
    /// <summary>
    /// CA invites Business Owner to manage their GST notices.
    /// </summary>
    public const string CaInvitesBo = "ca_invites_bo";

    /// <summary>
    /// Business Owner invites CA to help manage their GST notices.
    /// </summary>
    public const string BoInvitesCa = "bo_invites_ca";

    public static readonly string[] All = [CaInvitesBo, BoInvitesCa];

    public static bool IsValid(string type) => All.Contains(type);
}

/// <summary>
/// Invitation status constants.
/// </summary>
public static class CaInvitationStatus
{
    /// <summary>
    /// Invitation sent and awaiting response.
    /// </summary>
    public const string Pending = "pending";

    /// <summary>
    /// Invitation accepted by invitee.
    /// </summary>
    public const string Accepted = "accepted";

    /// <summary>
    /// Invitation declined by invitee.
    /// </summary>
    public const string Declined = "declined";

    /// <summary>
    /// Invitation expired (past ExpiresAt date).
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// Invitation cancelled by inviter.
    /// </summary>
    public const string Cancelled = "cancelled";

    public static readonly string[] All =
    [
        Pending, Accepted, Declined, Expired, Cancelled
    ];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool CanRespond(string status) => status == Pending;

    public static bool CanCancel(string status) => status == Pending;

    public static bool CanResend(string status) => status == Pending;
}
