namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Constants used throughout the GSTN integration.
/// </summary>
public static class GstnConstants
{
    /// <summary>
    /// Cache key prefixes.
    /// </summary>
    public static class CacheKeys
    {
        public const string ConnectionStatusPrefix = "gstn:status:";
        public const string OtpRateLimitPrefix = "gstn:otp:ratelimit:";
    }

    /// <summary>
    /// Notice source identifiers.
    /// </summary>
    public static class NoticeSources
    {
        public const string Upload = "upload";
        public const string Manual = "manual";
        public const string GstnPortal = "gstn_portal";
    }

    /// <summary>
    /// File URL placeholders.
    /// </summary>
    public static class FileUrlPlaceholders
    {
        /// <summary>
        /// Placeholder for documents pending download from GSTN.
        /// Format: pending://gstn/{noticeId}
        /// </summary>
        public const string PendingGstnPrefix = "pending://gstn/";
    }

    /// <summary>
    /// Error codes.
    /// </summary>
    public static class ErrorCodes
    {
        public const string NotFound = "NOT_FOUND";
        public const string InvalidGstin = "INVALID_GSTIN";
        public const string AlreadyConnected = "ALREADY_CONNECTED";
        public const string OtpExpired = "OTP_EXPIRED";
        public const string OtpMaxAttempts = "OTP_MAX_ATTEMPTS";
        public const string OtpInvalid = "OTP_INVALID";
        public const string ConnectionNotFound = "CONNECTION_NOT_FOUND";
        public const string NoRefreshToken = "NO_REFRESH_TOKEN";
        public const string TokenInvalid = "TOKEN_INVALID";
        public const string DecryptionFailed = "DECRYPTION_FAILED";
        public const string DownloadFailed = "DOWNLOAD_FAILED";
        public const string DocumentTooLarge = "DOCUMENT_TOO_LARGE";
        public const string NoGstin = "NO_GSTIN";
        public const string NotGstnNotice = "NOT_GSTN_NOTICE";
        public const string NoConnection = "NO_CONNECTION";
        public const string GspError = "GSP_ERROR";
        public const string SyncInProgress = "SYNC_IN_PROGRESS";
    }

    /// <summary>
    /// GSP provider identifiers.
    /// </summary>
    public static class GspProviders
    {
        public const string WhiteBooks = "whitebooks";
        public const string ClearTax = "cleartax";
        public const string Vayana = "vayana";
    }

    /// <summary>
    /// Default values.
    /// </summary>
    public static class Defaults
    {
        public const int SyncIntervalHours = 6;
        public const int MaxOtpAttempts = 3;
        public const int OtpExpiryMinutes = 5;
        public const int TokenRefreshThresholdMinutes = 120;
        public const int MaxConsecutiveFailures = 5;
    }
}
