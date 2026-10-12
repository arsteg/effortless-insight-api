namespace EffortlessInsight.Api.Services.EmailSync;

/// <summary>
/// Abstraction for email provider clients (Gmail, Outlook).
/// </summary>
public interface IEmailProviderClient
{
    /// <summary>
    /// Provider identifier (gmail, outlook).
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Generates the OAuth authorization URL.
    /// </summary>
    /// <param name="state">State parameter for CSRF protection.</param>
    /// <param name="redirectUri">OAuth callback URL.</param>
    /// <returns>Authorization URL to redirect user to.</returns>
    string GetAuthorizationUrl(string state, string redirectUri, string codeChallenge);

    /// <summary>
    /// Exchanges authorization code for access and refresh tokens.
    /// </summary>
    Task<OAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an expired access token.
    /// </summary>
    Task<OAuthTokenResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets user profile information from the OAuth provider.
    /// </summary>
    Task<EmailUserProfile> GetUserProfileAsync(string accessToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches emails matching the filter criteria.
    /// </summary>
    Task<EmailFetchResult> FetchEmailsAsync(string accessToken, EmailFetchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an attachment from an email.
    /// </summary>
    Task<AttachmentDownloadResult> DownloadAttachmentAsync(
        string accessToken,
        string messageId,
        string attachmentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of OAuth token exchange or refresh.
/// </summary>
public record OAuthTokenResult
{
    public bool Success { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? Scopes { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// User profile information from email provider.
/// </summary>
public record EmailUserProfile
{
    public string Email { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string? ProviderId { get; init; }
}

/// <summary>
/// Options for fetching emails.
/// </summary>
public record EmailFetchOptions
{
    /// <summary>
    /// Cursor for incremental sync (historyId for Gmail, deltaLink for Outlook).
    /// </summary>
    public string? SyncCursor { get; init; }

    /// <summary>
    /// Sender filter patterns (e.g., "@gst.gov.in").
    /// </summary>
    public List<string> SenderFilters { get; init; } = [];

    /// <summary>
    /// Maximum number of emails to fetch.
    /// </summary>
    public int MaxResults { get; init; } = 100;

    /// <summary>
    /// Start date for initial sync (when no cursor exists).
    /// </summary>
    public DateTime? AfterDate { get; init; }

    /// <summary>
    /// Only fetch emails with attachments.
    /// </summary>
    public bool OnlyWithAttachments { get; init; } = true;
}

/// <summary>
/// Result of email fetch operation.
/// </summary>
public record EmailFetchResult
{
    public bool Success { get; init; }
    public List<EmailMessage> Messages { get; init; } = [];
    public string? NextSyncCursor { get; init; }
    public bool HasMore { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Represents an email message.
/// </summary>
public record EmailMessage
{
    public string MessageId { get; init; } = string.Empty;
    public string ProviderMessageId { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Sender { get; init; } = string.Empty;
    public string? SenderName { get; init; }
    public DateTime ReceivedAt { get; init; }
    public string? BodyText { get; init; }
    public string? BodyHtml { get; init; }
    public List<EmailAttachment> Attachments { get; init; } = [];
}

/// <summary>
/// Represents an email attachment.
/// </summary>
public record EmailAttachment
{
    public string AttachmentId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string MimeType { get; init; } = string.Empty;
    public long Size { get; init; }
}

/// <summary>
/// Result of attachment download.
/// </summary>
public record AttachmentDownloadResult
{
    public bool Success { get; init; }
    public byte[]? Data { get; init; }
    public string? FileName { get; init; }
    public string? MimeType { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}
