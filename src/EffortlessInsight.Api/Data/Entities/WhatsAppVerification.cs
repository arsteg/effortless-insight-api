using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks WhatsApp phone verification/linking attempts.
/// OTP is sent via in-app notification (not WhatsApp) to prevent account takeover.
/// </summary>
public class WhatsAppVerification : BaseEntity
{
    /// <summary>
    /// User attempting to link their WhatsApp.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Phone number in E.164 format (e.g., 919876543210).
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// 6-digit verification code.
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string VerificationCode { get; set; } = string.Empty;

    /// <summary>
    /// When this verification code expires.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Number of verification attempts made.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Maximum allowed attempts.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Whether verification was successful.
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// When verification was completed.
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// How verification was initiated (app, bot).
    /// </summary>
    [MaxLength(20)]
    public string InitiatedFrom { get; set; } = "app";

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}
