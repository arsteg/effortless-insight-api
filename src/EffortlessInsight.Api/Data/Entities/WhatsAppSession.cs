using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks WhatsApp conversation sessions and state machine.
/// </summary>
public class WhatsAppSession : BaseEntity
{
    /// <summary>
    /// Linked user ID. Null until account is linked.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Phone number in E.164 format (e.g., 919876543210).
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Current conversation state (start, awaiting_email, awaiting_otp, linked).
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string CurrentState { get; set; } = "start";

    /// <summary>
    /// Email being verified during linking.
    /// </summary>
    [MaxLength(255)]
    public string? PendingEmail { get; set; }

    /// <summary>
    /// Pending verification ID during OTP flow.
    /// </summary>
    public Guid? PendingVerificationId { get; set; }

    /// <summary>
    /// Arbitrary context data for the conversation.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Context { get; set; } = new();

    /// <summary>
    /// Last message interaction timestamp.
    /// </summary>
    public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the 24-hour conversation window expires.
    /// </summary>
    public DateTime SessionExpiresAt { get; set; }

    /// <summary>
    /// Current page/offset for paginated responses.
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Number of messages sent in this session.
    /// </summary>
    public int MessageCount { get; set; }

    // Navigation properties
    public ApplicationUser? User { get; set; }
}

/// <summary>
/// WhatsApp session state constants.
/// </summary>
public static class WhatsAppSessionState
{
    public const string Start = "start";
    public const string AwaitingEmail = "awaiting_email";
    public const string AwaitingOtp = "awaiting_otp";
    public const string Linked = "linked";
}
