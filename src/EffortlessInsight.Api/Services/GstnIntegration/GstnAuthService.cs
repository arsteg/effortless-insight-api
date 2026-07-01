using EffortlessInsight.Api.Data;
using EffortlessInsight.Api.Data.Entities;
using EffortlessInsight.Api.Options;
using EffortlessInsight.Api.Services.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace EffortlessInsight.Api.Services.GstnIntegration;

/// <summary>
/// Service for GSTN portal authentication via OTP.
/// </summary>
public class GstnAuthService : IGstnAuthService
{
    private readonly ApplicationDbContext _context;
    private readonly IGspClient _gspClient;
    private readonly IFieldEncryptionService _encryption;
    private readonly GstnOptions _options;
    private readonly IGstnConnectionService _connectionService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<GstnAuthService> _logger;

    public GstnAuthService(
        ApplicationDbContext context,
        IGspClient gspClient,
        IFieldEncryptionService encryption,
        IOptions<GstnOptions> options,
        IGstnConnectionService connectionService,
        IWebHostEnvironment environment,
        ILogger<GstnAuthService> logger)
    {
        _context = context;
        _gspClient = gspClient;
        _encryption = encryption;
        _options = options.Value;
        _connectionService = connectionService;
        _environment = environment;
        _logger = logger;

        // Critical: Warn if mock mode is enabled in non-development environment
        if (_options.EnableMockMode && !_environment.IsDevelopment())
        {
            _logger.LogCritical(
                "SECURITY WARNING: GSTN mock mode is enabled in {Environment} environment. " +
                "This should only be enabled in Development!",
                _environment.EnvironmentName);
        }
    }

    public async Task<GstnOtpInitiationResponse> InitiateConnectionAsync(
        Guid organizationGstinId,
        Guid userId,
        string? ipAddress = null,
        string? userAgent = null,
        CancellationToken cancellationToken = default)
    {
        // Get the GSTIN with existing connection
        var gstin = await _context.OrganizationGstins
            .Include(g => g.GstnConnection)
            .FirstOrDefaultAsync(g => g.Id == organizationGstinId, cancellationToken);

        if (gstin == null)
        {
            return new GstnOtpInitiationResponse(
                Success: false,
                OtpDestination: null,
                OtpDestinationType: null,
                ExpiresAt: null,
                ErrorCode: "GSTIN_NOT_FOUND",
                ErrorMessage: "GSTIN not found"
            );
        }

        // Validate GSTIN format (15 alphanumeric: 2 state code + 10 PAN + 1 entity + 1 check + 1 default)
        if (!IsValidGstinFormat(gstin.Gstin))
        {
            return new GstnOtpInitiationResponse(
                Success: false,
                OtpDestination: null,
                OtpDestinationType: null,
                ExpiresAt: null,
                ErrorCode: "INVALID_GSTIN_FORMAT",
                ErrorMessage: "GSTIN format is invalid. Expected 15 alphanumeric characters."
            );
        }

        // Check if already connected - warn but allow reconnection
        if (gstin.GstnConnection?.Status == GstnConnectionStatus.Connected)
        {
            _logger.LogInformation(
                "GSTIN {GstinId} is already connected, user {UserId} initiating reconnection",
                organizationGstinId,
                userId);
        }

        // Cancel any existing pending OTP sessions
        await CancelPendingOtpSessionAsync(organizationGstinId, cancellationToken);

        // Check for mock mode - return simulated response for testing
        if (_options.EnableMockMode)
        {
            return await InitiateMockConnectionAsync(organizationGstinId, userId, ipAddress, userAgent, cancellationToken);
        }

        // Initiate OTP with GSP
        var result = await _gspClient.InitiateOtpAsync(gstin.Gstin, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "OTP initiation failed for GSTIN {GstinId}: {ErrorCode} - {ErrorMessage}",
                organizationGstinId,
                result.ErrorCode,
                result.ErrorMessage);

            return new GstnOtpInitiationResponse(
                Success: false,
                OtpDestination: null,
                OtpDestinationType: null,
                ExpiresAt: null,
                ErrorCode: result.ErrorCode,
                ErrorMessage: result.ErrorMessage
            );
        }

        // Create OTP session
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.OtpExpiryMinutes);

        var otpSession = new GstnOtpSession
        {
            OrganizationGstinId = organizationGstinId,
            InitiatedById = userId,
            GspSessionId = result.SessionId!,
            OtpDestination = result.OtpDestination,
            OtpDestinationType = result.OtpDestinationType,
            ExpiresAt = expiresAt,
            MaxAttempts = _options.MaxOtpAttempts,
            Status = GstnOtpSessionStatus.Pending,
            RequestIpAddress = ipAddress,
            RequestUserAgent = userAgent
        };

        _context.GstnOtpSessions.Add(otpSession);

        // Create or update connection with pending status
        var connection = await _context.GstnConnections
            .FirstOrDefaultAsync(c => c.OrganizationGstinId == organizationGstinId, cancellationToken);

        if (connection == null)
        {
            connection = new GstnConnection
            {
                OrganizationGstinId = organizationGstinId,
                Status = GstnConnectionStatus.PendingOtp,
                GspProvider = _options.DefaultGspProvider,
                SyncIntervalHours = _options.DefaultSyncIntervalHours
            };
            _context.GstnConnections.Add(connection);
        }
        else
        {
            connection.Status = GstnConnectionStatus.PendingOtp;
            connection.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "OTP initiated for GSTIN {GstinId}, session expires at {ExpiresAt}",
            organizationGstinId,
            expiresAt);

        return new GstnOtpInitiationResponse(
            Success: true,
            OtpDestination: result.OtpDestination,
            OtpDestinationType: result.OtpDestinationType,
            ExpiresAt: expiresAt,
            ErrorCode: null,
            ErrorMessage: null
        );
    }

    public async Task<GstnOtpVerificationResponse> VerifyOtpAsync(
        Guid organizationGstinId,
        string otp,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Get the pending OTP session
        var session = await _context.GstnOtpSessions
            .Where(s => s.OrganizationGstinId == organizationGstinId)
            .Where(s => s.Status == GstnOtpSessionStatus.Pending)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            return new GstnOtpVerificationResponse(
                Success: false,
                ConnectionStatus: null,
                ErrorCode: "NO_PENDING_OTP",
                ErrorMessage: "No pending OTP verification found. Please initiate connection again.",
                RemainingAttempts: null
            );
        }

        // Check if expired
        if (DateTime.UtcNow > session.ExpiresAt)
        {
            session.Status = GstnOtpSessionStatus.Expired;
            await _context.SaveChangesAsync(cancellationToken);

            return new GstnOtpVerificationResponse(
                Success: false,
                ConnectionStatus: null,
                ErrorCode: "OTP_EXPIRED",
                ErrorMessage: "OTP has expired. Please initiate connection again.",
                RemainingAttempts: null
            );
        }

        // Check attempts
        if (session.Attempts >= session.MaxAttempts)
        {
            session.Status = GstnOtpSessionStatus.Failed;
            await _context.SaveChangesAsync(cancellationToken);

            return new GstnOtpVerificationResponse(
                Success: false,
                ConnectionStatus: null,
                ErrorCode: "MAX_ATTEMPTS_EXCEEDED",
                ErrorMessage: "Maximum OTP attempts exceeded. Please initiate connection again.",
                RemainingAttempts: 0
            );
        }

        session.Attempts++;

        // Check for mock mode - OTP "123456" always succeeds
        GspTokenResult result;
        if (_options.EnableMockMode)
        {
            if (otp == "123456")
            {
                _logger.LogWarning("GSTN Mock Mode: OTP verified successfully for GSTIN {GstinId}", organizationGstinId);
                result = new GspTokenResult
                {
                    Success = true,
                    AccessToken = $"mock-access-token-{Guid.NewGuid():N}",
                    RefreshToken = $"mock-refresh-token-{Guid.NewGuid():N}",
                    AccessTokenExpiresAt = DateTime.UtcNow.AddHours(6),
                    RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30),
                    SessionId = session.GspSessionId
                };
            }
            else
            {
                result = new GspTokenResult
                {
                    Success = false,
                    ErrorCode = "INVALID_OTP",
                    ErrorMessage = "Invalid OTP. In mock mode, use '123456'."
                };
            }
        }
        else
        {
            // Verify with GSP
            result = await _gspClient.VerifyOtpAsync(session.GspSessionId, otp, cancellationToken);
        }

        if (!result.Success)
        {
            var remaining = session.MaxAttempts - session.Attempts;
            session.ErrorMessage = result.ErrorMessage;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "OTP verification failed for GSTIN {GstinId}: {ErrorCode} - {ErrorMessage}",
                organizationGstinId,
                result.ErrorCode,
                result.ErrorMessage);

            return new GstnOtpVerificationResponse(
                Success: false,
                ConnectionStatus: null,
                ErrorCode: result.ErrorCode ?? "INVALID_OTP",
                ErrorMessage: result.ErrorMessage ?? "Invalid OTP",
                RemainingAttempts: remaining
            );
        }

        // Success - update session and connection in a transaction
        // to ensure atomicity of the state change
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            session.Status = GstnOtpSessionStatus.Verified;
            session.VerifiedAt = DateTime.UtcNow;

            var connection = await _context.GstnConnections
                .FirstOrDefaultAsync(c => c.OrganizationGstinId == organizationGstinId, cancellationToken);

            if (connection != null)
            {
                // Encrypt tokens with verification
                string encryptedAccessToken;
                string? encryptedRefreshToken = null;
                try
                {
                    encryptedAccessToken = _encryption.Encrypt(result.AccessToken!);
                    // Verify encryption actually transformed the token
                    if (encryptedAccessToken == result.AccessToken)
                    {
                        throw new InvalidOperationException("Token encryption did not transform the value");
                    }

                    if (result.RefreshToken != null)
                    {
                        encryptedRefreshToken = _encryption.Encrypt(result.RefreshToken);
                        if (encryptedRefreshToken == result.RefreshToken)
                        {
                            throw new InvalidOperationException("Refresh token encryption did not transform the value");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to encrypt tokens for GSTIN {GstinId}", organizationGstinId);
                    throw new InvalidOperationException("Failed to securely store credentials", ex);
                }

                connection.Status = GstnConnectionStatus.Connected;
                connection.EncryptedAccessToken = encryptedAccessToken;
                connection.EncryptedRefreshToken = encryptedRefreshToken;
                connection.TokenExpiresAt = result.AccessTokenExpiresAt;
                connection.RefreshTokenExpiresAt = result.RefreshTokenExpiresAt;
                connection.GspSessionId = result.SessionId;
                connection.ConnectedById = userId;
                connection.ConnectedAt = DateTime.UtcNow;
                connection.ConsecutiveFailures = 0;
                connection.LastSyncError = null;
                connection.UpdatedAt = DateTime.UtcNow;

                if (connection.AutoSyncEnabled)
                {
                    connection.NextScheduledSyncAt = DateTime.UtcNow; // Sync immediately
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Invalidate cached status before fetching fresh data
            _connectionService.InvalidateConnectionStatusCache(organizationGstinId);

            _logger.LogInformation(
                "GSTN connection established for GSTIN {GstinId} by user {UserId}",
                organizationGstinId,
                userId);

            var status = await _connectionService.GetConnectionStatusAsync(organizationGstinId, cancellationToken);

            return new GstnOtpVerificationResponse(
                Success: true,
                ConnectionStatus: status,
                ErrorCode: null,
                ErrorMessage: null,
                RemainingAttempts: null
            );
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to complete OTP verification for GSTIN {GstinId}", organizationGstinId);
            throw;
        }
    }

    public async Task<GstnTokenRefreshResponse> RefreshTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection == null)
        {
            return new GstnTokenRefreshResponse(
                Success: false,
                NewExpiresAt: null,
                ErrorCode: "CONNECTION_NOT_FOUND",
                ErrorMessage: "Connection not found"
            );
        }

        if (string.IsNullOrEmpty(connection.EncryptedRefreshToken))
        {
            return new GstnTokenRefreshResponse(
                Success: false,
                NewExpiresAt: null,
                ErrorCode: "NO_REFRESH_TOKEN",
                ErrorMessage: "No refresh token available"
            );
        }

        string refreshToken;
        try
        {
            refreshToken = _encryption.Decrypt(connection.EncryptedRefreshToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to decrypt refresh token for connection {ConnectionId}",
                connectionId);
            return new GstnTokenRefreshResponse(
                Success: false,
                NewExpiresAt: null,
                ErrorCode: "DECRYPTION_FAILED",
                ErrorMessage: "Failed to decrypt stored token"
            );
        }

        var result = await _gspClient.RefreshTokenAsync(
            refreshToken,
            connection.OrganizationGstin.Gstin,
            cancellationToken);

        if (!result.Success)
        {
            connection.Status = GstnConnectionStatus.TokenExpired;
            connection.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            // Invalidate cached status
            _connectionService.InvalidateConnectionStatusCache(connection.OrganizationGstinId);

            _logger.LogWarning(
                "Token refresh failed for connection {ConnectionId}: {ErrorCode}",
                connectionId,
                result.ErrorCode);

            return new GstnTokenRefreshResponse(
                Success: false,
                NewExpiresAt: null,
                ErrorCode: result.ErrorCode,
                ErrorMessage: result.ErrorMessage
            );
        }

        connection.EncryptedAccessToken = _encryption.Encrypt(result.AccessToken!);
        if (result.RefreshToken != null)
        {
            connection.EncryptedRefreshToken = _encryption.Encrypt(result.RefreshToken);
        }
        connection.TokenExpiresAt = result.AccessTokenExpiresAt;
        connection.RefreshTokenExpiresAt = result.RefreshTokenExpiresAt;
        connection.GspSessionId = result.SessionId;
        connection.Status = GstnConnectionStatus.Connected;
        connection.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        // Invalidate cached status
        _connectionService.InvalidateConnectionStatusCache(connection.OrganizationGstinId);

        _logger.LogInformation(
            "Token refreshed for connection {ConnectionId}, expires at {ExpiresAt}",
            connectionId,
            result.AccessTokenExpiresAt);

        return new GstnTokenRefreshResponse(
            Success: true,
            NewExpiresAt: result.AccessTokenExpiresAt,
            ErrorCode: null,
            ErrorMessage: null
        );
    }

    public async Task<string?> GetValidAccessTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _context.GstnConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection == null || string.IsNullOrEmpty(connection.EncryptedAccessToken))
        {
            return null;
        }

        // Check if token is expiring soon
        var refreshThreshold = DateTime.UtcNow.AddMinutes(_options.TokenRefreshThresholdMinutes);

        if (connection.TokenExpiresAt <= refreshThreshold)
        {
            var refreshResult = await RefreshTokenAsync(connectionId, cancellationToken);
            if (!refreshResult.Success)
            {
                return null;
            }

            // Reload connection to get new token
            connection = await _context.GstnConnections
                .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

            if (connection == null || string.IsNullOrEmpty(connection.EncryptedAccessToken))
            {
                return null;
            }
        }

        return _encryption.Decrypt(connection.EncryptedAccessToken);
    }

    public async Task<bool> ValidateTokenAsync(
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _context.GstnConnections
            .Include(c => c.OrganizationGstin)
            .FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

        if (connection == null || string.IsNullOrEmpty(connection.EncryptedAccessToken))
        {
            return false;
        }

        var accessToken = _encryption.Decrypt(connection.EncryptedAccessToken);
        return await _gspClient.ValidateTokenAsync(
            accessToken,
            connection.OrganizationGstin.Gstin,
            cancellationToken);
    }

    public async Task CancelPendingOtpSessionAsync(
        Guid organizationGstinId,
        CancellationToken cancellationToken = default)
    {
        var pendingSessions = await _context.GstnOtpSessions
            .Where(s => s.OrganizationGstinId == organizationGstinId)
            .Where(s => s.Status == GstnOtpSessionStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var session in pendingSessions)
        {
            session.Status = GstnOtpSessionStatus.Cancelled;
            session.UpdatedAt = DateTime.UtcNow;
        }

        if (pendingSessions.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> CleanupExpiredOtpSessionsAsync(CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-_options.OtpSessionRetentionDays);

        var expiredSessions = await _context.GstnOtpSessions
            .Where(s => s.CreatedAt < cutoffDate)
            .ToListAsync(cancellationToken);

        if (expiredSessions.Count > 0)
        {
            _context.GstnOtpSessions.RemoveRange(expiredSessions);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cleaned up {Count} expired OTP sessions", expiredSessions.Count);
        }

        return expiredSessions.Count;
    }

    #region Helper Methods

    /// <summary>
    /// Validates GSTIN format: 15 alphanumeric characters.
    /// Format: 2 state code + 10 PAN + 1 entity code + 1 check digit + 1 default 'Z'
    /// </summary>
    private static bool IsValidGstinFormat(string gstin)
    {
        if (string.IsNullOrWhiteSpace(gstin) || gstin.Length != 15)
            return false;

        // GSTIN pattern: ^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$
        // Simplified check: 15 alphanumeric characters, starting with 2 digits
        var pattern = @"^[0-9]{2}[A-Z0-9]{13}$";
        return System.Text.RegularExpressions.Regex.IsMatch(gstin.ToUpperInvariant(), pattern);
    }

    /// <summary>
    /// Handles mock connection initiation for testing/development.
    /// </summary>
    private async Task<GstnOtpInitiationResponse> InitiateMockConnectionAsync(
        Guid organizationGstinId,
        Guid userId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning("GSTN Mock Mode is enabled - using simulated OTP flow for GSTIN {GstinId}", organizationGstinId);

        var expiresAt = DateTime.UtcNow.AddMinutes(_options.OtpExpiryMinutes);

        // Create mock OTP session - OTP is always "123456" in mock mode
        var otpSession = new GstnOtpSession
        {
            OrganizationGstinId = organizationGstinId,
            InitiatedById = userId,
            GspSessionId = $"mock-session-{Guid.NewGuid():N}",
            OtpDestination = "******1234",
            OtpDestinationType = "mobile",
            ExpiresAt = expiresAt,
            MaxAttempts = _options.MaxOtpAttempts,
            Status = GstnOtpSessionStatus.Pending,
            RequestIpAddress = ipAddress,
            RequestUserAgent = userAgent
        };

        _context.GstnOtpSessions.Add(otpSession);

        // Create or update connection
        var connection = await _context.GstnConnections
            .FirstOrDefaultAsync(c => c.OrganizationGstinId == organizationGstinId, cancellationToken);

        if (connection == null)
        {
            connection = new GstnConnection
            {
                OrganizationGstinId = organizationGstinId,
                Status = GstnConnectionStatus.PendingOtp,
                GspProvider = "mock",
                SyncIntervalHours = _options.DefaultSyncIntervalHours
            };
            _context.GstnConnections.Add(connection);
        }
        else
        {
            connection.Status = GstnConnectionStatus.PendingOtp;
            connection.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return new GstnOtpInitiationResponse(
            Success: true,
            OtpDestination: "******1234",
            OtpDestinationType: "mobile",
            ExpiresAt: expiresAt,
            ErrorCode: null,
            ErrorMessage: null
        );
    }

    #endregion
}
