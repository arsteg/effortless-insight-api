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
    Task<OAuthLoginUrlResponse> GetOAuthLoginUrlAsync(string provider, string? state);
    Task<object> HandleOAuthCallbackAsync(string provider, string code, string? state, string ipAddress, string? userAgent);
}
