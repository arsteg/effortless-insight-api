using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Temporary session for OTP verification during GSTN portal connection.
/// Sessions are short-lived (5 minutes) and cleaned up after use.
/// </summary>
public class GstnOtpSession : BaseEntity
{
    [Required]
    public Guid OrganizationGstinId { get; set; }

    [ForeignKey(nameof(OrganizationGstinId))]
    public OrganizationGstin OrganizationGstin { get; set; } = null!;

    /// <summary>
    /// User who initiated the OTP request.
    /// </summary>
    [Required]
    public Guid InitiatedById { get; set; }

    [ForeignKey(nameof(InitiatedById))]
    public ApplicationUser InitiatedBy { get; set; } = null!;

    /// <summary>
    /// GSP-provided session identifier for this OTP flow.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string GspSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Masked destination where OTP was sent (e.g., "xxxx@gmail.com" or "xxxxxx1234").
    /// </summary>
    [MaxLength(100)]
    public string? OtpDestination { get; set; }

    /// <summary>
    /// Type of OTP destination: mobile or email.
    /// </summary>
    [MaxLength(20)]
    public string? OtpDestinationType { get; set; }

    /// <summary>
    /// When this OTP session expires.
    /// </summary>
    [Required]
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Number of OTP verification attempts made.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Maximum allowed OTP attempts.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Current status of the OTP session.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = GstnOtpSessionStatus.Pending;

    /// <summary>
    /// When the OTP was verified (if successful).
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Error message if verification failed.
    /// </summary>
    [MaxLength(500)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// IP address from which OTP was requested.
    /// </summary>
    [MaxLength(45)]
    public string? RequestIpAddress { get; set; }

    /// <summary>
    /// User agent string from request.
    /// </summary>
    [MaxLength(500)]
    public string? RequestUserAgent { get; set; }
}

/// <summary>
/// OTP session status constants.
/// </summary>
public static class GstnOtpSessionStatus
{
    /// <summary>
    /// OTP sent, awaiting user input.
    /// </summary>
    public const string Pending = "pending";

    /// <summary>
    /// OTP verified successfully.
    /// </summary>
    public const string Verified = "verified";

    /// <summary>
    /// OTP expired without verification.
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// Max attempts exceeded.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>
    /// Session cancelled by user.
    /// </summary>
    public const string Cancelled = "cancelled";

    public static readonly string[] All =
    [
        Pending, Verified, Expired, Failed, Cancelled
    ];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool CanAttempt(string status) => status == Pending;
}
