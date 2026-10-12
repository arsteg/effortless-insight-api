using System.ComponentModel.DataAnnotations;

namespace EffortlessInsight.Api.Options;

/// <summary>
/// Configuration options for email sync integration (Gmail and Outlook).
/// </summary>
public class EmailSyncOptions : IValidatableObject
{
    public const string SectionName = "EmailSync";
    public string PublicApiBaseUrl { get; set; } = "";
    public List<string> AllowedReturnUris { get; set; } = [];
    public string SyncTimeZone { get; set; } = "Asia/Kolkata";
    [Range(0, 20)] public int MaxAiCallsPerSync { get; set; } = 3;
    public bool EnableAiFallback { get; set; } = true;

    /// <summary>
    /// Whether email sync feature is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gmail OAuth configuration.
    /// </summary>
    public GmailSyncOptions Gmail { get; set; } = new();

    /// <summary>
    /// Outlook/Microsoft OAuth configuration.
    /// </summary>
    public OutlookSyncOptions Outlook { get; set; } = new();

    /// <summary>
    /// Cron schedule for daily sync job (default: 3 AM in SyncTimeZone).
    /// </summary>
    public string SyncScheduleCron { get; set; } = "0 3 * * *";

    /// <summary>
    /// List of allowed sender domains/addresses for GST notices.
    /// </summary>
    public List<string> AllowedSenders { get; set; } =
    [
        "@gst.gov.in",
        "@icegate.gov.in",
        "@cbic.gov.in",
        "@gstn.org.in"
    ];

    /// <summary>
    /// Keywords to detect GST notices in email subject/body.
    /// </summary>
    public List<string> NoticeKeywords { get; set; } =
    [
        "DRC-01", "DRC-01A", "DRC-07", "DRC-03",
        "ASMT-10", "ASMT-12", "ASMT-14", "ASMT-16",
        "REG-17", "REG-18", "REG-19", "REG-21",
        "GST Notice", "Show Cause Notice",
        "Demand Notice", "Assessment Order",
        "Section 61", "Section 62", "Section 63", "Section 73", "Section 74"
    ];

    /// <summary>
    /// Maximum emails to process per sync operation.
    /// </summary>
    [Range(1, 500)]
    public int MaxEmailsPerSync { get; set; } = 100;

    /// <summary>
    /// Maximum attachments to process per sync operation.
    /// </summary>
    [Range(1, 200)]
    public int MaxAttachmentsPerSync { get; set; } = 50;

    /// <summary>
    /// Days to look back for initial sync.
    /// </summary>
    [Range(1, 365)]
    public int InitialSyncLookbackDays { get; set; } = 90;

    /// <summary>
    /// AI confidence threshold for notice detection (0.0 - 1.0).
    /// AI classifications below this threshold remain pending for review/retry.
    /// </summary>
    [Range(0.0, 1.0)]
    public double AiConfidenceThreshold { get; set; } = 0.7;

    /// <summary>
    /// Maximum attachment size in bytes (default: 25MB).
    /// </summary>
    [Range(1, 25 * 1024 * 1024)]
    public long MaxAttachmentSizeBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>
    /// Maximum consecutive sync failures before suspending account.
    /// </summary>
    [Range(1, 20)]
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// Minutes before token expiry to trigger refresh.
    /// </summary>
    [Range(10, 120)]
    public int TokenRefreshBufferMinutes { get; set; } = 30;

    /// <summary>
    /// Number of days to retain sync logs.
    /// </summary>
    [Range(7, 365)]
    public int SyncLogRetentionDays { get; set; } = 90;

    /// <summary>
    /// Allowed MIME types for attachments.
    /// </summary>
    public List<string> AllowedAttachmentTypes { get; set; } =
    [
        "application/pdf",
        "text/html",
        "application/html", "text/plain"
    ];

    /// <summary>
    /// Enable mock mode for development/testing.
    /// </summary>
    public bool EnableMockMode { get; set; } = false;

    /// <summary>
    /// Validates the options configuration.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Enabled)
        {
            if (Gmail.Scopes.Except(new[] { "https://www.googleapis.com/auth/gmail.readonly", "https://www.googleapis.com/auth/userinfo.email" }, StringComparer.Ordinal).Any() ||
                !Gmail.Scopes.Contains("https://www.googleapis.com/auth/gmail.readonly") || !Gmail.Scopes.Contains("https://www.googleapis.com/auth/userinfo.email"))
                yield return new ValidationResult("Gmail requires only gmail.readonly and userinfo.email scopes.");
            if (Outlook.Scopes.Except(new[] { "offline_access", "Mail.Read", "User.Read" }, StringComparer.Ordinal).Any() ||
                new[] { "offline_access", "Mail.Read", "User.Read" }.Except(Outlook.Scopes).Any())
                yield return new ValidationResult("Microsoft requires only offline_access, Mail.Read and User.Read delegated scopes.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(SyncScheduleCron, @"^([0-5]?\d) ([01]?\d|2[0-3]) \* \* \*$"))
                yield return new ValidationResult("Email sync must run at one minute/hour daily; use 'minute hour * * *'.");
            if (AllowedSenders.Any(s => !Services.EmailSync.EmailSenderPolicy.IsValidRule(s)))
                yield return new ValidationResult("AllowedSenders must contain exact email addresses or domains.");
            if (AllowedSenders.Count == 0)
            {
                yield return new ValidationResult(
                    "At least one allowed sender must be configured when EmailSync is enabled",
                    [nameof(AllowedSenders)]);
            }

            if (NoticeKeywords.Count == 0)
            {
                yield return new ValidationResult(
                    "At least one notice keyword must be configured when EmailSync is enabled",
                    [nameof(NoticeKeywords)]);
            }
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(SyncTimeZone, out _))
            yield return new ValidationResult("SyncTimeZone must be a valid time zone identifier.");

        if (AiConfidenceThreshold < 0.0 || AiConfidenceThreshold > 1.0)
        {
            yield return new ValidationResult(
                "AI confidence threshold must be between 0.0 and 1.0",
                [nameof(AiConfidenceThreshold)]);
        }
    }
}

/// <summary>
/// Gmail-specific OAuth configuration.
/// </summary>
public class GmailSyncOptions
{
    /// <summary>
    /// Google OAuth Client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Google OAuth Client Secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Whether Gmail sync is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// OAuth scopes required for Gmail access.
    /// </summary>
    public List<string> Scopes { get; set; } =
    [
        "https://www.googleapis.com/auth/gmail.readonly",
        "https://www.googleapis.com/auth/userinfo.email",

    ];

    /// <summary>
    /// Checks if Gmail sync is properly configured.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// Outlook/Microsoft-specific OAuth configuration.
/// </summary>
public class OutlookSyncOptions
{
    /// <summary>
    /// Microsoft OAuth Client ID.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Microsoft OAuth Client Secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD tenant ID (default: "common" for multi-tenant).
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// Whether Outlook sync is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// OAuth scopes required for Outlook access.
    /// </summary>
    public List<string> Scopes { get; set; } =
    [
        "offline_access",
        "Mail.Read",
        "User.Read"
    ];

    /// <summary>
    /// Checks if Outlook sync is properly configured.
    /// </summary>
    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
