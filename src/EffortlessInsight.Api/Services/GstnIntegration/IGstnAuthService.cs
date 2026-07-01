using EffortlessInsight.Api.Data.Entities;

namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Service for GSTN portal authentication via OTP.
/// </summary>
public interface IGstnAuthService
{
    /// <summary>
    /// Initiates OTP-based connection to GSTN portal.
    /// </summary>
    Task<GstnOtpInitiationResponse> InitiateConnectionAsync(
        Guid organizationGstinId,
        Guid userId,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the OTP and completes the connection.
    /// </summary>
    Task<GstnOtpVerificationResponse> VerifyOtpAsync(
        Guid organizationGstinId,
        string otp,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the access token using refresh token.
    /// </summary>
    Task<GstnTokenRefreshResponse> RefreshTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a valid access token for a connection, refreshing if needed.
    /// </summary>
    Task<string?> GetValidAccessTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if the current token is still valid.
    /// </summary>
    Task<bool> ValidateTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any pending OTP session for a GSTIN.
    /// </summary>
    Task CancelPendingOtpSessionAsync(
        Guid organizationGstinId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up expired OTP sessions.
    /// </summary>
    Task<int> CleanupExpiredOtpSessionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Response from OTP initiation.
/// </summary>
public record GstnOtpInitiationResponse(
    bool Success,
    string? OtpDestination,
    string? OtpDestinationType,
    DateTime? ExpiresAt,
    string? ErrorCode,
    string? ErrorMessage
);

/// <summary>
/// Response from OTP verification.
/// </summary>
public record GstnOtpVerificationResponse(
    bool Success,
    GstnConnectionStatusDto? ConnectionStatus,
    string? ErrorCode,
    string? ErrorMessage,
    int? RemainingAttempts
);

/// <summary>
/// Response from token refresh.
/// </summary>
public record GstnTokenRefreshResponse(
    bool Success,
    DateTime? NewExpiresAt,
    string? ErrorCode,
    string? ErrorMessage
);
