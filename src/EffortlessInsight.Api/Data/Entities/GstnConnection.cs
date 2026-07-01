using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Tracks the connection state between an organization's GSTIN and the GST portal via GSP.
/// </summary>
public class GstnConnection : BaseEntity
{
    [Required]
    public Guid OrganizationGstinId { get; set; }

    [ForeignKey(nameof(OrganizationGstinId))]
    public OrganizationGstin OrganizationGstin { get; set; } = null!;

    /// <summary>
    /// Current connection status.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = GstnConnectionStatus.Disconnected;

    /// <summary>
    /// Encrypted access token from GSP (valid ~6 hours).
    /// MaxLength 2000 to accommodate encrypted token storage.
    /// </summary>
    [MaxLength(2000)]
    public string? EncryptedAccessToken { get; set; }

    /// <summary>
    /// Encrypted refresh token from GSP.
    /// </summary>
    [MaxLength(2000)]
    public string? EncryptedRefreshToken { get; set; }

    /// <summary>
    /// When the current access token expires.
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// When the refresh token expires.
    /// </summary>
    public DateTime? RefreshTokenExpiresAt { get; set; }

    /// <summary>
    /// GSP provider identifier (e.g., "whitebooks", "cleartax").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string GspProvider { get; set; } = "whitebooks";

    /// <summary>
    /// GSP session identifier for this connection.
    /// </summary>
    [MaxLength(255)]
    public string? GspSessionId { get; set; }

    /// <summary>
    /// When the last successful sync occurred.
    /// </summary>
    public DateTime? LastSyncAt { get; set; }

    /// <summary>
    /// When the next scheduled sync should occur.
    /// </summary>
    public DateTime? NextScheduledSyncAt { get; set; }

    /// <summary>
    /// Whether automatic syncing is enabled.
    /// </summary>
    public bool AutoSyncEnabled { get; set; } = true;

    /// <summary>
    /// Hours between automatic syncs.
    /// </summary>
    public int SyncIntervalHours { get; set; } = 6;

    /// <summary>
    /// Count of consecutive sync failures.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Last error message from sync attempt.
    /// </summary>
    public string? LastSyncError { get; set; }

    /// <summary>
    /// User who initiated the connection.
    /// </summary>
    public Guid? ConnectedById { get; set; }

    [ForeignKey(nameof(ConnectedById))]
    public ApplicationUser? ConnectedBy { get; set; }

    /// <summary>
    /// When the connection was established.
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// User who disconnected (if disconnected).
    /// </summary>
    public Guid? DisconnectedById { get; set; }

    [ForeignKey(nameof(DisconnectedById))]
    public ApplicationUser? DisconnectedBy { get; set; }

    /// <summary>
    /// When the connection was disconnected.
    /// </summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>
    /// Reason for disconnection.
    /// </summary>
    [MaxLength(500)]
    public string? DisconnectionReason { get; set; }

    // Navigation properties
    public ICollection<GstnSyncLog> SyncLogs { get; set; } = [];
}

/// <summary>
/// Connection status constants for GSTN portal connections.
/// </summary>
public static class GstnConnectionStatus
{
    /// <summary>
    /// Not connected to GST portal.
    /// </summary>
    public const string Disconnected = "disconnected";

    /// <summary>
    /// OTP has been sent, awaiting verification.
    /// </summary>
    public const string PendingOtp = "pending_otp";

    /// <summary>
    /// Successfully connected with valid tokens.
    /// </summary>
    public const string Connected = "connected";

    /// <summary>
    /// Token has expired, needs refresh or reconnection.
    /// </summary>
    public const string TokenExpired = "token_expired";

    /// <summary>
    /// Connection suspended due to repeated failures.
    /// </summary>
    public const string Suspended = "suspended";

    /// <summary>
    /// Connection revoked by user from GST portal.
    /// </summary>
    public const string Revoked = "revoked";

    public static readonly string[] All =
    [
        Disconnected, PendingOtp, Connected, TokenExpired, Suspended, Revoked
    ];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool CanSync(string status) => status == Connected;

    /// <summary>
    /// Validates if a status transition is allowed.
    /// </summary>
    public static bool CanTransition(string from, string to)
    {
        return (from, to) switch
        {
            // From Disconnected
            (Disconnected, PendingOtp) => true,
            (Disconnected, Connected) => true, // Mock mode or direct connection

            // From PendingOtp
            (PendingOtp, Connected) => true, // OTP verified
            (PendingOtp, Disconnected) => true, // Cancelled

            // From Connected
            (Connected, Disconnected) => true, // User disconnected
            (Connected, TokenExpired) => true, // Token expired
            (Connected, Suspended) => true, // Too many failures
            (Connected, Revoked) => true, // Revoked from portal

            // From TokenExpired
            (TokenExpired, Connected) => true, // Token refreshed
            (TokenExpired, Disconnected) => true, // User disconnected

            // From Suspended
            (Suspended, Connected) => true, // Unsuspended
            (Suspended, Disconnected) => true, // User disconnected

            // From Revoked
            (Revoked, Disconnected) => true, // User acknowledged
            (Revoked, PendingOtp) => true, // Re-connecting

            // Same status is always allowed (no-op)
            _ when from == to => true,

            _ => false
        };
    }
}
