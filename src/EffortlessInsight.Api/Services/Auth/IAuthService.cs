using EffortlessInsight.Api.DTOs;

namespace EffortlessInsight.Api.Services.Auth;

public interface IAuthService
{
    Task<RegisterResponse> RegisterAsync(RegisterRequest request, string ipAddress, string? userAgent);
    Task<object> LoginAsync(LoginRequest request, string ipAddress, string? userAgent);
    Task<TokenResponse> RefreshTokenAsync(string refreshToken, string ipAddress, string? userAgent);
    Task VerifyEmailAsync(string token);
    Task ForgotPasswordAsync(string email, string ipAddress);
    Task ResetPasswordAsync(ResetPasswordRequest request);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task LogoutAsync(Guid userId, string? refreshTokenJti, bool allDevices);

    // OTP Login
    Task<OtpResponse> RequestOtpLoginAsync(string mobile, string ipAddress);
    Task<object> VerifyOtpLoginAsync(OtpVerifyRequest request, string ipAddress, string? userAgent);

    // 2FA Setup
    Task<TwoFactorSetupResponse> Setup2faAsync(Guid userId);
    Task<TwoFactorVerifySetupResponse> VerifySetup2faAsync(Guid userId, string code);
    Task Disable2faAsync(Guid userId, string password);

    // 2FA Login
    Task<TwoFactorLoginResponse> Complete2faLoginAsync(TwoFactorLoginRequest request, string ipAddress, string? userAgent);

    // Password History
    Task<bool> IsPasswordRecentlyUsedAsync(Guid userId, string password);
    Task AddPasswordHistoryAsync(Guid userId, string passwordHash);

    // OAuth
    Task<OAuthProvidersResponse> GetEnabledOAuthProvidersAsync();

    /// <summary>
    /// Get OAuth login URL for a provider.
    /// </summary>
    /// <param name="provider">The OAuth provider (google, microsoft)</param>
    /// <param name="state">Optional custom state token (one will be generated if not provided)</param>
    /// <param name="forceReauth">If true, forces the user to re-authenticate even if already signed in</param>
    /// <param name="redirectUri">Optional custom redirect URI for mobile apps (deep link)</param>
    /// <param name="platform">Optional platform identifier (web, ios, android)</param>
    Task<OAuthLoginUrlResponse> GetOAuthLoginUrlAsync(string provider, string? state, bool forceReauth = false, string? redirectUri = null, string? platform = null);

    /// <summary>
    /// Handle OAuth callback from provider. State is required for CSRF protection.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when state is missing, invalid, expired, or provider mismatches.</exception>
    Task<object> HandleOAuthCallbackAsync(string provider, string code, string state, string ipAddress, string? userAgent);

    /// <summary>
    /// Disconnect an OAuth provider from the user's account.
    /// Requires the user to have a password set or another OAuth provider linked.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="provider">Provider to disconnect (google, microsoft)</param>
    /// <param name="password">Password for verification</param>
    /// <exception cref="InvalidOperationException">Thrown when provider not linked or would leave account without auth method.</exception>
    Task DisconnectOAuthAsync(Guid userId, string provider, string password);

    /// <summary>
    /// Get the user's currently linked OAuth provider info (legacy single-provider).
    /// </summary>
    Task<UserOAuthInfoResponse?> GetUserOAuthInfoAsync(Guid userId);

    /// <summary>
    /// Get all OAuth providers linked to a user.
    /// </summary>
    Task<UserOAuthProvidersResponse> GetUserOAuthProvidersAsync(Guid userId);

    /// <summary>
    /// Link an additional OAuth provider to an existing user account.
    /// </summary>
    /// <param name="userId">User ID to link to</param>
    /// <param name="provider">OAuth provider name</param>
    /// <param name="code">Authorization code from OAuth callback</param>
    /// <param name="state">State token for CSRF protection</param>
    Task<LinkedOAuthProviderDto> LinkOAuthProviderAsync(Guid userId, string provider, string code, string state);
}
