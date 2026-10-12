using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EffortlessInsight.Api.Data.Entities;

/// <summary>
/// Represents a connected email account (Gmail or Outlook) for automatic GST notice detection.
/// </summary>
public class ConnectedEmailAccount : BaseEntity
{
    /// <summary>
    /// The user who connected this email account.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// The organization this email account belongs to.
    /// </summary>
    [Required]
    public Guid OrganizationId { get; set; }

    [ForeignKey(nameof(OrganizationId))]
    public Organization Organization { get; set; } = null!;

    /// <summary>
    /// Email provider: "gmail" or "outlook".
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The email address of the connected account.
    /// </summary>
    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display name from the OAuth profile.
    /// </summary>
    [MaxLength(255)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Encrypted OAuth access token (AES-256-GCM).
    /// MaxLength 4000 to accommodate larger OAuth tokens.
    /// </summary>
    public string? AccessTokenEncrypted { get; set; }

    /// <summary>
    /// Encrypted OAuth refresh token (AES-256-GCM).
    /// </summary>
    public string? RefreshTokenEncrypted { get; set; }

    /// <summary>
    /// When the current access token expires.
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// OAuth scopes granted by the user.
    /// </summary>
    [MaxLength(1000)]
    public string? Scopes { get; set; }

    /// <summary>
    /// Current connection status.
    /// </summary>
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = EmailAccountStatus.Connected;

    /// <summary>
    /// When the last successful sync occurred.
    /// </summary>
    public DateTime? LastSyncAt { get; set; }
    public DateTime? LastScheduledSyncDay { get; set; }

    /// <summary>
    /// Cursor for incremental sync (Gmail historyId or Outlook deltaLink).
    /// </summary>
    public string? LastSyncCursor { get; set; }

    /// <summary>
    /// Result of the last sync operation.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public EmailSyncResult? LastSyncResult { get; set; }

    /// <summary>
    /// Count of consecutive sync failures.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Whether automatic daily sync is enabled.
    /// </summary>
    public bool AutoSyncEnabled { get; set; } = true;

    /// <summary>
    /// When the connection was established.
    /// </summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// When the connection was disconnected.
    /// </summary>
    public DateTime? DisconnectedAt { get; set; }

    /// <summary>
    /// Reason for disconnection.
    /// </summary>
    [MaxLength(500)]
    public string? DisconnectionReason { get; set; }

    /// <summary>
    /// Last error message from sync or token refresh.
    /// </summary>
    public string? LastError { get; set; }

    // Navigation properties
    public ICollection<EmailSyncLog> SyncLogs { get; set; } = [];
}

/// <summary>
/// Result of an email sync operation, stored as JSON.
/// </summary>
public class EmailSyncResult
{
    public string? Status { get; set; }
    public string? ErrorCode { get; set; }
    public int EmailsScanned { get; set; }
    public int NoticesImported { get; set; }
    public int AttachmentsSkipped { get; set; }
    public int DuplicatesSkipped { get; set; }
    public int FailedToProcess { get; set; }
    public DateTime? SyncPeriodFrom { get; set; }
    public DateTime? SyncPeriodTo { get; set; }
}

/// <summary>
/// Email account connection status constants.
/// </summary>
public static class EmailAccountStatus
{
    /// <summary>
    /// Successfully connected with valid tokens.
    /// </summary>
    public const string Connected = "connected";

    /// <summary>
    /// Token refresh failed, user needs to reconnect.
    /// </summary>
    public const string ReauthenticationRequired = "reauthentication_required";

    /// <summary>
    /// Sync operation failed.
    /// </summary>
    public const string SyncFailed = "sync_failed";

    /// <summary>
    /// Account suspended due to repeated failures.
    /// </summary>
    public const string Suspended = "suspended";

    /// <summary>
    /// Account disconnected by user.
    /// </summary>
    public const string Disconnected = "disconnected";

    public static readonly string[] All =
    [
        Connected, ReauthenticationRequired, SyncFailed, Suspended, Disconnected
    ];

    public static bool IsValid(string status) => All.Contains(status);

    public static bool CanSync(string status) => status is Connected or SyncFailed;

    /// <summary>
    /// Validates if a status transition is allowed.
    /// </summary>
    public static bool CanTransition(string from, string to)
    {
        return (from, to) switch
        {
            // From Connected
            (Connected, ReauthenticationRequired) => true,
            (Connected, SyncFailed) => true,
            (Connected, Suspended) => true,
            (Connected, Disconnected) => true,

            // From ReauthenticationRequired
            (ReauthenticationRequired, Connected) => true, // Re-authenticated
            (ReauthenticationRequired, Disconnected) => true,

            // From SyncFailed
            (SyncFailed, Connected) => true, // Sync succeeded
            (SyncFailed, Suspended) => true, // Too many failures
            (SyncFailed, Disconnected) => true,

            // From Suspended
            (Suspended, Connected) => true, // Unsuspended
            (Suspended, Disconnected) => true,

            // Same status is always allowed (no-op)
            _ when from == to => true,

            _ => false
        };
    }
}

/// <summary>
/// Email provider constants.
/// </summary>
public static class EmailProvider
{
    public const string Gmail = "gmail";
    public const string Outlook = "outlook";

    public static readonly string[] All = [Gmail, Outlook];

    public static bool IsValid(string provider) => All.Contains(provider);
}
