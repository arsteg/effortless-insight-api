namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Interface for GSP (GST Suvidha Provider) API client.
/// Abstracts communication with the GSP for GSTN portal operations.
/// </summary>
public interface IGspClient
{
    /// <summary>
    /// Gets the GSP provider name.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Initiates OTP request for GSTIN authentication.
    /// </summary>
    /// <param name="gstin">The GSTIN to authenticate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>OTP initiation result with session ID and masked destination.</returns>
    Task<GspOtpInitiationResult> InitiateOtpAsync(string gstin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies OTP and obtains access tokens.
    /// </summary>
    /// <param name="sessionId">The GSP session ID from OTP initiation.</param>
    /// <param name="otp">The OTP entered by user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token result with access and refresh tokens.</returns>
    Task<GspTokenResult> VerifyOtpAsync(string sessionId, string otp, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an access token using the refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="gstin">The GSTIN for the connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New token result.</returns>
    Task<GspTokenResult> RefreshTokenAsync(string refreshToken, string gstin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes tokens and disconnects from GSTN.
    /// </summary>
    /// <param name="accessToken">The access token to revoke.</param>
    /// <param name="gstin">The GSTIN for the connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RevokeTokenAsync(string accessToken, string gstin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches notices from the GST portal.
    /// </summary>
    /// <param name="accessToken">Valid access token.</param>
    /// <param name="gstin">The GSTIN to fetch notices for.</param>
    /// <param name="fromDate">Start date for notice fetch.</param>
    /// <param name="toDate">End date for notice fetch.</param>
    /// <param name="pageToken">Optional pagination token for fetching next page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of notices from the portal.</returns>
    Task<GspNoticesResult> FetchNoticesAsync(
        string accessToken,
        string gstin,
        DateTime fromDate,
        DateTime toDate,
        string? pageToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a notice document from the portal.
    /// </summary>
    /// <param name="accessToken">Valid access token.</param>
    /// <param name="gstin">The GSTIN.</param>
    /// <param name="noticeId">The GSTN notice ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document content and metadata.</returns>
    Task<GspDocumentResult> DownloadNoticeDocumentAsync(
        string accessToken,
        string gstin,
        string noticeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the access token is still valid.
    /// </summary>
    /// <param name="accessToken">The access token to validate.</param>
    /// <param name="gstin">The GSTIN.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if token is valid.</returns>
    Task<bool> ValidateTokenAsync(string accessToken, string gstin, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of OTP initiation request.
/// </summary>
public class GspOtpInitiationResult
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public string? OtpDestination { get; set; }
    public string? OtpDestinationType { get; set; }
    public int ExpirySeconds { get; set; } = 300;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Result of token operations (OTP verification, refresh).
/// </summary>
public class GspTokenResult
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? AccessTokenExpiresAt { get; set; }
    public DateTime? RefreshTokenExpiresAt { get; set; }
    public string? SessionId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Result of notice fetch operation.
/// </summary>
public class GspNoticesResult
{
    public bool Success { get; set; }
    public List<GspNotice> Notices { get; set; } = [];
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
    public string? NextPageToken { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Notice data from GSP.
/// </summary>
public class GspNotice
{
    public string NoticeId { get; set; } = string.Empty;
    public string? ReferenceNumber { get; set; }
    public string? NoticeType { get; set; }
    public string? NoticeCategory { get; set; }
    public string? Section { get; set; }
    public string? Subject { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal? TaxAmount { get; set; }
    public decimal? PenaltyAmount { get; set; }
    public decimal? InterestAmount { get; set; }
    public string? IssuingAuthority { get; set; }
    public string? IssuingOfficer { get; set; }
    public string? Jurisdiction { get; set; }
    public string? Status { get; set; }
    public string? FinancialYear { get; set; }
    public DateTime? PeriodFrom { get; set; }
    public DateTime? PeriodTo { get; set; }
    public bool HasDocument { get; set; }
    public string? DocumentFormat { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }
}

/// <summary>
/// Result of document download operation.
/// </summary>
public class GspDocumentResult
{
    public bool Success { get; set; }
    public byte[]? Content { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? ContentLength { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CorrelationId { get; set; }
}
