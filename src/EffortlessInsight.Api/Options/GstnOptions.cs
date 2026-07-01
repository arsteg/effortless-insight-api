using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for GSTN portal integration.
/// </summary>
public class GstnOptions : IValidatableObject
{
    public const string SectionName = "Gstn";

    /// <summary>
    /// Default GSP provider to use for new connections.
    /// </summary>
    public string DefaultGspProvider { get; set; } = "whitebooks";

    /// <summary>
    /// OTP expiry time in minutes.
    /// </summary>
    [Range(1, 30, ErrorMessage = "OTP expiry must be between 1 and 30 minutes")]
    public int OtpExpiryMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of OTP verification attempts.
    /// </summary>
    [Range(1, 10, ErrorMessage = "Max OTP attempts must be between 1 and 10")]
    public int MaxOtpAttempts { get; set; } = 3;

    /// <summary>
    /// Minutes before token expiry to trigger refresh.
    /// </summary>
    public int TokenRefreshThresholdMinutes { get; set; } = 120;

    /// <summary>
    /// Maximum consecutive sync failures before suspending connection.
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// Default sync interval in hours.
    /// </summary>
    public int DefaultSyncIntervalHours { get; set; } = 6;

    /// <summary>
    /// Minimum allowed sync interval in hours.
    /// </summary>
    public int MinSyncIntervalHours { get; set; } = 1;

    /// <summary>
    /// Maximum allowed sync interval in hours.
    /// </summary>
    public int MaxSyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Whether to enable GSTN integration feature.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of days to retain sync logs.
    /// </summary>
    public int SyncLogRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of days to retain expired OTP sessions.
    /// </summary>
    public int OtpSessionRetentionDays { get; set; } = 7;

    /// <summary>
    /// Maximum notices to fetch per sync operation.
    /// </summary>
    public int MaxNoticesPerSync { get; set; } = 100;

    /// <summary>
    /// Days to look back for initial sync.
    /// </summary>
    public int InitialSyncDaysBack { get; set; } = 365;

    /// <summary>
    /// Enable mock mode for development/testing.
    /// </summary>
    public bool EnableMockMode { get; set; } = false;

    /// <summary>
    /// Default access token expiry in hours (used if GSP doesn't provide expiry).
    /// </summary>
    public int DefaultAccessTokenExpiryHours { get; set; } = 6;

    /// <summary>
    /// Default refresh token expiry in days (used if GSP doesn't provide expiry).
    /// </summary>
    public int DefaultRefreshTokenExpiryDays { get; set; } = 30;

    /// <summary>
    /// Maximum document size in bytes (default 50MB).
    /// </summary>
    public long MaxDocumentSizeBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Maximum backoff hours for retry scheduling (caps exponential backoff).
    /// </summary>
    [Range(1, 168, ErrorMessage = "Max backoff must be between 1 and 168 hours (1 week)")]
    public int MaxBackoffHours { get; set; } = 24;

    /// <summary>
    /// Validates the options configuration.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Check that min sync interval is not greater than max
        if (MinSyncIntervalHours > MaxSyncIntervalHours)
        {
            yield return new ValidationResult(
                "MinSyncIntervalHours cannot be greater than MaxSyncIntervalHours",
                new[] { nameof(MinSyncIntervalHours), nameof(MaxSyncIntervalHours) });
        }

        // Check that default sync interval is within range
        if (DefaultSyncIntervalHours < MinSyncIntervalHours || DefaultSyncIntervalHours > MaxSyncIntervalHours)
        {
            yield return new ValidationResult(
                "DefaultSyncIntervalHours must be between MinSyncIntervalHours and MaxSyncIntervalHours",
                new[] { nameof(DefaultSyncIntervalHours) });
        }

        // Warn if mock mode is enabled (should only be allowed in development)
        // This validation is informational - actual enforcement is done in GstnAuthService
        if (EnableMockMode)
        {
            // Note: This doesn't fail validation, but logs a warning via the hosting environment check in GstnAuthService
        }
    }
}
